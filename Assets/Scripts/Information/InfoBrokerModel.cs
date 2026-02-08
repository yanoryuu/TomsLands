
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
    
    public ReactiveProperty<List<InfoMessage>> CurrentInfoMessages { get; private set; } = new();

    private readonly ItemModel itemModel;

    //メッセージテンプレート
    private readonly List<string> equipmentMessageTemplates = new()
    {
        "{0}買ってたよ。あれ装備するかもね",
        "{0}に興味示してたな。多分装備する",
        "さっき{0}見てたから、きっと装備するよ",
        "{0}の値段聞いてたし、装備する気だと思う",
        @"{0}手に取ってたから、装備する可能性高いね",
        "勇者さん、{0}気に入ってたみたい。装備するかも",
        "{0}について質問してたから、装備検討してるね"
    };

    // ダンジョン別のメッセージテンプレート
    private readonly Dictionary<string, List<string>> specialDungeonMessages = new()
    {
        ["dungeon_ice_mist"] = new List<string>
        {
            "{0}の寒さについて聞いてたよ。防寒対策してるのかな",
            "{0}の氷結対策調べてたし、挑戦する気だと思う",
            "勇者さん、{0}の極寒エリアのこと心配してたね"
        },
        ["dungeon_beast_forest"] = new List<string>
        {
            "{0}の獣たちについて調べてたから、挑戦するかも",
            "{0}の迷路マップ見てたし、準備してるね",
            "勇者さん、{0}の木属性モンスター対策考えてたよ"
        },
        ["dungeon_volcano_prison"] = new List<string>
        {
            "{0}のマグマ対策について質問してたから、行く気だと思う",
            "{0}の火山の熱について調べてたよ。挑戦するかもね",
            "勇者さん、{0}のドラゴンのこと気にしてたな"
        },
        ["dungeon_forgotten_mausoleum"] = new List<string>
        {
            "{0}の亡霊対策聞いてたし、挑戦する気だと思う",
            "{0}の恐怖状態について調べてたから、行く準備してるね",
            "勇者さん、{0}の光属性武器のこと聞いてたよ"
        },
        ["dungeon_metalion"] = new List<string>
        {
            "{0}の機械トラップについて調べてたから、挑戦するかも",
            "{0}の雷属性対策聞いてたし、行く気だと思う",
            "勇者さん、{0}の古代技術に興味津々だったね"
        }
    };

    private readonly List<string> dungeonMessageTemplates = new()
    {
        "{0}の情報聞いてたよ。行く気かもね",
        "{0}について調べてたから、挑戦するかも",
        "さっき{0}の話してたな。行くつもりかな",
        "{0}の攻略法聞いてたし、挑戦する気だと思う",
        "勇者さん、{0}に興味津々だったよ",
        "{0}の難易度確認してたから、行く準備してるね",
        "{0}のこと気にしてたから、きっと挑戦する"
    };

    private readonly List<string> lowConfidenceTemplates = new()
    {
        "でも、確信はないけどね",
        @"ま、予想だけど",
        "そんな気がするだけだけど",
        "たぶんだけどね",
        "よくわからないけど、そんな感じ"
    };

    private readonly List<string> highConfidenceTemplates = new()
    {
        "間違いないと思う",
        "確実だね",
        "絶対そうだよ",
        "100%そうだと思う",
        "確信してる"
    };

    //コンストラクタ
    public InfoBrokerModel(ItemModel itemModel)
    {
        this.itemModel = itemModel;
        InitializeDungeons();
    }

    public void RecordHeroPurchase(string itemId, int quantity, int price)
    {
        var history = new HeroPurchaseHistory(itemId, quantity, price);
        heroPurchaseHistory.Add(history);
        UpdateInfoMessages();
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

    // 装備確信度計算（属性相性を強化）
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

        // ダンジョン相性ボーナス
        float dungeonCompatibilityFactor = CalculateDungeonCompatibilityBonus(item);

        return Mathf.Clamp01(baseConfidence * timeFactor * quantityFactor * typeFactor * levelFactor * dungeonCompatibilityFactor);
    }

    // 購入したアイテムがどのダンジョンに適しているかボーナス計算
    private float CalculateDungeonCompatibilityBonus(RuntimeItemData item)
    {
        var masterItem = itemModel.GetMasterItem(item.ItemId);
        if (masterItem == null) return 1.0f;

        // 勇者のレベルに適したダンジョンを特定
        var suitableDungeons = availableDungeons
            .Where(d => Math.Abs(currentHeroData.level.Value - d.recommendedLevel) <= 5)
            .ToList();

        // 購入アイテムの属性がそれらのダンジョンに有効かチェック
        foreach (var dungeon in suitableDungeons)
        {
            if (IsItemEffectiveForDungeon(masterItem, dungeon))
            {
                return 1.2f; // ダンジョン攻略に適したアイテムなら確信度アップ
            }
        }

        return 1.0f;
    }

    // アイテムがダンジョンに有効かどうかの判定
    private bool IsItemEffectiveForDungeon(ItemData item, DungeonData dungeon)
    {
        // 各ダンジョンの弱点属性定義
        var effectiveAttributes = dungeon.key switch
        {
            DungeonName.IceMistCave => new[] { ItemTypeData.ItemAttribute.Fire }, // 氷に火が有効
            DungeonName.DeepGreenBeastForest => new[] { ItemTypeData.ItemAttribute.Fire, ItemTypeData.ItemAttribute.Light }, // 木に火、闇に光
            DungeonName.ScorchingVolcanoPrison => new[] { ItemTypeData.ItemAttribute.Water }, // 火に水が有効
            DungeonName.MausoleumOblivion => new[] { ItemTypeData.ItemAttribute.Light }, // 闇に光が有効
             // => new[] { ItemTypeData.ItemAttribute.Water, ItemTypeData.ItemAttribute.Earth }, // 機械に水・土が有効
            _ => new ItemTypeData.ItemAttribute[] { }
        };

        return effectiveAttributes.Contains(item.itemAttribute);
    }

    private float CalculateDungeonConfidence(DungeonData dungeon)
    {
        float baseConfidence = 0.4f;

        // レベル適正計算
        int levelDiff = currentHeroData.level.Value - dungeon.recommendedLevel;
        float levelFactor = levelDiff >= 3 ? 1.4f :
                           levelDiff >= 0 ? 1.1f :
                           levelDiff >= -3 ? 0.8f :
                           0.4f;

        // 装備相性計算
        float equipmentFactor = CalculateEquipmentCompatibility(dungeon);

        // 購入傾向計算
        float purchaseFactor = CalculateRecentPurchaseFactor(dungeon);

        // 新機能：ダンジョン固有のリスク評価
        float riskFactor = CalculateDungeonRiskFactor(dungeon);

        return Mathf.Clamp01(baseConfidence * levelFactor * equipmentFactor * purchaseFactor * riskFactor);
    }

    // ダンジョン固有のリスク評価
    private float CalculateDungeonRiskFactor(DungeonData dungeon)
    {
        return dungeon.key switch
        {
            DungeonName.IceMistCave => 0.9f,        // 極寒ダメージでやや危険
            DungeonName.DeepGreenBeastForest => 1.0f,    // 標準的な危険度
            DungeonName.ScorchingVolcanoPrison => 0.8f,  // マグマダメージで危険
            DungeonName.MausoleumOblivion => 0.7f, // 恐怖状態で非常に危険
            // "dungeon_metalion" => 0.6f,        // 機械トラップで最も危険
            _ => 1.0f
        };
    }

    private float CalculateEquipmentCompatibility(DungeonData dungeon)
    {
        var currentWeapon = itemModel.GetMasterItem(currentHeroData.weaponId.Value);
        var currentArmor = itemModel.GetMasterItem(currentHeroData.armorId.Value);

        float compatibility = 1.0f;

        // ダンジョン別の有効属性チェック
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
            return $"{mainMessage}。{highTemplate}！";
        }
        else if (confidence < 0.5f)
        {
            var lowTemplate = lowConfidenceTemplates[random.Next(lowConfidenceTemplates.Count)];
            return $"{mainMessage}。{lowTemplate}。";
        }
        else
        {
            return $"{mainMessage}。";
        }
    }

    // ダンジョン特別メッセージ
    private string GenerateDungeonMessageText(string dungeonName, string dungeonId, float confidence)
    {
        var random = new System.Random();

        // 特別メッセージがあるかチェック
        string mainMessage;
        if (specialDungeonMessages.ContainsKey(dungeonId) && random.NextDouble() < 0.4) // 40%の確率で特別メッセージ
        {
            var specialTemplates = specialDungeonMessages[dungeonId];
            var specialTemplate = specialTemplates[random.Next(specialTemplates.Count)];
            mainMessage = string.Format(specialTemplate, dungeonName);
        }
        else
        {
            // 通常メッセージ
            var template = dungeonMessageTemplates[random.Next(dungeonMessageTemplates.Count)];
            mainMessage = string.Format(template, dungeonName);
        }

        if (confidence > 0.8f)
        {
            var highTemplate = highConfidenceTemplates[random.Next(highConfidenceTemplates.Count)];
            return $"{mainMessage}。{highTemplate}！";
        }
        else if (confidence < 0.5f)
        {
            var lowTemplate = lowConfidenceTemplates[random.Next(lowConfidenceTemplates.Count)];
            return $"{mainMessage}。{lowTemplate}。";
        }
        else
        {
            return $"{mainMessage}。";
        }
    }

    //ダンジョンデータ
    private void InitializeDungeons()
    {
        availableDungeons = new List<DungeonData>
    {
        // //氷霧の洞窟
        // new DungeonData(),
        //
        // //深緑の獣林
        // new DungeonData(),
        //
        // // 灼熱の火山牢
        // new DungeonData(),
        //
        // // 忘却の霊廟
        // new DungeonData(),
        //
        // //古代機構城メタリオン
        // new DungeonData()
    };
    }
}