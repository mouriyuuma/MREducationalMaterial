using System.Collections.Generic;
using UnityEngine;

// 右クリックメニューから簡単にこのデータを作れるようにする属性
[CreateAssetMenu(fileName = "NewMolecule", menuName = "Chemistry Puzzle/Molecule Data")]
public class MoleculeData : ScriptableObject
{
    [Header("Basic Info")]
    public string MoleculeName; // 分子の名前（例：水）
    public string Formula;      // 化学式（例：H2O）

    [Header("Required Atoms (ノード)")]
    // どの原子が、何個必要か
    public List<ElementCount> RequiredAtoms;

    [Header("Required Bonds (エッジ)")]
    // どの原子とどの原子が繋がっている必要があるか（グラフの辺）
    public List<BondRequirement> RequiredBonds;
}

// Inspectorで設定できるようにするシリアライズ属性
[System.Serializable]
public class ElementCount
{
    public string ElementType; // "C", "H", "O" など
    public int Count;          // 必要な数
}

[System.Serializable]
public class BondRequirement
{
    [Tooltip("結合する原子その1 (例: O)")]
    public string ElementA;
    [Tooltip("結合する原子その2 (例: H)")]
    public string ElementB;
    
    [Tooltip("何重結合か (今は単結合のみなので1で固定)")]
    public int BondOrder = 1; 
}