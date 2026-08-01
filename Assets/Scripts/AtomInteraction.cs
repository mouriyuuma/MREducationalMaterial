using System.Collections.Generic;
using UnityEngine;

// 責任：VRの掴む/離す操作と、物理的な結合(FixedJoint)の生成・破壊を担当する
[RequireComponent(typeof(Atom), typeof(Rigidbody))]
public class AtomInteraction : MonoBehaviour
{
    public bool IsGrabbed { get; private set; }

    private Atom _atom;
    private Rigidbody _rb;

    // プレビュー用の線を描画するコンポーネント
    private LineRenderer _previewLine;

    // 掴まれた瞬間のローカル姿勢（分子全体の同期用）
    private Vector3 _grabLocalPos;
    private Quaternion _grabLocalRot;

    private void Awake()
    {
        _atom = GetComponent<Atom>();
        _rb = GetComponent<Rigidbody>();

        // プレビュー用のLineRendererをプログラムから動的に追加して設定する
        _previewLine = gameObject.AddComponent<LineRenderer>();
        _previewLine.startWidth = 0.015f; // 線の太さ
        _previewLine.endWidth = 0.015f;
        _previewLine.material = new Material(Shader.Find("Sprites/Default")); // シンプルな発光マテリアル
        _previewLine.startColor = Color.green; // 緑色の線
        _previewLine.endColor = Color.cyan;    // 先端は少し水色っぽく
        _previewLine.enabled = false;          // 最初は非表示
    }

    // VRで掴まれた時 (Wrapperから呼ばれる)
    public void OnGrabbed()
    {
        IsGrabbed = true;

        // 親（MoleculeGroup）がいる場合、掴んだ時点のローカル位置関係を記憶
        if (transform.parent != null)
        {
            _grabLocalPos = transform.localPosition;
            _grabLocalRot = transform.localRotation;
        }
    }

    // VRで離された時 (Wrapperから呼ばれる)
    public void OnReleased()
    {
        IsGrabbed = false;

        // MR空間でのピタッと止まるブレーキ
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        // 結合できる相手を探して結合処理を実行
        TryConnect();
    }

    private void TryConnect()
    {
        BondPoint bestMyBond = null;
        BondPoint bestTargetBond = null;
        float minDistance = float.MaxValue;

        foreach (BondPoint myBond in _atom.BondPoints)
        {
            BondPoint target = myBond.GetBestHoverTarget();
            if (target != null)
            {
                float dist = Vector3.Distance(myBond.transform.position, target.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestMyBond = myBond;
                    bestTargetBond = target;
                }
            }
        }

        if (bestMyBond != null && bestTargetBond != null)
        {
            ExecuteConnection(bestMyBond, bestTargetBond);
        }
    }

    public void ExecuteConnection(BondPoint myBond, BondPoint targetBond)
    {
        Atom targetAtom = targetBond.ParentAtom;
        Rigidbody targetRb = targetAtom.GetComponent<Rigidbody>();

        // SDKの干渉を防ぐため物理演算を設定
        _rb.isKinematic = false;
        _rb.useGravity = false;
        targetRb.isKinematic = false;
        targetRb.useGravity = false;

        SnapToTarget(myBond, targetBond);

        // お互いにJointを張る
        // 特定の相手に対するJointがあるかチェックして張る
        if (!HasJointTo(targetAtom.gameObject))
        {
            FixedJoint joint1 = gameObject.AddComponent<FixedJoint>();
            joint1.connectedBody = targetRb;
            joint1.breakForce = Mathf.Infinity;
        }

        AtomInteraction targetInteraction = targetAtom.GetComponent<AtomInteraction>();
        if (targetInteraction != null && !targetInteraction.HasJointTo(this.gameObject))
        {
            FixedJoint joint2 = targetAtom.gameObject.AddComponent<FixedJoint>();
            joint2.connectedBody = _rb;
            joint2.breakForce = Mathf.Infinity;
        }

        // BondPoint含む「すべての子コライダー」同士の衝突を無視する
        Collider[] myCols = GetComponentsInChildren<Collider>();
        Collider[] targetCols = targetAtom.GetComponentsInChildren<Collider>();
        foreach (var c1 in myCols)
        {
            foreach (var c2 in targetCols)
            {
                Physics.IgnoreCollision(c1, c2, true);
            }
        }

        // データレイヤーの状態を更新
        myBond.ConnectTo(targetBond, 1);
        targetBond.ConnectTo(myBond, 1);

        // ★現象2の解決：分子全体のグループ化（親子構造の構築・統合）
        UpdateMoleculeGrouping(this._atom);

        // 結合が完了したことをManagerに報告する
        if (MoleculeManager.Instance != null)
        {
            MoleculeManager.Instance.OnStructureChanged(this._atom);
        }
    }

