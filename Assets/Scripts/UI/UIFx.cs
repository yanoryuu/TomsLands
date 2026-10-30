using DG.Tweening;
using UnityEngine;

/// <summary>
/// UI演出の共通ヘルパー（DOTween）。
/// パネル表示時のフェードインなど、画面をまたいで使う小さな演出をまとめる。
/// </summary>
public static class UIFx
{
    /// <summary>
    /// パネル表示時のフェードイン。
    /// ScreenSpaceのCanvas自体はトランスフォームを動かせないため、
    /// Canvas（無ければルート）にCanvasGroupを付与してalphaのみを短くフェードする。
    /// fromAlpha を上げると、全画面の切替で背景（カメラの背景色）が一瞬抜けて見えるのを抑えられる。
    /// </summary>
    public static void PanelOpen(GameObject panelRoot, float duration = 0.18f, float fromAlpha = 0f)
    {
        if (panelRoot == null || !panelRoot.activeInHierarchy) return;
        // UIMotion.Close の閉じ演出中に開き直されたら、閉じ終わりで消されないよう取り消す（閉じ演出中でなければ何もしない）
        UIMotion.CancelClose(panelRoot);

        var canvas = panelRoot.GetComponentInChildren<Canvas>();
        var target = canvas != null ? canvas.gameObject : panelRoot;

        if (!target.TryGetComponent<CanvasGroup>(out var cg))
            cg = target.AddComponent<CanvasGroup>();

        cg.DOKill();
        cg.alpha = Mathf.Clamp01(fromAlpha);
        cg.DOFade(1f, duration).SetLink(target);
    }

    /// <summary>短いポップ（縮小→等倍）。選択切替や内容更新のフィードバックに。</summary>
    public static void Pop(Transform target, float fromScale = 0.97f, float duration = 0.16f)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return;
        target.DOKill();
        target.localScale = Vector3.one * fromScale;
        target.DOScale(1f, duration).SetEase(Ease.OutCubic).SetLink(target.gameObject);
    }
}
