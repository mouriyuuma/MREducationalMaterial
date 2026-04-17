using UnityEngine;

public class Atom : MonoBehaviour
{
    [Header("Atom Data")]
    public string ElementType = "C";
    public Molecule ParentMolecule;
    public bool IsGrabbed = false;

    public BondPoint[] BondPoints;

    void Start()
    {
        BondPoints = GetComponentsInChildren<BondPoint>();
    }

    public void OnGrabbed()
    {
        IsGrabbed = true;
    }

    // Meta XR SDKから「手を離した時」に呼ばれるメソッド
    public void OnReleased()
    {
        IsGrabbed = false;

        // 自分のすべての腕（BondPoint）をチェックする
        foreach (BondPoint myPoint in BondPoints)
        {
            // もし結合候補（HoverTarget）がいて、まだ繋がっていなければ
            if (myPoint.HoverTarget != null && !myPoint.IsConnected)
            {
                // 結合処理を実行！
                ExecuteConnection(myPoint, myPoint.HoverTarget);
                break; // 1回手を離して繋がるのは1箇所だけ
            }
        }
    }

    // 実際に結合する処理
    // 引数を元に戻し、OnReleasedから呼び出せるようにする
    public void ExecuteConnection(BondPoint myBond, BondPoint targetBond)
    {
        // 引数から消したtargetAtomは、ここでBondPointの親から取得する
        Atom targetAtom = targetBond.GetComponentInParent<Atom>();

        // 1. スナップ処理
        SnapToTarget(myBond, targetBond);

        // 2. 物理的な結合
        Rigidbody myRb = GetComponent<Rigidbody>();
        Rigidbody targetRb = targetAtom.GetComponent<Rigidbody>();

        myRb.linearVelocity = Vector3.zero;
        myRb.angularVelocity = Vector3.zero;

        if (gameObject.GetComponent<FixedJoint>() == null)
        {
            FixedJoint joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = targetRb;
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
        }

        // 3. 状態の更新（お互いを記憶）
        myBond.ConnectTo(targetBond);
        targetBond.ConnectTo(myBond);

        // 4. 【復活】Molecule（分子データ）の統合処理
        Molecule myMolecule = GetComponentInParent<Molecule>();
        Molecule targetMolecule = targetAtom.GetComponentInParent<Molecule>();
        
        // お互いがMoleculeを持っていて、かつまだ同じ分子ではない場合
        if (myMolecule != null && targetMolecule != null && myMolecule != targetMolecule)
        {
            myMolecule.MergeWith(targetMolecule);
            Debug.Log("分子が統合されました！");
        }
    }

    private void SnapToTarget(BondPoint myBond, BondPoint targetBond)
    {
        // ① 回転の計算：お互いのY軸が逆を向くようにする
        Quaternion rotationDiff = Quaternion.FromToRotation(myBond.transform.up, -targetBond.transform.up);
        transform.rotation = rotationDiff * transform.rotation;

        // ② 位置の計算：Transformの位置ではなく、Colliderの「ズラした中心位置」を取得する
        SphereCollider myCollider = myBond.GetComponent<SphereCollider>();
        SphereCollider targetCollider = targetBond.GetComponent<SphereCollider>();

        // TransformPointを使って、ローカルのズレ(Y=1.0など)をワールド座標に変換（ここが結合手の真の先端）
        Vector3 myTipWorldPos = myBond.transform.TransformPoint(myCollider.center);
        Vector3 targetTipWorldPos = targetBond.transform.TransformPoint(targetCollider.center);

        // 自分の中心から先端までのオフセット
        Vector3 offset = transform.position - myTipWorldPos;
        
        // 相手の先端位置 ＋ オフセットの位置に移動
        transform.position = targetTipWorldPos + offset;
    }
}