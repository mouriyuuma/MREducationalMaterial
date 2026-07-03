using UnityEditor;
using UnityEngine;

public class EncyclopediaManager : MonoBehaviour
{
    public MoleculeDatabase database;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        foreach (MoleculeData molecule in database.molecules)
        {
            Debug.Log(molecule.MoleculeName);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