    private void Update()
    {
        // 自分が掴まれている間だけ、繋がっている原子との距離を測る
        if (IsGrabbed)
        {
            CheckBondDistances();
            UpdateConnectionPreview(); // 掴んでいる間はプレビュー線を更新
        }
        else
        {
            _previewLine.enabled = false; // 離したら線を消す
        }
    }

    // ★現象2の解決：掴まれた原子の動きに合わせて、分子全体（親グループ）を動かす
    private void LateUpdate()
    {
        // 「片手持ち」のときだけ親（MoleculeGroup）を追従させる。
        // 両手で分子内の2つを掴んでいるときは追従をスキップし、手の引きちぎり動作（CheckBondDistances）に委ねる！
        if (IsGrabbed && transform.parent != null && !IsAnyOtherAtomInGroupGrabbed())
        {
            // VR SDKによって動かされたこの原子の目標ワールド座標・回転を取得
            Vector3 targetWorldPos = transform.position;
            Quaternion targetWorldRot = transform.rotation;

            // 原子自体のローカル変形を防ぐため、元のローカル姿勢に固定
            transform.localPosition = _grabLocalPos;
            transform.localRotation = _grabLocalRot;

            // この原子が目標位置・回転にピッタリ合うように、親（MoleculeGroup）側を移動・回転させる
            Quaternion deltaRot = targetWorldRot * Quaternion.Inverse(transform.rotation);
            Vector3 localOffset = transform.position - transform.parent.position;

            transform.parent.rotation = deltaRot * transform.parent.rotation;
            transform.parent.position = targetWorldPos - (deltaRot * localOffset);
        }
    }

    // 結合プレビューの線を引くメソッド
    private void UpdateConnectionPreview()
    {
        BondPoint bestMyBond = null;
        BondPoint bestTargetBond = null;
        float minDistance = float.MaxValue;

        foreach (BondPoint myBond in _atom.BondPoints)
        {
            BondPoint target = myBond.GetBestHoverTarget();
            if (target != null)
            {
                float dist = Vector3.Distance(myBond.transform.position, target.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestMyBond = myBond;
                    bestTargetBond = target;
                }
            }
        }

        if (bestMyBond != null && bestTargetBond != null)
        {
            _previewLine.enabled = true;
            _previewLine.SetPosition(0, bestMyBond.transform.position);
            _previewLine.SetPosition(1, bestTargetBond.transform.position);
        }
        else
        {
            _previewLine.enabled = false;
        }
    }

    // 押し込み・引っ張りを検知するロジック
    private void CheckBondDistances()
    {
        foreach (BondPoint myBond in _atom.BondPoints)
        {
            if (myBond.IsConnected && myBond.ConnectedTarget != null)
            {
                Atom targetAtom = myBond.ConnectedTarget.ParentAtom;
                AtomInteraction targetInteraction = targetAtom.GetComponent<AtomInteraction>();

                // 「自分も相手も掴まれている（両手で操作している）時」だけ距離判定を行う！
                if (this.IsGrabbed && targetInteraction != null && targetInteraction.IsGrabbed)
                {
                    if (this.gameObject.GetInstanceID() < targetAtom.gameObject.GetInstanceID())
                        continue;

                    float myDist = Vector3.Distance(transform.position, myBond.transform.position);
                    float targetDist = Vector3.Distance(targetAtom.transform.position, myBond.ConnectedTarget.transform.position);
                    float baseDistance = myDist + targetDist;

                    float currentDistance = Vector3.Distance(transform.position, targetAtom.transform.position);

                    // ★現象1の解決：基準距離の 1.4倍 以上引っ張られたら BreakBond を呼び出す
                    if (currentDistance > baseDistance * 1.4f)
                    {
                        BreakBond(myBond, myBond.ConnectedTarget);
                        return; // 切断後はループを抜ける
                    }

                    int currentOrder = myBond.CurrentBondOrder;
                    int newOrder = currentOrder;

                    if (currentDistance < baseDistance * 0.55f) newOrder = 3;
                    else if (currentDistance < baseDistance * 0.75f) newOrder = 2;
                    else if (currentDistance > baseDistance * 0.85f) newOrder = 1;

                    if (newOrder != currentOrder)
                    {
                        ChangeBondOrder(myBond, myBond.ConnectedTarget, newOrder);
                    }
                }
            }
        }
    }

