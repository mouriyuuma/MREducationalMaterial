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

    private void Awake()
    {
        _atom = GetComponent<Atom>();
        _rb = GetComponent<Rigidbody>();

        // ★追加: プレビュー用のLineRendererをプログラムから動的に追加して設定する
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
        BondPoint bestMyBond = null;
        BondPoint bestTargetBond = null;
        float minDistance = float.MaxValue;

        // UpdateConnectionPreviewと同じ要領で、離した瞬間のベストな相手を探す
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

        // ベストな相手がいた場合のみ、実際の結合処理（ExecuteConnection）を実行！
        if (bestMyBond != null && bestTargetBond != null)
        {
            ExecuteConnection(bestMyBond, bestTargetBond);
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
        myBond.ConnectTo(targetBond, 1);
        targetBond.ConnectTo(myBond, 1);

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

        // 生成したマテリアルをメモリから解放してエラーを防ぐ
        if (_previewLine != null && _previewLine.material != null)
        {
            Destroy(_previewLine.material);
        }
    }
    private void SnapToTarget(BondPoint myBond, BondPoint targetBond, int bondOrder = 1)
    {
        // 1. 回転合わせ
        Quaternion rotationDiff = Quaternion.FromToRotation(myBond.transform.up, -targetBond.transform.up);
        transform.rotation = rotationDiff * transform.rotation;

        // 2. 結合数に応じて距離の係数を決める（二重結合なら70%の距離、三重結合なら50%の距離に近づく）
        float distanceMultiplier = 1.0f;
        if (bondOrder == 2) distanceMultiplier = 0.7f;
        if (bondOrder == 3) distanceMultiplier = 0.5f;

        // 3. 係数を掛けて「仮想の先端座標」を計算
        SphereCollider myCol = myBond.GetComponent<SphereCollider>();
        SphereCollider targetCol = targetBond.GetComponent<SphereCollider>();

        Vector3 myLocalTip = myCol.center * distanceMultiplier;
        Vector3 targetLocalTip = targetCol.center * distanceMultiplier;

        Vector3 myVirtualTip = myBond.transform.TransformPoint(myLocalTip);
        Vector3 targetVirtualTip = targetBond.transform.TransformPoint(targetLocalTip);

        // 4. 移動
        Vector3 offset = transform.position - myVirtualTip;
        transform.position = targetVirtualTip + offset;
    }

    private void Update()
    {
        // ★追加: 自分が掴まれている間だけ、繋がっている原子との距離を測る
        if (IsGrabbed)
        {
            CheckBondDistances();
            UpdateConnectionPreview(); // ★追加: 掴んでいる間はプレビュー線を更新
        }
        else
        {
            _previewLine.enabled = false; // ★追加: 離したら線を消す
        }
    }

    // ★追加: 結合プレビューの線を引くメソッド
    private void UpdateConnectionPreview()
    {
        BondPoint bestMyBond = null;
        BondPoint bestTargetBond = null;
        float minDistance = float.MaxValue;

        // 自分が持っているすべての腕の中から、最も近い「運命の相手」を探す
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

        // 結合可能な相手が見つかっていれば、その2点間にレーザーを引く
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

    // ★追加: 押し込み・引っ張りを検知するロジック
    private void CheckBondDistances()
    {
        foreach (BondPoint myBond in _atom.BondPoints)
        {
            if (myBond.IsConnected && myBond.ConnectedTarget != null)
            {
                Atom targetAtom = myBond.ConnectedTarget.ParentAtom;
                AtomInteraction targetInteraction = targetAtom.GetComponent<AtomInteraction>();

                // ★大修正: 「自分も相手も掴まれている（両手で操作している）時」だけ距離判定を行う！
                if (this.IsGrabbed && targetInteraction != null && targetInteraction.IsGrabbed)
                {
                    // 両方の原子から判定が走るのを防ぐためのIDチェック（片方だけが処理する）
                    if (this.gameObject.GetInstanceID() < targetAtom.gameObject.GetInstanceID()) 
                        continue; 

                    float myDist = Vector3.Distance(transform.position, myBond.transform.position);
                    float targetDist = Vector3.Distance(targetAtom.transform.position, myBond.ConnectedTarget.transform.position);
                    float baseDistance = myDist + targetDist;

                    float currentDistance = Vector3.Distance(transform.position, targetAtom.transform.position);

                    // 基準距離の 1.4倍 以上引っ張られたら、結合を引きちぎる
                    if (currentDistance > baseDistance * 1.4f)
                    {
                        // ここはご自身の環境に合わせて結合解除のメソッドを呼んでください
                        // 例: BreakBond(myBond, myBond.ConnectedTarget);
                        myBond.Disconnect();
                        myBond.ConnectedTarget.Disconnect();
                        
                        // FixedJointも壊す
                        FixedJoint joint = GetComponent<FixedJoint>();
                        if (joint != null) Destroy(joint);
                        FixedJoint targetJoint = targetAtom.GetComponent<FixedJoint>();
                        if (targetJoint != null) Destroy(targetJoint);

                        continue; 
                    }

                    int currentOrder = myBond.CurrentBondOrder;
                    int newOrder = currentOrder;

                    // 押し込み・引っ張り具合で目標の結合数を決める
                    if (currentDistance < baseDistance * 0.55f) newOrder = 3;
                    else if (currentDistance < baseDistance * 0.75f) newOrder = 2; 
                    else if (currentDistance > baseDistance * 0.85f) newOrder = 1; // 引っ張ったら単結合に戻る

                    // 結合の強さに変化があった場合
                    if (newOrder != currentOrder)
                    {
                        if (newOrder > currentOrder)
                        {
                            int requiredExtra = newOrder - currentOrder;
                            int available = Mathf.Min(_atom.AvailableValency, targetAtom.AvailableValency);
                            
                            if (available < requiredExtra)
                            {
                                newOrder = currentOrder + available;
                            }
                        }
                        
                        if (newOrder != currentOrder)
                        {
                            myBond.SetBondOrder(newOrder);
                            myBond.ConnectedTarget.SetBondOrder(newOrder);
                        }
                    }
                }
            }
        }
    }

    // ★追加: 結合を完全に引きちぎるメソッド
    private void BreakBond(BondPoint myBond, BondPoint targetBond)
    {
        Atom targetAtom = targetBond.ParentAtom;

        // 1. 物理的な固定（FixedJoint）を双方から完全に削除
        DestroyExistingJointsTo(targetAtom.gameObject);
        targetAtom.GetComponent<AtomInteraction>().DestroyExistingJointsTo(this.gameObject);

        // 2. 結合時に無視していた「原子同士のコリジョン」を復活させる（再びぶつかるようになる）
        Collider myCol = GetComponent<Collider>();
        Collider targetCol = targetAtom.GetComponent<Collider>();
        if (myCol && targetCol)
        {
            Physics.IgnoreCollision(myCol, targetCol, false); // 第3引数をfalseにすると無視を解除
        }

        // 3. データレイヤーの切断処理（見た目の円柱もここでリセットされます）
        myBond.Disconnect();
        targetBond.Disconnect();

        Debug.Log($"【結合切断】{_atom.ElementType} と {targetAtom.ElementType} の結合が引きちぎられました！");

        // 4. 構造が変わったことをManagerに報告
        if (MoleculeManager.Instance != null)
        {
            // 重要：結合が切れて「2つの独立した分子の島」に分かれた可能性があるため、
            // 自分側と相手側の両方を起点にして、それぞれ最新のトポロジーを再スキャンさせます
            MoleculeManager.Instance.OnStructureChanged(this._atom);
            MoleculeManager.Instance.OnStructureChanged(targetAtom);
        }
    }

    // ★追加: 結合の強さを変更し、再固定するメソッド
    private void ChangeBondOrder(BondPoint myBond, BondPoint targetBond, int newOrder)
    {
        myBond.SetBondOrder(newOrder);
        targetBond.SetBondOrder(newOrder);

        // 既存のJointを一旦破壊して、新しい距離で張り直す
        RebuildJoint(myBond, targetBond, newOrder);

        // 構造が変わったことをManagerに報告（これで正解判定が再評価されます！）
        if (MoleculeManager.Instance != null)
        {
            MoleculeManager.Instance.OnStructureChanged(this._atom);
        }
    }

    private void RebuildJoint(BondPoint myBond, BondPoint targetBond, int newOrder)
    {
        Atom targetAtom = targetBond.ParentAtom;
        Rigidbody targetRb = targetAtom.GetComponent<Rigidbody>();

        // お互いのFixedJointを削除
        DestroyExistingJointsTo(targetAtom.gameObject);
        targetAtom.GetComponent<AtomInteraction>().DestroyExistingJointsTo(this.gameObject);

        // 新しい結合度合いに応じた距離でスナップし直す
        SnapToTarget(myBond, targetBond, newOrder);

        // 再びJointで固定
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
}