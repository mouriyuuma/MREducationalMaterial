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

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero; // 移動の勢いを殺す
            rb.angularVelocity = Vector3.zero; // 回転の勢いを殺す
        }
    }

    // 実際に結合する処理
    // 引数を元に戻し、OnReleasedから呼び出せるようにする
    public void ExecuteConnection(BondPoint myBond, BondPoint targetBond)
    {
        Atom targetAtom = targetBond.GetComponentInParent<Atom>();

        // 1. スナップ処理
        SnapToTarget(myBond, targetBond);

        // 2. 物理的な結合
        Rigidbody myRb = GetComponent<Rigidbody>();
        Rigidbody targetRb = targetAtom.GetComponent<Rigidbody>();

        // 【修正箇所 1】強制的に物理演算をONにする（両方とも false にする！）
        myRb.isKinematic = false;
        myRb.useGravity = false;
        targetRb.isKinematic = false; // ← これがないと相手が空中に固定されたままになります
        targetRb.useGravity = false;

        // 【修正箇所 2】自分の原子から相手へ Joint を張る
        if (gameObject.GetComponent<FixedJoint>() == null)
        {
            FixedJoint joint1 = gameObject.AddComponent<FixedJoint>();
            joint1.connectedBody = targetRb;
            joint1.breakForce = Mathf.Infinity;
            joint1.breakTorque = Mathf.Infinity;
        }

        // 【修正箇所 3】相手の原子から自分へ Joint を張る（両方への付与）
        if (targetAtom.gameObject.GetComponent<FixedJoint>() == null)
        {
            FixedJoint joint2 = targetAtom.gameObject.AddComponent<FixedJoint>();
            joint2.connectedBody = myRb;
            joint2.breakForce = Mathf.Infinity;
            joint2.breakTorque = Mathf.Infinity;
        }

        // お互いのコライダーの衝突を無視する（食い込みによる反発を防ぐ）
        Collider myCol = GetComponent<Collider>();
        Collider targetCol = targetAtom.GetComponent<Collider>();
        if (myCol && targetCol) Physics.IgnoreCollision(myCol, targetCol);

        // 3. 状態の更新（お互いを記憶）
        myBond.ConnectTo(targetBond);
        targetBond.ConnectTo(myBond);

        // 4. Molecule（分子データ）の統合処理
        Molecule myMolecule = GetComponentInParent<Molecule>();
        Molecule targetMolecule = targetAtom.GetComponentInParent<Molecule>();

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