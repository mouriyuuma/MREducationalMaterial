using System.Collections.Generic;
using UnityEngine;

// --- 1. 有機化学の分類定義 (高校化学の主要カリキュラムを網羅) ---
[System.Flags]
public enum ChemicalCategory
{
    None = 0,
    Aliphatic      = 1 << 0,  // 脂肪族
    Aromatic       = 1 << 1,  // 芳香族
    Alcohol        = 1 << 2,  // アルコール
    Phenol         = 1 << 3,  // フェノール類
    Aldehyde       = 1 << 4,  // アルデヒド
    Ketone         = 1 << 5,  // ケトン
    CarboxylicAcid = 1 << 6,  // カルボン酸
    Ester          = 1 << 7,  // エステル
    NitroCompound  = 1 << 8,  // ニトロ化合物
    Amine          = 1 << 9,  // アミン / アミド
}

// --- 2. パズル用：原子ノードの要求データ（以前定義したもの） ---
[System.Serializable]
public class AtomNodeRequirement
{
    public int AtomId;        // 0, 1, 2... と割り振るID
    public string ElementType; // "C", "H", "O" など
}

// --- 3. 分子データ本体 ---
[CreateAssetMenu(fileName = "NewMoleculeData", menuName = "ChemistryPuzzle/MoleculeData")]
public class MoleculeData : ScriptableObject
{
    [Header("=== 図鑑用：基本情報 ===")]
    [Tooltip("分子の一般的な名称（例：酢酸、ベンゼン）")]
    public string MoleculeName;

    [Tooltip("化学式（例：CH3COOH、C6H6）")]
    public string Formula;

    [Tooltip("図鑑に表示する2Dの構造式画像")]
    public Sprite StructuralFormula;

    [Tooltip("官能基の名前(例：ヒドロキシ基、カルボキシ基)")]
    public string FunctionalGroup;

    [Tooltip("官能基の化学式（例：-OH、-COOH）")]
    public string FunctionalGroupFormula;

    [TextArea(4, 10)]
    [Tooltip("分子の特徴や高校化学での重要ポイントなどの説明文")]
    public string Description;

    [Header("=== 図鑑用：カテゴリ分類 ===")]
    [Tooltip("複数選択可能です（例：サリチル酸なら Aromatic, Phenol, CarboxylicAcid を選択）")]
    public ChemicalCategory Categories;

    [Header("=== 図鑑用：3D表示プレハブ ===")]
    [Tooltip("図鑑画面でグルグル回して見せるための、完成した分子の3Dモデル（Prefab）")]
    public GameObject EncyclopediaModelPrefab;

    [Header("=== パズル用：判定データ ===")]
    [Tooltip("この分子を構成する原子ノードのリスト")]
    public List<AtomNodeRequirement> RequiredAtoms;
    
    [Tooltip("どの原子の、何番目の結合手（腕）同士が繋がっているべきかのリスト")]
    public List<BondEdgeRequirement> RequiredBonds;
}


[System.Serializable]
public class BondEdgeRequirement
{
    [Header("Atom A (接続元)")]
    public int AtomIdA;        // 原子AのID
    public int BondIndexA;     // 原子Aの何番目の結合手（BondPoint）か

    [Header("Atom B (接続先)")]
    public int AtomIdB;        // 原子BのID
    public int BondIndexB;     // 原子Bの何番目の結合手（BondPoint）か

    [Header("Bond Properties")]
    [Tooltip("何重結合か (1=単結合, 2=二重結合, 3=三重結合)")]
    [Range(1, 3)] // Inspectorでスライダー表示にしておくと便利
    public int BondOrder = 1;
}