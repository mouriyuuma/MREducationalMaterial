using UnityEngine;
using TMPro; // TextMeshProを使うために必要

public class TargetBoardUI : MonoBehaviour
{
    public static TargetBoardUI Instance { get; private set; }

    [Header("Text References")]
    [SerializeField] private TextMeshProUGUI _moleculeNameText; // 分子名（例：水）
    [SerializeField] private TextMeshProUGUI _formulaText;      // 化学式（例：H₂O）

    [Header("Clear Animation")]
    [SerializeField] private GameObject _clearTextObject; // 「CLEAR!」の3Dテキストや画像

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // 最初はクリア表示を隠しておく
        if (_clearTextObject != null)
        {
            _clearTextObject.SetActive(false);
        }
    }

    private void Start()
    {
        // ゲーム開始時に現在のお題を表示する
        RefreshDisplay();
    }

    // お題の表示を最新データに更新するメソッド
    public void RefreshDisplay()
    {
        if (_clearTextObject != null) _clearTextObject.SetActive(false);

        if (PuzzleManager.Instance == null || PuzzleManager.Instance.CurrentTargetData == null)
        {
            _moleculeNameText.text = "No Target";
            _formulaText.text = "-";
            return;
        }

        // PuzzleManagerから現在のお題データを取得してUIに反映
        MoleculeData data = PuzzleManager.Instance.CurrentTargetData;
        _moleculeNameText.text = data.MoleculeName;
        _formulaText.text = data.Formula;
    }

    // パズルをクリアしたときに呼び出される演出メソッド
    public void ShowClearVisual()
    {
        if (_clearTextObject != null)
        {
            _clearTextObject.SetActive(true);
            
            // (オプション) ここでちょっとしたアニメーション（Popアップなど）を
            // iTweenやDOTween、あるいはシンプルなiEnumeratorで入れるとさらに良くなります
        }
        
        Debug.Log("【UI演出】画面にお題クリアを表示しました！");
    }
}