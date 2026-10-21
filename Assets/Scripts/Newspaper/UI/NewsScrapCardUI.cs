using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 右上ペインのスクラップカード1枚（Docs/News_Spec.md §9）。
/// 状態バッジ（砂時計 / ? / ○ / △ / ×）・見出し・リード。? のときだけ「確認 1,500G」を出す。
/// カード自体を押すと記事の全文（<see cref="NewspaperView"/> の記事ポップアップ）を開く。
/// </summary>
public class NewsScrapCardUI : MonoBehaviour
{
    [SerializeField] private Image badgeImage;
    [SerializeField] private Sprite pendingSprite;      // 22−2状態バッジ保留
    [SerializeField] private Sprite unconfirmedSprite;  // 22−1状態バッジ未確認
    [SerializeField] private Sprite hitSprite;          // 丸
    [Tooltip("誤報だったときのバッジ。未設定なら確定バッジを赤く染めて代用する")]
    [SerializeField] private Sprite missSprite;         // ばつ
    [SerializeField] private Color missTint = new Color(0.75f, 0.25f, 0.2f, 1f);
    [Tooltip("誇張（△）のとき確定バッジに掛ける色（専用の素材が無いので色で分ける。I-15）")]
    [SerializeField] private Color partialTint = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private TextMeshProUGUI headlineText;
    [SerializeField] private TextMeshProUGUI leadText;
    [SerializeField] private Button confirmButton;          // 確認
    [SerializeField] private TextMeshProUGUI confirmLabel;  // 「確認 1,500G」
    [Tooltip("カード全体のボタン（押すと記事を読む）。未設定ならルートの Button を使う")]
    [SerializeField] private Button rootButton;

    public string EntryKey { get; private set; }
    public ScrapState State => lastState;
    public Sprite BadgeSprite => badgeImage != null ? badgeImage.sprite : null;
    public Color BadgeColor => badgeImage != null ? badgeImage.color : Color.white;

    public event Action<string> OnConfirmClicked;
    public event Action<string> OnCardClicked;

    private ScrapState lastState = ScrapState.Pending;

    private void Awake()
    {
        if (confirmButton != null)
            confirmButton.onClick.AddListener(() => OnConfirmClicked?.Invoke(EntryKey));
        if (rootButton == null) rootButton = GetComponent<Button>();
        if (rootButton != null)
            rootButton.onClick.AddListener(() => OnCardClicked?.Invoke(EntryKey));
        // パッド/キー: カードの内側にある「確認」へ右で移れるようにする
        if (rootButton != null && confirmButton != null) InnerSelectableNavigation.Attach(rootButton, confirmButton);
    }

    /// <param name="reveal">直前の表示から状態が変わった（確認の結果が出た）ときに true。バッジの演出を出す</param>
    /// <param name="vetState">裏取り（酒場 Lv5）で分かった真偽。まだ確認していないカードのバッジをこの ○△× にする（確認ボタンはそのまま）</param>
    public void SetData(string entryKey, string headline, string lead, ScrapState state, bool canAfford, int cost,
        bool reveal = false, ScrapState? vetState = null)
    {
        EntryKey = entryKey;
        lastState = state;

        if (headlineText != null) headlineText.text = headline;
        if (leadText != null) leadText.text = lead;

        if (badgeImage != null)
        {
            bool resolved = state == ScrapState.ConfirmedHit || state == ScrapState.ConfirmedPartial || state == ScrapState.ConfirmedMiss;
            var shown = !resolved && vetState.HasValue ? vetState.Value : state;
            PaintBadge(badgeImage, shown);
            if (reveal) PlayReveal(shown);
        }

        if (confirmButton != null)
        {
            confirmButton.gameObject.SetActive(state == ScrapState.Unconfirmed);
            confirmButton.interactable = canAfford;
        }
        if (confirmLabel != null) confirmLabel.text = $"確認 {cost:N0}G";
    }

    /// <summary>
    /// ○△× などの状態バッジを target に塗る。カードのバッジのほか、裏取り（酒場 Lv5）のハンコにも同じ素材を使う
    /// （記事の一面トップ・小記事。呼び出し側はカードのプレハブを見本に渡す）。
    /// </summary>
    public void PaintBadge(Image target, ScrapState state)
    {
        if (target == null) return;
        target.color = Color.white;
        switch (state)
        {
            case ScrapState.Pending: target.sprite = pendingSprite; break;
            case ScrapState.Unconfirmed: target.sprite = unconfirmedSprite; break;
            case ScrapState.ConfirmedHit: target.sprite = hitSprite; break;
            case ScrapState.ConfirmedPartial: target.sprite = hitSprite; target.color = partialTint; break;
            case ScrapState.ConfirmedMiss:
                if (missSprite != null) target.sprite = missSprite;
                else { target.sprite = hitSprite; target.color = missTint; }
                break;
        }
        // 誇張（△）は専用のバッジ素材が無いので、確定バッジを薄くして「△」の字を重ねる（I-15）
        var mark = PartialMarkOf(target);
        if (mark != null) mark.gameObject.SetActive(state == ScrapState.ConfirmedPartial);
    }

    private TextMeshProUGUI PartialMarkOf(Image target)
    {
        var existing = target.transform.Find("PartialMark");
        if (existing != null) return existing.GetComponent<TextMeshProUGUI>();
        var go = new GameObject("PartialMark", typeof(RectTransform));
        go.layer = target.gameObject.layer;
        go.transform.SetParent(target.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var mark = go.AddComponent<TextMeshProUGUI>();
        if (headlineText != null) mark.font = headlineText.font;
        mark.text = "△";
        mark.alignment = TextAlignmentOptions.Center;
        // カードのバッジは 40。裏取りのハンコは大きさに合わせる
        mark.fontSize = target == badgeImage ? 40f : Mathf.Max(14f, target.rectTransform.rect.height * 0.6f);
        mark.color = new Color32(0xA4, 0x50, 0x0E, 0xFF); // 付箋・記事ポップアップの「△ 誇張」と同じ濃さ
        mark.raycastTarget = false;
        return mark;
    }

    /// <summary>確認の結果が出たときの演出。バッジを跳ねさせ、誤報ならカードを揺らす。</summary>
    private void PlayReveal(ScrapState state)
    {
        var badge = badgeImage.transform;
        if (!badge.gameObject.activeInHierarchy) return;
        badge.DOKill();
        badge.localScale = Vector3.one * 0.2f;
        badge.localRotation = Quaternion.Euler(0f, 0f, -90f);
        DOTween.Sequence()
            .Append(badge.DOScale(1.25f, 0.22f).SetEase(Ease.OutBack))
            .Join(badge.DOLocalRotate(Vector3.zero, 0.22f).SetEase(Ease.OutCubic))
            .Append(badge.DOScale(1f, 0.12f))
            .SetLink(badge.gameObject);

        var card = transform;
        card.DOKill(true);
        if (state == ScrapState.ConfirmedMiss)
            card.DOPunchPosition(new Vector3(10f, 0f, 0f), 0.35f, 14, 0.6f).SetDelay(0.15f).SetLink(gameObject);
        else
            card.DOPunchScale(Vector3.one * 0.06f, 0.3f, 6, 0.6f).SetDelay(0.15f).SetLink(gameObject);
    }
}
