using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMolecule", menuName = "Chemistry Puzzle/Molecule Data")]
public class MoleculeData : ScriptableObject
{
    [Header("Basic Info")]
    public string MoleculeName; // 分子の名前（例：シス-2-ブテン）
    public string Formula;      // 化学式（例：C4H8）

    [Header("Required Atoms (ノード定義)")]
    [Tooltip("この分子を構成する原子ノードのリスト。インデックスがそのままAtomIDになります")]
    public List<AtomNodeRequirement> RequiredAtoms;

    [Header("Required Bonds (エッジ・結合手定義)")]
    [Tooltip("どの原子の、何番目の結合手（腕）同士が繋がっているべきかのリスト")]
    public List<BondEdgeRequirement> RequiredBonds;
}

[System.Serializable]
public class AtomNodeRequirement
{
    public int AtomId;          // 固有ID（0, 1, 2...）
    public string ElementType;  // "C", "H", "O" など
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
    [Range(1, 3)] // Inspectorでスライダー表示にしておくと便利です
    public int BondOrder = 1;
}