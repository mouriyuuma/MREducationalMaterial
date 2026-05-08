using UnityEngine;

// 責任：箱から原子が引き抜かれたことを検知して補充する
public class DispenserSlot : MonoBehaviour
{
    public GameObject AtomPrefab; 
    public Transform SpawnPoint;

    void Start()
    {
        SpawnNewAtom();
    }

    private void OnTriggerExit(Collider other)
    {
        // データ(Atom)ではなく、操作状態(AtomInteraction)を取得する
        AtomInteraction interaction = other.GetComponentInParent<AtomInteraction>();

        // 手で掴まれている状態のものが外に出たら補充
        if (interaction != null && interaction.IsGrabbed)
        {
            Debug.Log("【ディスペンサー】新しい原子を補充します。");
            SpawnNewAtom();
        }
    }

    private void SpawnNewAtom()
    {
        if (AtomPrefab != null && SpawnPoint != null)
        {
            Instantiate(AtomPrefab, SpawnPoint.position, SpawnPoint.rotation);
        }
    }
}