using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Oculus.Platform;
using System.Collections.Generic;

public class MoleculeDetailPanel : MonoBehaviour
{
    private readonly Dictionary<ChemicalCategory, string> categoryNames =
        new Dictionary<ChemicalCategory, string>()
    {
        { ChemicalCategory.Aliphatic,      "脂肪族" },
        { ChemicalCategory.Aromatic,       "芳香族" },
        { ChemicalCategory.Alcohol,        "アルコール" },
        { ChemicalCategory.Phenol,         "フェノール類" },
        { ChemicalCategory.Aldehyde,       "アルデヒド" },
        { ChemicalCategory.Ketone,         "ケトン" },
        { ChemicalCategory.CarboxylicAcid, "カルボン酸" },
        { ChemicalCategory.Ester,          "エステル" },
        { ChemicalCategory.NitroCompound,  "ニトロ化合物" },
        { ChemicalCategory.Amine,          "アミン・アミド" }
    };


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
        CategoryText.text = CategoryToJapanese(Data.Categories);
        DescriptionText.text = Data.Description;

        StructuralFormulaImage.sprite = Data.StructuralFormula;

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private string CategoryToJapanese(ChemicalCategory categories)
    {
        List<string> result = new List<string>();
        foreach (var pair in categoryNames)
        {
            if (categories.HasFlag(pair.Key))
            {
                result.Add(pair.Value);
            }
        }

        return string.Join("\n",result);
    }
}