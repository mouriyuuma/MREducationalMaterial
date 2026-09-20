using System.Collections.Generic;
using UnityEngine;

// 責任：ベンゼン環（芳香環）を見つけて、等価な結合に組み替える
//
// 高校化学ではベンゼンを「単結合と二重結合が交互のケクレ構造」で導入したあと、
// 「実際は6本とも等価で、単結合と二重結合の中間」と教える。
// このクラスはその2段目を担当する。プレイヤーがケクレ構造を組み上げた瞬間に、
// 二重結合から腕を1本ずつ解放して環に垂直に立てる。解放された6本がそのままπ電子で、
// 見た目は環の内側の円としてまとめて描かれる。
//
//   変換前（ケクレ）              変換後（芳香環）
//     C: 環に2本＋1本              C: 環に1本ずつ×2
//        ＋H に1本      = 4           ＋H に1本
//                                     ＋π として1本  = 4
//
// 腕の本数が環の6方向で等しくなるので、結合長も自動的に等価になる。
// 価数も 4 で閉じるため、環に余計な原子がくっつくこともない。
public static class MoleculeAromatics
{
    private const int RingSize = 6;

    private static readonly List<BondPoint> _armsBuffer = new List<BondPoint>();

    // 分子全体を見て、芳香環になっている環を最新の状態にする。
    // 環が壊れた原子は芳香族の指定を解除する
    public static void Update(List<Atom> atoms, List<List<Atom>> rings)
    {
        HashSet<Atom> stillAromatic = new HashSet<Atom>();

        foreach (List<Atom> ring in rings)
        {
            // すでに変換済みならそのまま保つ
            if (IsAlreadyAromatic(ring))
            {
                foreach (Atom atom in ring) stillAromatic.Add(atom);
                continue;
            }

            // ケクレ構造が完成した瞬間に変換する
            if (IsKekuleBenzene(ring))
            {
                Aromatize(ring);
                foreach (Atom atom in ring) stillAromatic.Add(atom);
            }
        }

        foreach (Atom atom in atoms)
        {
            if (atom.IsAromatic && !stillAromatic.Contains(atom)) ClearAromatic(atom);
        }
    }

    // 変換済みの芳香環か。炭素6個がすべて芳香族で、それぞれπ電子の腕を1本持っている状態
    public static bool IsAlreadyAromatic(List<Atom> ring)
    {
        if (ring.Count != RingSize) return false;

        foreach (Atom atom in ring)
        {
            if (atom.ElementType != "C" || !atom.IsAromatic) return false;
            if (CountPiArms(atom) != 1) return false;
        }
        return true;
    }

    // ケクレ構造（炭素6個の環で、単結合と二重結合が交互）になっているか
    private static bool IsKekuleBenzene(List<Atom> ring)
    {
        if (ring.Count != RingSize) return false;

        foreach (Atom atom in ring)
        {
            if (atom.ElementType != "C") return false;
        }

        int[] orders = new int[RingSize];
        for (int i = 0; i < RingSize; i++)
        {
            ring[i].GetBondPointsTo(ring[(i + 1) % RingSize], _armsBuffer);
            orders[i] = _armsBuffer.Count;
        }

        for (int i = 0; i < RingSize; i++)
        {
            // 単結合か二重結合のどちらかで、かつ隣どうしが違う値であれば交互になっている
            if (orders[i] != 1 && orders[i] != 2) return false;
            if (orders[i] == orders[(i + 1) % RingSize]) return false;
        }
        return true;
    }

    // 二重結合から腕を1本ずつ解放して、π電子として立てる
    private static void Aromatize(List<Atom> ring)
    {
        for (int i = 0; i < RingSize; i++)
        {
            Atom a = ring[i];
            Atom b = ring[(i + 1) % RingSize];

            a.GetBondPointsTo(b, _armsBuffer);
            if (_armsBuffer.Count < 2) continue;

            // 2本目の腕だけ外す。1本目は環の結合として残るので、環は切れない
            BondPoint mine = _armsBuffer[1];
            BondPoint theirs = mine.ConnectedTarget;

            mine.Disconnect();
            mine.SetPiArm(true);

            if (theirs != null)
            {
                theirs.Disconnect();
                theirs.SetPiArm(true);
            }
        }

        foreach (Atom atom in ring) atom.IsAromatic = true;

        Debug.Log("【芳香環】ベンゼン環ができました。6本の結合はすべて等価になります。");
    }

    private static void ClearAromatic(Atom atom)
    {
        atom.IsAromatic = false;
        if (atom.BondPoints == null) return;

        foreach (BondPoint bp in atom.BondPoints)
        {
            if (bp.IsPiArm) bp.SetPiArm(false);
        }
    }

    private static int CountPiArms(Atom atom)
    {
        if (atom.BondPoints == null) return 0;

        int count = 0;
        foreach (BondPoint bp in atom.BondPoints)
        {
            if (bp.IsPiArm) count++;
        }
        return count;
    }
}
