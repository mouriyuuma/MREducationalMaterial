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
        // TODO: 次のステップで、ここにお題の「RequiredBonds」と照らし合わせる
        // 本格的なグラフ同型性判定（Graph Isomorphism）や、ベンゼン環の判定ロジックを実装します。
        // 現在は「原子の数が合っていればとりあえずヨシ」としています。
        return true; 
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