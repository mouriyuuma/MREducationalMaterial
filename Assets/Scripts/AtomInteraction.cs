using System.Collections.Generic;
using UnityEngine;

// 責任：VRの掴む/離す操作と、物理的な結合(FixedJoint)の生成・破壊を担当する
//
// 結合次数は「同じ相手に腕を何本かけたか」で決まる。分子模型キットと同じで、
// 腕を2本ひっかければ二重結合、3本なら三重結合になる。
// 手を離した瞬間に重なっている腕のペアをすべて拾って、まとめて結合する。
[RequireComponent(typeof(Atom), typeof(Rigidbody))]
public class AtomInteraction : MonoBehaviour
{
    // 三重結合まで対応する
    public const int MaxBondOrder = 3;

    // 問題の切り替えやアプリ終了で原子をまとめて消している最中は true。
    // 消えていく原子どうしが互いに分子を組み直そうとして壊れたオブジェクトに触るのを防ぐ
    public static bool IsTearingDown { get; set; }

    public bool IsGrabbed { get; private set; }

    private Atom _atom;
    private Rigidbody _rb;

    // プレビュー用の線。何重結合になるかを結合前に見せるため、最大3本ぶん用意する
    private LineRenderer[] _previewLines;

    // 掴まれた瞬間のローカル姿勢（分子全体の同期用）
    private Vector3 _grabLocalPos;
    private Quaternion _grabLocalRot;

    // 結合候補になっている腕のペア
    private struct BondCandidate
    {
        public BondPoint Mine;
        public BondPoint Theirs;
        public float Distance;
    }

    private readonly List<BondCandidate> _candidates = new List<BondCandidate>();
    private readonly List<BondCandidate> _bestPairs = new List<BondCandidate>();
    private readonly List<Atom> _neighbors = new List<Atom>();
    private readonly List<BondPoint> _armsToNeighbor = new List<BondPoint>();

    // プレビューは掴んでいる間ずっと毎フレーム走るので、作業用の入れ物は使い回してゴミを出さない
    private readonly List<Atom> _seenAtoms = new List<Atom>();
    private readonly List<BondCandidate> _pairBuffer = new List<BondCandidate>();
    private readonly List<BondPoint> _usedMine = new List<BondPoint>();
    private readonly List<BondPoint> _usedTheirs = new List<BondPoint>();

    // 結合次数ごとのプレビュー線の色（1本=緑 / 2本=黄 / 3本=橙）
    private static readonly Color[] OrderColors = { Color.green, Color.yellow, new Color(1f, 0.55f, 0.1f) };

    private static readonly System.Comparison<BondCandidate> ByDistance =
        (a, b) => a.Distance.CompareTo(b.Distance);

    private void Awake()
    {
        _atom = GetComponent<Atom>();
        _rb = GetComponent<Rigidbody>();

        CreatePreviewLines();
    }

    // 結合次数ごとに色を変えたプレビュー線を用意する（1本=緑 / 2本=黄 / 3本=橙）
    private void CreatePreviewLines()
    {
        _previewLines = new LineRenderer[MaxBondOrder];

        for (int i = 0; i < MaxBondOrder; i++)
        {
            // 原子の子にすると原子側のスケール(0.1)で線が細くなってしまうので、親を付けずにワールドに置く
            GameObject holder = new GameObject($"BondPreview_{name}_{i}");
            LineRenderer line = holder.AddComponent<LineRenderer>();

            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = 0.008f;
            line.endWidth = 0.008f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.enabled = false;

            _previewLines[i] = line;
        }

        ApplyPreviewColors(1);
    }

    private void ApplyPreviewColors(int order)
    {
        Color color = OrderColors[Mathf.Clamp(order - 1, 0, OrderColors.Length - 1)];
        foreach (LineRenderer line in _previewLines)
        {
            line.startColor = color;
            line.endColor = color;
        }
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
        if (TryConnect()) return;

        // 結合しなかった場合でも、引っ張って伸びたままの結合を本来の形に戻す。
        // これをしないと「見た目は離れているのに繋がったまま」という状態が残ってしまう
        if (HasAnyBond()) MoleculeLayout.Rebuild(FindLayoutSeed());
    }

    // === 結合の成立 ===

    private bool TryConnect()
    {
        Atom targetAtom = FindBestConnection(_bestPairs);
        if (targetAtom == null || _bestPairs.Count == 0) return false;

        ExecuteConnection(targetAtom, _bestPairs);
        return true;
    }

