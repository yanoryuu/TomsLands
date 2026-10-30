using DG.Tweening;
using UnityEngine;

/// <summary>
/// #8 アイドル: アイコンがゆっくり上下する（既定 6px・2.4 秒周期）。個体ごとに位相をずらすので一斉に揃わない。
/// レイアウトグループが位置を決める要素には付けず、その中のアイコン（子）に付ける（位置を書き換えるため）。
/// 無効化・Detach で元の位置に戻る。省略モードでは動かない。
/// </summary>
[DisallowMultipleComponent]
public sealed class IdleBob : MonoBehaviour
{
    private float amplitude = 6f;
    private float period = 2.4f;
    private Vector2 basePos;
    private bool running;
    private Tween loop;

    /// <summary>target を上下させる（既に付いていれば設定を変えて続ける）。</summary>
    public static IdleBob Attach(RectTransform target, float amplitude = 6f, float period = 2.4f)
    {
        if (target == null) return null;
        if (!target.TryGetComponent<IdleBob>(out var bob)) bob = target.gameObject.AddComponent<IdleBob>();
        bob.amplitude = amplitude;
        bob.period = Mathf.Max(0.2f, period);
        bob.enabled = true;
        bob.Restart();
        return bob;
    }

    /// <summary>止めて元の位置に戻し、部品を外す。</summary>
    public static void Detach(RectTransform target)
    {
        if (target == null || !target.TryGetComponent<IdleBob>(out var bob)) return;
        bob.Stop();
        UIMotion.DestroySafe(bob);
    }

    private void OnEnable()
    {
        if (!running) Begin();
    }

    private void OnDisable() => Stop();

    private void OnDestroy() => Stop();

    private void Restart()
    {
        Stop();
        Begin();
    }

    private void Begin()
    {
        if (UIMotion.IsSkip || !(transform is RectTransform rt)) return;
        running = true;
        basePos = rt.anchoredPosition;
        // 位相: InstanceID から 0〜1 を作る（同じ画面のアイコンが揃って動かない）
        float phase = Mathf.Repeat(GetInstanceID() * 0.6180339f, 1f);
        float amp = amplitude;
        var b = basePos;
        loop = UIMotion.RunLoop(DOVirtual.Float(0f, 1f, period, t =>
            {
                float k = (1f - Mathf.Cos((t + phase) * Mathf.PI * 2f)) * 0.5f;
                rt.anchoredPosition = new Vector2(b.x, b.y + k * amp);
            })
            .SetEase(Ease.Linear).SetLoops(-1).SetTarget(this), gameObject);
        if (loop == null) running = false;
    }

    private void Stop()
    {
        if (!running) return;
        running = false;
        loop?.Kill();
        loop = null;
        if (transform is RectTransform rt) rt.anchoredPosition = basePos;
    }
}
