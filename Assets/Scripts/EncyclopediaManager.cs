using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;

public class EncyclopediaManager : MonoBehaviour
{
    [Header("Database")]
    [SerializeField] private MoleculeDatabase Database; // 参照するデータベース

    [Header("UI")]
    [SerializeField] private Transform ContentParent; // 生成したボタン置き場
    [SerializeField] private GameObject ButtonPrefab; // ボタンの種類(Prefab)

    //[SerializeField] private TMP_FontAsset japaneseFont;
    [SerializeField] private TMP_FontAsset JapaneseFont;

    [Header("display")]
    [SerializeField] private MoleculeDetailPanel DetailPanel;

    private void Start()
    {
        CreateButtons();
    }

    private void CreateButtons()
    {
        foreach (MoleculeData Molecule in Database.molecules) // databaseにmoleculeが存在する間
        {
            GameObject ButtonObject =
                Instantiate(ButtonPrefab, ContentParent); // ボタンの種類(Prefab)のコピー

            TMP_Text text =
                ButtonObject.GetComponentInChildren<TMP_Text>(); //ボタンが持つはずの文字情報の参照

            text.font = JapaneseFont;
            text.text = Molecule.MoleculeName; // 分子の名前を参照してテキストを生成

            Button Button = ButtonObject.GetComponent<Button>();

            //text.font = japaneseFont;
            text.text = molecule.MoleculeName; // 分子の名前を参照してテキストを生成
            Button.onClick.AddListener(() =>
            {
                DetailPanel.Show(Molecule);
            });
        }
    }
}