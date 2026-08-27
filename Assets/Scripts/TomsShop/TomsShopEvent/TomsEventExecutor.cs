using UnityEngine;

public class TomsEventExecutor
{
    private TomsModel player;
    private ItemModel itemModel;
    private DarkShopManager darkShopManager;
    private EventFragManager eventFragManager;
    private ShopStatusModel shopStatusModel;

    public TomsEventExecutor(TomsModel player, ItemModel itemModel, DarkShopManager darkShopManager, EventFragManager eventFragManager, ShopStatusModel shopStatusModel)
    {
        this.player = player;
        this.itemModel = itemModel;
        this.darkShopManager = darkShopManager;
        this.eventFragManager = eventFragManager;
        this.shopStatusModel = shopStatusModel;
    }

    public void Execute(TomsEvent e)
    {
        Debug.Log($"[Event] {e.title}: {e.description}");

        foreach (var cmd in e.commands)
        {
            switch (cmd.command)
            {
                case "ChangeMoney":
                    player.PlayerMoney.Value += ParseIntParam(e, cmd, "amount");
                    player.SavePlayerMoney();
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

                case "ShowMessageOnly":
                    break;

                default:
                    Debug.LogWarning($"Unknown command: {cmd.command}");
                    break;
            }
        }
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
