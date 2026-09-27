using System.Collections.Generic;
using UnityEngine;

// 責任：分子のなかの環（閉路）を見つける
//
// 「その結合を取り除いても両端がまだ繋がっているなら、その結合は環の一部」という判定を使う。
// 取り除いたうえで最短経路をたどれば、その結合を含むいちばん小さい環が隣り合う順で得られる。
// 分子はせいぜい数十原子なので、素朴な幅優先探索で十分に速い。
public static class MoleculeRings
{
    // 原子どうしの繋がりを引きやすい形にまとめる（多重結合でも相手は1つとして数える）
    public static Dictionary<Atom, List<Atom>> BuildAdjacency(List<Atom> atoms)
    {
        Dictionary<Atom, List<Atom>> adjacency = new Dictionary<Atom, List<Atom>>();
        foreach (Atom atom in atoms) adjacency[atom] = new List<Atom>();

        foreach (Atom atom in atoms)
        {
            if (atom.BondPoints == null) continue;
            foreach (BondPoint bp in atom.BondPoints)
            {
                Atom neighbor = bp.ConnectedAtom;
                if (neighbor == null || !adjacency.ContainsKey(neighbor)) continue;
                if (!adjacency[atom].Contains(neighbor)) adjacency[atom].Add(neighbor);
            }
        }
        return adjacency;
    }

    // 環を構成する原子を、隣り合う順に並べて返す
    public static List<List<Atom>> FindRings(List<Atom> atoms)
    {
        List<List<Atom>> rings = new List<List<Atom>>();
        if (atoms == null || atoms.Count < 3) return rings;

        Dictionary<Atom, List<Atom>> adjacency = BuildAdjacency(atoms);
        HashSet<string> seen = new HashSet<string>();

        foreach (Atom a in atoms)
        {
            foreach (Atom b in adjacency[a])
            {
                // 1本の結合につき1回だけ調べる
                if (a.GetInstanceID() > b.GetInstanceID()) continue;

                List<Atom> path = ShortestPathAvoidingDirectBond(adjacency, a, b);
                if (path == null || path.Count < 3) continue;

                if (seen.Add(RingKey(path)))
                {
                    Canonicalize(path);
                    rings.Add(path);
                }
            }
        }

        // 小さい環を優先する（縮合環で大きい環に取られないように）
        rings.Sort((x, y) => x.Count.CompareTo(y.Count));
        return rings;
    }

    // 環をたどる順番を毎回同じにそろえる。
    // どの結合から環を見つけたかによって並びや向きが変わると、
    // 同じ分子でもレイアウトのたびに環がわずかに回ってしまう
    private static void Canonicalize(List<Atom> ring)
    {
        int count = ring.Count;
        if (count < 3) return;

        // いちばん小さいIDの原子を先頭に持ってくる
        int start = 0;
        for (int i = 1; i < count; i++)
        {
            if (ring[i].GetInstanceID() < ring[start].GetInstanceID()) start = i;
        }

        // 2番目に来る原子のIDが小さくなる向きにそろえる
        bool forward = ring[(start + 1) % count].GetInstanceID()
                       < ring[(start + count - 1) % count].GetInstanceID();

        List<Atom> ordered = new List<Atom>(count);
        for (int i = 0; i < count; i++)
        {
            int index = forward ? (start + i) % count : (start - i + count * 2) % count;
            ordered.Add(ring[index]);
        }

        ring.Clear();
        ring.AddRange(ordered);
    }

    // 原子から、それが属する環を引けるようにする。
    // 縮合環（1つの原子が複数の環に属する）の場合は、いちばん小さい環だけを採用する
    public static Dictionary<Atom, List<Atom>> MapAtomsToRings(List<List<Atom>> rings)
    {
        Dictionary<Atom, List<Atom>> map = new Dictionary<Atom, List<Atom>>();
        foreach (List<Atom> ring in rings)
        {
            foreach (Atom atom in ring)
            {
                if (!map.ContainsKey(atom)) map[atom] = ring;
            }
        }
        return map;
    }

    // start と goal を直接つなぐ結合を使わずに、最短でたどり着く経路を返す
    private static List<Atom> ShortestPathAvoidingDirectBond(
        Dictionary<Atom, List<Atom>> adjacency, Atom start, Atom goal)
    {
        Dictionary<Atom, Atom> cameFrom = new Dictionary<Atom, Atom>();
        HashSet<Atom> visited = new HashSet<Atom> { start };
        Queue<Atom> queue = new Queue<Atom>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Atom current = queue.Dequeue();
            foreach (Atom neighbor in adjacency[current])
            {
                // 今調べている結合そのものは通らない
                if (current == start && neighbor == goal) continue;
                if (!visited.Add(neighbor)) continue;

                cameFrom[neighbor] = current;

                if (neighbor == goal) return Trace(cameFrom, start, goal);
                queue.Enqueue(neighbor);
            }
        }
        return null;
    }

    private static List<Atom> Trace(Dictionary<Atom, Atom> cameFrom, Atom start, Atom goal)
    {
        List<Atom> path = new List<Atom>();
        Atom current = goal;
        while (true)
        {
            path.Add(current);
            if (current == start) break;
            current = cameFrom[current];
        }
        path.Reverse();
        return path;
    }

    private static string RingKey(List<Atom> ring)
    {
        List<int> ids = new List<int>();
        foreach (Atom atom in ring) ids.Add(atom.GetInstanceID());
        ids.Sort();
        return string.Join(",", ids);
    }
}
