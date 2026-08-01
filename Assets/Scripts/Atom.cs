using UnityEngine;

// 責任：原子の基本データと、自分が持つ結合手(BondPoint)を管理するだけ
public class Atom : MonoBehaviour
{
    [Header("Atom Data")]
    public string ElementType; // "C", "H", "O" など

    // 自分が持っている結合手のリスト
    public BondPoint[] BondPoints { get; private set; }

    // この原子の最大結合手数（プレハブに付けたBondPointの数＝価数）
    // 例: 酸素のプレハブにBondPointが2つ付いていれば、MaxValencyは 2 になる
    public int MaxValency => BondPoints != null ? BondPoints.Length : 0;

    // 現在使っている結合手数の合計（二重結合は2としてカウント）
    public int GetUsedValency()
    {
        int used = 0;
        if (BondPoints == null) return 0;
        foreach (var bp in BondPoints)
        {
            if (bp.IsConnected)
            {
                used += bp.CurrentBondOrder; // 単結合なら+1、二重結合なら+2
            }
        }
        return used;
    }

    // 今「余っている」結合手数
    public int AvailableValency => MaxValency - GetUsedValency();
    
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