    // 結合を完全に引きちぎるメソッド
    public void BreakBond(BondPoint myBond, BondPoint targetBond)
    {
        if (myBond == null || targetBond == null) return;
        Atom targetAtom = targetBond.ParentAtom;

        // 1. 物理的な固定（FixedJoint）を双方から完全に削除
        DestroyExistingJointsTo(targetAtom.gameObject);
        if (targetAtom != null)
        {
            AtomInteraction targetInteraction = targetAtom.GetComponent<AtomInteraction>();
            if (targetInteraction != null)
            {
                targetInteraction.DestroyExistingJointsTo(this.gameObject);
            }
        }

        // 2. 結合時に無視していた「原子同士のコリジョン」を復活させる
        // 子コライダー含め、衝突無視を解除する
        Collider[] myCols = GetComponentsInChildren<Collider>();
        Collider[] targetCols = targetAtom != null ? targetAtom.GetComponentsInChildren<Collider>() : null;
        if (myCols != null && targetCols != null)
        {
            foreach (var c1 in myCols)
            {
                foreach (var c2 in targetCols)
                {
                    Physics.IgnoreCollision(c1, c2, false);
                }
            }
        }

        // 3. データレイヤーの切断処理
        myBond.Disconnect();
        targetBond.Disconnect();

        Debug.Log($"【結合切断】{_atom.ElementType} と {targetAtom.ElementType} の結合が引きちぎられました！");

        // 4. ★現象1の解決：分子グループの再構築（切断によって独立した原子・分子を分ける）
        UpdateMoleculeGrouping(this._atom);
        if (targetAtom != null)
        {
            UpdateMoleculeGrouping(targetAtom);
        }

        // 5. 構造が変わったことをManagerに報告
        if (MoleculeManager.Instance != null)
        {
            MoleculeManager.Instance.OnStructureChanged(this._atom);
            if (targetAtom != null)
            {
                MoleculeManager.Instance.OnStructureChanged(targetAtom);
            }
        }
    }

    // 結合の強さを変更し、再固定するメソッド
    private void ChangeBondOrder(BondPoint myBond, BondPoint targetBond, int newOrder)
    {
        myBond.SetBondOrder(newOrder);
        targetBond.SetBondOrder(newOrder);

        RebuildJoint(myBond, targetBond, newOrder);

        if (MoleculeManager.Instance != null)
        {
            MoleculeManager.Instance.OnStructureChanged(this._atom);
        }
    }

    private void RebuildJoint(BondPoint myBond, BondPoint targetBond, int newOrder)
    {
        Atom targetAtom = targetBond.ParentAtom;
        Rigidbody targetRb = targetAtom.GetComponent<Rigidbody>();

        DestroyExistingJointsTo(targetAtom.gameObject);
        targetAtom.GetComponent<AtomInteraction>().DestroyExistingJointsTo(this.gameObject);

        SnapToTarget(myBond, targetBond, newOrder);

        FixedJoint joint1 = gameObject.AddComponent<FixedJoint>();
        joint1.connectedBody = targetRb;
        joint1.breakForce = Mathf.Infinity;

        FixedJoint joint2 = targetAtom.gameObject.AddComponent<FixedJoint>();
        joint2.connectedBody = _rb;
        joint2.breakForce = Mathf.Infinity;
    }

    public void DestroyExistingJointsTo(GameObject targetObject)
    {
        FixedJoint[] joints = GetComponents<FixedJoint>();
        foreach (var j in joints)
        {
            if (j.connectedBody != null && j.connectedBody.gameObject == targetObject)
            {
                Destroy(j);
            }
        }
    }

    private void SnapToTarget(BondPoint myBond, BondPoint targetBond, int bondOrder = 1)
    {
        Quaternion rotationDiff = Quaternion.FromToRotation(myBond.transform.up, -targetBond.transform.up);
        transform.rotation = rotationDiff * transform.rotation;

        float distanceMultiplier = 1.0f;
        if (bondOrder == 2) distanceMultiplier = 0.7f;
        if (bondOrder == 3) distanceMultiplier = 0.5f;

        SphereCollider myCol = myBond.GetComponent<SphereCollider>();
        SphereCollider targetCol = targetBond.GetComponent<SphereCollider>();

        Vector3 myLocalTip = myCol.center * distanceMultiplier;
        Vector3 targetLocalTip = targetCol.center * distanceMultiplier;

        Vector3 myVirtualTip = myBond.transform.TransformPoint(myLocalTip);
        Vector3 targetVirtualTip = targetBond.transform.TransformPoint(targetLocalTip);

        Vector3 offset = transform.position - myVirtualTip;
        transform.position = targetVirtualTip + offset;
    }

