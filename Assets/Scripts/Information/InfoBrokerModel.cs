
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using R3;

public class InfoBrokerModel
{
    public List<HeroPurchaseHistory> heroPurchaseHistory { get; private set; } = new();
    public List<DungeonData> availableDungeons { get; private set; } = new();
    public RuntimeHeroData currentHeroData { get; private set; }
    
    // ?E????w???????????
    public bool IsHeroInfoPurchased { get; private set; } = false;
    
    public ReactiveProperty<List<InfoMessage>> CurrentInfoMessages { get; private set; } = new();

    private readonly ItemModel itemModel;
    private readonly DungeonRepository dungeonRepository;
    private readonly HeroModel heroModel;

    private readonly Dictionary<DungeonName, int[]> dungeonInfoCosts;

    //???b?Z?[?W?e???v???[?g
    private readonly List<string> equipmentMessageTemplates = new()
    {
        "{0}?????????B??????????????",
        "{0}??????????????B????????????",
        "??????{0}?????????A??????????????",
        "{0}??l?i??????????A????????C????v??",
        @"{0}????????????A?????????\????????",
        "?E?????A{0}?C??????????????B??????????",
        "{0}????????????????A??????????????"
    };

    // ?_???W?????????b?Z?[?W?e???v???[?g
    private readonly Dictionary<string, List<string>> specialDungeonMessages = new()
    {
        ["dungeon_ice_mist"] = new List<string>
        {
            "{0}???????????????????B?h???????????",
            "{0}??X??????????????A??????C????v??",
            "?E?????A{0}?????G???A?????S?z???????"
        },
        ["dungeon_beast_forest"] = new List<string>
        {
            "{0}??b???????????????????A????????",
            "{0}????H?}?b?v????????A??????????",
            "?E?????A{0}???????????X?^?[???l???????"
        },
        ["dungeon_volcano_prison"] = new List<string>
        {
            "{0}??}?O?}??????????????????A?s???C????v??",
            "{0}???R??M?????????????B??????????",
            "?E?????A{0}??h???S???????C????????"
        },
        ["dungeon_forgotten_mausoleum"] = new List<string>
        {
            "{0}??S??????????????A??????C????v??",
            "{0}????|?????????????????A?s????????????",
            "?E?????A{0}???????????????????????"
        },
        ["dungeon_metalion"] = new List<string>
        {
            "{0}??@?B?g???b?v???????????????A????????",
            "{0}????????????????????A?s???C????v??",
            "?E?????A{0}????Z?p???????X????????"
        }
    };

    private readonly List<string> dungeonMessageTemplates = new()
    {
        "{0}??????????B?s???C??????",
        "{0}???????????????A????????",
        "??????{0}??b???????B?s??????肩??",
        "{0}??U???@??????????A??????C????v??",
        "?E?????A{0}???????X????????",
        "{0}????x?m?F?????????A?s????????????",
        "{0}?????C??????????A???????????"
    };

    private readonly List<string> lowConfidenceTemplates = new()
    {
        "????A?m?M?????????",
        @"??A?\?z??????",
        "?????C??????????????",
        "?????????",
        "????????????A????????"
    };

    private readonly List<string> highConfidenceTemplates = new()
    {
        "????????v??",
        "?m??????",
        "??????????",
        "100%????????v??",
        "?m?M?????"
    };

    //?R???X?g???N?^
    public InfoBrokerModel(ItemModel itemModel, DungeonRepository dungeonRepository, HeroModel heroModel)
    {
        this.itemModel = itemModel;
        this.dungeonRepository = dungeonRepository;
        this.heroModel = heroModel;
        this.currentHeroData = heroModel.heroData;
        dungeonInfoCosts = LoadDungeonInfoCostsFromCSV("DungeonInfoCosts");
        InitializeDungeons();
    }

    /// <summary>
    /// CSV????_???W???????R?X?g???????
    /// CSV?t?H?[?}?b?g: DungeonName,Cost1,Cost2,Cost3
    /// </summary>
    private Dictionary<DungeonName, int[]> LoadDungeonInfoCostsFromCSV(string csvFileName)
    {
        var result = new Dictionary<DungeonName, int[]>();

        TextAsset csvFile = Resources.Load<TextAsset>(csvFileName);
        if (csvFile == null)
        {
            Debug.LogError($"CSV file not found: {csvFileName}");
            return result;
        }

        string[] lines = csvFile.text.Split('\n');

        // ?w?b?_?[?s???X?L?b?v (i = 1????J?n)
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] values = line.Split(',');
            if (values.Length < 2) continue;

