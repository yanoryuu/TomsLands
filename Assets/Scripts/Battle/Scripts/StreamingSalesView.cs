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
    // 売上の文字にいま出ている値（#2 で数字を回すときの出発点。回っている途中ならその途中の値）
    private long _shownMain;
    private bool _mainShown;
    private bool _shownLedger;

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
            // #2 数字を回す（共通部品 UIMotion.CountTo。途中で次の値が来たら今見えている値から続ける。
            // 最終の文字は従来と同じ。省略モード・非表示中は即座に最終の文字）
            bool ledger = _ledgerMode;
            long target = ledger ? _sales - _spending : _sales;
            string Format(long v)
            {
                _shownMain = v;
                if (!ledger) return $"{v:N0} G";
                return v >= 0 ? $"+{v:N0} G" : $"<color={NegativeColor}>-{-v:N0} G</color>";
            }
            if (!_mainShown || _shownLedger != ledger)
            {
                // 初回と、売上 → 配信収支へ表示が切り替わったときは数え上げずにそのまま出す
                _mainShown = true;
                _shownLedger = ledger;
                UIMotion.CountTo(totalSalesText, target, target, Format);
            }
            else if (target != _shownMain)
            {
                UIMotion.CountTo(totalSalesText, _shownMain, target, Format, 0.5f);
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

    /// <summary>
    /// 売れた分を「+1,200」で浮かせる（売れた瞬間に儲かったことが数字で返る）。
    /// #2 共通部品の浮き文字（MoneyFx.FloatText）と弾み（UIMotion.Bump）に置き換え。省略モードでは出さない。
    /// 文字の色は従来の浮き文字（gainPopupTemplate）の色。
    /// </summary>
    private void ShowGainPopup(int gain)
    {
        if (totalSalesText == null || !isActiveAndEnabled) return;
        var color = gainPopupTemplate != null ? gainPopupTemplate.color : new Color(1f, 0.84f, 0.35f, 1f);
        MoneyFx.FloatText(totalSalesText.rectTransform, $"+{gain:N0}", color, totalSalesText.fontSize * 0.75f, 30f);
        UIMotion.Bump(totalSalesText.transform);
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

        // #2 売れた品から売上の枠へコインが飛ぶ（3 枚。演出用の層に描くので売り場のドラッグを邪魔しない）
        if (totalSalesText != null && isActiveAndEnabled && itemSlots[slotIndex] != null)
            CoinBurst.Fly((RectTransform)itemSlots[slotIndex].transform, totalSalesText.rectTransform, 3,
                onEachArrive: () => UIMotion.Bump(totalSalesText.transform, 1.06f, 0.14f));
    }
}
