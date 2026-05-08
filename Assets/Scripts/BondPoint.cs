using UnityEngine;

// 責任：結合手の現在の状態（誰と繋がっているか）を管理するだけ
public class BondPoint : MonoBehaviour
{
    public bool IsConnected { get; private set; }
    public BondPoint ConnectedTarget { get; private set; }
    
    // 自分が属している原子
    public Atom ParentAtom { get; private set; }

    // Atom.csから呼ばれる初期化
    public void Initialize(Atom parent)
    {
        ParentAtom = parent;
    }

    public void ConnectTo(BondPoint target)
    {
        IsConnected = true;
        ConnectedTarget = target;
    }

    public void Disconnect()
    {
        IsConnected = false;
        ConnectedTarget = null;
    }
}