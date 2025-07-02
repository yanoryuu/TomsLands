using System.IO;
using UnityEngine;
using R3;

public class TomsShopModel : MonoBehaviour
{
    public ReactiveProperty<int> PlayerMoney { get; private set; }
    
    public ReactiveProperty<int> BlacksmithLevel { get; private set; }

    public void Initialize(int defaultMoney = 1000, int defaultBlacksmithLevel = 1)
    {
        PlayerMoney = new ReactiveProperty<int>(defaultMoney);
        BlacksmithLevel = new ReactiveProperty<int>(defaultBlacksmithLevel);
    }

    public void SavePlayerMoney()
    {
        var data = new TomsShopData(PlayerMoney.Value);
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
        }
        else
        {
            PlayerMoney.Value = 1000; // デフォルト資金
            BlacksmithLevel.Value = 1; // デフォルトの鍛冶屋レベル
        }
    }
}