    private bool HasAnyBond()
    {
        if (_atom.BondPoints == null) return false;
        foreach (BondPoint bp in _atom.BondPoints)
        {
            if (bp.IsConnected) return true;
        }
        return false;
    }

    // 並べ直しの基準にする原子。
    // まだ手に持たれている原子があればそれを基準にしないと、分子が手から飛び出してしまう
    private Atom FindLayoutSeed()
    {
        foreach (Atom a in MoleculeLayout.CollectMolecule(_atom))
        {
            AtomInteraction other = a.GetComponent<AtomInteraction>();
            if (other != null && other.IsGrabbed) return a;
        }
        return _atom;
    }

    // 今いちばん多くの腕が重なっている相手を探し、その腕のペアを result に詰める。
    // 重なっている腕が2ペアなら二重結合、3ペアなら三重結合になる
    private Atom FindBestConnection(List<BondCandidate> result)
    {
        result.Clear();
        CollectCandidates();
        if (_candidates.Count == 0) return null;

        // 相手の原子ごとに、どれだけの腕が重なっているかを調べる
        Atom bestAtom = null;
        int bestCount = 0;
        float bestDistance = float.MaxValue;

        _seenAtoms.Clear();

        foreach (BondCandidate candidate in _candidates)
        {
            Atom other = candidate.Theirs.ParentAtom;
            if (other == null || _seenAtoms.Contains(other)) continue;
            _seenAtoms.Add(other);

            MatchPairs(other, _pairBuffer);
            if (_pairBuffer.Count == 0) continue;

            float totalDistance = 0f;
            foreach (BondCandidate pair in _pairBuffer) totalDistance += pair.Distance;

            // ペア数が多い方を優先し、同数なら腕どうしが近い方を選ぶ
            if (_pairBuffer.Count > bestCount || (_pairBuffer.Count == bestCount && totalDistance < bestDistance))
            {
                bestAtom = other;
                bestCount = _pairBuffer.Count;
                bestDistance = totalDistance;

                result.Clear();
                result.AddRange(_pairBuffer);
            }
        }

        return bestAtom;
    }

    // 重なっている腕の組み合わせをすべて洗い出す
    private void CollectCandidates()
    {
        _candidates.Clear();
        if (_atom.BondPoints == null) return;

        foreach (BondPoint myBond in _atom.BondPoints)
        {
            if (!myBond.CanConnect()) continue;

            foreach (BondPoint theirs in myBond.GetHoverCandidates())
            {
                if (theirs == null || theirs.ParentAtom == _atom) continue;
                if (!theirs.CanConnect()) continue;

                _candidates.Add(new BondCandidate
                {
                    Mine = myBond,
                    Theirs = theirs,
                    Distance = Vector3.Distance(myBond.TipPosition, theirs.TipPosition)
                });
            }
        }

        _candidates.Sort(ByDistance);
    }

    // 特定の相手との腕を、近い順に1対1で組んでいく
    private void MatchPairs(Atom other, List<BondCandidate> result)
    {
        result.Clear();

        // 結べる本数は、お互いの余っている結合手の少ない方で決まる
        int limit = Mathf.Min(_atom.AvailableValency, other.AvailableValency, MaxBondOrder);
        if (limit <= 0) return;

        _usedMine.Clear();
        _usedTheirs.Clear();

        // _candidates は距離の近い順に並んでいるので、前から採用すれば自然に良い組み合わせになる
        foreach (BondCandidate candidate in _candidates)
        {
            if (result.Count >= limit) break;
            if (candidate.Theirs.ParentAtom != other) continue;
            if (_usedMine.Contains(candidate.Mine) || _usedTheirs.Contains(candidate.Theirs)) continue;

            _usedMine.Add(candidate.Mine);
            _usedTheirs.Add(candidate.Theirs);
            result.Add(candidate);
        }
    }

