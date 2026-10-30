using UnityEngine;

public class TomsEventExecutor
{
    private readonly TomsModel player;
    private readonly ItemModel itemModel;
    private readonly DarkShopManager darkShopManager;
    private readonly EventFragManager eventFragManager;
    private readonly ShopStatusModel shopStatusModel;
    private readonly RelicInventoryModel relicInventory;
    private readonly RelicRewardService relicRewardService;
    private readonly RelicEffectResolver relicResolver;

    public TomsEventExecutor(TomsModel player, ItemModel itemModel, DarkShopManager darkShopManager, EventFragManager eventFragManager, ShopStatusModel shopStatusModel,
        RelicInventoryModel relicInventory, RelicRewardService relicRewardService, RelicEffectResolver relicResolver)
    {
        this.relicResolver = relicResolver;
        this.player = player;
        this.itemModel = itemModel;
        this.darkShopManager = darkShopManager;
        this.eventFragManager = eventFragManager;
        this.shopStatusModel = shopStatusModel;
        this.relicInventory = relicInventory;
        this.relicRewardService = relicRewardService;
    }

    public void Execute(TomsEvent e)
    {
        Debug.Log($"[Event] {e.title}: {e.description}");

        foreach (var cmd in e.commands)
        {
            switch (cmd.command)
            {
                case "ChangeMoney":
                    ApplyChangeMoney(player, relicResolver, e.id, ParseIntParam(e, cmd, "amount"));
                    break;

                case "ChangeTrust":
                    // バズ判定・炎上判定が参照する店ステータスの信頼度を変更する
                    // （statMin～statMax にクランプされる。旧実装の TomsModel.Trust は未使用のため廃止）
                    shopStatusModel.ChangeTrust(ParseIntParam(e, cmd, "amount"));
                    shopStatusModel.SaveData();
                    break;

                case "AddItem":
                    if (cmd.parameters.TryGetValue("itemId", out var addItemId))
                    {
                        itemModel.PurchaseItem(addItemId, 1);
                        itemModel.SaveData();
                    }
                    break;

                case "SetFlag":
                    if (cmd.parameters.TryGetValue("flag", out var flag) &&
                        cmd.parameters.TryGetValue("value", out var flagValue) &&
                        bool.TryParse(flagValue, out var parsedFlag))
                    {
                        eventFragManager.SetFrag(flag, parsedFlag);
                    }
                    break;

                case "OpenYamiShop":
                    if (cmd.parameters.TryGetValue("itemId", out var yamiItemId))
                    {
                        darkShopManager.OpenDarkShop(yamiItemId, ParseIntParam(e, cmd, "price"));
                    }
                    break;

                case "GrantRelic":
                    // イベント報酬でレリックを付与（relicId 指定）。呪いレリックの付与にも使う
                    if (cmd.parameters.TryGetValue("relicId", out var relicId))
                    {
                        relicInventory.Add(relicId, player.CurrentTurn.Value, GameConst.RelicMaxEquipSlots);
                    }
                    break;

                case "GrantRandomRelic":
                    // レア度重み付きランダム付与（所持済み・呪い除外）
                    {
                        var candidates = relicRewardService.PickChoices(1);
                        if (candidates.Count > 0)
                            relicInventory.Add(candidates[0].relicId, player.CurrentTurn.Value, GameConst.RelicMaxEquipSlots);
                    }
                    break;

                case "ShowMessageOnly":
                    break;

                default:
                    Debug.LogWarning($"Unknown command: {cmd.command}");
                    break;
            }
        }
    }

    /// <summary>
    /// ChangeMoney コマンドを適用して保存する。
    /// インライン経路（Execute）と EventScene 経路（EventScenePresenter）で同じ結果にするための共通処理。
    /// </summary>
    public static void ApplyChangeMoney(TomsModel player, RelicEffectResolver relicResolver, string eventId, int amount)
    {
        // レリック補正（強運ビルド: EventRewardMul）。もらえる金額のみ増幅し、ペナルティ(負値)はそのまま
        if (amount > 0 && relicResolver != null)
            amount = relicResolver.ModifyInt(RelicStatId.EventRewardMul, amount);
        // ペナルティ（負値）は所持金を下回らない（所持金がマイナスになると購入上限の計算や
        // 表示が壊れるため。払いきれない分は取り立てない）。Jev AutoPlay で検出（2026-10-05）
        int before = player.PlayerMoney.Value;
        // 納付日の朝の損は、所持金を納付額より下へは減らさない（streamTuning.paymentMorningLossFloor。既定 off = 0 で止めるだけ）
        int floor = amount < 0 ? StreamTuning.PaymentMorningFloor(player, relicResolver) : 0;
        int after = StreamTuning.MoneyAfterLoss(before, amount, floor);
        player.PlayerMoney.Value = after;
        if (amount < 0 && before + amount < 0 && floor <= 0)
            Debug.Log($"[Event] {eventId}: 支払い {-amount}G のうち {-(before + amount)}G は所持金不足のため免除");
        if (amount < 0 && floor > 0 && after > Mathf.Max(0, before + amount))
            Debug.Log($"[Event] {eventId}: 納付日の朝なので、損 {-amount}G のうち {after - Mathf.Max(0, before + amount)}G は取らない（所持金 {before}G・納付額 {floor}G）");
        player.SavePlayerMoney();
    }

    /// <summary>
    /// コマンドの整数パラメータを安全に取得する。
    /// 欠損・非数値（マスターデータ不備）の場合は 0 を返して警告のみ出す（例外で進行を止めない）。
    /// </summary>
    private static int ParseIntParam(TomsEvent e, TomsEventCommand cmd, string key)
    {
        if (!cmd.parameters.TryGetValue(key, out var raw) || !int.TryParse(raw, out var value))
        {
            Debug.LogWarning($"[TomsEventExecutor] イベント {e.id}「{e.title}」の {cmd.command}.{key} が数値ではありません（'{raw}'）。0として扱います。");
            return 0;
        }
        return value;
    }
}
