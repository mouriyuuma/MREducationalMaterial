using UnityEngine;

public class BondPoint : MonoBehaviour
{
    [Header("Bond Status")]
    // public bool IsConnected = false;
    public Atom ParentAtom;
    public bool IsConnected { get; private set; }
    public BondPoint ConnectedTarget { get; private set; }
    
    // 今近づいている相手の腕（プレビュー用）
    public BondPoint HoverTarget; 

    void Start()
    {
        ParentAtom = GetComponentInParent<Atom>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsConnected) return; // すでに繋がっていれば無視

        BondPoint target = other.GetComponent<BondPoint>();
        
        // 相手がBondPointで、未結合で、自分自身の腕じゃなければ
        if (target != null && !target.IsConnected && target.ParentAtom != this.ParentAtom)
        {
            HoverTarget = target; // 相手を「結合候補」として記憶！
            Debug.Log($"【プレビュー】{ParentAtom.ElementType} が {target.ParentAtom.ElementType} に近づきました");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        BondPoint target = other.GetComponent<BondPoint>();
        
        // 記憶している相手から離れたら、候補をリセットする
        if (target != null && HoverTarget == target)
        {
            HoverTarget = null;
            Debug.Log("【プレビュー解除】離れました");
        }
    }

    // 結合を実行したときに呼ばれる
    public void ConnectTo(BondPoint target)
    {
        IsConnected = true;
        ConnectedTarget = target;
    }

    // 分離したときに呼ばれる
    public void Disconnect()
    {
        IsConnected = false;
        ConnectedTarget = null;
    }
}