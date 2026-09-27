using System.Collections.Generic;
using UnityEngine;

// 責任：結合したあと・切れたあとの分子全体の形を組み直す
//
//   1. 各原子の結合手を混成軌道（sp3 / sp2 / sp）に沿って向け直す
//   2. 結合をたどって原子を並べ直し、腕の先どうしがぴったり重なるようにする
//   3. FixedJoint を張り直して剛体として固定する
//
// 多重結合を作ると価電子対の数が変わるため、たとえば炭素は正四面体(109.47°)から
// 平面三角(120°)へ組み替わる。その結果、隣に繋がっている原子も動かす必要があるので、
// 一部だけではなく分子全体をまとめて並べ直している。
public static class MoleculeLayout
{
    // 組み替えの作業用。毎フレーム呼ぶ処理ではないが、無駄な確保は避ける
    private static readonly List<Vector2> _sigmaAngles = new List<Vector2>();
    private static readonly List<Vector3> _splayDirs = new List<Vector3>();
    private static readonly List<BondPoint> _scratchArms = new List<BondPoint>();

    // 組み替えを滑らかに見せるための、動かす対象と組み替え前の姿勢
    private static readonly List<Transform> _animTargets = new List<Transform>();
    private static readonly List<Vector3> _animFromPositions = new List<Vector3>();
    private static readonly List<Quaternion> _animFromRotations = new List<Quaternion>();

    // 組み替える前の分子の中心。組み替えで分子全体の位置がずれないようにするために使う
    private static Vector3 _centroidBeforeRebuild;

    // 鎖をジグザグに伸ばし直している最中かどうか（重なりが起きたときの作り直し用）
    private static bool _extendChain;

    // 結合手のまとまり。「同じ相手に向かう腕の束」＝σ結合1本ぶん
    private class ArmGroup
    {
        public Atom Neighbor;                   // 余っている腕の場合は null
        public int FirstArmIndex;               // 並び替えを毎回同じ結果にするための番号
        public List<BondPoint> Arms = new List<BondPoint>();
    }

    // seed の位置・向きを動かさずに、分子全体を組み直す
    public static void Rebuild(Atom seed)
    {
        if (seed == null) return;

        List<Atom> atoms = CollectMolecule(seed);

        // 並べ直している最中に物理が暴れないよう、いったんすべての固定を外す
        foreach (Atom a in atoms)
        {
            AtomInteraction ai = a.GetComponent<AtomInteraction>();
            if (ai != null) ai.DestroyAllJoints();
        }

        // 組み替えの前の姿勢を覚えておく。あとで滑らかに補間するため
        CaptureBeforePoses(atoms);

        // 環は閉路なので、結合をたどる木構造の配置では最後の1本が閉じない。
        // 先に環を多角形として置いてしまい、そこから生える枝だけを木構造でたどる
        List<List<Atom>> rings = MoleculeRings.FindRings(atoms);

        // ケクレ構造が完成していれば、ここで等価な芳香環へ組み替える。
        // 結合の本数が変わって結合長にも効くので、配置より前に済ませる
        MoleculeAromatics.Update(atoms, rings);

        Dictionary<Atom, List<Atom>> ringOf = MoleculeRings.MapAtomsToRings(rings);

        // まずはプレイヤーが作った形をなるべく残したまま配置する
        PlaceAll(seed, ringOf, false);

        // 鎖が同じ向きに巻いて自分自身に重なってしまったときだけ、ジグザグに伸ばし直す。
        // 炭素の sp3 は 109.47° で正五角形の内角 108° とほぼ同じなので、
        // 六員環を開いたときなどに5個で一周して両端が重なることがある
        if (HasSelfOverlap(atoms))
        {
            foreach (Atom a in atoms) a.LayoutSignature = 0; // 配置し直させる
            PlaceAll(seed, ringOf, true);
        }

        // 手に持たれている原子がなければ、分子の中心が動かないように全体をずらす。
        // そうしないと「引き伸ばして離した原子」を基準に組み直すので、分子全体の位置がずれてしまう
        PreserveCentroidIfReleased(atoms);

        // 最終姿勢は transform に入っているので、いったん元に戻して滑らかに動かす。
        // Joint は補間が終わってから張る（動かしている最中に物理が引っぱり合うのを防ぐ）
        MoleculeAnimator.Play(
            seed.gameObject, _animTargets, _animFromPositions, _animFromRotations,
            () => FinishRebuild(atoms));
    }

