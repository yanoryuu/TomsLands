using System.Collections.Generic;
using R3;
using UnityEngine;

public class BlackSmithModel
{
    public List<RuntimeItemData> armorRuntimeItems { get; private set; }
    public List<RuntimeItemData> weaponRuntimeItems { get; private set; }

    public Dictionary<string, BlackSmithItemData> itemCount { get; }

    // ========================================
    // 会話チュートリアルからの指示（TutorialPresenter → BlackSmithPresenter の受け渡し）
    // ========================================

    /// <summary>
    /// 鍛冶屋の説明会話が終わった。初回オート購入待ちなら、調整不可の予算ポップアップを開かせる。
    /// </summary>
    public Subject<Unit> TutorialAutoBuyRequested { get; } = new();

    /// <summary>
    /// 「需要が高く価格が安い」商品を一覧で強調表示するか。
    /// 立て替え後の会話が終わったら true、鍛冶屋を閉じたら false に戻す。
    /// </summary>
    public ReactiveProperty<bool> TutorialBargainHighlight { get; } = new(false);

    public BlackSmithModel()
    {
        armorRuntimeItems  = new List<RuntimeItemData>();
        weaponRuntimeItems = new List<RuntimeItemData>();
        itemCount          = new Dictionary<string, BlackSmithItemData>();
    }

    public void SetRuntimeItems(List<RuntimeItemData> weaponRuntimeItems, List<RuntimeItemData> armorRuntimeItems)
    {
        Debug.Log($"武器の数{weaponRuntimeItems.Count}、防具の数{armorRuntimeItems.Count}");
        this.armorRuntimeItems  = armorRuntimeItems;
        this.weaponRuntimeItems = weaponRuntimeItems;
    }

    public void SetItemCount(string key, int count, int maxCount)
    {
        if (itemCount.TryAdd(key, new BlackSmithItemData(count, maxCount))) return;
        if (itemCount.TryGetValue(key, out var value))
        {
            value.maxCount.Value = maxCount;
            value.count.Value    = Mathf.Clamp(count, 0, value.maxCount.Value);
        }
    }

    /// <summary>
    /// 予約数を加算/減算（0〜maxCountにクランプして保存）
    /// 戻り値：更新後の予約数
    /// </summary>
    public int AddToCount(string itemId, int delta)
    {
        if (!itemCount.TryGetValue(itemId, out var value))
            return 0;

        int next = Mathf.Clamp(value.count.Value + delta, 0, value.maxCount.Value);
        if (next != value.count.Value)
            value.count.Value = next; // ReactiveProperty → 購読者(ビュー等)に通知
        return next;
    }

    /// <summary>
    /// 直接指定（0〜maxにクランプ）
    /// </summary>
    public int SetCountClamped(string itemId, int newValue)
    {
        if (!itemCount.TryGetValue(itemId, out var value))
            return 0;

        int next = Mathf.Clamp(newValue, 0, value.maxCount.Value);
        if (next != value.count.Value)
            value.count.Value = next;
        return next;
    }

    // 購入確定：現在の予約数を返し、count=0、maxCountを購入後残量に更新
    public int PurchaseItem(string itemId, int maxStockAfter)
    {
        if (itemCount.TryGetValue(itemId, out var value))
        {
            var count = value.count.Value;
            SetItemCount(itemId, 0, maxStockAfter);
            return count;
        }
        return 0;
    }
}

public class BlackSmithItemData
{
    public ReactiveProperty<int> count { get; }
    public ReactiveProperty<int> maxCount { get; }

    public BlackSmithItemData(int count, int maxCount)
    {
        this.count    = new ReactiveProperty<int>(count);
        this.maxCount = new ReactiveProperty<int>(maxCount);
    }
}
