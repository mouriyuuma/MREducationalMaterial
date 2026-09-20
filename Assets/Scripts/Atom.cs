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
    }
}