    // 補間が終わったあとの後始末。速度を止めて、結合を FixedJoint で固定する
    private static void FinishRebuild(List<Atom> atoms)
    {
        List<Atom> neighbors = new List<Atom>();

        foreach (Atom a in atoms)
        {
            if (a == null) continue;

            Rigidbody rb = a.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        foreach (Atom a in atoms)
        {
            if (a == null) continue;

            AtomInteraction ai = a.GetComponent<AtomInteraction>();
            if (ai == null) continue;

            a.GetDistinctNeighbors(neighbors);
            foreach (Atom neighbor in neighbors)
            {
                ai.CreateJointTo(neighbor);
            }
        }
    }

    // 組み替え前の姿勢（親から見たローカル姿勢）を控える。
    // 原子そのものだけでなく、向きが変わる結合手も対象にする
    private static void CaptureBeforePoses(List<Atom> atoms)
    {
        _animTargets.Clear();
        _animFromPositions.Clear();
        _animFromRotations.Clear();

        _centroidBeforeRebuild = Vector3.zero;
        foreach (Atom a in atoms)
        {
            if (a != null) _centroidBeforeRebuild += a.transform.position;
        }
        if (atoms.Count > 0) _centroidBeforeRebuild /= atoms.Count;

        foreach (Atom a in atoms)
        {
            if (a == null) continue;

            // 手に持たれている原子は補間の対象にしない。
            // AtomInteraction.LateUpdate が「掴まれた原子のローカル姿勢を固定して、
            // その差分だけ親を動かす」処理をしているため、補間が毎フレーム姿勢を書き換えると
            // 毎フレーム差分が生まれて分子全体が回り続けてしまう
            AtomInteraction interaction = a.GetComponent<AtomInteraction>();
            if (interaction == null || !interaction.IsGrabbed) Capture(a.transform);

            if (a.BondPoints == null) continue;

            // 結合手の向きは親が動いても影響しないので、掴まれた原子でも補間してよい
            foreach (BondPoint bp in a.BondPoints)
            {
                if (bp != null) Capture(bp.transform);
            }
        }
    }

    private static void Capture(Transform target)
    {
        _animTargets.Add(target);
        _animFromPositions.Add(target.localPosition);
        _animFromRotations.Add(target.localRotation);
    }

    // 組み替えの前後で分子の中心がずれないように、全体を平行移動する。
    // 手に持たれている原子があるときは、その原子が動かないことのほうが大事なので何もしない
    private static void PreserveCentroidIfReleased(List<Atom> atoms)
    {
        if (atoms.Count == 0) return;

        Vector3 after = Vector3.zero;
        foreach (Atom a in atoms)
        {
            AtomInteraction interaction = a.GetComponent<AtomInteraction>();
            if (interaction != null && interaction.IsGrabbed) return;

            after += a.transform.position;
        }
        after /= atoms.Count;

        Vector3 shift = _centroidBeforeRebuild - after;
        if (shift.sqrMagnitude < 1e-8f) return;

        foreach (Atom a in atoms)
        {
            a.transform.position += shift;
        }
    }

    // 環を先に置き、そこから枝を結合をたどって配置する。
    // extendChain が true のときだけ、鎖をジグザグ（アンチ配座）に伸ばす
    private static void PlaceAll(Atom seed, Dictionary<Atom, List<Atom>> ringOf, bool extendChain)
    {
        _extendChain = extendChain;

        HashSet<Atom> placed = new HashSet<Atom>();
        Queue<Atom> queue = new Queue<Atom>();

        if (ringOf.TryGetValue(seed, out List<Atom> seedRing))
        {
            // 起点が環の中にある場合は、seed を動かさずに環全体を置く
            PlaceRing(seedRing, seed, null);
            foreach (Atom a in seedRing)
            {
                placed.Add(a);
                queue.Enqueue(a);
            }
        }
        else
        {
            ApplyHybridization(seed, null);
            placed.Add(seed);
            queue.Enqueue(seed);
        }

        // 鎖をジグザグに伸ばすために、どの原子から伸ばしてきたかを覚えておく
        Dictionary<Atom, Atom> parentOf = new Dictionary<Atom, Atom>();

        List<Atom> neighbors = new List<Atom>();
        while (queue.Count > 0)
        {
            Atom parent = queue.Dequeue();
            parent.GetDistinctNeighbors(neighbors);

            // ループの中でリストを詰め替えるのでコピーしてから回す
            Atom[] currentNeighbors = neighbors.ToArray();
            foreach (Atom child in currentNeighbors)
            {
                if (child == null || placed.Contains(child)) continue;

                PlaceChild(parent, child);

                if (ringOf.TryGetValue(child, out List<Atom> childRing))
                {
                    // 枝の先に環がぶら下がっていた場合。
                    // 入口の原子から見て、親へ向かう結合が環の外向きになるように環を置く
                    Vector3 outward = (parent.transform.position - child.transform.position).normalized;
                    PlaceRing(childRing, child, outward);

                    foreach (Atom a in childRing)
                    {
                        if (placed.Add(a)) queue.Enqueue(a);
                    }
                }
                else
                {
                    parentOf.TryGetValue(parent, out Atom grandparent);
                    ApplyHybridization(child, parent, grandparent);

                    parentOf[child] = parent;
                    placed.Add(child);
                    queue.Enqueue(child);
                }
            }
        }

        _extendChain = false;
    }

    // 結合していない原子どうしが重なってしまっていないか
    private static bool HasSelfOverlap(List<Atom> atoms)
    {
        const float MinSeparation = 0.6f; // 結合長に対する割合

        for (int i = 0; i < atoms.Count; i++)
        {
            for (int j = i + 1; j < atoms.Count; j++)
            {
                if (atoms[i].GetBondOrderTo(atoms[j]) > 0) continue;

                float limit = (atoms[i].MaxValency > 0 && atoms[i].BondPoints.Length > 0
                    ? atoms[i].BondPoints[0].ArmLength * 2f : 0.2f) * MinSeparation;

                if (Vector3.Distance(atoms[i].transform.position, atoms[j].transform.position) < limit)
                {
                    return true;
                }
            }
        }
        return false;
    }

    // 結合をたどって繋がっている原子をすべて集める
    public static List<Atom> CollectMolecule(Atom startAtom)
    {
        List<Atom> result = new List<Atom>();
        if (startAtom == null) return result;

        HashSet<Atom> visited = new HashSet<Atom> { startAtom };
        Queue<Atom> queue = new Queue<Atom>();
        queue.Enqueue(startAtom);

        while (queue.Count > 0)
        {
            Atom current = queue.Dequeue();
            result.Add(current);

            if (current.BondPoints == null) continue;
            foreach (BondPoint bp in current.BondPoints)
            {
                Atom neighbor = bp.ConnectedAtom;
                if (neighbor != null && visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return result;
    }

    // === 混成軌道に沿った結合手の配置 ===

    // atom の結合手を σ骨格に沿って向け直す。
    // anchorNeighbor が指定された場合、その相手に向かう腕は「既に正しい向きに置かれている」ものとして
    // 動かさず、残りの腕をその周りに配置する（親から順に置いていくときに使う）。
    // grandparent は1つ手前の原子。鎖をジグザグに伸ばすために使う
    public static void ApplyHybridization(Atom atom, Atom anchorNeighbor, Atom grandparent = null)
    {
        if (atom == null || atom.BondPoints == null || atom.BondPoints.Length == 0) return;

        // σ骨格の形が前回と同じなら、腕はすでに正しい向きを向いている。
        // 空いている腕に原子が1つ付いただけのときはここで抜けるので、
        // 既存の腕やそこに繋がっている原子は一切動かない
        int signature = atom.ComputeGeometrySignature();
        if (signature == atom.LayoutSignature) return;
        atom.LayoutSignature = signature;

        List<ArmGroup> groups = BuildArmGroups(atom, anchorNeighbor);
        int sigmaCount = groups.Count;
        if (sigmaCount == 0) return;

        // 今の向きをなるべく壊さないよう、1つ目のまとまりの現在の向きを基準軸にする
        Vector3 axis = GroupLocalDirection(atom, groups[0]);

        Vector3 reference = GetAzimuthReference(atom, groups, anchorNeighbor, grandparent, axis);

        // 基準軸から他の腕を傾けるための回転軸。方位角0°が2つ目のまとまりの向きになる
        Vector3 tiltAxis = Vector3.Cross(axis, reference);
        if (tiltAxis.sqrMagnitude < 1e-8f)
        {
            tiltAxis = HybridizationTable.AnyPerpendicular(axis);
        }
        tiltAxis.Normalize();

        HybridizationTable.GetSigmaAngles(atom.ElementType, sigmaCount, _sigmaAngles);

        // σ方向を確定させる
        Vector3[] sigmas = new Vector3[sigmaCount];
        for (int i = 0; i < sigmaCount && i < _sigmaAngles.Count; i++)
        {
            Vector2 angle = _sigmaAngles[i];
            Vector3 tilted = Quaternion.AngleAxis(angle.x, tiltAxis) * axis;
            sigmas[i] = (Quaternion.AngleAxis(angle.y, axis) * tilted).normalized;
        }

        // σ骨格が作る平面の法線。多重結合はこの平面から垂直に開く（π結合の向きに対応する）
        Vector3 frameNormal = sigmaCount > 1 ? Vector3.Cross(sigmas[0], sigmas[1]) : Vector3.zero;
        if (frameNormal.sqrMagnitude < 1e-8f)
        {
            frameNormal = HybridizationTable.AnyPerpendicular(axis);
        }
        frameNormal.Normalize();

        for (int i = 0; i < sigmaCount; i++)
        {
            ArmGroup group = groups[i];

            // 親に向かう腕は既に正しい位置に置かれているので触らない
            if (anchorNeighbor != null && group.Neighbor == anchorNeighbor) continue;

            Vector3 splayAxis = Vector3.Cross(sigmas[i], frameNormal);
            if (splayAxis.sqrMagnitude < 1e-8f)
            {
                splayAxis = HybridizationTable.AnyPerpendicular(sigmas[i]);
            }
            splayAxis.Normalize();

            HybridizationTable.GetSplayDirections(sigmas[i], splayAxis, group.Arms.Count, _splayDirs);
            AssignArms(atom, group.Arms, _splayDirs);
        }
    }

    // 方位角 0° をどちらに向けるかを決める。
    //
    // そのまま「今の向き」を使うと、鎖が同じ向きに巻き続けてしまうことがある。
    // 炭素の sp3 は 109.47° で、正五角形の内角 108° とほぼ同じなので、
    // 巻いたまま伸ばすと5個で一周し、6個目が1個目に重なってしまう
    // （ベンゼン環を1か所切ったときに実際に起きた）。
    // 1つ手前の原子と反対側へ伸ばす（アンチ配座）ことで、鎖はジグザグに伸びる
    private static Vector3 GetAzimuthReference(
        Atom atom, List<ArmGroup> groups, Atom anchorNeighbor, Atom grandparent, Vector3 axis)
    {
        // ふだんはプレイヤーが作った形をそのまま残す。
        // 鎖が巻いて自分自身に重なってしまったときだけ、ジグザグに伸ばし直す
        if (_extendChain && anchorNeighbor != null && grandparent != null && grandparent != atom)
        {
            Vector3 axisWorld = atom.transform.TransformDirection(axis);
            Vector3 perpendicular = Vector3.ProjectOnPlane(
                grandparent.transform.position - anchorNeighbor.transform.position, axisWorld);

            if (perpendicular.sqrMagnitude > 1e-8f)
            {
                return atom.transform.InverseTransformDirection(-perpendicular.normalized);
            }
        }

        if (groups.Count > 1) return GroupLocalDirection(atom, groups[1]);
        return HybridizationTable.AnyPerpendicular(axis);
    }

    // 結合手を「同じ相手に向かう束」にまとめる。
    // 余っている腕もいずれσ結合になるので、1本ずつ独立したまとまりとして数える
    private static List<ArmGroup> BuildArmGroups(Atom atom, Atom anchorNeighbor)
    {
        List<ArmGroup> groups = new List<ArmGroup>();
        List<ArmGroup> freeArms = new List<ArmGroup>();

        int armIndex = -1;
        foreach (BondPoint bp in atom.BondPoints)
        {
            armIndex++;
            // π電子の腕は環に垂直に立てるので、σ骨格の勘定には入れない。
            // これで芳香環の炭素は「環2本＋水素1本」の3方向 ＝ sp2 と判定される
            if (bp.IsPiArm) continue;

            Atom neighbor = bp.ConnectedAtom;

            if (neighbor == null)
            {
                ArmGroup free = new ArmGroup { Neighbor = null, FirstArmIndex = armIndex };
                free.Arms.Add(bp);
                freeArms.Add(free);
                continue;
            }

            ArmGroup existing = groups.Find(g => g.Neighbor == neighbor);
            if (existing == null)
            {
                existing = new ArmGroup { Neighbor = neighbor, FirstArmIndex = armIndex };
                groups.Add(existing);
            }
            existing.Arms.Add(bp);
        }

        // 鎖の続き（さらに他の原子と繋がっている相手）を先に置く。
        // 方位角 0° がアンチ配座の向きなので、そこに鎖の続きが来るとジグザグに伸びる。
        // 水素のような行き止まりの相手が先に来てしまうと、鎖が横向きに折れて巻いてしまう。
        //
        // 繋がりの数が同じときは腕の並び順で決める。List.Sort は同順位の順番を保証しないので、
        // ここで決めておかないと呼ぶたびに並びが変わり、腕の割り当てが入れ替わってしまう
        groups.Sort((x, y) =>
        {
            int compare = CountConnections(y.Neighbor).CompareTo(CountConnections(x.Neighbor));
            return compare != 0 ? compare : x.FirstArmIndex.CompareTo(y.FirstArmIndex);
        });

        // 基準にする相手を先頭に持ってくる（そこを動かさずに残りを組み立てるため）
        if (anchorNeighbor != null)
        {
            int index = groups.FindIndex(g => g.Neighbor == anchorNeighbor);
            if (index > 0)
            {
                ArmGroup anchor = groups[index];
                groups.RemoveAt(index);
                groups.Insert(0, anchor);
            }
        }

        groups.AddRange(freeArms);
        return groups;
    }

    // その原子が何個の相手と繋がっているか。行き止まり（水素など）は 1 になる
    private static int CountConnections(Atom atom)
    {
        if (atom == null || atom.BondPoints == null) return 0;

        List<Atom> neighbors = new List<Atom>();
        atom.GetDistinctNeighbors(neighbors);
        return neighbors.Count;
    }

    // まとまり全体が向いている方向（＝σ結合の向き）を原子のローカル空間で求める
    private static Vector3 GroupLocalDirection(Atom atom, ArmGroup group)
    {
        Vector3 sum = Vector3.zero;
        foreach (BondPoint bp in group.Arms)
        {
            sum += atom.transform.InverseTransformDirection(bp.Direction);
        }

        return sum.sqrMagnitude < 1e-8f ? Vector3.up : sum.normalized;
    }

    // 腕を割り当てる。今の向きに一番近い方向を選ぶことで、組み替えによる動きを最小にする
    private static void AssignArms(Atom atom, List<BondPoint> arms, List<Vector3> localDirections)
    {
        bool[] used = new bool[localDirections.Count];

        foreach (BondPoint bp in arms)
        {
            Vector3 current = atom.transform.InverseTransformDirection(bp.Direction);

            int best = -1;
            float bestDot = -2f;
            for (int i = 0; i < localDirections.Count; i++)
            {
                if (used[i]) continue;
                float dot = Vector3.Dot(current, localDirections[i]);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }

            if (best < 0) continue;
            used[best] = true;

            Vector3 worldDir = atom.transform.TransformDirection(localDirections[best]);
            bp.transform.rotation = Quaternion.FromToRotation(Vector3.up, worldDir);
        }
    }

    // === 原子の配置 ===

    // parent の腕の先に child を置き、child の腕を相手の腕先にぴったり向ける
    private static void PlaceChild(Atom parent, Atom child)
    {
        parent.GetBondPointsTo(child, _scratchArms);
        if (_scratchArms.Count == 0) return;

        BondPoint[] parentArms = _scratchArms.ToArray();
        int bondOrder = parentArms.Length;

        // parent 側の腕が開いている中心方向。これが結合の軸になる
        Vector3 sigma = Vector3.zero;
        foreach (BondPoint arm in parentArms) sigma += arm.Direction;
        if (sigma.sqrMagnitude < 1e-8f) return;
        sigma.Normalize();

        // child 側の腕が今向いている方向
        Vector3 childSigma = Vector3.zero;
        foreach (BondPoint arm in parentArms)
        {
            if (arm.ConnectedTarget != null) childSigma += arm.ConnectedTarget.Direction;
        }
        if (childSigma.sqrMagnitude < 1e-8f) return;
        childSigma.Normalize();

        // 結合の軸に沿って向かい合わせる。軸まわりの回転は今のままにして動きを最小にする
        child.transform.rotation = Quaternion.FromToRotation(childSigma, -sigma) * child.transform.rotation;

        // 原子中心どうしの距離は腕の長さと開き角から決まる。二重・三重ほど自然に短くなる
        float lengthScale = HybridizationTable.GetBondLengthMultiplier(bondOrder);
        float parentReach = parentArms[0].ArmLength;
        float childReach = ChildArmLength(parentArms);
        child.transform.position = parent.transform.position + sigma * (parentReach + childReach) * lengthScale;

        // 腕の先どうしが正確に重なるよう、child 側の腕を相手の腕先にまっすぐ向ける
        foreach (BondPoint arm in parentArms)
        {
            BondPoint childArm = arm.ConnectedTarget;
            if (childArm == null) continue;

            Vector3 toTip = arm.TipPosition - child.transform.position;
            if (toTip.sqrMagnitude < 1e-8f) continue;

            childArm.transform.rotation = Quaternion.FromToRotation(Vector3.up, toTip.normalized);
        }
    }

    private static float ChildArmLength(BondPoint[] parentArms)
    {
        foreach (BondPoint arm in parentArms)
        {
            if (arm.ConnectedTarget != null) return arm.ConnectedTarget.ArmLength;
        }
        return 0f;
    }

    // === 環の配置 ===
    //
    // 環は閉路なので、親から子へ順に置いていく方法では最後の1本が閉じない。
    // そこで「すべての原子を同じ円周上に置き、各結合をその円の弦にする」方法をとる。
    // 弦の長さが違っていても（ケクレ構造の単結合と二重結合が交互など）必ず閉じるのが利点。

    // anchor の位置を動かさずに環全体を置く。
    // outwardHint に値がある場合、anchor から見てその向きが環の外側になるように置く
    // （環の外から枝をたどって来たとき、入口の結合が正しく外を向くようにするため）
    private static void PlaceRing(List<Atom> ring, Atom anchor, Vector3? outwardHint)
    {
        int count = ring.Count;
        if (count < 3) return;

        int anchorIndex = ring.IndexOf(anchor);
        if (anchorIndex < 0) return;

        // 各結合の長さ。結合次数によって変わる
        float[] chords = new float[count];
        for (int i = 0; i < count; i++)
        {
            chords[i] = RestBondLength(ring[i], ring[(i + 1) % count]);
        }

        float radius = SolveRingRadius(chords);
        if (radius <= 0f) return;

        // 環の向きは「アンカー自身の結合手がどちらを向いているか」から決める。
        // 全原子の位置の平均から求めると、環の原子が1つ引き伸ばされているだけで
        // 重心も法線も傾き、環全体が回転して見えてしまう。
        // アンカーの腕は他の原子が動いても影響を受けないので、こちらのほうが安定する
        Atom previousOfAnchor = ring[(anchorIndex + count - 1) % count];
        Atom nextOfAnchor = ring[(anchorIndex + 1) % count];

        Vector3 toPrevious = ArmDirectionTo(anchor, previousOfAnchor);
        Vector3 toNext = ArmDirectionTo(anchor, nextOfAnchor);

        Vector3 fallbackCenter = Vector3.zero;
        foreach (Atom a in ring) fallbackCenter += a.transform.position;
        fallbackCenter /= count;

        Vector3 normal = Vector3.Cross(toPrevious, toNext);
        if (normal.sqrMagnitude < 1e-8f) normal = EstimateRingNormal(ring, fallbackCenter);
        normal.Normalize();

        // 環の外向き（中心からアンカーへ向かう半径方向）
        Vector3 radial;
        if (outwardHint.HasValue)
        {
            // 外から来た結合の向きをそのまま半径方向にして、環の面はそれに直交させる。
            // 先に面へ投影してしまうと向きが傾くので、順序を逆にしてはいけない
            radial = outwardHint.Value.normalized;

            normal = Vector3.ProjectOnPlane(normal, radial);
            if (normal.sqrMagnitude < 1e-8f) normal = HybridizationTable.AnyPerpendicular(radial);
            normal.Normalize();
        }
        else
        {
            radial = -(toPrevious + toNext);
            if (radial.sqrMagnitude < 1e-8f) radial = anchor.transform.position - fallbackCenter;

            radial = Vector3.ProjectOnPlane(radial, normal);
            if (radial.sqrMagnitude < 1e-8f) radial = HybridizationTable.AnyPerpendicular(normal);
            radial.Normalize();
        }

        Vector3 side = Vector3.Cross(normal, radial);

        // 環をたどる向き。アンカーの「次の相手」へ向かう腕がどちら側にあるかで決める
        float winding = Vector3.Dot(toNext, side) >= 0f ? 1f : -1f;

        // アンカーがちょうど円周上の 0° に来るように中心を決める。
        // こうするとアンカーは動かないので、あとから平行移動する必要がない
        Vector3 center = anchor.transform.position - radius * radial;

        Vector3[] positions = new Vector3[count];
        float angle = 0f;
        for (int k = 0; k < count; k++)
        {
            int index = (anchorIndex + k) % count;
            positions[index] = center + radius * (Mathf.Cos(angle) * radial + winding * Mathf.Sin(angle) * side);

            float halfChord = Mathf.Clamp(chords[index] / (2f * radius), -1f, 1f);
            angle += 2f * Mathf.Asin(halfChord);
        }

        for (int k = 0; k < count; k++)
        {
            ring[k].transform.position = positions[k];
        }

        // 環の結合はまず両側そろえて向ける。
        // 二重結合は腕が環の面から上下に開くので、片側ずつ決めると上下が食い違って腕先が合わなくなる
        for (int k = 0; k < count; k++)
        {
            AssignRingBondArms(ring[k], ring[(k + 1) % count], normal);
        }

        // 残りの腕（水素や置換基）を環の外側に配る
        for (int k = 0; k < count; k++)
        {
            AssignOuterArms(
                ring[k], ring[(k + count - 1) % count], ring[(k + 1) % count], center, normal);

            // π電子の腕は環に垂直に立てる。p軌道が環の面と垂直に並ぶのと同じ向き
            AlignPiArms(ring[k], normal);

            // 環のなかでも腕の向きは確定したので、記録を更新しておく。
            // あとで環から外れたときに、変化の有無を正しく判定できるようにするため
            ring[k].LayoutSignature = ring[k].ComputeGeometrySignature();
        }

        // 芳香環なら内側に円を出す（教科書の ⌬ 表記）
        if (MoleculeAromatics.IsAlreadyAromatic(ring)) AromaticRingVisual.EnsureFor(ring);
    }

    // 2つの原子が本来とるべき中心間距離
    public static float RestBondLength(Atom a, Atom b)
    {
        if (a == null || b == null) return 0f;

        a.GetBondPointsTo(b, _scratchArms);
        if (_scratchArms.Count == 0) return 0f;

        BondPoint arm = _scratchArms[0];
        float reachA = arm.ArmLength;
        float reachB = arm.ConnectedTarget != null ? arm.ConnectedTarget.ArmLength : reachA;

        // 芳香環の結合は腕1本ぶんだが、長さは単結合と二重結合の中間になる
        float multiplier = (a.IsAromatic && b.IsAromatic)
            ? HybridizationTable.AromaticBondLengthMultiplier
            : HybridizationTable.GetBondLengthMultiplier(_scratchArms.Count);

        return (reachA + reachB) * multiplier;
    }

    // π電子になった腕を環に垂直に向ける
    private static void AlignPiArms(Atom atom, Vector3 ringNormal)
    {
        if (atom.BondPoints == null) return;

        foreach (BondPoint bp in atom.BondPoints)
        {
            if (!bp.IsPiArm) continue;
            bp.transform.rotation = Quaternion.FromToRotation(Vector3.up, ringNormal);
        }
    }

    // すべての結合を弦として1つの円周に収める半径を求める。
    // 半径を大きくするほど中心角の合計は小さくなるので、二分探索で 360° になる点を探す
    private static float SolveRingRadius(float[] chords)
    {
        float longest = 0f;
        foreach (float c in chords) longest = Mathf.Max(longest, c);
        if (longest <= 0f) return 0f;

        float low = longest * 0.5f;
        float high = longest * chords.Length;

        for (int i = 0; i < 60; i++)
        {
            float mid = (low + high) * 0.5f;
            float total = 0f;
            foreach (float c in chords)
            {
                total += 2f * Mathf.Asin(Mathf.Clamp(c / (2f * mid), -1f, 1f));
            }

            if (total > 2f * Mathf.PI) low = mid;
            else high = mid;
        }
        return (low + high) * 0.5f;
    }

    // その相手に向かっている腕が向いている方向（多重結合なら平均＝σ方向）
    private static Vector3 ArmDirectionTo(Atom atom, Atom neighbor)
    {
        atom.GetBondPointsTo(neighbor, _scratchArms);
        if (_scratchArms.Count == 0) return Vector3.zero;

        Vector3 sum = Vector3.zero;
        foreach (BondPoint bp in _scratchArms) sum += bp.Direction;

        return sum.sqrMagnitude < 1e-8f ? Vector3.zero : sum.normalized;
    }

    // 今の原子の並びから環の法線を求める（ニューウェル法）
    private static Vector3 EstimateRingNormal(List<Atom> ring, Vector3 center)
    {
        Vector3 normal = Vector3.zero;
        int count = ring.Count;

        for (int i = 0; i < count; i++)
        {
            Vector3 a = ring[i].transform.position - center;
            Vector3 b = ring[(i + 1) % count].transform.position - center;
            normal += Vector3.Cross(a, b);
        }

        if (normal.sqrMagnitude < 1e-8f) normal = Vector3.up;
        return normal.normalized;
    }

    // 環のなかの1本の結合について、両側の腕をそろえて向ける。
    // まず a 側を決め、b 側は「a の腕先をまっすぐ指す」ように決めるので、
    // 二重結合が環の面から上下どちらに開くかが必ず一致する
    private static void AssignRingBondArms(Atom a, Atom b, Vector3 ringNormal)
    {
        a.GetBondPointsTo(b, _scratchArms);
        if (_scratchArms.Count == 0) return;

        // _scratchArms は使い回されるのでコピーを取る
        List<BondPoint> arms = new List<BondPoint>(_scratchArms);

        Vector3 sigma = (b.transform.position - a.transform.position).normalized;
        AssignSplayedArms(arms, sigma, ringNormal);

        foreach (BondPoint arm in arms)
        {
            BondPoint partner = arm.ConnectedTarget;
            if (partner == null) continue;

            Vector3 toTip = arm.TipPosition - b.transform.position;
            if (toTip.sqrMagnitude < 1e-8f) continue;

            partner.transform.rotation = Quaternion.FromToRotation(Vector3.up, toTip.normalized);
        }
    }

    // 環に入っていない結合手（水素や置換基、余っている腕）を環の外側に配る
    private static void AssignOuterArms(
        Atom atom, Atom previous, Atom next, Vector3 ringCenter, Vector3 ringNormal)
    {
        Vector3 position = atom.transform.position;

        // 原子は円周上にあるので、中心から見た半径方向がそのまま環の外側になる。
        // 結合長が違う環（ケクレ構造など）では2本の環結合の二等分線と少しずれるため、半径方向を使う
        Vector3 outward = Vector3.ProjectOnPlane(position - ringCenter, ringNormal);
        if (outward.sqrMagnitude < 1e-8f)
        {
            outward = -((previous.transform.position - position).normalized
                        + (next.transform.position - position).normalized);
        }
        if (outward.sqrMagnitude < 1e-8f) outward = HybridizationTable.AnyPerpendicular(ringNormal);
        outward.Normalize();

        List<ArmGroup> others = new List<ArmGroup>();
        foreach (ArmGroup group in BuildArmGroups(atom, null))
        {
            if (group.Neighbor != previous && group.Neighbor != next) others.Add(group);
        }
        if (others.Count == 0) return;

        // 1本なら真外側、2本なら環の面から上下に開く（sp3 相当）
        List<Vector3> outerSigmas = new List<Vector3>();
        if (others.Count == 1)
        {
            outerSigmas.Add(outward);
        }
        else
        {
            float half = 0.5f * HybridizationTable.GetBondAngle(atom.ElementType, 4) * Mathf.Deg2Rad;
            outerSigmas.Add(Mathf.Cos(half) * outward + Mathf.Sin(half) * ringNormal);
            outerSigmas.Add(Mathf.Cos(half) * outward - Mathf.Sin(half) * ringNormal);
        }

        for (int i = 0; i < others.Count && i < outerSigmas.Count; i++)
        {
            AssignSplayedArms(others[i].Arms, outerSigmas[i].normalized, ringNormal);
        }
    }

    // σ方向のまわりに腕を開いて割り当てる（多重結合は環の面から上下に開く）
    private static void AssignSplayedArms(List<BondPoint> arms, Vector3 sigma, Vector3 ringNormal)
    {
        Vector3 splayAxis = Vector3.Cross(sigma, ringNormal);
        if (splayAxis.sqrMagnitude < 1e-8f) splayAxis = HybridizationTable.AnyPerpendicular(sigma);
        splayAxis.Normalize();

        HybridizationTable.GetSplayDirections(sigma, splayAxis, arms.Count, _splayDirs);
        AssignArmsWorld(arms, _splayDirs);
    }

    // AssignArms のワールド座標版。環の配置は位置から直接方向が決まるのでこちらを使う
    private static void AssignArmsWorld(List<BondPoint> arms, List<Vector3> worldDirections)
    {
        bool[] used = new bool[worldDirections.Count];

        foreach (BondPoint bp in arms)
        {
            Vector3 current = bp.Direction;

            int best = -1;
            float bestDot = -2f;
            for (int i = 0; i < worldDirections.Count; i++)
            {
                if (used[i]) continue;
                float dot = Vector3.Dot(current, worldDirections[i]);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }

            if (best < 0) continue;
            used[best] = true;
            bp.transform.rotation = Quaternion.FromToRotation(Vector3.up, worldDirections[best]);
        }
    }
}
