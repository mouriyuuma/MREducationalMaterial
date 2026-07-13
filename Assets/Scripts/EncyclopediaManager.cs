using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EncyclopediaManager : MonoBehaviour
{
    [Header("Database")]
    [SerializeField] private MoleculeDatabase database; // 参照するデータベース

    [Header("UI")]
    [SerializeField] private Transform contentParent; // 生成したボタン置き場
    [SerializeField] private GameObject buttonPrefab; // ボタンの種類(Prefab)

    //[SerializeField] private TMP_FontAsset japaneseFont;

    private void Start()
    {
        CreateButtons();
    }

    private void CreateButtons()
    {
        foreach (MoleculeData molecule in database.molecules) // databaseにmoleculeが存在する間
        {
            GameObject buttonObject =
                Instantiate(buttonPrefab, contentParent); // ボタンの種類(Prefab)のコピー

            TMP_Text text =
                buttonObject.GetComponentInChildren<TMP_Text>(); //ボタンが持つはずの文字情報の参照

            //text.font = japaneseFont;
            text.text = molecule.MoleculeName; // 分子の名前を参照してテキストを生成
        }
    }
}