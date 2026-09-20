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
        // 1. 起点となる原子から、繋がっている全原子を芋づる式に取得する（幅優先探索）
        List<Atom> currentMoleculeAtoms = MoleculeLayout.CollectMolecule(triggerAtom);

        // 2. まとまった原子のリストを、判定役（PuzzleManager）に渡す
        if (PuzzleManager.Instance != null)
        {
            PuzzleManager.Instance.CheckMoleculeMatch(currentMoleculeAtoms);
        }
    }
}