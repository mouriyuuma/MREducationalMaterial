using UnityEngine;

// 責任：触れた不要な原子オブジェクトをシーンから削除する
public class TrashZone : MonoBehaviour
{
    private void OnTriggerStay(Collider other)
    {
        AtomInteraction interaction = other.GetComponentInParent<AtomInteraction>();

        if (interaction != null)
        {
            // 手で掴んでいる最中のものは消さない
            if (interaction.IsGrabbed) return;

            // 原子をシーンから削除する
            // ※削除時の後処理（結合の解除など）はAtomInteractionのOnDestroyが自動で行う
            Destroy(interaction.gameObject);
            
            Debug.Log("【ゴミ箱】原子を破棄しました。");
        }
    }
}