            // DungeonName???Enum??p?[?X
            if (!Enum.TryParse<DungeonName>(values[0].Trim(), out var dungeonName))
            {
                Debug.LogWarning($"Unknown DungeonName in CSV row {i}: {values[0]}");
                continue;
            }

            // Cost??i2????~?j??int?z?????
            var costs = new List<int>();
            for (int j = 1; j < values.Length; j++)
            {
                if (int.TryParse(values[j].Trim(), out int cost))
                {
                    costs.Add(cost);
                }
            }

            result[dungeonName] = costs.ToArray();
        }

        Debug.Log($"Loaded {result.Count} dungeon info costs from CSV.");
        return result;
    }

    /// <summary>
    /// ?_???W???????R?X?g????????擾
    /// </summary>
    public Dictionary<DungeonName, int[]> GetDungeonInfoCosts()
    {
        return dungeonInfoCosts;
    }

    /// <summary>
    /// ?E??f?[?^????V?????X?V
    /// </summary>
    public void RefreshHeroData()
    {
        if (heroModel != null)
        {
            currentHeroData = heroModel.heroData;
            
            // weaponId / armorId からアイテム名を解決して設定
            if (currentHeroData != null)
            {
                var weapon = itemModel.GetMasterItem(currentHeroData.weaponId.Value);
                currentHeroData.weaponName.Value = weapon != null ? weapon.itemName : "";
                
                var armor = itemModel.GetMasterItem(currentHeroData.armorId.Value);
                currentHeroData.armorName.Value = armor != null ? armor.itemName : "";
            }
        }
    }

    public string GetHeroInfluenceSummary()
    {
        RefreshHeroData();

        string weaponName = currentHeroData?.weaponName.Value ?? "";
        string armorName = currentHeroData?.armorName.Value ?? "";
        var settings = Resources.Load<BattlePriceSettings>("BattlePriceSettings");
        return HeroBattleInfluence.BuildSummary(currentHeroData, weaponName, armorName, settings);
    }

    /// <summary>
    /// ?E??????w??????
    /// </summary>
    public void PurchaseHeroInfo()
    {
        IsHeroInfoPurchased = true;
        RefreshHeroData();
        Debug.Log("?E??????w??????????B");
    }

    public void RecordHeroPurchase(string itemId, int quantity, int price)
    {
        var history = new HeroPurchaseHistory(itemId, quantity, price);
        heroPurchaseHistory.Add(history);
        UpdateInfoMessages();
    }

    /// <summary>
    /// ?_???W?????????w?????AisShowedInfo ?? true ?????
    /// </summary>
    public void PurchaseDungeonInfo(DungeonName dungeonName)
    {
        var dungeon = dungeonRepository.GetById(dungeonName);
        if (dungeon == null)
        {
            Debug.LogWarning($"Dungeon not found: {dungeonName}");
            return;
        }

        dungeon.isShowedInfo = true;
        Debug.Log($"Purchased dungeon info: {dungeonName}");
    }

    public void UpdateInfoMessages()
    {
        var messages = new List<InfoMessage>();
        var equipmentMessages = GenerateEquipmentMessages();
        messages.AddRange(equipmentMessages);
        var dungeonMessages = GenerateDungeonMessages();
        messages.AddRange(dungeonMessages);
        messages = messages.OrderByDescending(m => m.confidence).Take(5).ToList();
        CurrentInfoMessages.Value = messages;
    }

    private List<InfoMessage> GenerateEquipmentMessages()
    {
        var messages = new List<InfoMessage>();
        var recentPurchases = heroPurchaseHistory
            .Where(h => (DateTime.Now - h.purchaseDate).TotalDays <= 3)
            .OrderByDescending(h => h.purchaseDate)
            .Take(3)
            .ToList();

        foreach (var purchase in recentPurchases)
        {
            var item = itemModel.GetRuntimeItem(purchase.itemId);
            var masterItem = itemModel.GetMasterItem(purchase.itemId);

            if (item != null && masterItem != null)
            {
                float confidence = CalculateEquipmentConfidence(item, purchase);
                string messageText = GenerateEquipmentMessageText(masterItem.itemName, confidence);

                var message = new InfoMessage(messageText, InfoType.Equipment, confidence)
                {
                    targetItemId = purchase.itemId
                };
                messages.Add(message);
            }
        }

        return messages;
    }

    private List<InfoMessage> GenerateDungeonMessages()
    {
        var messages = new List<InfoMessage>();

        foreach (var dungeon in availableDungeons)
        {
            float confidence = CalculateDungeonConfidence(dungeon);

            if (confidence > 0.3f)
            {
                string messageText = GenerateDungeonMessageText(dungeon.dungeonName, dungeon.dungeonName, confidence);

                var message = new InfoMessage(messageText, InfoType.Dungeon, confidence)
                {
                    targetDungeonId = dungeon.dungeonName
                };
                messages.Add(message);
            }
        }

        return messages;
    }

    // ?????m?M?x?v?Z?i???????????????j
    private float CalculateEquipmentConfidence(RuntimeItemData item, HeroPurchaseHistory purchase)
    {
        float baseConfidence = 0.5f;

        var timeSincePurchase = DateTime.Now - purchase.purchaseDate;
        float timeFactor = timeSincePurchase.TotalHours < 1 ? 1.3f :
                          timeSincePurchase.TotalHours < 6 ? 1.1f :
                          timeSincePurchase.TotalDays < 1 ? 1.0f : 0.8f;

        float quantityFactor = purchase.quantity > 1 ? 1.2f : 1.0f;

        float typeFactor = item.ItemType switch
        {
            ItemTypeData.ItemType.Weapon => 1.2f,
            ItemTypeData.ItemType.Armor => 1.1f,
            ItemTypeData.ItemType.Tool => 0.9f,
            _ => 1.0f
        };

        var masterItem = itemModel.GetMasterItem(item.ItemId);
        float levelFactor = 1.0f;
        if (masterItem != null)
        {
            int estimatedLevel = masterItem.basePrice / 100;
            int levelDiff = Mathf.Abs(currentHeroData.level.Value - estimatedLevel);
            levelFactor = levelDiff <= 2 ? 1.3f : levelDiff <= 5 ? 1.0f : 0.7f;
        }

        // ?_???W?????????{?[?i?X
        float dungeonCompatibilityFactor = CalculateDungeonCompatibilityBonus(item);

        return Mathf.Clamp01(baseConfidence * timeFactor * quantityFactor * typeFactor * levelFactor * dungeonCompatibilityFactor);
    }

    // ?w???????A?C?e???????_???W??????K????????{?[?i?X?v?Z
    private float CalculateDungeonCompatibilityBonus(RuntimeItemData item)
    {
        var masterItem = itemModel.GetMasterItem(item.ItemId);
        if (masterItem == null) return 1.0f;

        // ?E?????x????K?????_???W?????????
        var suitableDungeons = availableDungeons
            .Where(d => Math.Abs(currentHeroData.level.Value - d.recommendedLevel) <= 5)
            .ToList();

        // ?w???A?C?e???????????????_???W??????L?????`?F?b?N
        foreach (var dungeon in suitableDungeons)
        {
            if (IsItemEffectiveForDungeon(masterItem, dungeon))
            {
                return 1.2f; // ?_???W?????U????K?????A?C?e?????m?M?x?A?b?v
            }
        }

        return 1.0f;
    }

    // ?A?C?e?????_???W??????L??????????????
    private bool IsItemEffectiveForDungeon(ItemData item, DungeonData dungeon)
    {
        // ?e?_???W???????_??????`
        var effectiveAttributes = dungeon.key switch
        {
            DungeonName.IceMistCave => new[] { ItemTypeData.ItemAttribute.Fire }, // ?X?????L??
            DungeonName.DeepGreenBeastForest => new[] { ItemTypeData.ItemAttribute.Fire, ItemTypeData.ItemAttribute.Light }, // ????A????
            DungeonName.ScorchingVolcanoPrison => new[] { ItemTypeData.ItemAttribute.Water }, // ???????L??
            DungeonName.MausoleumOblivion => new[] { ItemTypeData.ItemAttribute.Light }, // ???????L??
             // => new[] { ItemTypeData.ItemAttribute.Water, ItemTypeData.ItemAttribute.Earth }, // ?@?B????E?y???L??
            _ => new ItemTypeData.ItemAttribute[] { }
        };

        return effectiveAttributes.Contains(item.itemAttribute);
    }

    private float CalculateDungeonConfidence(DungeonData dungeon)
    {
        float baseConfidence = 0.4f;

        // ???x???K???v?Z
        int levelDiff = currentHeroData.level.Value - dungeon.recommendedLevel;
        float levelFactor = levelDiff >= 3 ? 1.4f :
                           levelDiff >= 0 ? 1.1f :
                           levelDiff >= -3 ? 0.8f :
                           0.4f;

        // ?????????v?Z
        float equipmentFactor = CalculateEquipmentCompatibility(dungeon);

        // ?w???X???v?Z
        float purchaseFactor = CalculateRecentPurchaseFactor(dungeon);

        // ?V?@?\?F?_???W??????L????X?N?]??
        float riskFactor = CalculateDungeonRiskFactor(dungeon);

        return Mathf.Clamp01(baseConfidence * levelFactor * equipmentFactor * purchaseFactor * riskFactor);
    }

    // ?_???W??????L????X?N?]??
    private float CalculateDungeonRiskFactor(DungeonData dungeon)
    {
        return dungeon.key switch
        {
            DungeonName.IceMistCave => 0.9f,        // ????_???[?W?????
            DungeonName.DeepGreenBeastForest => 1.0f,    // ?W???I????x
            DungeonName.ScorchingVolcanoPrison => 0.8f,  // ?}?O?}?_???[?W???
            DungeonName.MausoleumOblivion => 0.7f, // ???|????????
            // "dungeon_metalion" => 0.6f,        // ?@?B?g???b?v??????
            _ => 1.0f
        };
    }

    private float CalculateEquipmentCompatibility(DungeonData dungeon)
    {
        var currentWeapon = itemModel.GetMasterItem(currentHeroData.weaponId.Value);
        var currentArmor = itemModel.GetMasterItem(currentHeroData.armorId.Value);

        float compatibility = 1.0f;

        // ?_???W???????L???????`?F?b?N
        if (IsItemEffectiveForDungeon(currentWeapon, dungeon))
            compatibility += 0.3f;
        if (IsItemEffectiveForDungeon(currentArmor, dungeon))
            compatibility += 0.2f;

        return compatibility;
    }

    private float CalculateRecentPurchaseFactor(DungeonData dungeon)
    {
        var recentPurchases = heroPurchaseHistory
            .Where(h => (DateTime.Now - h.purchaseDate).TotalDays <= 2)
            .ToList();

        if (recentPurchases.Count == 0) return 1.0f;

        int compatibleItems = 0;
        foreach (var purchase in recentPurchases)
        {
            var masterItem = itemModel.GetMasterItem(purchase.itemId);
            if (masterItem != null && IsItemEffectiveForDungeon(masterItem, dungeon))
            {
                compatibleItems++;
            }
        }

        return compatibleItems > 0 ? 1.4f : 1.0f;
    }

    private string GenerateEquipmentMessageText(string itemName, float confidence)
    {
        var random = new System.Random();
        var template = equipmentMessageTemplates[random.Next(equipmentMessageTemplates.Count)];
        var mainMessage = string.Format(template, itemName);

        if (confidence > 0.8f)
        {
            var highTemplate = highConfidenceTemplates[random.Next(highConfidenceTemplates.Count)];
            return $"{mainMessage}?B{highTemplate}?I";
        }
        else if (confidence < 0.5f)
        {
            var lowTemplate = lowConfidenceTemplates[random.Next(lowConfidenceTemplates.Count)];
            return $"{mainMessage}?B{lowTemplate}?B";
        }
        else
        {
            return $"{mainMessage}?B";
        }
    }

    // ?_???W??????????b?Z?[?W
    private string GenerateDungeonMessageText(string dungeonName, string dungeonId, float confidence)
    {
        var random = new System.Random();

        // ??????b?Z?[?W???????`?F?b?N
        string mainMessage;
        if (specialDungeonMessages.ContainsKey(dungeonId) && random.NextDouble() < 0.4) // 40%??m?????????b?Z?[?W
        {
            var specialTemplates = specialDungeonMessages[dungeonId];
            var specialTemplate = specialTemplates[random.Next(specialTemplates.Count)];
            mainMessage = string.Format(specialTemplate, dungeonName);
        }
        else
        {
            // ????b?Z?[?W
            var template = dungeonMessageTemplates[random.Next(dungeonMessageTemplates.Count)];
            mainMessage = string.Format(template, dungeonName);
        }

        if (confidence > 0.8f)
        {
            var highTemplate = highConfidenceTemplates[random.Next(highConfidenceTemplates.Count)];
            return $"{mainMessage}?B{highTemplate}?I";
        }
        else if (confidence < 0.5f)
        {
            var lowTemplate = lowConfidenceTemplates[random.Next(lowConfidenceTemplates.Count)];
            return $"{mainMessage}?B{lowTemplate}?B";
        }
        else
        {
            return $"{mainMessage}?B";
        }
    }

    // ダンジョンデータを DungeonRepository から取得
    public void InitializeDungeons()
    {
        if (dungeonRepository != null && dungeonRepository.availableDungeons != null)
        {
            availableDungeons = new List<DungeonData>(dungeonRepository.availableDungeons);
            Debug.Log($"[InfoBrokerModel] {availableDungeons.Count} 件のダンジョンを読み込みました");
        }
        else
        {
            availableDungeons = new List<DungeonData>();
            Debug.LogWarning("[InfoBrokerModel] DungeonRepository が null または空です");
        }
    }
}
