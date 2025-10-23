using System.IO;
using UnityEngine;
using R3;

public class TomsModel
{
    public ReactiveProperty<int> PlayerMoney { get; private set; }
    
    public ReactiveProperty<int> BlacksmithLevel { get; private set; }
    
    public ReactiveProperty<int> ToolShopLevel { get; private set; }
    
    public ReactiveProperty<int> InfoBrokerLevel { get; private set; }
    
    public ReactiveProperty<float> Trust { get; private set; }
    
    public ReactiveProperty<int> CurrentTurn { get; private set; }

    public TomsModel()
    {
        Initialize();
        LoadPlayerMoney();
        
    }

    public void Initialize(int defaultMoney = GameConst.InitMoney, int defaultBlacksmithLevel = 1,int defaultToolLevel = 1,int defaultInfoBrokerLevel = 1, float defaultTrust = 1 ,int defaultTurn =1)
    {
        PlayerMoney = new ReactiveProperty<int>(defaultMoney);
        BlacksmithLevel = new ReactiveProperty<int>(defaultBlacksmithLevel);
        ToolShopLevel = new ReactiveProperty<int>(defaultToolLevel);
        InfoBrokerLevel = new ReactiveProperty<int>(defaultInfoBrokerLevel);
        Trust = new ReactiveProperty<float>(defaultTrust);
        CurrentTurn = new ReactiveProperty<int>(defaultTurn);
    }

    public void SavePlayerMoney()
    {
        var data = new TomsData(PlayerMoney.Value,BlacksmithLevel.Value,CurrentTurn.Value);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(Application.persistentDataPath + "/tomsData.json", json);
    }

    public void LoadPlayerMoney()
    {
        string path = Application.persistentDataPath + "/tomsData.json";
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<TomsData>(json);
            PlayerMoney.Value = data.shopMoney;
            BlacksmithLevel.Value = data.blacksmithLevel;
            CurrentTurn.Value = data.currentTurn;
        }
        else
        {
            PlayerMoney.Value = GameConst.InitMoney; // デフォルト資金
            BlacksmithLevel.Value = 1; // デフォルトの鍛冶屋レベル
            ToolShopLevel.Value = 1;
            InfoBrokerLevel.Value = 1;
        }
    }

    
    public void Settlement(int price)
    {
        // 売り処理
        PlayerMoney.Value += price;
        
    }

    public void PurchaseItem(int price)
    {
        //購入処理
        PlayerMoney.Value -= price;
        Debug.Log(PlayerMoney.Value);
    }
}