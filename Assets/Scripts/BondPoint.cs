using System.Collections.Generic; // 修正点1: これが必要です
using UnityEngine;

// 責任：結合手の現在の状態管理と、近くにある他の結合手の検知（センサー）
public class BondPoint : MonoBehaviour
{
    public bool IsConnected { get; private set; }
    public BondPoint ConnectedTarget { get; private set; }
    
    // 現在重なっている（結合候補の）相手
    public BondPoint HoverTarget { get; private set; }
    
    public Atom ParentAtom { get; private set; }

    // 現在の結合の強さ（1=単結合, 2=二重結合, 3=三重結合）
    public int CurrentBondOrder { get; set; } = 1;

    [Header("Bond Visuals (結合の見た目)")]
    [Tooltip("単結合用のメイン円柱")]
    public GameObject MainCylinder;
    [Tooltip("二重・三重結合時に追加表示する円柱の配列（2つセットしてください）")]
    public GameObject[] ExtraCylinders;

    // 触れている（近づいている）相手のBondPointのリスト
    private List<BondPoint> _hoverCandidates = new List<BondPoint>();

    public void Initialize(Atom parent)
    {
        ParentAtom = parent;
    }

    // 今一番近くにある「接続可能なBondPoint」を返すメソッド
    public BondPoint GetBestHoverTarget()
    {
        // 自分が既に繋がっているか、親原子の「余っている手」がもう無い場合は候補を出さない
        if (IsConnected || ParentAtom.AvailableValency <= 0) return null;

        BondPoint bestTarget = null;
        float minDistance = float.MaxValue;

        // リストの中から、破棄されたものや既に接続済みのものを除外（クリーンアップ）
        _hoverCandidates.RemoveAll(bp => bp == null || bp.IsConnected);

        foreach (var candidate in _hoverCandidates)
        {
            // 自分と同じ原子の結合手とはくっつかないようにする
            if (candidate.ParentAtom == this.ParentAtom) continue;

            // 相手の原子に「余っている手」がない場合はスキップ（くっつかない）
            if (candidate.ParentAtom.AvailableValency <= 0) continue;

            // 距離を測って、一番近いものを記憶する
            float dist = Vector3.Distance(transform.position, candidate.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                bestTarget = candidate;
            }
        }
        return bestTarget;
    }

    private void OnTriggerEnter(Collider other)
    {
        BondPoint target = other.GetComponent<BondPoint>();
        if (target != null && target != this && target.ParentAtom != this.ParentAtom)
        {
            HoverTarget = target;
            
            // レーザープレビュー用の処理
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
            if (target == HoverTarget)
            {
                HoverTarget = null;
            }
            
            // レーザープレビュー用の処理
            if (_hoverCandidates.Contains(target))
            {
                _hoverCandidates.Remove(target);
            }
        }
    }

    public void ConnectTo(BondPoint target, int bondOrder = 1)
    {
        IsConnected = true;
        ConnectedTarget = target;
        HoverTarget = null; // 結合したのでターゲットからは外す
        
        SetBondOrder(bondOrder); // 見た目を更新
    }

    public void Disconnect()
    {
        IsConnected = false;
        ConnectedTarget = null;
        SetBondOrder(1); // 単結合に戻す
    }

    // 結合数に応じて円柱の表示を切り替えるメソッド
    public void SetBondOrder(int order)
    {
        CurrentBondOrder = order;

        if (MainCylinder != null) MainCylinder.SetActive(true);
        
        if (ExtraCylinders != null && ExtraCylinders.Length >= 2)
        {
            // 二重結合以上なら1本目を表示、三重結合なら2本目も表示
            ExtraCylinders[0].SetActive(order >= 2);
            ExtraCylinders[1].SetActive(order >= 3);
        }
    }
}