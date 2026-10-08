using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 右上ペインのスクラップカード1枚（Docs/News_Spec.md §9）。
/// 状態バッジ（砂時計 / ? / ○ / ×）・見出し・リード。? のときだけ「確認 1,500G」を出す。
/// </summary>
public class NewsScrapCardUI : MonoBehaviour
{
    [SerializeField] private Image badgeImage;
    [SerializeField] private Sprite pendingSprite;      // 22−2状態バッジ保留
    [SerializeField] private Sprite unconfirmedSprite;  // 22−1状態バッジ未確認
    [SerializeField] private Sprite hitSprite;          // 22−0状態バッジ確定
    [Tooltip("誤報だったときのバッジ。未設定なら確定バッジを赤く染めて代用する")]
    [SerializeField] private Sprite missSprite;
    [SerializeField] private Color missTint = new Color(0.75f, 0.25f, 0.2f, 1f);
    [SerializeField] private TextMeshProUGUI headlineText;
    [SerializeField] private TextMeshProUGUI leadText;
    [SerializeField] private Button confirmButton;          // 23確認
    [SerializeField] private TextMeshProUGUI confirmLabel;  // 「確認\n1,500G」

    public string EntryKey { get; private set; }
    public event Action<string> OnConfirmClicked;

    private ScrapState lastState = ScrapState.Pending;
    private bool hasState;

    private void Awake()
    {
        if (confirmButton != null)
            confirmButton.onClick.AddListener(() => OnConfirmClicked?.Invoke(EntryKey));
    }

    public void SetData(string entryKey, string headline, string lead, ScrapState state, bool canAfford, int cost)
    {
        bool changed = hasState && EntryKey == entryKey && lastState != state;
        EntryKey = entryKey;
        lastState = state;
        hasState = true;

        if (headlineText != null) headlineText.text = headline;
        if (leadText != null) leadText.text = lead;

        if (badgeImage != null)
        {
            badgeImage.color = Color.white;
            switch (state)
            {
                case ScrapState.Pending: badgeImage.sprite = pendingSprite; break;
                case ScrapState.Unconfirmed: badgeImage.sprite = unconfirmedSprite; break;
                case ScrapState.ConfirmedHit: badgeImage.sprite = hitSprite; break;
                case ScrapState.ConfirmedMiss:
                    if (missSprite != null) badgeImage.sprite = missSprite;
                    else { badgeImage.sprite = hitSprite; badgeImage.color = missTint; }
                    break;
            }
            if (changed) UIFx.Pop(badgeImage.transform, 0.6f, 0.25f);
        }

        if (confirmButton != null)
        {
            confirmButton.gameObject.SetActive(state == ScrapState.Unconfirmed);
            confirmButton.interactable = canAfford;
        }
        if (confirmLabel != null) confirmLabel.text = $"確認\n{cost:N0}G";
    }
}
