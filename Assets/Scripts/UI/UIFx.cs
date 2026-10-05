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
    /// </summary>
    public static void PanelOpen(GameObject panelRoot, float duration = 0.18f)
    {
        if (panelRoot == null || !panelRoot.activeInHierarchy) return;

        var canvas = panelRoot.GetComponentInChildren<Canvas>();
        var target = canvas != null ? canvas.gameObject : panelRoot;

        var cg = target.GetComponent<CanvasGroup>();
        if (cg == null) cg = target.AddComponent<CanvasGroup>();

        cg.DOKill();
        cg.alpha = 0f;
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
