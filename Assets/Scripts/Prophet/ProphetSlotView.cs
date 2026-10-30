using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>枠に入っている品の表示用の中身。</summary>
public struct ProphetSlotInfo
{
    public string ItemId;
    public string Name;
    public Sprite Icon;
    public int Price;
    public int Stock;
    public int TurnsLeft;
    public int TotalTurns;
}

/// <summary>
/// 上げる枠（緑）・下げる枠（赤）の 1 つ。空き（点線・「ここに品のカードを入れる」）→ 持ってきた（光る・「需要 ▲ 3 日／力を 1 つ使う」）
/// → 効いている（実線・鍵・品のカード・あと N ターンの目盛り）。押すと、品を選んでいればここへ入れる（ProphetView が決める）。
/// </summary>
public sealed class ProphetSlotView : MonoBehaviour
{
    [SerializeField] private ProphetDirection direction = ProphetDirection.Up;
    [SerializeField] private Button button;
    [Tooltip("枠の地（空きは薄く、効いている間は濃く）")]
    [SerializeField] private Image background;
    [Tooltip("空きの点線の縁")]
    [SerializeField] private GameObject dashedFrame;
    [Tooltip("効いている間の実線の縁")]
    [SerializeField] private GameObject solidFrame;
    [Tooltip("空きの案内（ここに品のカードを入れる／需要が 3 日上がる・力を 1 つ使う）")]
    [SerializeField] private GameObject emptyRoot;
    [SerializeField] private TextMeshProUGUI emptyText;
    [SerializeField] private TextMeshProUGUI emptySubText;
    [Tooltip("持ってきたときの確かめ（需要 ▲ 3 日／力を 1 つ使う、または入れられない理由）")]
    [SerializeField] private GameObject previewRoot;
    [SerializeField] private TextMeshProUGUI previewText;
    [Tooltip("効いている間の中身")]
    [SerializeField] private GameObject activeRoot;
    [Tooltip("枠に入った品のカード（はまる演出の単位）")]
    [SerializeField] private RectTransform activeCard;
    [SerializeField] private Image activeIcon;
    [SerializeField] private TextMeshProUGUI activeCardName;
    [SerializeField] private TextMeshProUGUI activeCardInfo;
    [SerializeField] private TextMeshProUGUI activeTitle;
    [SerializeField] private TextMeshProUGUI turnsText;
    [Tooltip("残り日数の目盛り（左から光る）")]
    [SerializeField] private Image[] turnPips;
    [Tooltip("鍵と「切れるまで出し入れ不可」")]
    [SerializeField] private GameObject lockMark;
    [Tooltip("力が 0 のときなどに重ねる幕（押せない見た目。押すと占い師が理由を言う）")]
    [SerializeField] private GameObject blockedShade;
    [Tooltip("置ける間に光らせる枠（DragFeel.SetSlotGlow）")]
    [SerializeField] private RectTransform glowTarget;

    private static readonly Color PipOn = new(1f, 0.85f, 0.44f, 1f);
    private static readonly Color PipOff = new(1f, 1f, 1f, 0.2f);

    public ProphetDirection Direction => direction;
    public Button Button => button;
    public bool IsOccupied { get; private set; }
    public string ItemId { get; private set; } = string.Empty;
    public RectTransform GlowTarget => glowTarget != null ? glowTarget : (RectTransform)transform;
    public RectTransform ActiveCard => activeCard;

    private string Arrow => direction == ProphetDirection.Up ? "▲" : "▼";

    /// <summary>空き。effectTurns は効く日数（案内の文に使う）。</summary>
    public void ShowEmpty(int effectTurns)
    {
        IsOccupied = false;
        ItemId = string.Empty;
        if (dashedFrame != null) dashedFrame.SetActive(true);
        if (solidFrame != null) solidFrame.SetActive(false);
        if (emptyRoot != null) emptyRoot.SetActive(true);
        if (activeRoot != null) activeRoot.SetActive(false);
        if (lockMark != null) lockMark.SetActive(false);
        if (emptyText != null) emptyText.text = "ここに品のカードを入れる";
        if (emptySubText != null)
            emptySubText.text = $"需要が {effectTurns} 日{(direction == ProphetDirection.Up ? "上がる" : "下がる")}・力を 1 つ使う";
        SetBackgroundStrong(false);
    }

    /// <summary>効いている（鍵・残り日数の目盛り）。</summary>
    public void ShowActive(in ProphetSlotInfo info)
    {
        IsOccupied = true;
        ItemId = info.ItemId ?? string.Empty;
        if (dashedFrame != null) dashedFrame.SetActive(false);
        if (solidFrame != null) solidFrame.SetActive(true);
        if (emptyRoot != null) emptyRoot.SetActive(false);
        if (activeRoot != null) activeRoot.SetActive(true);
        if (lockMark != null) lockMark.SetActive(true);
        if (activeIcon != null)
        {
            activeIcon.sprite = info.Icon;
            activeIcon.enabled = info.Icon != null;
            activeIcon.preserveAspect = true;
        }
        if (activeCardName != null) activeCardName.text = info.Name;
        if (activeCardInfo != null) activeCardInfo.text = $"{info.Price:N0}G・在庫{info.Stock}";
        if (activeTitle != null)
            activeTitle.text = $"{info.Name}の需要を{(direction == ProphetDirection.Up ? "上げている" : "下げている")}";
        if (turnsText != null) turnsText.text = $"あと {info.TurnsLeft} 日";
        if (turnPips != null)
        {
            int total = Mathf.Max(info.TotalTurns, 1);
            for (int i = 0; i < turnPips.Length; i++)
            {
                if (turnPips[i] == null) continue;
                turnPips[i].gameObject.SetActive(i < total);
                turnPips[i].color = i < info.TurnsLeft ? PipOn : PipOff;
            }
        }
        SetBackgroundStrong(true);
        SetPreview(null);
    }

    /// <summary>カードを持ってきた間の確かめ（null で消す）。ok=false なら入れられない理由（赤みの文字）。</summary>
    public void SetPreview(string text, bool ok = true)
    {
        bool on = !string.IsNullOrEmpty(text);
        if (previewRoot != null) previewRoot.SetActive(on);
        if (emptyRoot != null && !IsOccupied) emptyRoot.SetActive(!on);
        if (previewText != null)
        {
            previewText.text = text ?? string.Empty;
            previewText.color = ok ? new Color(1f, 0.95f, 0.82f, 1f) : new Color(1f, 0.66f, 0.58f, 1f);
        }
        DragFeel.SetSlotGlow(GlowTarget, on && ok);
    }

    /// <summary>入れられない見た目（力が 0 のときなど）。押せるまま（押すと占い師が理由を言う）。</summary>
    public void SetBlocked(bool on)
    {
        if (blockedShade != null) blockedShade.SetActive(on && !IsOccupied);
    }

    /// <summary>はまる演出（カードがめくれて入り、枠が弾む）。</summary>
    public void PlayPlaced()
    {
        if (activeCard != null)
            CardFlip.Flip(activeCard, null, 0f, rare: true, onDone: () => { if (activeCard != null) DragFeel.Puff(activeCard); });
        UIMotion.Bump(transform, 1.05f, 0.26f);
    }

    /// <summary>入れられなかったときの首振り。</summary>
    public void PlayRejected()
    {
        UIMotion.Shake((RectTransform)transform);
    }

    private void SetBackgroundStrong(bool strong)
    {
        if (background == null) return;
        var c = background.color;
        c.a = strong ? 0.92f : 0.55f;
        background.color = c;
    }

    public string ArrowLabel => Arrow;
}
