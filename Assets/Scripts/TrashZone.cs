using UnityEngine;

public class TrashZone : MonoBehaviour
{
    private void OnTriggerStay(Collider other)
    {
        // 触れたものが「原子（Atom）」かどうか確認する
        Atom targetAtom = other.GetComponentInParent<Atom>();

        if (targetAtom != null)
        {
            // 手で掴んでいる最中のものは消さない（オプション：好みで変更可）
            if (targetAtom.IsGrabbed) return;

            // 原子をシーンから削除する
            // ※とりあえず今はシンプルにAtomのGameObjectを消します。
            // （FixedJointで繋がっている他の原子も、Jointが外れてバラバラになります）
            Destroy(targetAtom.gameObject);
            
            Debug.Log($"【ゴミ箱】{targetAtom.ElementType} を破棄しました。");
        }
    }
}