using UnityEngine;

public class TomsEventExecutor
{
    private TomsModel player;
    private ItemModel itemModel;
    private DarkShopManager darkShopManager;
    private EventFragManager eventFragManager;

    public TomsEventExecutor(TomsModel player, ItemModel itemModel, DarkShopManager darkShopManager,EventFragManager eventFragManager)
    {
        this.player = player;
        this.itemModel = itemModel;
        this.darkShopManager = darkShopManager;
        this.eventFragManager = eventFragManager;
    }

    public void Execute(TomsEvent e)
    {
        Debug.Log($"[Event] {e.title}: {e.description}");

        foreach (var cmd in e.commands)
        {
            switch (cmd.command)
            {
                case "ChangeMoney":
                    player.PlayerMoney.Value += int.Parse(cmd.parameters["price"]);
                    break;

                case "ChangeTrust":
                    player.Trust.Value += int.Parse(cmd.parameters["amount"]);
                    break;

                case "AddItem":
                    itemModel.PurchaseItem(cmd.parameters["itemId"], 1);
                    break;

                case "SetFlag":
                    eventFragManager.SetFrag(cmd.parameters["flag"], bool.Parse(cmd.parameters["value"]));
                    break;

                case "OpenYamiShop":
                    darkShopManager.OpenDarkShop(cmd.parameters["itemId"], int.Parse(cmd.parameters["price"]));
                    break;

                case "ShowMessageOnly":
                    break;

                default:
                    Debug.LogWarning($"Unknown command: {cmd.command}");
                    break;
            }
        }
    }
}