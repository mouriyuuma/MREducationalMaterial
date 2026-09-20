using System.Collections.Generic;
using UnityEngine;

// 責任：結合手の現在の状態管理と、近くにある他の結合手の検知（センサー）
//
// 結合次数はここでは持たない。「同じ相手に何本の腕が向いているか」が結合次数そのものなので、
// Atom.GetBondOrderTo() で数える。分子模型キットで腕を2本使って二重結合を作るのと同じ考え方で、
// 腕1本につき円柱1本が見えるため、二重結合は自然に2本線として表示される。
public class BondPoint : MonoBehaviour
{
    public bool IsConnected { get; private set; }
    public BondPoint ConnectedTarget { get; private set; }

    public Atom ParentAtom { get; private set; }

    // 芳香環のπ電子として環に溶けた腕。特定の相手とは結ばれないが電子は使っている。
    // 見た目は環の内側の円としてまとめて描くので、この腕自体は隠す
    public bool IsPiArm { get; private set; }

    // 触れている（近づいている）相手のBondPointのリスト
    private readonly List<BondPoint> _hoverCandidates = new List<BondPoint>();

    private SphereCollider _tipCollider;

    // この腕が繋がっている相手の原子（繋がっていなければ null）
    public Atom ConnectedAtom => (IsConnected && ConnectedTarget != null) ? ConnectedTarget.ParentAtom : null;

    // 腕が向いているワールド方向
    public Vector3 Direction => transform.up;

    // 腕の先端（結合したとき相手の腕先と重なる点）のワールド座標
    public Vector3 TipPosition
    {
        get
        {
            if (_tipCollider == null) _tipCollider = GetComponent<SphereCollider>();
            if (_tipCollider == null) return transform.position + transform.up * 0.1f;
            return transform.TransformPoint(_tipCollider.center);
        }
    }

    // 原子の中心から腕の先端までの長さ
    public float ArmLength
    {
        get
        {
            if (ParentAtom == null) return 0f;
            return Vector3.Distance(ParentAtom.transform.position, TipPosition);
        }
    }

    public void Initialize(Atom parent)
    {
        ParentAtom = parent;
        _tipCollider = GetComponent<SphereCollider>();
    }

    // この腕が指定の原子に向かって繋がっているか
    public bool IsConnectedTo(Atom other)
    {
        return other != null && ConnectedAtom == other;
    }

    // この腕が今すぐ結合に使える状態か。π電子になった腕は結合には使えない
    public bool CanConnect()
    {
        return !IsConnected && !IsPiArm && ParentAtom != null && ParentAtom.AvailableValency > 0;
    }

    // π電子として環に溶けた状態にする / 解除する
    public void SetPiArm(bool isPi)
    {
        IsPiArm = isPi;
        SetArmVisible(!isPi);
    }

    // 腕の円柱を表示するかどうか
    public void SetArmVisible(bool visible)
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
        }
    }

    // 今近くにある結合候補のリスト。
    // 多重結合の判定では「同じ相手と何ペア重なっているか」を見るので、
    // 一番近い1つではなく候補すべてを返す
    public IReadOnlyList<BondPoint> GetHoverCandidates()
    {
        _hoverCandidates.RemoveAll(bp => bp == null || bp.IsConnected);
        return _hoverCandidates;
    }

    private void OnTriggerEnter(Collider other)
    {
        BondPoint target = other.GetComponent<BondPoint>();
        if (target != null && target != this && target.ParentAtom != this.ParentAtom)
        {
            if (!_hoverCandidates.Contains(target))
            {
                _hoverCandidates.Add(target);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        BondPoint target = other.GetComponent<BondPoint>();
        if (target != null)
        {
            _hoverCandidates.Remove(target);
        }
    }

    public void ConnectTo(BondPoint target)
    {
        IsConnected = true;
        ConnectedTarget = target;
        // 候補リストはそのまま残す。切断されたあと、まだ重なったままでも
        // OnTriggerEnter を待たずに再結合できるようにするため
    }

    public void Disconnect()
    {
        IsConnected = false;
        ConnectedTarget = null;
    }
}
