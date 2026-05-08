using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events; // イベント用

public class Molecule : MonoBehaviour
{
    public List<Atom> Atoms = new List<Atom>();

    // 形が変わったときに発火するイベント（外部のManagerがこれを監視する）
    public UnityEvent<Molecule> OnStructureChangedEvent;

    public void OnStructureChanged()
    {
        // 判定は自分では行わず、「変わったこと」だけを外部に知らせる
        OnStructureChangedEvent?.Invoke(this);
    }
}