    private void OnDestroy()
    {
        if (_atom == null || _atom.BondPoints == null) return;

        foreach (BondPoint myPoint in _atom.BondPoints)
        {
            if (myPoint.IsConnected && myPoint.ConnectedTarget != null)
            {
                Atom neighborAtom = myPoint.ConnectedTarget.ParentAtom;

                myPoint.ConnectedTarget.Disconnect();
                myPoint.Disconnect();

                if (MoleculeManager.Instance != null && neighborAtom != null)
                {
                    MoleculeManager.Instance.OnStructureChanged(neighborAtom);
                }
            }
        }

        if (_previewLine != null && _previewLine.material != null)
        {
            Destroy(_previewLine.material);
        }
    }

    // === 動的分子グループ（MoleculeGroup）の管理ヘルパー ===

    public static void UpdateMoleculeGrouping(Atom startAtom)
    {
        if (startAtom == null) return;

        List<Atom> connectedAtoms = GetConnectedMoleculeGroup(startAtom);

        // 1つの原子（単体）になった場合はグループ解除
        if (connectedAtoms.Count <= 1)
        {
            foreach (Atom a in connectedAtoms)
            {
                if (a.transform.parent != null && a.transform.parent.name.StartsWith("MoleculeGroup"))
                {
                    Transform oldParent = a.transform.parent;
                    a.transform.SetParent(null);

                    if (oldParent.childCount == 0)
                    {
                        Destroy(oldParent.gameObject);
                    }
                }
            }
            return;
        }

        // 既存のMoleculeGroupがあるか検索
        GameObject targetGroup = null;
        foreach (Atom a in connectedAtoms)
        {
            if (a.transform.parent != null && a.transform.parent.name.StartsWith("MoleculeGroup"))
            {
                targetGroup = a.transform.parent.gameObject;
                break;
            }
        }

        // なければ新規作成
        if (targetGroup == null)
        {
            targetGroup = new GameObject("MoleculeGroup");
            Vector3 centerPos = Vector3.zero;
            foreach (Atom a in connectedAtoms) centerPos += a.transform.position;
            centerPos /= connectedAtoms.Count;
            targetGroup.transform.position = centerPos;
        }

        // 繋がっているすべての原子をグループ配下に設定
        foreach (Atom a in connectedAtoms)
        {
            if (a.transform.parent != targetGroup.transform)
            {
                Transform oldParent = a.transform.parent;
                a.transform.SetParent(targetGroup.transform);

                if (oldParent != null && oldParent != targetGroup.transform && oldParent.childCount == 0)
                {
                    Destroy(oldParent.gameObject);
                }
            }
        }
    }

    private static List<Atom> GetConnectedMoleculeGroup(Atom startAtom)
    {
        List<Atom> result = new List<Atom>();
        HashSet<Atom> visited = new HashSet<Atom>();
        Queue<Atom> queue = new Queue<Atom>();

        queue.Enqueue(startAtom);
        visited.Add(startAtom);

        while (queue.Count > 0)
        {
            Atom current = queue.Dequeue();
            result.Add(current);

            if (current.BondPoints != null)
            {
                foreach (BondPoint bp in current.BondPoints)
                {
                    if (bp.IsConnected && bp.ConnectedTarget != null && bp.ConnectedTarget.ParentAtom != null)
                    {
                        Atom neighbor = bp.ConnectedTarget.ParentAtom;
                        if (!visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }
        }

        return result;
    }

    // FixedJointの重複チェック用ヘルパー
    public bool HasJointTo(GameObject targetObject)
    {
        FixedJoint[] joints = GetComponents<FixedJoint>();
        foreach (var j in joints)
        {
            if (j.connectedBody != null && j.connectedBody.gameObject == targetObject)
            {
                return true;
            }
        }
        return false;
    }

    // グループ内に「他に掴まれている原子があるか」をチェックするヘルパー
    private bool IsAnyOtherAtomInGroupGrabbed()
    {
        if (transform.parent == null) return false;

        AtomInteraction[] siblings = transform.parent.GetComponentsInChildren<AtomInteraction>();
        foreach (var sibling in siblings)
        {
            if (sibling != this && sibling.IsGrabbed)
            {
                return true;
            }
        }
        return false;
    }
}