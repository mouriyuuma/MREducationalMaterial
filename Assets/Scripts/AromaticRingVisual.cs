using System.Collections.Generic;
using UnityEngine;

// 責任：芳香環の内側に円を描く（教科書の ⌬ 表記にあたる）
//
// 6本の結合がすべて等価であることを示す記号。分子は手で動かされるので、
// 毎フレーム原子の位置から円を引き直す。環が壊れたら自分で消える。
[RequireComponent(typeof(LineRenderer))]
public class AromaticRingVisual : MonoBehaviour
{
    // 円のなめらかさ
    private const int Segments = 36;

    // 環の外接円に対する円の大きさ。六角形の内側に収まるように少し小さくする
    private const float RadiusRatio = 0.62f;

    private static readonly List<AromaticRingVisual> Active = new List<AromaticRingVisual>();

    private Atom[] _ring;
    private LineRenderer _line;

    // まだ円が付いていない芳香環なら、新しく作る
    public static void EnsureFor(List<Atom> ring)
    {
        if (ring == null || ring.Count < 3) return;

        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if (Active[i] == null) { Active.RemoveAt(i); continue; }
            if (Active[i].Covers(ring)) return;
        }

        GameObject holder = new GameObject("AromaticRing");
        AromaticRingVisual visual = holder.AddComponent<AromaticRingVisual>();
        visual.Initialize(ring);
    }

    private void Initialize(List<Atom> ring)
    {
        _ring = ring.ToArray();

        _line = GetComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.loop = true;
        _line.positionCount = Segments;
        _line.startWidth = 0.006f;
        _line.endWidth = 0.006f;
        _line.material = new Material(Shader.Find("Sprites/Default"));

        Color color = new Color(0.45f, 0.75f, 1f);
        _line.startColor = color;
        _line.endColor = color;

        Active.Add(this);
        UpdateCircle();
    }

    private bool Covers(List<Atom> ring)
    {
        if (_ring == null || _ring.Length != ring.Count) return false;

        foreach (Atom atom in ring)
        {
            if (System.Array.IndexOf(_ring, atom) < 0) return false;
        }
        return true;
    }

    private void LateUpdate()
    {
        // 環が壊れた（もう芳香環ではない）なら役目を終える
        foreach (Atom atom in _ring)
        {
            if (atom == null || !atom.IsAromatic)
            {
                Destroy(gameObject);
                return;
            }
        }

        UpdateCircle();
    }

    private void UpdateCircle()
    {
        int count = _ring.Length;

        Vector3 center = Vector3.zero;
        foreach (Atom atom in _ring) center += atom.transform.position;
        center /= count;

        // 環の法線（ニューウェル法）と、面内の基準軸を求める
        Vector3 normal = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = _ring[i].transform.position - center;
            Vector3 b = _ring[(i + 1) % count].transform.position - center;
            normal += Vector3.Cross(a, b);
        }
        if (normal.sqrMagnitude < 1e-8f) return;
        normal.Normalize();

        Vector3 axis = Vector3.ProjectOnPlane(_ring[0].transform.position - center, normal);
        if (axis.sqrMagnitude < 1e-8f) return;

        float radius = axis.magnitude * RadiusRatio;
        axis.Normalize();
        Vector3 side = Vector3.Cross(normal, axis);

        for (int i = 0; i < Segments; i++)
        {
            float angle = 2f * Mathf.PI * i / Segments;
            _line.SetPosition(i, center + radius * (Mathf.Cos(angle) * axis + Mathf.Sin(angle) * side));
        }
    }

    private void OnDestroy()
    {
        Active.Remove(this);
        if (_line != null && _line.material != null) Destroy(_line.material);
    }
}
