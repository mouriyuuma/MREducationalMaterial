using System.Collections.Generic;
using UnityEngine;

// 責任：VRの掴む/離す操作と、結合・切断の成立を担当する
//
// 結合次数は「同じ相手に腕を何本かけたか」で決まる。分子模型キットと同じで、
// 腕を2本ひっかければ二重結合、3本なら三重結合になる。
// 手を離した瞬間に重なっている腕のペアをすべて拾って、まとめて結合する。
[RequireComponent(typeof(Atom), typeof(Rigidbody))]
public class AtomInteraction : MonoBehaviour
{
    // 三重結合まで対応する
    public const int MaxBondOrder = 3;

    // 腕先どうしがこれだけ離れたら結合が切れる。
    // 原子の中心間距離ではなく腕先の隙間で測るので、白い線の長さがそのまましきい値になり、
    // どの向きに引っ張っても同じ見た目で切れる
    public const float BreakGap = 0.08f;

    // 多重結合は1本増えるごとにこれだけ切れにくくする
    public const float BreakGapPerExtraBond = 0.02f;

    // 問題の切り替えやアプリ終了で原子をまとめて消している最中は true。
    // 消えていく原子どうしが互いに分子を組み直そうとして壊れたオブジェクトに触るのを防ぐ
    public static bool IsTearingDown { get; set; }

    public bool IsGrabbed { get; private set; }

    // 相棒の原子を参照するために公開する
    public Atom Atom => _atom;

    private Atom _atom;
    private Rigidbody _rb;

    // プレビュー用の線。何重結合になるかを結合前に見せるため、最大3本ぶん用意する
    private LineRenderer[] _previewLines;

    // すでに結合している腕を示す線。引っ張っている間だけ現れ、切れた瞬間に消える
    private LineRenderer[] _bondLines;

    // 掴んだ瞬間に控えた、自分から見た分子の形。これを保ったまま運ぶ
    private readonly Dictionary<Atom, Pose> _carryPoses = new Dictionary<Atom, Pose>();
    private bool _carryValid;

    // このフレームで自分が運ぶ範囲（相手を掴んでいればその結合の手前まで）
    private readonly List<Atom> _carryScope = new List<Atom>();

    // 手の向きのブレを均したあとの、自分の向き
    private Quaternion _smoothedSelfRotation;

    // 補間中に「手がどれだけ動いたか」を測るための、前フレームの手の姿勢
    private Vector3 _prevHandPosition;
    private Quaternion _prevHandRotation;
    private bool _hasPrevHandPose;

    // 隣り合わない2原子を掴んだとき、分子を剛体に保つための基準
    private AtomInteraction _twoHandPartner;
    private Vector3 _twoHandRestSelf;
    private Vector3 _twoHandRestPartner;
    private Quaternion _twoHandRestRotation;

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

    // すでに結合している腕を示す線の色。これから結合する線（緑〜橙）と区別できるよう白にする
    private static readonly Color HoldingColor = Color.white;

    // 1つの原子が持つ結合手の最大数（炭素の4本）
    private const int MaxArmsPerAtom = 4;

    // 分子の向きが手の動きに追従する速さ。大きいほど機敏、小さいほど滑らか。
    // 25 なら時定数は約40ミリ秒で、手のブレは消えるが遅れはほとんど感じない
    private const float RotationSmoothing = 25f;

    // 腕先の隙間がこれ以下なら線を出さない。
    // 結合しているとき腕先はぴったり重なるので、引っ張って離れて初めて線が現れる
    private const float MinVisibleGap = 0.012f;

    private static readonly System.Comparison<BondCandidate> ByDistance =
        (a, b) => a.Distance.CompareTo(b.Distance);

    private void Awake()
    {
        _atom = GetComponent<Atom>();
        _rb = GetComponent<Rigidbody>();

        // これから結合する腕を示す線（結合次数で色が変わる）
        _previewLines = CreateLines("BondPreview", MaxBondOrder);
        ApplyPreviewColors(1);

        // すでに結合している腕を示す線。引っ張って隙間ができた分だけ伸びる
        _bondLines = CreateLines("BondHolding", MaxArmsPerAtom);
        foreach (LineRenderer line in _bondLines)
        {
            line.startColor = HoldingColor;
            line.endColor = HoldingColor;
        }
    }

