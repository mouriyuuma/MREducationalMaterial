using System.Collections.Generic;
using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    public static PuzzleManager Instance { get; private set; }

    [Tooltip("現在のお題データ（ScriptableObject）")]
    public MoleculeData CurrentTargetData;

    [Header("Feedback Effects")]
    [Tooltip("クリア時に出すパーティクルなどのプレハブ")]
    public GameObject ClearEffectPrefab;
    [Tooltip("クリア時に鳴らす効果音")]
    public AudioClip ClearSound;

    private AudioSource _audioSource;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // 音を鳴らすためのコンポーネントを自動追加
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
    }

    // MoleculeManagerから「ひとまとまりの分子」が送られてくる
    public void CheckMoleculeMatch(List<Atom> moleculeAtoms)
    {
        if (CurrentTargetData == null) return;

        // ステップ1：原子の「数と種類」が合っているかチェック
        if (!CheckAtomCounts(moleculeAtoms)) return;

        // ステップ2：繋がり方（構造）が合っているかチェック
        if (!CheckBondStructure(moleculeAtoms)) return;

        // すべて一致したらクリア！
        Debug.Log($"【クリア】お題「{CurrentTargetData.MoleculeName}」が完成しました！");
        
        // ※ここでベンゼン環へのモデル置換や、パーティクル演出を実行します
        // クリア処理に完成した分子のデータを渡す
        OnPuzzleCleared(moleculeAtoms);
    }

    // 原子の数をチェックするメソッド
    private bool CheckAtomCounts(List<Atom> moleculeAtoms)
    {
        // 今作られた分子の原子を数え上げる
        Dictionary<string, int> currentCounts = new Dictionary<string, int>();
        foreach (Atom atom in moleculeAtoms)
        {
            if (currentCounts.ContainsKey(atom.ElementType))
                currentCounts[atom.ElementType]++;
            else
                currentCounts[atom.ElementType] = 1;
        }

        // お題（ScriptableObject）の要求数と比較
        foreach (var req in CurrentTargetData.RequiredAtoms)
        {
            if (!currentCounts.ContainsKey(req.ElementType) || currentCounts[req.ElementType] != req.Count)
            {
                return false; // 足りない、または多すぎる場合は不合格
            }
        }

        // 余計な種類の原子が混ざっていないかも確認
        if (currentCounts.Keys.Count != CurrentTargetData.RequiredAtoms.Count) return false;

        return true; // 種類と数が完全に一致！
    }

    // トポロジー（繋がり方）をチェックするメソッド
    private bool CheckBondStructure(List<Atom> moleculeAtoms)
    {
        // 1. 結合の「総数」が一致するかを最初に確認する（余計な結合がないかのチェック）
        int totalPlayerBonds = 0;
        foreach (var atom in moleculeAtoms)
        {
            foreach (var bp in atom.BondPoints)
            {
                if (bp.IsConnected) totalPlayerBonds++;
            }
        }
        totalPlayerBonds /= 2; // 結合は双方向なので半分にする

        if (totalPlayerBonds != CurrentTargetData.RequiredBonds.Count)
        {
            return false; // 結合の数が違うなら、その時点で不正解
        }

        // 2. バックトラッキング（総当たり探索）で、完全一致する配置を探す
        Dictionary<int, Atom> mapping = new Dictionary<int, Atom>();
        return FindMapping(0, moleculeAtoms, mapping);
    }

    // お題の原子(ID)に対して、プレイヤーのどの原子を割り当てるか、全パターンを試すメソッド
    private bool FindMapping(int requirementIndex, List<Atom> availableAtoms, Dictionary<int, Atom> currentMapping)
    {
        // すべてのお題原子(ID)に、矛盾なくプレイヤーの原子を割り当てられたら「正解」！
        if (requirementIndex >= CurrentTargetData.RequiredAtoms.Count)
        {
            return true;
        }

        // 今から割り当て先を探す、お題の原子データ
        AtomNodeRequirement requiredNode = CurrentTargetData.RequiredAtoms[requirementIndex];

        // プレイヤーが作った分子の中から、候補になりそうな原子を1つずつ試す
        foreach (Atom candidateAtom in availableAtoms)
        {
            // 既に他のIDに割り当て済みの原子ならスキップ
            if (currentMapping.ContainsValue(candidateAtom)) continue;
            
            // 元素の種類（C, Hなど）が違うならスキップ
            if (candidateAtom.ElementType != requiredNode.ElementType) continue;

            // 仮にこの原子を、お題のIDに割り当ててみる
            currentMapping[requiredNode.AtomId] = candidateAtom;

            // 仮割り当てした状態で、結合のルール（腕の番号や多重結合）に矛盾がないかチェック
            if (IsPartialMappingValid(currentMapping))
            {
                // 矛盾がなければ、次のIDの割り当てに進む（再帰呼び出し）
                if (FindMapping(requirementIndex + 1, availableAtoms, currentMapping))
                {
                    return true; // 最終的にすべて成功したらtrueを伝言ゲームで返す
                }
            }

            // 矛盾があった、もしくは行き止まりだった場合、割り当てを取り消して次の候補を試す（これがバックトラッキング）
            currentMapping.Remove(requiredNode.AtomId);
        }

        return false; // どの候補を試してもダメだった
    }

    // 現在の「仮割り当て」状態で、お題の結合ルールを破っていないか確認するメソッド
    private bool IsPartialMappingValid(Dictionary<int, Atom> mapping)
    {
        foreach (BondEdgeRequirement reqBond in CurrentTargetData.RequiredBonds)
        {
            // 結合ルールの対象となる「原子A」と「原子B」が、両方とも既に割り当てられている場合のみチェックする
            if (mapping.TryGetValue(reqBond.AtomIdA, out Atom atomA) &&
                mapping.TryGetValue(reqBond.AtomIdB, out Atom atomB))
            {
                // プレハブの腕の数が足りない場合はエラー（データ設定ミスの防止）
                if (reqBond.BondIndexA >= atomA.BondPoints.Length || reqBond.BondIndexB >= atomB.BondPoints.Length)
                    return false; 

                BondPoint bpA = atomA.BondPoints[reqBond.BondIndexA];
                BondPoint bpB = atomB.BondPoints[reqBond.BondIndexB];

                // 1. 指定された腕（インデックス）同士がくっついているか？
                if (!bpA.IsConnected || bpA.ConnectedTarget != bpB)
                    return false;

                // 2. 結合の強さ（単結合・二重結合）は一致しているか？
                if (bpA.CurrentBondOrder != reqBond.BondOrder)
                    return false;
            }
        }
        return true; // 今のところすべての結合ルールを満たしている
    }

    private void OnPuzzleCleared(List<Atom> moleculeAtoms)
    {
        // 1. 分子の「中心位置」を計算する
        Vector3 centerPosition = Vector3.zero;
        foreach (Atom atom in moleculeAtoms)
        {
            centerPosition += atom.transform.position;
        }
        centerPosition /= moleculeAtoms.Count;

        // 2. エフェクトを生成する
        if (ClearEffectPrefab != null)
        {
            Instantiate(ClearEffectPrefab, centerPosition, Quaternion.identity);
        }

        // 3. 効果音を鳴らす
        if (ClearSound != null)
        {
            _audioSource.PlayOneShot(ClearSound);
        }

        // ※ここで次のお題に進む処理などを後々追加します
    }
}