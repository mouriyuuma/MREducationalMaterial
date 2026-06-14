using UnityEngine;
using UnityEngine.UI; // 必要に応じて OVRかCanvasUIか

public class SceneNavButton : MonoBehaviour
{
    [Header("=== 遷移先の設定 ===")]
    [Tooltip("遷移先のゲーム状態を選択")]
    [SerializeField] private GameManager.GameState _targetState;

    [Tooltip("遷移先のシーン名（Build Settingsに登録したものと一言一句同じにしてください）")]
    [SerializeField] private string _targetSceneName;

    private void Start()
    {
        // 「UnityのButtonコンポーネント」を使っている場合、自動でクリックイベントを登録する
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OnXROnClick);
        }
    }

    // インスペクターから手動でイベントを割り当てたい場合や、
    // VRの特殊なコントローラー用ボタン（MRTKやOculusの物理押しボタンなど）から呼び出す用のメソッド
    public void OnXROnClick()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("[SceneNavButton] GameManagerが存在しません！最初のシーンから起動してください。");
            return;
        }

        // GameManagerを介してシーンと状態を切り替える
        GameManager.Instance.ChangeState(_targetState, _targetSceneName);
    }
}