    private LineRenderer[] CreateLines(string label, int count)
    {
        LineRenderer[] lines = new LineRenderer[count];

        for (int i = 0; i < count; i++)
        {
            // 原子の子にすると原子側のスケール(0.1)で線が細くなってしまうので、親を付けずにワールドに置く
            GameObject holder = new GameObject($"{label}_{name}_{i}");
            LineRenderer line = holder.AddComponent<LineRenderer>();

            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = 0.008f;
            line.endWidth = 0.008f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.enabled = false;

            lines[i] = line;
        }
        return lines;
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

        // 掴んだ「この瞬間」の分子の形を控える。
        // LateUpdate まで待つと、そのフレームの手の移動がすでに反映されていて、
        // そのぶんが基準に焼き付いてズレの原因になる
        CaptureCarry();
    }

    // VRで離された時 (Wrapperから呼ばれる)
    public void OnReleased()
    {
        IsGrabbed = false;

        // MR空間でのピタッと止まるブレーキ
        if (!_rb.isKinematic)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        // 結合できる相手を探して結合処理を実行
        if (TryConnect()) return;

        // 結合しなかった場合でも組み直す。引っ張って伸びたままの形を戻すのと、
        // 掴んでいる間は持ち越していた切断後の組み直しを、ここでまとめて行う
        MoleculeLayout.Rebuild(FindLayoutSeed(_atom));
    }

    // === 結合の成立 ===

    private bool TryConnect()
    {
        Atom targetAtom = FindBestConnection(_bestPairs);
        if (targetAtom == null || _bestPairs.Count == 0) return false;

        ExecuteConnection(targetAtom, _bestPairs);
        return true;
    }

    // 並べ直しの基準にする原子。基準にした原子は動かず、まわりがそれに合わせて配置される。
    //
    // まだ手に持たれている原子があればそれを優先する（分子が手から飛び出さないように）。
    // いなければ preferred をそのまま使う。
    // 新しくくっつけた原子を基準にしてしまうと、その1個に合わせて分子全体が動いてしまうので、
    // 呼ぶ側は「もともと大きかったほうの分子」を渡すこと
    private static Atom FindLayoutSeed(Atom preferred)
    {
        if (preferred == null) return null;

        foreach (Atom a in MoleculeLayout.CollectMolecule(preferred))
        {
            AtomInteraction other = a.GetComponent<AtomInteraction>();
            if (other != null && other.IsGrabbed) return a;
        }
        return preferred;
    }

