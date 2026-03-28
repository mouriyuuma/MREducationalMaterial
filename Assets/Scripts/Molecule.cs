using System.Collections.Generic;
using UnityEngine;

public class Molecule : MonoBehaviour
{
    [Header("Molecule Data")]
    public List<Atom> Atoms = new List<Atom>(); // 属している原子のリスト

    // 他の分子と合体する時の処理
    public void MergeWith(Molecule other)
    {
        Debug.Log("分子同士が合体しました！");
    }

    // 分子が2つに割れる時の処理
    public void Split(Atom splitNodeA, Atom splitNodeB)
    {
        Debug.Log("分子が分割されました！");
    }
}