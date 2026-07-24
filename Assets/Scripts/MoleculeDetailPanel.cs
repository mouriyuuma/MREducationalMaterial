using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Oculus.Platform;

public class MoleculeDetailPanel : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] private TMP_Text NameText;
    [SerializeField] private TMP_Text FormulaText;
    [SerializeField] private TMP_Text FunctionalGroupText; 
    [SerializeField] private TMP_Text FunctionalGroupFormulaText;
    [SerializeField] private TMP_Text CategoryText;
    [SerializeField] private TMP_Text DescriptionText;

    [Header("Image")]
    [SerializeField] private Image StructuralFormulaImage;

    public void Show(MoleculeData Data)
    {
        NameText.text = Data.MoleculeName;
        FormulaText.text = Data.Formula;
        FunctionalGroupText.text = Data.FunctionalGroup;
        FunctionalGroupFormulaText.text  = Data.FunctionalGroupFormula;
        CategoryText.text = Data.Categories.ToString();
        DescriptionText.text = Data.Description;

        StructuralFormulaImage.sprite = Data.StructuralFormula;

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}