using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ターン進行フェーズ（イベント/仕入れ/商品陳列/営業）のUI出し分け。
/// 同一の Shop ホーム内で、現フェーズに対応するボタングループだけを表示する。
/// </summary>
public class TurnPhaseView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI phaseLabel;
    [Tooltip("次へ/スキップ ボタン（仕入れ・陳列フェーズで表示）")]
    [SerializeField] private Button advanceButton;

    [Header("フェーズ別ボタングループ")]
    [SerializeField] private GameObject procurementGroup; // 仕入れ：情報屋/鍛冶屋/道具屋/ダンジョンLv/広告/ヒーロー/マップ
    [SerializeField] private GameObject displayGroup;     // 陳列：陳列設定/預言者/需要ダッシュボード
    [SerializeField] private GameObject salesGroup;       // 営業：営業開始

    [Header("フェーズステッパー（任意）")]
    [Tooltip("イベント/仕入れ/陳列/営業 の順の4ラベル。現フェーズだけ強調表示する")]
    [SerializeField] private TextMeshProUGUI[] stepperLabels;
    [SerializeField] private Color stepperActiveColor = new(1f, 0.82f, 0.3f, 1f);
    [SerializeField] private Color stepperInactiveColor = new(1f, 1f, 1f, 0.45f);

    public Subject<Unit> OnAdvanceClicked { get; } = new();

    private TurnPhase? _lastShownPhase;
    private readonly Dictionary<GameObject, Vector2> _groupBasePos = new();

    private void Awake()
    {
        if (advanceButton != null)
            advanceButton.onClick.AddListener(() => OnAdvanceClicked.OnNext(Unit.Default));

        // グループ入場演出のために初期位置を記録しておく（Tween中断後の位置ずれ防止）
        foreach (var g in new[] { procurementGroup, displayGroup, salesGroup })
        {
            if (g != null && g.transform is RectTransform rt)
                _groupBasePos[g] = rt.anchoredPosition;
        }
    }

    /// <summary>現フェーズに応じてボタングループとラベル・次へボタンを切り替える。</summary>
    public void ShowForPhase(TurnPhase phase)
    {
        // 同一フェーズの再適用（ホーム復帰のForceNotify等）では演出を繰り返さない
        bool phaseChanged = _lastShownPhase != phase;
        _lastShownPhase = phase;

        SetActiveSafe(procurementGroup, phase == TurnPhase.Procurement);
        SetActiveSafe(displayGroup, phase == TurnPhase.Display);
        SetActiveSafe(salesGroup, phase == TurnPhase.Sales);

        // 「次へ」は仕入れ・陳列のみ。営業は salesGroup の「営業開始」で進み、イベントはポップアップ任せ。
        bool showAdvance = phase == TurnPhase.Procurement || phase == TurnPhase.Display;
        if (advanceButton != null) advanceButton.gameObject.SetActive(showAdvance);

        if (phaseLabel != null)
        {
            phaseLabel.text = ToLabel(phase);
            if (phaseChanged && phaseLabel.gameObject.activeInHierarchy)
            {
                phaseLabel.transform.DOKill();
                phaseLabel.transform.localScale = Vector3.one * 0.6f;
                phaseLabel.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetLink(phaseLabel.gameObject);
            }
        }

        if (phaseChanged)
        {
            AnimateGroupEnter(phase switch
            {
                TurnPhase.Procurement => procurementGroup,
                TurnPhase.Display => displayGroup,
                TurnPhase.Sales => salesGroup,
                _ => null,
            });
        }

        UpdateStepper(phase, phaseChanged);
    }

    /// <summary>フェーズグループの入場演出（フェード＋下からのスライド）。</summary>
    private void AnimateGroupEnter(GameObject group)
    {
        if (group == null || !group.activeInHierarchy) return;

        // salesGroup の CanvasGroup は SalesPhaseView が営業開始演出のオーバーレイとして
        // 使用するため、ここでは alpha を触らない（Tweenの競合防止）
        if (group != salesGroup)
        {
            var cg = group.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.DOKill();
                cg.alpha = 0f;
                cg.DOFade(1f, 0.25f).SetLink(group);
            }
        }

        if (group.transform is RectTransform rt && _groupBasePos.TryGetValue(group, out var basePos))
        {
            rt.DOKill();
            rt.anchoredPosition = basePos + new Vector2(0f, -30f);
            rt.DOAnchorPos(basePos, 0.3f).SetEase(Ease.OutCubic).SetLink(group);
        }
    }

    /// <summary>ステッパーの現フェーズラベルだけを強調表示する。</summary>
    private void UpdateStepper(TurnPhase phase, bool animate)
    {
        if (stepperLabels == null) return;
        for (int i = 0; i < stepperLabels.Length; i++)
        {
            var label = stepperLabels[i];
            if (label == null) continue;
            bool active = i == (int)phase;
            var targetColor = active ? stepperActiveColor : stepperInactiveColor;
            var targetScale = active ? Vector3.one * 1.15f : Vector3.one;
            label.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;

            label.DOKill();
            label.transform.DOKill();
            if (animate && label.gameObject.activeInHierarchy)
            {
                label.DOColor(targetColor, 0.2f).SetLink(label.gameObject);
                label.transform.DOScale(targetScale, 0.25f)
                    .SetEase(active ? Ease.OutBack : Ease.OutCubic)
                    .SetLink(label.gameObject);
            }
            else
            {
                label.color = targetColor;
                label.transform.localScale = targetScale;
            }
        }
    }

    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    private static string ToLabel(TurnPhase phase) => phase switch
    {
        TurnPhase.Event => "イベント",
        TurnPhase.Procurement => "仕入れ",
        TurnPhase.Display => "商品陳列",
        TurnPhase.Sales => "営業",
        _ => "",
    };
}
