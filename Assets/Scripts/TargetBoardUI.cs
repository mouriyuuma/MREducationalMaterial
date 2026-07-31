using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshProを使うために必要

public class TargetBoardUI : MonoBehaviour
{
    public static TargetBoardUI Instance { get; private set; }

    [Header("Text References")]
    [SerializeField] private TextMeshProUGUI _moleculeNameText; // 分子名（例：水）
    [SerializeField] private TextMeshProUGUI _formulaText;      // 化学式（例：H₂O）
    [SerializeField] private TextMeshProUGUI _progressText;     // 進捗（例：1 / 5）

    [Header("Image References")]
    [SerializeField] private Image _structureImage;             // 構造式の画像

    [Header("Clear Animation")]
    [SerializeField] private GameObject _clearTextObject; // 「CLEAR!」の3Dテキストや画像
    [SerializeField] private GameObject _nextButtonObject; // 「次へ」ボタン

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // 最初はクリア表示と次へボタンを隠しておく
        if (_clearTextObject != null)
        {
            _clearTextObject.SetActive(false);
        }
        if (_nextButtonObject != null)
        {
            _nextButtonObject.SetActive(false);
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
        if (_nextButtonObject != null) _nextButtonObject.SetActive(false);

        if (PuzzleManager.Instance == null || PuzzleManager.Instance.CurrentTargetData == null)
        {
            _moleculeNameText.text = "No Target";
            _formulaText.text = "-";
            if (_progressText != null) _progressText.text = "";
            if (_structureImage != null) _structureImage.sprite = null;
            return;
        }

        // PuzzleManagerから現在のお題データを取得してUIに反映
        MoleculeData data = PuzzleManager.Instance.CurrentTargetData;
        _moleculeNameText.text = data.MoleculeName;
        _formulaText.text = data.Formula;

        if (_progressText != null)
        {
            _progressText.text = $"問題 {PuzzleManager.Instance.CurrentPuzzleIndex + 1} / {PuzzleManager.Instance.TotalPuzzles}";
        }

        if (_structureImage != null)
        {
            _structureImage.sprite = data.StructuralFormula;
        }
    }

    public void ShowClearVisual()
    {
        if (_clearTextObject != null)
        {
            _clearTextObject.SetActive(true);
        }
        if (_nextButtonObject != null)
        {
            _nextButtonObject.SetActive(true);
        }
        
        Debug.Log("【UI演出】画面にお題クリアを表示しました！");
    }

    public void ShowCompleteVisual()
    {
        _moleculeNameText.text = "ALL CLEARED!";
        _formulaText.text = "おめでとうございます！";
        if (_progressText != null) _progressText.text = "";
        if (_structureImage != null) _structureImage.sprite = null;

        if (_clearTextObject != null) _clearTextObject.SetActive(false);
        if (_nextButtonObject != null) _nextButtonObject.SetActive(false);
    }

    public void OnNextButtonClicked()
    {
        if (PuzzleManager.Instance != null)
        {
            PuzzleManager.Instance.AdvanceToNextPuzzle();
        }
    }
}