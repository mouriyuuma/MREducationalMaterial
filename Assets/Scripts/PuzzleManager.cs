using System.Collections.Generic;
using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    public static PuzzleManager Instance { get; private set; }

    [Tooltip("出題する問題リスト")]
    public MoleculeData[] PuzzleList;

    [Tooltip("現在のお題データ（ScriptableObject）")]
    public MoleculeData CurrentTargetData;

    private int _currentPuzzleIndex = 0;
    private bool _isPuzzleCleared = false;

    public int CurrentPuzzleIndex => _currentPuzzleIndex;
    public int TotalPuzzles => PuzzleList != null ? PuzzleList.Length : 0;

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

    private void Start()
    {
        LoadPuzzle(0);
    }

    public void LoadPuzzle(int index)
    {
        if (PuzzleList == null || PuzzleList.Length == 0) return;

        _currentPuzzleIndex = index;
        
        if (_currentPuzzleIndex < PuzzleList.Length)
        {
            CurrentTargetData = PuzzleList[_currentPuzzleIndex];
            _isPuzzleCleared = false;

            if (TargetBoardUI.Instance != null)
            {
                TargetBoardUI.Instance.RefreshDisplay();
            }
        }
        else
        {
            // 全問クリア
            if (TargetBoardUI.Instance != null)
            {
                TargetBoardUI.Instance.ShowCompleteVisual();
            }
        }
    }

    // MoleculeManagerから「ひとまとまりの分子」が送られてくる
    public void CheckMoleculeMatch(List<Atom> moleculeAtoms)
    {
        if (CurrentTargetData == null)
        {
            Debug.Log("【判定】CurrentTargetData が null です。");
            return;
        }
        if (_isPuzzleCleared)
        {
            Debug.Log("【判定】既にクリア済みです。");
            return;
        }

        Debug.Log($"【判定開始】現在の原子グループのサイズ: {moleculeAtoms.Count}");

        // ステップ1：原子の「数と種類」が合っているかチェック
        if (!CheckAtomCounts(moleculeAtoms))
        {
            Debug.Log("【判定失敗】原子の数または種類が一致しません。");
            return;
        }

        // ステップ2：繋がり方（構造）が合っているかチェック
        if (!CheckBondStructure(moleculeAtoms))
        {
            Debug.Log("【判定失敗】結合の構造（繋がり方）が一致しません。");
            return;
        }

        // すべて一致したらクリア！
        Debug.Log($"【クリア】お題「{CurrentTargetData.MoleculeName}」が完成しました！");
        
        // ※ここでベンゼン環へのモデル置換や、パーティクル演出を実行します
        // クリア処理に完成した分子のデータを渡す
        OnPuzzleCleared(moleculeAtoms);
    }

    // 原子の数をチェックするメソッド
    private bool CheckAtomCounts(List<Atom> moleculeAtoms)
    {
        // 1. 全体の原子数が一致するか（そもそも数が違えば不合格）
        if (moleculeAtoms.Count != CurrentTargetData.RequiredAtoms.Count)
        {
            Debug.Log($"【判定】全体の原子数が違います。現在:{moleculeAtoms.Count} / お題:{CurrentTargetData.RequiredAtoms.Count}");
            return false;
        }

        // 2. プレイヤーが作った分子の各元素の数を数える
        Dictionary<string, int> currentCounts = new Dictionary<string, int>();
        foreach (Atom atom in moleculeAtoms)
        {
            if (currentCounts.ContainsKey(atom.ElementType))
                currentCounts[atom.ElementType]++;
            else
                currentCounts[atom.ElementType] = 1;
        }

        // 3. お題データ（RequiredAtomsのリスト）から、各元素が何個必要かを集計する
        Dictionary<string, int> targetCounts = new Dictionary<string, int>();
        foreach (var nodeReq in CurrentTargetData.RequiredAtoms)
        {
            if (targetCounts.ContainsKey(nodeReq.ElementType))
                targetCounts[nodeReq.ElementType]++;
            else
                targetCounts[nodeReq.ElementType] = 1;
        }

        // 4. 種類と数が完全に一致するか比較する
        if (currentCounts.Count != targetCounts.Count) return false;

        foreach (var pair in targetCounts)
        {
            if (!currentCounts.ContainsKey(pair.Key) || currentCounts[pair.Key] != pair.Value)
            {
                return false; 
            }
        }

        return true;
    }

    // トポロジー（繋がり方）をチェックするメソッド
    private bool CheckBondStructure(List<Atom> moleculeAtoms)
    {
        // 1. 結合の「総数」が一致するかを最初に確認する（余計な結合がないかのチェック）
        // 二重結合は腕を2本使うので、腕の本数ではなく「繋がっている原子のペアの数」を数える。
        // お題の RequiredBonds も結合1本につき1エントリ（次数は BondOrder が持つ）なので、これで対応が取れる
        HashSet<(int, int)> bondedPairs = new HashSet<(int, int)>();
        foreach (var atom in moleculeAtoms)
        {
            foreach (var bp in atom.BondPoints)
            {
                Atom neighbor = bp.ConnectedAtom;
                if (neighbor == null) continue;

                int a = atom.GetInstanceID();
                int b = neighbor.GetInstanceID();
                bondedPairs.Add(a < b ? (a, b) : (b, a));
            }
        }
        int totalPlayerBonds = bondedPairs.Count;

        if (totalPlayerBonds != CurrentTargetData.RequiredBonds.Count)
        {
            Debug.Log($"【判定】結合の総数が違います。プレイヤーの結合数:{totalPlayerBonds} / お題の要求数:{CurrentTargetData.RequiredBonds.Count}");
            return false; // 結合の数が違うなら、その時点で不正解
        }

        // 2. バックトラッキング（総当たり探索）で、完全一致する配置を探す
        Dictionary<int, Atom> mapping = new Dictionary<int, Atom>();
        bool isMatch = FindMapping(0, moleculeAtoms, mapping);

        if (!isMatch)
        {
            Debug.Log("【判定失敗】すべての組み合わせを試しましたが、お題の構造と完全一致するパターンがありませんでした。直前の【詳細ログ】を確認してください。");
        }

        return isMatch;
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
                // 結合次数 ＝ atomB に向かっている腕の本数。単結合なら1、二重結合なら2になる
                int playerBondOrder = atomA.GetBondOrderTo(atomB);

                if (playerBondOrder == 0)
                {
                    Debug.Log($"【詳細ログ】お題のID:{reqBond.AtomIdA}({atomA.ElementType}) と ID:{reqBond.AtomIdB}({atomB.ElementType}) が繋がっていません。");
                    return false;
                }

                if (playerBondOrder != reqBond.BondOrder)
                {
                    Debug.Log($"【詳細ログ】{atomA.ElementType} と {atomB.ElementType} は繋がっていますが、結合数が違います。(プレイヤー:{playerBondOrder} / お題:{reqBond.BondOrder})");
                    return false;
                }
            }
        }
        return true; // 今のところすべての結合ルールを満たしている
    }

    private void OnPuzzleCleared(List<Atom> moleculeAtoms)
    {
        _isPuzzleCleared = true;

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

        // UIボードにクリア演出を表示させる
        if (TargetBoardUI.Instance != null)
        {
            TargetBoardUI.Instance.ShowClearVisual();
        }

        // (以前の計画通り、ここにベンゼン環のモデル置換やパーティクル生成を後ほど実装します)
    }

    public void AdvanceToNextPuzzle()
    {
        CleanupAllAtoms();
        LoadPuzzle(_currentPuzzleIndex + 1);
    }

    private void CleanupAllAtoms()
    {
        // 消えていく原子どうしが互いに分子を組み直そうとしないよう、削除の間だけ止めておく
        AtomInteraction.IsTearingDown = true;
        StartCoroutine(ResumeStructureUpdates());

        Atom[] allAtoms = FindObjectsOfType<Atom>();
        foreach (Atom atom in allAtoms)
        {
            if (atom != null && atom.gameObject != null)
            {
                Transform parent = atom.transform.parent;
                if (parent != null && parent.name.StartsWith("MoleculeGroup"))
                {
                    Destroy(parent.gameObject);
                }
                else
                {
                    Destroy(atom.gameObject);
                }
            }
        }
    }

    // Destroy はフレームの終わりにまとめて実行されるので、それを待ってから元に戻す
    private System.Collections.IEnumerator ResumeStructureUpdates()
    {
        yield return new WaitForEndOfFrame();
        AtomInteraction.IsTearingDown = false;
    }
}