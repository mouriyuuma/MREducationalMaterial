using System.Collections.Generic;
using UnityEngine;

// 責任：原子の基本データと、自分が持つ結合手(BondPoint)を管理するだけ
public class Atom : MonoBehaviour
{
    // 芳香環の結合を表す番号。MOLファイルの慣例にならって 4 を使う。
    // 単結合と二重結合の中間（1.5重結合）にあたるので、1〜3 とは別枠にしている
    public const int AromaticBondOrder = 4;

    [Header("Atom Data")]
    public string ElementType; // "C", "H", "O" など

    // 芳香環（ベンゼン環）の一員になっているか。MoleculeAromatics が設定する
    public bool IsAromatic { get; set; }

    // 自分が持っている結合手のリスト
    public BondPoint[] BondPoints { get; private set; }

    // この原子の最大結合手数（プレハブに付けたBondPointの数＝価数）
    // 例: 酸素のプレハブにBondPointが2つ付いていれば、MaxValencyは 2 になる
    public int MaxValency => BondPoints != null ? BondPoints.Length : 0;

    // 現在使っている結合手の本数
    // 二重結合は「同じ相手に腕を2本使う」ので、単純な本数の合計で価数と一致する。
    // 芳香環のπ電子になった腕は相手こそいないが電子を使っているので、使用中として数える
    public int GetUsedValency()
    {
        int used = 0;
        if (BondPoints == null) return 0;
        foreach (var bp in BondPoints)
        {
            if (bp.IsConnected || bp.IsPiArm) used++;
        }
        return used;
    }

    // 今「余っている」結合手数
    public int AvailableValency => MaxValency - GetUsedValency();

    // 相手の原子との結合次数。その原子に向かっている腕の本数がそのまま何重結合かを表す
    // (1本=単結合, 2本=二重結合, 3本=三重結合)
    // 芳香環どうしの結合だけは本数では表せないので AromaticBondOrder を返す
    public int GetBondOrderTo(Atom other)
    {
        if (other == null || BondPoints == null) return 0;

        int order = 0;
        foreach (var bp in BondPoints)
        {
            if (bp.IsConnectedTo(other)) order++;
        }

        if (order > 0 && IsAromatic && other.IsAromatic) return AromaticBondOrder;
        return order;
    }

    // その相手に向かっている腕を集める
    public void GetBondPointsTo(Atom other, List<BondPoint> result)
    {
        result.Clear();
        if (other == null || BondPoints == null) return;

        foreach (var bp in BondPoints)
        {
            if (bp.IsConnectedTo(other)) result.Add(bp);
        }
    }

    // 前回レイアウトしたときの σ骨格の形。
    // これが変わっていなければ結合手はすでに正しい向きを向いているので、配置し直さない
    public int LayoutSignature { get; set; }

    // σ骨格の形を表す値。
    //
    // 見るのは「どの原子と繋がっているか」ではなく「何方向に、何本ずつの腕を出しているか」。
    // 空いている腕に原子が1つ付いただけなら、腕の本数も向きも変わらないので配置し直す必要がない。
    // 逆に二重結合ができて腕が1方向にまとまると本数が変わるので、sp3 から sp2 へ組み替える。
    //
    // 相手の identity を見てしまうと、原子が1つ増えるたびに既存の腕まで割り当て直され、
    // せっかく狙って繋いだ原子が別の腕の原子と入れ替わってしまう
    public int ComputeGeometrySignature()
    {
        if (BondPoints == null) return 0;

        List<Atom> seen = new List<Atom>();
        List<int> armsPerDirection = new List<int>();
        int freeArms = 0;

        foreach (var bp in BondPoints)
        {
            if (bp.IsPiArm) continue; // π電子は σ骨格に数えない

            Atom neighbor = bp.ConnectedAtom;
            if (neighbor == null)
            {
                freeArms++; // 空いている腕もいずれσ結合になるので1方向ぶんとして数える
                continue;
            }

            int index = seen.IndexOf(neighbor);
            if (index < 0)
            {
                seen.Add(neighbor);
                armsPerDirection.Add(1);
            }
            else
            {
                armsPerDirection[index]++;
            }
        }

        for (int i = 0; i < freeArms; i++) armsPerDirection.Add(1);
        armsPerDirection.Sort();

        int hash = IsAromatic ? 17 : 19;
        foreach (int count in armsPerDirection) hash = hash * 31 + count;
        return hash;
    }

    // 繋がっている「異なる原子」の一覧。多重結合でも相手は1つとして数える
    public void GetDistinctNeighbors(List<Atom> result)
    {
        result.Clear();
        if (BondPoints == null) return;

        foreach (var bp in BondPoints)
        {
            Atom neighbor = bp.ConnectedAtom;
            if (neighbor != null && !result.Contains(neighbor)) result.Add(neighbor);
        }
    }

    private void Awake()
    {
        // 起動時に自分の子供にあるBondPointをすべて取得して記憶しておく
        BondPoints = GetComponentsInChildren<BondPoint>();

        // BondPointに親（自分）を教える
        foreach (var bp in BondPoints)
        {
            bp.Initialize(this);
        }

        // プレハブの結合手の配置（炭素なら正四面体）はすでに正しいので、
        // それを「レイアウト済み」として記録しておく。
        // これをしないと、生成したばかりの原子が初めて結合したときだけ
        // 形が変わったと誤判定され、無関係な腕まで配置し直されてしまう
        LayoutSignature = ComputeGeometrySignature();
    }
}
