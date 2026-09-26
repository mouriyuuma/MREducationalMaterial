using System;
using System.Collections.Generic;
using UnityEngine;

// 責任：分子の形が変わるときに、瞬間移動ではなく滑らかに動かす
//
// 結合するたびに原子が瞬間移動すると、何が起きたのかプレイヤーが追えない。
// 組み替えの前後の姿勢を補間して見せることで、
// 「正四面体から平面三角に組み替わった」「引っ張って伸びた結合が元に戻った」が目で追える。
//
// 姿勢は親（MoleculeGroup）から見たローカル座標で覚える。
// そのため補間の最中にプレイヤーが分子を動かしても、ちゃんと手に付いてくる。
public class MoleculeAnimator : MonoBehaviour
{
    public const float DefaultDuration = 0.18f;

    // 位置と向きがこれ以下しか変わらないなら、補間せず即座に終わらせる
    private const float PositionEpsilon = 0.0005f;
    private const float AngleEpsilon = 0.5f;

    private struct Step
    {
        public Transform Target;
        public Vector3 FromPosition;
        public Quaternion FromRotation;
        public Vector3 ToPosition;
        public Quaternion ToRotation;
    }

    private static readonly List<MoleculeAnimator> Running = new List<MoleculeAnimator>();

    // 補間の最中かどうか。移動の途中の距離で結合を切ってしまわないように見る
    public static bool IsBusy
    {
        get
        {
            for (int i = Running.Count - 1; i >= 0; i--)
            {
                if (Running[i] == null) Running.RemoveAt(i);
            }
            return Running.Count > 0;
        }
    }

    private readonly List<Step> _steps = new List<Step>();
    private readonly HashSet<Transform> _owned = new HashSet<Transform>();

    private float _elapsed;
    private float _duration;
    private Action _onComplete;

    // 組み替え後の姿勢はすでに transform に入っている前提。
    // いったん from の姿勢に戻してから、そこへ向けて補間を始める
    public static void Play(
        GameObject owner,
        IList<Transform> targets,
        IList<Vector3> fromPositions,
        IList<Quaternion> fromRotations,
        Action onComplete)
    {
        if (owner == null || targets == null || targets.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        // 同じ原子を動かしている補間が走っていたら、先にそれを終わらせる
        for (int i = Running.Count - 1; i >= 0; i--)
        {
            if (Running[i] == null) { Running.RemoveAt(i); continue; }
            if (Running[i].Overlaps(targets)) Running[i].Finish();
        }

        MoleculeAnimator animator = owner.GetComponent<MoleculeAnimator>();
        if (animator == null) animator = owner.AddComponent<MoleculeAnimator>();

        animator.Begin(targets, fromPositions, fromRotations, onComplete);
    }

    private void Begin(
        IList<Transform> targets,
        IList<Vector3> fromPositions,
        IList<Quaternion> fromRotations,
        Action onComplete)
    {
        _steps.Clear();
        _owned.Clear();

        bool moved = false;

        for (int i = 0; i < targets.Count; i++)
        {
            Transform target = targets[i];
            if (target == null) continue;

            Step step = new Step
            {
                Target = target,
                FromPosition = fromPositions[i],
                FromRotation = fromRotations[i],
                ToPosition = target.localPosition,
                ToRotation = target.localRotation
            };

            if (Vector3.Distance(step.FromPosition, step.ToPosition) > PositionEpsilon ||
                Quaternion.Angle(step.FromRotation, step.ToRotation) > AngleEpsilon)
            {
                moved = true;
            }

            _steps.Add(step);
            _owned.Add(target);
        }

        _onComplete = onComplete;

        // ほとんど動いていないなら補間する意味がない
        if (!moved)
        {
            Finish();
            return;
        }

        _elapsed = 0f;
        _duration = DefaultDuration;

        Apply(0f);

        if (!Running.Contains(this)) Running.Add(this);
        enabled = true;
    }

    private bool Overlaps(IList<Transform> targets)
    {
        foreach (Transform target in targets)
        {
            if (target != null && _owned.Contains(target)) return true;
        }
        return false;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;

        if (_elapsed >= _duration)
        {
            Finish();
            return;
        }

        Apply(_elapsed / _duration);
    }

    private void Apply(float t)
    {
        // 行き過ぎない滑らかな加減速（スムーズステップ）
        float eased = t * t * (3f - 2f * t);

        foreach (Step step in _steps)
        {
            if (step.Target == null) continue;
            step.Target.localPosition = Vector3.Lerp(step.FromPosition, step.ToPosition, eased);
            step.Target.localRotation = Quaternion.Slerp(step.FromRotation, step.ToRotation, eased);
        }
    }

    // 補間を打ち切って最終姿勢にする。後始末はこのあとに呼ばれる
    private void Finish()
    {
        foreach (Step step in _steps)
        {
            if (step.Target == null) continue;
            step.Target.localPosition = step.ToPosition;
            step.Target.localRotation = step.ToRotation;
        }

        _steps.Clear();
        _owned.Clear();
        Running.Remove(this);
        enabled = false;

        Action callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

    private void OnDestroy()
    {
        Running.Remove(this);
    }
}
