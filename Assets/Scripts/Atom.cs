using UnityEngine;

// 責任：原子の基本データと、自分が持つ結合手(BondPoint)を管理するだけ
public class Atom : MonoBehaviour
{
    [Header("Atom Data")]
    public string ElementType; // "C", "H", "O" など

    // 自分が持っている結合手のリスト
    public BondPoint[] BondPoints { get; private set; }

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