    private void ExecuteConnection(Atom targetAtom, List<BondCandidate> pairs)
    {
        Rigidbody targetRb = targetAtom.GetComponent<Rigidbody>();
        if (targetRb == null) return;

        // SDKの干渉を防ぐため物理演算を設定
        _rb.isKinematic = false;
        _rb.useGravity = false;
        targetRb.isKinematic = false;
        targetRb.useGravity = false;

        // データレイヤーの状態を更新。ここで結ばれた腕の本数がそのまま結合次数になる
        foreach (BondCandidate pair in pairs)
        {
            pair.Mine.ConnectTo(pair.Theirs);
            pair.Theirs.ConnectTo(pair.Mine);
        }

        // BondPoint含む「すべての子コライダー」同士の衝突を無視する
        SetCollisionIgnored(targetAtom, true);

        Debug.Log($"【結合】{_atom.ElementType} と {targetAtom.ElementType} が {pairs.Count} 重結合になりました。");

        // 分子全体のグループ化（親子構造の構築・統合）
        UpdateMoleculeGrouping(_atom);

        // 混成軌道の組み替え・原子の並べ直し・Jointの張り直し。
        // まだ手に持たれている原子があればそこを基準にして、分子が手から飛び出さないようにする
        MoleculeLayout.Rebuild(FindLayoutSeed());

        // 結合が完了したことをManagerに報告する
        if (MoleculeManager.Instance != null)
        {
            MoleculeManager.Instance.OnStructureChanged(_atom);
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
            HidePreview(); // 離したら線を消す
        }
    }

    // 掴まれた原子の動きに合わせて、分子全体（親グループ）を動かす
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

    // === プレビュー ===

    // 結合プレビューの線を引く。重なっている腕の本数だけ線が出るので、
    // 手を離す前に「今なら何重結合になるか」が分かる
    private void UpdateConnectionPreview()
    {
        Atom targetAtom = FindBestConnection(_bestPairs);
        if (targetAtom == null || _bestPairs.Count == 0)
        {
            HidePreview();
            return;
        }

        ApplyPreviewColors(_bestPairs.Count);

        for (int i = 0; i < _previewLines.Length; i++)
        {
            if (i < _bestPairs.Count)
            {
                _previewLines[i].enabled = true;
                _previewLines[i].SetPosition(0, _bestPairs[i].Mine.TipPosition);
                _previewLines[i].SetPosition(1, _bestPairs[i].Theirs.TipPosition);
            }
            else
            {
                _previewLines[i].enabled = false;
            }
        }
    }

    private void HidePreview()
    {
        if (_previewLines == null) return;
        foreach (LineRenderer line in _previewLines)
        {
            if (line != null) line.enabled = false;
        }
    }

    // === 切断 ===

    // 押し込み・引っ張りを検知するロジック
    private void CheckBondDistances()
    {
        _atom.GetDistinctNeighbors(_neighbors);
        if (_neighbors.Count == 0) return;

        // GetDistinctNeighbors は共有リストなので、切断でリストが変わる前にコピーしておく
        Atom[] neighbors = _neighbors.ToArray();

        foreach (Atom targetAtom in neighbors)
        {
            AtomInteraction targetInteraction = targetAtom.GetComponent<AtomInteraction>();

            // 「自分も相手も掴まれている（両手で操作している）時」だけ距離判定を行う！
            if (targetInteraction == null || !targetInteraction.IsGrabbed) continue;

            // 両側から二重に処理しないよう、片方だけが判定する
            if (gameObject.GetInstanceID() < targetAtom.gameObject.GetInstanceID()) continue;

            // 本来あるべき原子間距離。多重結合ほど腕が開くぶん短くなる
            float restDistance = MoleculeLayout.RestBondLength(_atom, targetAtom);
            if (restDistance <= 0f) continue;

            float currentDistance = Vector3.Distance(transform.position, targetAtom.transform.position);

            // 基準距離の 1.4倍 以上引っ張られたら引きちぎる
            if (currentDistance > restDistance * 1.4f)
            {
                BreakBond(targetAtom);
                return; // 切断後はループを抜ける
            }
        }
    }