    // まだ手に持たれている原子がある分子は組み直さない（手を離したときに行う）
    private static void RebuildIfReleased(Atom atom)
    {
        if (atom == null) return;

        foreach (Atom a in MoleculeLayout.CollectMolecule(atom))
        {
            AtomInteraction interaction = a.GetComponent<AtomInteraction>();
            if (interaction != null && interaction.IsGrabbed) return;
        }

        MoleculeLayout.Rebuild(atom);
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

        // くっつける前に、どちらが大きい分子かを調べておく。
        // 小さいほう（たいていは今手に持っている1個）を基準にすると、
        // 大きい分子のほうが動いて既存の原子の配置が入れ替わってしまう
        int mySize = MoleculeLayout.CollectMolecule(_atom).Count;
        int targetSize = MoleculeLayout.CollectMolecule(targetAtom).Count;
        Atom largerSide = targetSize > mySize ? targetAtom : _atom;

        // 分子の形は MoleculeLayout が、掴んだときの移動は LateUpdate の追従が受け持つ。
        // 物理に任せる部分はないので kinematic にしておく。
        // 非 kinematic にして FixedJoint で繋ぐと、両手で離れた2原子を掴んだときに
        // 間の原子が両側から引っ張られて暴れてしまう
        _rb.isKinematic = true;
        _rb.useGravity = false;
        targetRb.isKinematic = true;
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

        // 混成軌道の組み替えと原子の並べ直し。
        // もともと大きかったほうの分子を基準にして、既存の配置が動かないようにする
        MoleculeLayout.Rebuild(FindLayoutSeed(largerSide));

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
            UpdateBondLines();         // まだ繋がっている腕も見せる
        }
        else
        {
            HidePreview(); // 離したら線を消す
        }
    }

    // すでに結合している腕を、腕先どうしを結ぶ線で示す。
    //
    // 結合しているとき腕先はぴったり重なっているので線の長さはゼロ（＝見えない）。
    // 引っ張って隙間ができた分だけ線が伸びるので、
    // 「引っ張れているが、まだ繋がっている」ことが一目で分かる。
    // 結合が切れれば線は消えるので、分離できたかどうかを手を離す前に確かめられる。
    //
    // 出すのは切断の判定対象になっている結合（両手で掴んでいる2原子の間）だけ。
    // 切れない結合にまで線を出すと、いくら引いても線が消えず、
    // 「引っ張り足りないのか操作が違うのか」が分からなくなる
    private void UpdateBondLines()
    {
        int used = 0;

        if (_atom.BondPoints != null)
        {
            foreach (BondPoint bp in _atom.BondPoints)
            {
                if (used >= _bondLines.Length) break;
                if (!bp.IsConnected || bp.ConnectedTarget == null) continue;

                Atom partner = bp.ConnectedAtom;
                AtomInteraction partnerInteraction =
                    partner != null ? partner.GetComponent<AtomInteraction>() : null;
                if (partnerInteraction == null || !partnerInteraction.IsGrabbed) continue;

                Vector3 from = bp.TipPosition;
                Vector3 to = bp.ConnectedTarget.TipPosition;
                if (Vector3.Distance(from, to) < MinVisibleGap) continue;

                LineRenderer line = _bondLines[used++];
                line.enabled = true;
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }
        }

        for (int i = used; i < _bondLines.Length; i++) _bondLines[i].enabled = false;
    }

    // 掴まれた原子の動きに合わせて、分子全体（親グループ）を動かす
    // 掴んだ原子が「自分の側の塊」を運ぶ。片手でも両手でも同じ仕組みで動かす。
    //
    // 片手と両手で別々の仕組みにしていたときは、切り替わる瞬間に基準を取り直す必要があり、
    // その時点ですでに手が動いていたぶん（1フレーム分）が基準に焼き付いてズレていた。
    // 運ぶ形を1つにすれば取り直し自体が要らなくなる
    private void LateUpdate()
    {
        if (!IsGrabbed)
        {
            _twoHandPartner = null;
            return;
        }

        // 組み替えの補間が走っている間は、形は補間に任せて、手が動いたぶんだけ付いていく
        if (MoleculeAnimator.IsBusy)
        {
            FollowDuringAnimation();
            return;
        }
        _hasPrevHandPose = false;

        // 基準は掴んだ瞬間に控えてある。手で動かされる前の、正しい分子の形
        if (!_carryValid) CaptureCarry();

        AtomInteraction partner = FindOtherGrabbedInGroup();
        bool adjacent = partner != null && _atom.GetBondOrderTo(partner.Atom) > 0;

        if (partner != null && !adjacent)
        {
            // 隣り合わない2原子：どこも切れない＝模型は変形しないので、分子まるごと剛体で追従する
            CarryWholeMoleculeRigidly(partner);
            return;
        }

        _twoHandPartner = null;

        // 運ぶ範囲を決める。
        // 隣り合う相手がいるならその結合の手前まで（相手側は相手の手が運ぶ）、
        // いなければ繋がっている分子全体
        if (adjacent) CollectMySide(partner.Atom);
        else MoleculeLayout.CollectFragment(_atom, null, _carryScope);

        SmoothOwnRotation();
        ApplyCarry();
    }

    // 組み替えの補間が走っている間の追従。
    //
    // 補間は親（MoleculeGroup）から見たローカル姿勢を動かしているので、
    // こちらが絶対位置で並べ直すと補間が毎フレーム打ち消されてしまう。
    // そこで「手がこのフレームで動いたぶん」だけ親をずらす。
    // 形は補間が決め、位置と向きは手が決める、という分担になる
    private void FollowDuringAnimation()
    {
        Transform group = transform.parent;
        if (group == null)
        {
            _hasPrevHandPose = false;
            return;
        }

        SmoothOwnRotation();

        Vector3 hand = transform.position;
        Quaternion handRotation = transform.rotation;

        if (!_hasPrevHandPose)
        {
            _prevHandPosition = hand;
            _prevHandRotation = handRotation;
            _hasPrevHandPose = true;
            return;
        }

        // 掴んだ原子を中心に、手の回転ぶんだけ回してから、動いたぶんだけ平行移動する
        Quaternion deltaRotation = handRotation * Quaternion.Inverse(_prevHandRotation);
        Vector3 deltaPosition = hand - _prevHandPosition;

        group.rotation = deltaRotation * group.rotation;
        group.position = _prevHandPosition
                         + deltaRotation * (group.position - _prevHandPosition)
                         + deltaPosition;

        // 親を動かすと掴んだ原子もつられて動くので、手の位置に戻す
        transform.position = hand;
        transform.rotation = handRotation;

        _prevHandPosition = hand;
        _prevHandRotation = handRotation;
    }

    // 掴んだ瞬間の分子の形を、自分から見た相対姿勢として控える。
    // 相手側の原子も含めて全部控えておけば、片手に戻ったときも取り直さずに済む
    private void CaptureCarry()
    {
        _carryPoses.Clear();

        Vector3 origin = transform.position;
        Quaternion inverse = Quaternion.Inverse(transform.rotation);

        foreach (Atom a in MoleculeLayout.CollectMolecule(_atom))
        {
            _carryPoses[a] = new Pose(inverse * (a.transform.position - origin),
                                      inverse * a.transform.rotation);
        }

        _smoothedSelfRotation = transform.rotation;
        _carryValid = true;
    }

    // 相手との結合で分けたときの、自分の側の塊を求める
    private void CollectMySide(Atom partnerAtom)
    {
        MoleculeLayout.CollectFragment(_atom, partnerAtom, _carryScope);

        // 環のなかの結合だと別の道で相手に届いてしまい、2つに分けられない。
        // その場合は「相手の原子だけが向こう側」として扱う
        if (_carryScope.Contains(partnerAtom)) _carryScope.Remove(partnerAtom);
    }

    // 手の向きのブレがそのまま塊全体に伝わると、離れた原子ほど大きく震えて見える。
    // 掴んだ原子の向きだけ均してから運ぶことで、塊ごと滑らかに動く。
    // 位置は均さないので、掴んだ原子は手から遅れない
    private void SmoothOwnRotation()
    {
        float t = 1f - Mathf.Exp(-RotationSmoothing * Time.deltaTime);
        _smoothedSelfRotation = Quaternion.Slerp(_smoothedSelfRotation, transform.rotation, t);
        transform.rotation = _smoothedSelfRotation;
    }

    // 控えた相対姿勢のまま、自分の側の塊を連れていく
    private void ApplyCarry()
    {
        Vector3 origin = transform.position;
        Quaternion rotation = transform.rotation;

        foreach (Atom a in _carryScope)
        {
            if (a == null || a == _atom) continue;
            if (!_carryPoses.TryGetValue(a, out Pose pose)) continue;

            a.transform.position = origin + rotation * pose.position;
            a.transform.rotation = rotation * pose.rotation;
        }
    }

    // 親の回転を、目標へ滑らかに近づける。
    //
    // 手の姿勢には細かいブレが常にあり、親を回すとそのブレが「回転量 × 距離」で
    // 離れた原子ほど大きく現れる（掴んだ原子は手に乗っているので揺れて見えない）。
    // そのまま反映すると分子が小刻みに震えるので、回転だけ時定数をかけて均す。
    // 位置は平滑化しないので、掴んだ原子は手の位置から遅れない
    private static void ApplySmoothedRotation(Transform group, Quaternion targetRotation)
    {
        float t = 1f - Mathf.Exp(-RotationSmoothing * Time.deltaTime);
        group.rotation = Quaternion.Slerp(group.rotation, targetRotation, t);
    }

    // 隣り合わない2原子を掴んだ場合。
    //
    // どこも切れない＝模型は変形しないので、分子は形を保ったまま両手に合わせて動く。
    //   ・向き … 2原子を結ぶ向きが両手を結ぶ向きに合うように回す
    //   ・位置 … 2原子の中点が両手の中点に来るように動かす
    // 掴んだ原子は手から多少ずれるが、そのかわり原子どうしの間に隙間は一切できない
    private void CarryWholeMoleculeRigidly(AtomInteraction partner)
    {
        // 2原子が同じことをすると打ち消し合うので、片方だけが分子を動かす
        if (GetInstanceID() > partner.GetInstanceID())
        {
            _twoHandPartner = null;
            return;
        }

        // 両手持ちが始まった時点の配置を基準にする
        if (_twoHandPartner != partner)
        {
            _twoHandPartner = partner;
            _twoHandRestSelf = transform.position;
            _twoHandRestPartner = partner.transform.position;
            _twoHandRestRotation = transform.rotation;
            return;
        }

        Vector3 selfTarget = transform.position;
        Vector3 partnerTarget = partner.transform.position;

        Vector3 restDirection = _twoHandRestPartner - _twoHandRestSelf;
        Vector3 handDirection = partnerTarget - selfTarget;
        if (restDirection.sqrMagnitude < 1e-8f || handDirection.sqrMagnitude < 1e-8f) return;

        // 基準の姿勢を、両手を結ぶ向きに合わせて回す
        Quaternion targetRotation =
            Quaternion.FromToRotation(restDirection, handDirection) * _twoHandRestRotation;

        float t = 1f - Mathf.Exp(-RotationSmoothing * Time.deltaTime);
        _smoothedSelfRotation = Quaternion.Slerp(_smoothedSelfRotation, targetRotation, t);

        // 自分を基準に分子を組み立て直し、2原子の中点が両手の中点に来るようにずらす
        transform.rotation = _smoothedSelfRotation;
        transform.position = selfTarget;

        MoleculeLayout.CollectFragment(_atom, null, _carryScope);
        ApplyCarry();

        Vector3 offset = (selfTarget + partnerTarget) * 0.5f
                         - (transform.position + partner.transform.position) * 0.5f;

        transform.position += offset;
        foreach (Atom a in _carryScope)
        {
            if (a != null && a != _atom) a.transform.position += offset;
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
        Hide(_previewLines);
        Hide(_bondLines);
    }

    private static void Hide(LineRenderer[] lines)
    {
        if (lines == null) return;
        foreach (LineRenderer line in lines)
        {
            if (line != null) line.enabled = false;
        }
    }

    // === 切断 ===

    // 引っ張りを検知して結合を引きちぎるロジック
    private void CheckBondDistances()
    {
        // 補間の最中は原子が移動している途中なので、その距離で切ってしまわないようにする
        if (MoleculeAnimator.IsBusy) return;

        _atom.GetDistinctNeighbors(_neighbors);
        if (_neighbors.Count == 0) return;

        // GetDistinctNeighbors は共有リストなので、切断でリストが変わる前にコピーしておく
        Atom[] neighbors = _neighbors.ToArray();

        // いちばん伸びている結合を選ぶ。「引っ張った結合が切れる」となるので予想しやすい。
        //
        // 対象は「両手で掴んでいる2原子の間の結合」だけに限る。
        // 掴んでいない相手との結合まで見ると、次の2つの問題が起きる:
        //   ・切りたいところではない結合が切れる
        //   ・この判定は Update、分子の追従は LateUpdate で走るため、
        //     片手で素早く動かすと「掴んだ原子だけ動いて残りが追いついていない」瞬間を拾って切れてしまう
        Atom mostStretched = null;
        float worstExcess = 0f;

        foreach (Atom targetAtom in neighbors)
        {
            AtomInteraction targetInteraction = targetAtom.GetComponent<AtomInteraction>();
            if (targetInteraction == null || !targetInteraction.IsGrabbed) continue;

            // 両側から二重に処理しないよう、片方だけが判定する
            if (gameObject.GetInstanceID() < targetAtom.gameObject.GetInstanceID()) continue;

            int order = _atom.GetBondOrderTo(targetAtom);
            if (order <= 0) continue;

            float gap = GetBondGap(targetAtom);
            float limit = BreakGap + BreakGapPerExtraBond * (order - 1);

            float excess = gap - limit;
            if (excess > worstExcess)
            {
                worstExcess = excess;
                mostStretched = targetAtom;
            }
        }

        if (mostStretched != null) BreakBond(mostStretched);
    }

    // 相手との結合で、腕先どうしがどれだけ離れているか（多重結合なら平均）。
    // 白い線が示している長さそのもの
    private float GetBondGap(Atom targetAtom)
    {
        _atom.GetBondPointsTo(targetAtom, _armsToNeighbor);

        float total = 0f;
        int count = 0;

        foreach (BondPoint bp in _armsToNeighbor)
        {
            if (bp.ConnectedTarget == null) continue;
            total += Vector3.Distance(bp.TipPosition, bp.ConnectedTarget.TipPosition);
            count++;
        }

        return count > 0 ? total / count : 0f;
    }

    // 相手の原子との結合を完全に引きちぎる。多重結合なら使っている腕をすべて外す
    public void BreakBond(Atom targetAtom)
    {
        if (targetAtom == null) return;

        int order = _atom.GetBondOrderTo(targetAtom);
        if (order == 0) return;

        // 1. 結合時に無視していた「原子同士のコリジョン」を復活させる
        SetCollisionIgnored(targetAtom, false);

        // 2. データレイヤーの切断処理。多重結合でかけている腕をすべて外す
        _atom.GetBondPointsTo(targetAtom, _armsToNeighbor);
        foreach (BondPoint myBond in new List<BondPoint>(_armsToNeighbor))
        {
            if (myBond.ConnectedTarget != null) myBond.ConnectedTarget.Disconnect();
            myBond.Disconnect();
        }

        Debug.Log($"【結合切断】{_atom.ElementType} と {targetAtom.ElementType} の{order}重結合が引きちぎられました！");

        // 3. 分子グループの再構築（切断によって独立した原子・分子を分ける）
        UpdateMoleculeGrouping(_atom);
        UpdateMoleculeGrouping(targetAtom);

        // 4. 離れた両側とも、結合が減ったぶん混成が戻る（sp2 → sp3 など）。
        // ただし、まだ手に持たれている分子はこの場では組み直さない。
        // 切った瞬間に分子全体が作り直されると、手の中の原子が鎖の遠い端へ飛ばされたように見えるため、
        // 手を離したとき（OnReleased）にまとめて組み直す
        RebuildIfReleased(_atom);
        RebuildIfReleased(targetAtom);

        // 5. 構造が変わったことをManagerに報告
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

        DestroyLines(_previewLines);
        DestroyLines(_bondLines);
    }

    private static void DestroyLines(LineRenderer[] lines)
    {
        if (lines == null) return;
        foreach (LineRenderer line in lines)
        {
            if (line == null) continue;
            if (line.material != null) Destroy(line.material);
            Destroy(line.gameObject);
        }
    }

    // === コライダーのヘルパー ===

    // 分子を物理で固定するのはやめたので、残っている FixedJoint があれば取り除く。
    // 以前のバージョンで作られた Joint や、シーンに置かれたままのものへの保険
    public void DestroyAllJoints()
    {
        foreach (FixedJoint joint in GetComponents<FixedJoint>())
        {
            Destroy(joint);
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

        // 使い回せる既存のMoleculeGroupを探す。
        //
        // 条件は「そのグループの中身が、今の分子の原子だけでできていること」。
        // 切断で分子が2つに割れたとき、単に既存グループを見つけて使うと
        // 両方の断片が同じグループに残り、結合が切れているのに一緒に動いてしまう
        GameObject targetGroup = null;
        foreach (Atom a in connectedAtoms)
        {
            Transform parent = a.transform.parent;
            if (parent == null || !parent.name.StartsWith("MoleculeGroup")) continue;

            bool holdsOnlyThisMolecule = true;
            foreach (Transform child in parent)
            {
                Atom childAtom = child.GetComponent<Atom>();
                if (childAtom == null || !connectedAtoms.Contains(childAtom))
                {
                    holdsOnlyThisMolecule = false; // 別の分子になった原子が混じっている
                    break;
                }
            }

            if (holdsOnlyThisMolecule)
            {
                targetGroup = parent.gameObject;
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

                // 運ぶときの基準は「掴んだ原子から見た相対姿勢」なので、
                // 親が変わっても取り直す必要はない。
                // ここで取り直すと、そのフレームの手の移動が基準に焼き付いてズレる

                if (oldParent != null && oldParent != targetGroup.transform && oldParent.childCount == 0)
                {
                    Destroy(oldParent.gameObject);
                }
            }
        }
    }

    // 分子の形が変わったあと、MoleculeLayout から呼ばれる。
    // 並べ直しが終わった直後は分子が正しい形になっているので、ここで控えれば正確
    public void RefreshCarryReference()
    {
        if (IsGrabbed) CaptureCarry();
    }

    // 同じ分子のなかで、他に掴まれている原子を探す（両手持ちの相棒）
    private AtomInteraction FindOtherGrabbedInGroup()
    {
        if (transform.parent == null) return null;

        AtomInteraction[] siblings = transform.parent.GetComponentsInChildren<AtomInteraction>();
        foreach (var sibling in siblings)
        {
            if (sibling != this && sibling.IsGrabbed) return sibling;
        }
        return null;
    }
}
