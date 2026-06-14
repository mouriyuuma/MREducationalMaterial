using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    // どこからでも GameManager.Instance でアクセスできるようにする（シングルトン）
    public static GameManager Instance { get; private set; }

    // === ゲーム状態の定義 ===
    public enum GameState
    {
        StartMenu,
        Encyclopedia,
        Puzzle
    }

    [Header("=== 現在の状態 ===")]
    public GameState CurrentState { get; private set; } = GameState.StartMenu;

    [Header("=== シーン間で引き継ぐデータ ===")]
    [Tooltip("図鑑やパズルで現在プレイヤーが選択している分子のデータ")]
    public MoleculeData CurrentSelectedMolecule;

    private void Awake()
    {
        // シングルトンの厳密な管理（重複生成の防止）
        if (Instance == null)
        {
            Instance = this;
            // シーンが切り替わっても、このGameManagerオブジェクトを破壊しない
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // 既に他のシーンでGameManagerが作られていたら、新しく作られた方は削除する
            Destroy(gameObject);
        }
    }

    /// ゲームの状態を変更し、必要に応じてシーンを切り替えるメソッド
    /// newState: 遷移先の状態
    /// sceneName: 読み込むシーンの名前（空欄ならシーン遷移はしない）
    public void ChangeState(GameState newState, string sceneName = "")
    {
        CurrentState = newState;
        Debug.Log($"[GameManager] 状態が変更されました: {CurrentState}");

        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}