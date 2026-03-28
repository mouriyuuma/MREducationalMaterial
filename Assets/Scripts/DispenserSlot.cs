using UnityEngine;

public class DispenserSlot : MonoBehaviour
{
    [Header("Dispenser Settings")]
    [Tooltip("出現させる原子のプレハブ")]
    public GameObject AtomPrefab; 
    
    [Tooltip("原子が出現する中心位置")]
    public Transform SpawnPoint;

    void Start()
    {
        // 起動時に、最初の1個目の見本（プレビュー）を生成する
        SpawnNewAtom();
    }

    // 箱のセンサー（Trigger）から何かが外に出た瞬間に呼ばれる
    private void OnTriggerExit(Collider other)
    {
        // 出ていったオブジェクトが「Atom」を持っているか確認
        Atom pulledAtom = other.GetComponentInParent<Atom>();

        // もし出ていったのが原子で、かつ「今まさに手で掴まれている状態」なら
        // if (pulledAtom != null && pulledAtom.IsGrabbed)
        if (pulledAtom != null)
        {
            Debug.Log("【ディスペンサー】原子が引き抜かれました！新しい原子を補充します。");
            SpawnNewAtom();
        }
    }

    // 新しい原子を指定位置に生成するメソッド
    private void SpawnNewAtom()
    {
        if (AtomPrefab != null && SpawnPoint != null)
        {
            Instantiate(AtomPrefab, SpawnPoint.position, SpawnPoint.rotation);
        }
    }
}