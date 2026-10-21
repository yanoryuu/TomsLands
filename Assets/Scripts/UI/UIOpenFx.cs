using DG.Tweening;
using UnityEngine;

/// <summary>
/// 表示された（OnEnable）ときに開く演出を出す。画面の中で SetActive だけで出すパネルに付ける。
/// ・パネル全体: UIFx.PanelOpen（フェード）
/// ・window を指定したとき: UIFx.Pop（モーダルの窓だけ小さくポップ）
/// 開いたままの再表示（既に有効なものを SetActive(true)）では何もしない。未設定でもエラーにならない。
/// </summary>
[DisallowMultipleComponent]
public class UIOpenFx : MonoBehaviour
{
    [Tooltip("ポップさせる窓（モーダルの本体）。空ならフェードだけ")]
    [SerializeField] private RectTransform window;
    [SerializeField, Min(0f)] private float fadeDuration = 0.18f;
    [SerializeField, Range(0.5f, 1f)] private float popFromScale = 0.92f;
    [SerializeField, Min(0f)] private float popDuration = 0.2f;

    private void OnEnable()
    {
        // UIFx.PanelOpen は子の Canvas を探してそこをフェードするので、ここでは自分の CanvasGroup を直接フェードする
        if (!TryGetComponent<CanvasGroup>(out var cg)) cg = gameObject.AddComponent<CanvasGroup>();
        cg.DOKill();
        cg.alpha = 0f;
        cg.DOFade(1f, fadeDuration).SetLink(gameObject);
        if (window != null) UIFx.Pop(window, popFromScale, popDuration);
    }
}
