using System.IO;
using UnityEngine;
using R3;

public class TomsShopModel : MonoBehaviour
{
    public ReactiveProperty<int> PlayerMoney { get; private set; }
    
    public ReactiveProperty<int> BlacksmithLevel { get; private set; }
    
    public ReactiveProperty<float> Trust { get; private set; }
    
    public ReactiveProperty<int> CurrentTurn { get; private set; }

    public void Initialize(int defaultMoney = 1000, int defaultBlacksmithLevel = 1, float defaultTrust = 1 ,int defaultTurn =1)
    {
        PlayerMoney = new ReactiveProperty<int>(defaultMoney);
        BlacksmithLevel = new ReactiveProperty<int>(defaultBlacksmithLevel);
        Trust = new ReactiveProperty<float>(defaultTrust);
        CurrentTurn = new ReactiveProperty<int>(defaultTurn);
    }

    public void SavePlayerMoney()
    {
        var data = new TomsShopData(PlayerMoney.Value,BlacksmithLevel.Value,CurrentTurn.Value);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(Application.persistentDataPath + "/tomsShopData.json", json);
    }

    public void LoadPlayerMoney()
    {
        string path = Application.persistentDataPath + "/tomsShopData.json";
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<TomsShopData>(json);
            PlayerMoney.Value = data.shopMoney;
            BlacksmithLevel.Value = data.blacksmithLevel;
            CurrentTurn.Value = data.currentTurn;
        }
        else
        {
            PlayerMoney.Value = 1000; // デフォルト資金
            BlacksmithLevel.Value = 1; // デフォルトの鍛冶屋レベル
        }
    }

    public void Settlement(int price, int quantity)
    {
        // 購入処理
        if (PlayerMoney.Value >= price * quantity)
        {
            PlayerMoney.Value += price * quantity;
            Debug.Log($"販売成功: {quantity}個を{price}Gで売り。残り資金: {PlayerMoney.Value}G");
        }
    }
}