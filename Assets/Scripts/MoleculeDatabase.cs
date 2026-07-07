using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ChemistryPuzzle/Molecule Database")]
public class MoleculeDatabase : ScriptableObject
{
    public List<MoleculeData> molecules = new List<MoleculeData>();
}