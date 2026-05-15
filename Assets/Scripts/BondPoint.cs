using UnityEngine;

// 責任：結合手の現在の状態（誰と繋がっているか）を管理するだけ
public class BondPoint : MonoBehaviour
{
    public bool IsConnected { get; private set; }
    public BondPoint ConnectedTarget { get; private set; }

    // 現在重なっている（結合候補の）相手
    public BondPoint HoverTarget { get; private set; }

    // 自分が属している原子
    public Atom ParentAtom { get; private set; }

    // Atom.csから呼ばれる初期化
    public void Initialize(Atom parent)
    {
        ParentAtom = parent;
    }

    private void OnTriggerEnter(Collider other)
        {
            // 相手がBondPointコンポーネントを持っているか確認
            BondPoint target = other.GetComponent<BondPoint>();
            
            // 相手が存在し、自分自身ではなく、かつ別の原子に属している場合のみターゲットとする
            if (target != null && target != this && target.ParentAtom != this.ParentAtom)
            {
                HoverTarget = target;
            }
        }

    private void OnTriggerExit(Collider other)
    {
        BondPoint target = other.GetComponent<BondPoint>();
        if (target == HoverTarget)
        {
            HoverTarget = null;
        }
    }

    public void ConnectTo(BondPoint target)
    {
        IsConnected = true;
        ConnectedTarget = target;
        HoverTarget = null; // 結合したのでターゲットからは外す
    }

    public void Disconnect()
    {
        IsConnected = false;
        ConnectedTarget = null;
    }
}