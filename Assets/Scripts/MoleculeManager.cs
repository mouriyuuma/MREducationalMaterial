using System.Collections.Generic;
using UnityEngine;

public class MoleculeManager : MonoBehaviour
{
    // どこからでもアクセスできるシングルトン（シーンに1つだけ）
    public static MoleculeManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // AtomInteractionから「結合した」「離れた」瞬間に呼ばれる
    public void OnStructureChanged(Atom triggerAtom)
    {
        // 1. 起点となる原子から、繋がっている全原子を芋づる式に取得する
        List<Atom> currentMoleculeAtoms = GetConnectedAtoms(triggerAtom);

        // 2. まとまった原子のリストを、判定役（PuzzleManager）に渡す
        if (PuzzleManager.Instance != null)
        {
            PuzzleManager.Instance.CheckMoleculeMatch(currentMoleculeAtoms);
        }
    }

    // 幅優先探索（BFS）によるグラフ走査アルゴリズム
    private List<Atom> GetConnectedAtoms(Atom startAtom)
    {
        List<Atom> connectedAtoms = new List<Atom>();
        Queue<Atom> queue = new Queue<Atom>();
        HashSet<Atom> visited = new HashSet<Atom>();

        // 最初の原子をキュー（探索予定リスト）に入れる
        queue.Enqueue(startAtom);
        visited.Add(startAtom);

        while (queue.Count > 0)
        {
            Atom current = queue.Dequeue();
            connectedAtoms.Add(current);

            // 今見ている原子のすべての腕をチェック
            foreach (BondPoint bp in current.BondPoints)
            {
                // 腕が誰かと繋がっていたら
                if (bp.IsConnected && bp.ConnectedTarget != null)
                {
                    Atom neighbor = bp.ConnectedTarget.ParentAtom;
                    
                    // まだ探索していない原子なら、探索予定リストに追加
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        // 繋がっているすべての原子のリストを返す
        return connectedAtoms;
    }
}