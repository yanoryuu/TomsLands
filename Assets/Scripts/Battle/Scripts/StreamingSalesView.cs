using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class StreamingSalesView : MonoBehaviour
{
    [SerializeField] private TMP_Text totalSalesText;
    [SerializeField] private List<ItemSlotView> itemSlots;

    [Header("配信収支メーター（SUMMARY E-6）。未設定なら売上の文字を複製して作る")]
    [Tooltip("介入・補充に使えるお金（所持金 + 売上 − 補充 − 介入）")]
    [SerializeField] private TMP_Text availableText;
    [Tooltip("売れた瞬間に浮かぶ「+1,200」")]
    [SerializeField] private TMP_Text gainPopupTemplate;
    [SerializeField] private float mainTextLift = 12f;
    [SerializeField] private float availableOffsetY = -26f;
    [SerializeField] private float availableFontSize = 20f;

    private const string NegativeColor = "#FF8A80";

    private int _sales;
    private int _spending;
    private int _available;
    private bool _ledgerMode;
    private bool _layoutDone;

    /// <summary>売上の累計（StreamingSalesModel.TotalSales）。表示は「売上 − 補充 − 介入」の配信収支。</summary>
    public void UpdateTotalSales(int amount)
    {
        int gain = amount - _sales;
        _sales = amount;
        Refresh();
        if (gain > 0) ShowGainPopup(gain);
    }

    /// <summary>
    /// 補充・介入で払った額と、使えるお金を入れる（BattleSceneStarter が配信中に更新する）。
    /// 一度でも呼ばれると、メーターは売上ではなく配信収支（純利益と同じ式。防衛報酬は決着時に入るので含まない）を出す。
    /// </summary>
    public void SetLedger(int spending, int available)
    {
        if (_ledgerMode && spending == _spending && available == _available) return;
        _ledgerMode = true;
        _spending = spending;
        _available = available;
        Refresh();
    }

    private void Refresh()
    {
        EnsureLayout();
        if (totalSalesText != null)
        {
            if (!_ledgerMode)
            {
                totalSalesText.text = $"{_sales:N0} G";
            }
            else
            {
                int net = _sales - _spending;
                totalSalesText.text = net >= 0 ? $"+{net:N0} G" : $"<color={NegativeColor}>-{-net:N0} G</color>";
            }
        }
        if (availableText != null)
        {
            availableText.gameObject.SetActive(_ledgerMode);
            availableText.text = $"使えるお金 {Mathf.Max(0, _available):N0} G";
        }
    }

    private void EnsureLayout()
    {
        if (_layoutDone || totalSalesText == null) return;
        _layoutDone = true;
        var mainRt = totalSalesText.rectTransform;
        if (availableText == null)
        {
            availableText = Instantiate(totalSalesText, mainRt.parent);
            availableText.name = "AvailableMoney";
            availableText.raycastTarget = false;
            availableText.enableAutoSizing = false;
            availableText.fontSize = availableFontSize;
            availableText.rectTransform.anchoredPosition = mainRt.anchoredPosition + new Vector2(0f, availableOffsetY);
            availableText.gameObject.SetActive(false);
            mainRt.anchoredPosition += new Vector2(0f, mainTextLift);
        }
        if (gainPopupTemplate == null)
        {
            gainPopupTemplate = Instantiate(totalSalesText, mainRt.parent);
            gainPopupTemplate.name = "GainPopupTemplate";
            gainPopupTemplate.raycastTarget = false;
            gainPopupTemplate.enableAutoSizing = false;
            gainPopupTemplate.fontSize = totalSalesText.fontSize * 0.75f;
            gainPopupTemplate.color = new Color(1f, 0.84f, 0.35f, 1f);
            gainPopupTemplate.gameObject.SetActive(false);
        }
    }

    /// <summary>売れた分を「+1,200」で浮かせる（売れた瞬間に儲かったことが数字で返る）。</summary>
    private void ShowGainPopup(int gain)
    {
        if (gainPopupTemplate == null || totalSalesText == null || !isActiveAndEnabled) return;
        var popup = Instantiate(gainPopupTemplate, gainPopupTemplate.transform.parent);
        popup.gameObject.SetActive(true);
        popup.text = $"+{gain:N0}";
        var rt = popup.rectTransform;
        // 枠の上（空き）へ浮かせる。下には「使えるお金」があるので重ねない
        var from = totalSalesText.rectTransform.anchoredPosition + new Vector2(0f, 30f);
        rt.anchoredPosition = from;
        float duration = 0.8f * GameSettings.EffectDurationScale;
        var seq = DOTween.Sequence();
        seq.Append(rt.DOAnchorPosY(from.y + 26f, duration).SetEase(Ease.OutCubic));
        seq.Insert(duration * 0.45f, popup.DOFade(0f, duration * 0.55f));
        seq.OnComplete(() => { if (popup != null) Destroy(popup.gameObject); });
        seq.SetLink(popup.gameObject);
        UIFx.Pop(totalSalesText.transform, 1.12f, 0.18f);
    }

    /// <summary>
    /// 売り場の商品を初期表示
    /// </summary>
    public void DisplayItems(List<RuntimeItemData> items)
    {
        for (int i = 0; i < itemSlots.Count; i++)
        {
            itemSlots[i].SetItem(i < items.Count ? items[i] : null);
        }
    }

    /// <summary>
    /// 特定のスロットの在庫表示だけを更新
    /// </summary>
    public void UpdateItemStock(int slotIndex, int newStock)
    {
        if (slotIndex < 0 || slotIndex >= itemSlots.Count) return;
        itemSlots[slotIndex].UpdateStock(newStock);
    }

    /// <summary>売り場スロット（左から順）。パッドの「X で並べる」の置き先探しに使う。</summary>
    public IReadOnlyList<ItemSlotView> ItemSlots => itemSlots;

    public int GetSlotIndex(ItemSlotView slot) => itemSlots.IndexOf(slot);

    /// <summary>
    /// 特定のスロットで、売れたアニメーションを再生
    /// </summary>
    public void PlaySoldAnimation(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= itemSlots.Count) return;
        itemSlots[slotIndex].PlaySoldAnimation();
    }
}
