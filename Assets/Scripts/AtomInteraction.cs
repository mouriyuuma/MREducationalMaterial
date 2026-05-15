using UnityEngine;

// 責任：VRの掴む/離す操作と、物理的な結合(FixedJoint)の生成・破壊を担当する
[RequireComponent(typeof(Atom), typeof(Rigidbody))]
public class AtomInteraction : MonoBehaviour
{
    public bool IsGrabbed { get; private set; }

    private Atom _atom;
    private Rigidbody _rb;

    private void Awake()
    {
        _atom = GetComponent<Atom>();
        _rb = GetComponent<Rigidbody>();
    }

    // VRで掴まれた時 (Wrapperから呼ばれる)
    public void OnGrabbed()
    {
        IsGrabbed = true;
    }

    // VRで離された時 (Wrapperから呼ばれる)
    public void OnReleased()
    {
        IsGrabbed = false;

        // MR空間でのピタッと止まるブレーキ
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        // 結合できる相手を探して結合処理を実行（※以前のコードを微修正）
        TryConnect();
    }

    private void TryConnect()
    {
        // 自分が持っているすべての結合手をチェック
        foreach (BondPoint myPoint in _atom.BondPoints)
        {
            // 近くに相手（HoverTarget）がいて、かつお互いが未結合の場合
            if (myPoint.HoverTarget != null && !myPoint.IsConnected && !myPoint.HoverTarget.IsConnected)
            {
                // 結合処理を実行
                ExecuteConnection(myPoint, myPoint.HoverTarget);
                
                // 1回の「離す」動作で結合するのは1箇所までとする（不自然な多重結合防止）
                break; 
            }
        }
    }

    public void ExecuteConnection(BondPoint myBond, BondPoint targetBond)
    {
        Atom targetAtom = targetBond.ParentAtom;
        Rigidbody targetRb = targetAtom.GetComponent<Rigidbody>();

        // SDKの干渉を防ぐため物理演算をON
        _rb.isKinematic = false;
        _rb.useGravity = false;
        targetRb.isKinematic = false;
        targetRb.useGravity = false;

        SnapToTarget(myBond, targetBond);

        // お互いにJointを張る
        if (gameObject.GetComponent<FixedJoint>() == null)
        {
            FixedJoint joint1 = gameObject.AddComponent<FixedJoint>();
            joint1.connectedBody = targetRb;
            joint1.breakForce = Mathf.Infinity;
        }
        if (targetAtom.gameObject.GetComponent<FixedJoint>() == null)
        {
            FixedJoint joint2 = targetAtom.gameObject.AddComponent<FixedJoint>();
            joint2.connectedBody = _rb;
            joint2.breakForce = Mathf.Infinity;
        }

        Collider myCol = GetComponent<Collider>();
        Collider targetCol = targetAtom.GetComponent<Collider>();
        if (myCol && targetCol) Physics.IgnoreCollision(myCol, targetCol);

        // データレイヤーの状態を更新
        myBond.ConnectTo(targetBond);
        targetBond.ConnectTo(myBond);

        // 結合が完了したことをManagerに報告する
        if (MoleculeManager.Instance != null)
        {
            MoleculeManager.Instance.OnStructureChanged(this._atom);
        }
    }

    private void OnDestroy()
    {
        // ゴミ箱などで自身が削除される直前に呼ばれる
        if (_atom == null || _atom.BondPoints == null) return;

        foreach (BondPoint myPoint in _atom.BondPoints)
        {
            // もし誰かと繋がったまま捨てられたなら
            if (myPoint.IsConnected && myPoint.ConnectedTarget != null)
            {
                Atom neighborAtom = myPoint.ConnectedTarget.ParentAtom;

                // 相手の結合を解除し、自分の結合も解除する
                myPoint.ConnectedTarget.Disconnect();
                myPoint.Disconnect();

                // 残された相手の原子を起点に、分子構造がどう変わったか（分断されたか）を再計算させる
                if (MoleculeManager.Instance != null && neighborAtom != null)
                {
                    MoleculeManager.Instance.OnStructureChanged(neighborAtom);
                }
            }
        }
    }
    private void SnapToTarget(BondPoint myBond, BondPoint targetBond)
    {
        Quaternion rotationDiff = Quaternion.FromToRotation(myBond.transform.up, -targetBond.transform.up);
        transform.rotation = rotationDiff * transform.rotation;

        SphereCollider myCol = myBond.GetComponent<SphereCollider>();
        SphereCollider targetCol = targetBond.GetComponent<SphereCollider>();

        Vector3 myTip = myBond.transform.TransformPoint(myCol.center);
        Vector3 targetTip = targetBond.transform.TransformPoint(targetCol.center);

        Vector3 offset = transform.position - myTip;
        transform.position = targetTip + offset;
    }
}