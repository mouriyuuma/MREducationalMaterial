using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EncyclopediaManager : MonoBehaviour
{
    [Header("Database")]
    [SerializeField] private MoleculeDatabase database;

    [Header("UI")]
    [SerializeField] private Transform contentParent;
    [SerializeField] private GameObject buttonPrefab;

    private void Start()
    {
        CreateButtons();
    }

    private void CreateButtons()
    {
        foreach (MoleculeData molecule in database.molecules)
        {
            GameObject buttonObject =
                Instantiate(buttonPrefab, contentParent);

            TMP_Text text =
                buttonObject.GetComponentInChildren<TMP_Text>();

            text.text = molecule.MoleculeName;
        }
    }
}