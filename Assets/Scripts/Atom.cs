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
        private void ExecuteConnection(BondPoint myPoint, BondPoint targetPoint)
        {
            // 1. 向きの調整（お互いの腕が「向かい合わせ」になるように親の原子を回転させる）
            // ※UnityのCylinderはY軸(up)方向に伸びるため、upベクトルを基準に計算します
            Quaternion targetRotation = Quaternion.FromToRotation(myPoint.transform.up, -targetPoint.transform.up);
            transform.rotation = targetRotation * transform.rotation;

            // 2. 位置の調整（回転させた後、自分の腕の先端と相手の腕の先端の「ズレ」を計算して移動する）
            Vector3 offset = targetPoint.transform.position - myPoint.transform.position;
            transform.position += offset; // ズレの分だけ親（Atom全体）を移動させる

            // 3. お互いの腕を「結合済み」状態にする
            myPoint.Connect(targetPoint);
            targetPoint.Connect(myPoint);

            // 4. 物理的に固定する（FixedJointの生成）
            FixedJoint joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = targetPoint.ParentAtom.GetComponent<Rigidbody>();
            joint.breakForce = 500f; 

            Debug.Log($"【ダンベル結合成功！】{ElementType} と {targetPoint.ParentAtom.ElementType} が球棒モデルでくっつきました！");
        }
}