    // 相手の原子との結合を完全に引きちぎる。多重結合なら使っている腕をすべて外す
    public void BreakBond(Atom targetAtom)
    {
        if (targetAtom == null) return;

        int order = _atom.GetBondOrderTo(targetAtom);
        if (order == 0) return;

        // 1. 物理的な固定（FixedJoint）を双方から完全に削除
        DestroyExistingJointsTo(targetAtom.gameObject);
        AtomInteraction targetInteraction = targetAtom.GetComponent<AtomInteraction>();
        if (targetInteraction != null)
        {
            targetInteraction.DestroyExistingJointsTo(gameObject);
        }

        // 2. 結合時に無視していた「原子同士のコリジョン」を復活させる
        SetCollisionIgnored(targetAtom, false);

        // 3. データレイヤーの切断処理。多重結合でかけている腕をすべて外す
        _atom.GetBondPointsTo(targetAtom, _armsToNeighbor);
        foreach (BondPoint myBond in new List<BondPoint>(_armsToNeighbor))
        {
            if (myBond.ConnectedTarget != null) myBond.ConnectedTarget.Disconnect();
            myBond.Disconnect();
        }

        Debug.Log($"【結合切断】{_atom.ElementType} と {targetAtom.ElementType} の{order}重結合が引きちぎられました！");

        // 4. 分子グループの再構築（切断によって独立した原子・分子を分ける）
        UpdateMoleculeGrouping(_atom);
        UpdateMoleculeGrouping(targetAtom);

        // 5. 離れた両側とも、結合が減ったぶん混成が戻る（sp2 → sp3 など）
        MoleculeLayout.Rebuild(_atom);
        MoleculeLayout.Rebuild(targetAtom);

        // 6. 構造が変わったことをManagerに報告
        if (MoleculeManager.Instance != null)
        {
            MoleculeManager.Instance.OnStructureChanged(_atom);
            MoleculeManager.Instance.OnStructureChanged(targetAtom);
        }
    }

    private void OnApplicationQuit()
    {
        IsTearingDown = true;
    }

    private void OnDestroy()
    {
        if (!IsTearingDown && _atom != null && _atom.BondPoints != null)
        {
            _atom.GetDistinctNeighbors(_neighbors);
            Atom[] neighbors = _neighbors.ToArray();

            foreach (BondPoint myPoint in _atom.BondPoints)
            {
                if (myPoint.ConnectedTarget != null) myPoint.ConnectedTarget.Disconnect();
                myPoint.Disconnect();
            }

            foreach (Atom neighbor in neighbors)
            {
                if (neighbor == null) continue;

                UpdateMoleculeGrouping(neighbor);
                MoleculeLayout.Rebuild(neighbor);

                if (MoleculeManager.Instance != null)
                {
                    MoleculeManager.Instance.OnStructureChanged(neighbor);
                }
            }
        }

        if (_previewLines != null)
        {
            foreach (LineRenderer line in _previewLines)
            {
                if (line == null) continue;
                if (line.material != null) Destroy(line.material);
                Destroy(line.gameObject);
            }
        }
    }

    // === Joint / コライダーのヘルパー ===

    // MoleculeLayout が並べ直す前に呼ぶ。動かしている最中に物理が引っぱり合うのを防ぐ
    public void DestroyAllJoints()
    {
        foreach (FixedJoint joint in GetComponents<FixedJoint>())
        {
            Destroy(joint);
        }
    }

    // MoleculeLayout が並べ直したあとに呼ぶ。DestroyAllJoints の直後に使う前提
    public void CreateJointTo(Atom other)
    {
        if (other == null) return;

        Rigidbody otherRb = other.GetComponent<Rigidbody>();
        if (otherRb == null) return;

        FixedJoint joint = gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = otherRb;
        joint.breakForce = Mathf.Infinity;
        joint.enableCollision = false;
    }

    public void DestroyExistingJointsTo(GameObject targetObject)
    {
        foreach (FixedJoint joint in GetComponents<FixedJoint>())
        {
            if (joint.connectedBody != null && joint.connectedBody.gameObject == targetObject)
            {
                Destroy(joint);
            }
        }
    }

    private void SetCollisionIgnored(Atom other, bool ignored)
    {
        if (other == null) return;

        Collider[] myCols = GetComponentsInChildren<Collider>();
        Collider[] otherCols = other.GetComponentsInChildren<Collider>();

        foreach (Collider c1 in myCols)
        {
            foreach (Collider c2 in otherCols)
            {
                if (c1 == null || c2 == null) continue;
                Physics.IgnoreCollision(c1, c2, ignored);
            }
        }
    }

    // === 動的分子グループ（MoleculeGroup）の管理ヘルパー ===

    public static void UpdateMoleculeGrouping(Atom startAtom)
    {
        if (startAtom == null) return;

        List<Atom> connectedAtoms = MoleculeLayout.CollectMolecule(startAtom);

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

    // FixedJointの重複チェック用ヘルパー
    public bool HasJointTo(GameObject targetObject)
    {
        foreach (FixedJoint joint in GetComponents<FixedJoint>())
        {
            if (joint.connectedBody != null && joint.connectedBody.gameObject == targetObject)
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
