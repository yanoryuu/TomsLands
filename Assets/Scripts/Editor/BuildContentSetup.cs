#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 9ビルド対応のコンテンツ（レリック17種・マシン4種追加・製造機の選択式化）と
/// 設備画面の追加UI（生産アイテム選択・設置済み一覧）を生成するエディタツール。
/// MCP経由でも手動でも実行できるよう、メニューアイテムとして提供する。
/// 何度実行しても安全（既存アセットは値を上書き、UIは存在チェック）。
/// </summary>
public static class BuildContentSetup
{
    // =====================================================
    // ① マスターデータ生成（レリック・マシン）
    // =====================================================

    [MenuItem("Tools/TomsLands/ビルドコンテンツ生成（レリック・マシン）")]
    public static void CreateBuildContent()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings != null ? settings.DefaultGroup : null;
        int created = 0, updated = 0;

        T Ensure<T>(string path, out bool isNew) where T : ScriptableObject
        {
            var so = AssetDatabase.LoadAssetAtPath<T>(path);
            isNew = so == null;
            if (isNew)
            {
                so = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(so, path);
                created++;
            }
            else updated++;
            return so;
        }

        void Register(string path, string address, string label)
        {
            if (settings == null) return;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = address;
            settings.AddLabel(label, false);
            entry.SetLabel(label, true, true);
        }

        // ---------- レリック ----------
        RelicDefinition Relic(string id, string name, string desc, RelicRarity rarity, bool curse,
            (RelicStatId stat, RelicOp op, float v)[] mods, (string key, float param)[] behaviours = null)
        {
            string path = $"Assets/Resources_moved/Relic/{id}.asset";
            var r = Ensure<RelicDefinition>(path, out _);
            r.relicId = id; r.relicName = name; r.description = desc; r.rarity = rarity; r.isCurse = curse;
            r.modifiers = mods.Select(m => new RelicModifier { stat = m.stat, op = m.op, value = m.v }).ToList();
            r.behaviours = behaviours != null
                ? behaviours.Select(b => new RelicBehaviourRef { behaviourKey = b.key, param = b.param }).ToList()
                : new List<RelicBehaviourRef>();
            EditorUtility.SetDirty(r);
            Register(path, "Relic/" + id, "RelicData");
            return r;
        }

        // 魔王ビルド
        Relic("relic_demon_pact", "魔王の密約", "魔王軍との裏取引。ダンジョン防衛報酬+50%。", RelicRarity.Rare, false,
            new[] { (RelicStatId.DefeatRewardMul, RelicOp.Mul, 1.5f) });
        Relic("relic_demon_banner", "魔王軍の軍旗", "店先に掲げると勇者の士気が下がる。防衛報酬+30%、勇者の強さ-15%。", RelicRarity.Epic, false,
            new[] { (RelicStatId.DefeatRewardMul, RelicOp.Mul, 1.3f), (RelicStatId.HeroPowerMul, RelicOp.Mul, 0.85f) });
        // 薄利多売ビルド
        Relic("relic_wholesaler_pass", "問屋の顔パス", "顔なじみの問屋割引。仕入れ価格-5%。", RelicRarity.Common, false,
            new[] { (RelicStatId.ProcurementCostMul, RelicOp.Mul, 0.95f) });
        Relic("relic_guild_license", "商人ギルドの証", "正会員だけの卸値。仕入れ価格-15%。", RelicRarity.Epic, false,
            new[] { (RelicStatId.ProcurementCostMul, RelicOp.Mul, 0.85f) });
        // 不労所得ビルド
        Relic("relic_dividend_book", "配当王の手帳", "配当銘柄の目利き。配当収入+50%。", RelicRarity.Rare, false,
            new[] { (RelicStatId.DividendMul, RelicOp.Mul, 1.5f) });
        Relic("relic_compound_tome", "複利の教本", "利息は雪だるま式に。債券利息+50%。", RelicRarity.Rare, false,
            new[] { (RelicStatId.FinanceYieldMul, RelicOp.Mul, 1.5f) });
        // インフルエンサービルド
        Relic("relic_gossip_notes", "話題のネタ帳", "配信ネタが尽きない。バズ発生率+5%。", RelicRarity.Common, false,
            new[] { (RelicStatId.BuzzChanceAdd, RelicOp.Add, 5f) });
        Relic("relic_charisma", "生まれつきの華", "何をしても絵になる。バズ発生率+12%、営業売上+5%。", RelicRarity.Epic, false,
            new[] { (RelicStatId.BuzzChanceAdd, RelicOp.Add, 12f), (RelicStatId.ShopRevenueMul, RelicOp.Mul, 1.05f) });
        // 相場師ビルド
        Relic("relic_trader_creed", "相場師の心得", "約定値幅が±15%広がる。上振れも下振れも大きくなる。", RelicRarity.Rare, false,
            new[] { (RelicStatId.SellClampAdd, RelicOp.Add, 0.15f) });
        Relic("relic_allin_dice", "命知らずのサイコロ", "約定値幅が±35%広がるが、借金取りも強気になる（返済額+10%）。", RelicRarity.Rare, true,
            new[] { (RelicStatId.SellClampAdd, RelicOp.Add, 0.35f), (RelicStatId.DebtAmountMul, RelicOp.Mul, 1.1f) });
        // 強運ビルド
        Relic("relic_fairy_coin", "妖精のコイン", "良いことが起きやすい気がする。イベントで得る金額+50%。", RelicRarity.Common, false,
            new[] { (RelicStatId.EventRewardMul, RelicOp.Mul, 1.5f) });
        Relic("relic_star_chart", "星詠みの札", "運命を味方につける。イベントで得る金額が2倍。", RelicRarity.Epic, false,
            new[] { (RelicStatId.EventRewardMul, RelicOp.Mul, 2f) });
        // 自転車操業ビルド
        Relic("relic_collector_secret", "借金取りの弱み", "先方の帳簿の穴を知っている。返済額-10%。", RelicRarity.Rare, false,
            new[] { (RelicStatId.DebtAmountMul, RelicOp.Mul, 0.9f) });
        // 工房ビルド
        Relic("relic_blueprint", "職人の設計図", "無駄のない工程表。製造機の毎朝の製造予算+500G。", RelicRarity.Rare, false,
            new[] { (RelicStatId.ProductionBudgetAdd, RelicOp.Add, 500f) });
        Relic("relic_furnace_core", "魔導炉心", "炉が眠らない。製造機の毎朝の製造予算+1,200G。", RelicRarity.Epic, false,
            new[] { (RelicStatId.ProductionBudgetAdd, RelicOp.Add, 1200f) });
        // 百貨店ビルド
        Relic("relic_display_manual", "大陳列マニュアル", "什器の魔術。同時陳列できる銘柄+2。", RelicRarity.Rare, false,
            new[] { (RelicStatId.DisplayKindsAdd, RelicOp.Add, 2f) });
        Relic("relic_dept_creed", "百貨店の理念", "「並べたものは売れる」。同時陳列+3、営業売上+5%。", RelicRarity.Epic, false,
            new[] { (RelicStatId.DisplayKindsAdd, RelicOp.Add, 3f), (RelicStatId.ShopRevenueMul, RelicOp.Mul, 1.05f) });

        // ---------- マシン ----------
        ShopMachineData Machine(string id, string name, string desc, ShopMachineEffectType effect, int cost, int reqLv)
        {
            string path = $"Assets/Resources_moved/ShopMachine/{id}.asset";
            var m = Ensure<ShopMachineData>(path, out _);
            m.machineId = id; m.machineName = name; m.description = desc; m.effectType = effect; m.cost = cost; m.requiredShopLevel = reqLv;
            EditorUtility.SetDirty(m);
            Register(path, "ShopMachine/" + id, "ShopMachineData");
            return m;
        }

        // 既存の自動鍛造炉を「選択式」に更新
        var maker = AssetDatabase.LoadAssetAtPath<ShopMachineData>("Assets/Resources_moved/ShopMachine/machine_maker.asset");
        if (maker != null)
        {
            maker.machineName = "自動鍛造炉";
            maker.description = "選んだ武具を毎朝コツコツ鍛え上げる魔導炉。高い武具ほど時間がかかる。";
            maker.dailyItemSelectable = true;
            maker.dailyProductionBudget = 1000;
            EditorUtility.SetDirty(maker);
        }

        var moneyM = Machine("machine_money_m", "中型貯金ゴーレム", "そこそこ稼いでくる働き者のゴーレム。", ShopMachineEffectType.DailyMoney, 7000, 2);
        moneyM.dailyMoney = 1300; EditorUtility.SetDirty(moneyM);
        var makerL = Machine("machine_maker_l", "大型魔導炉", "工房の主役。選んだ武具を高速で製造する。", ShopMachineEffectType.DailyItem, 15000, 4);
        makerL.dailyItemSelectable = true; makerL.dailyProductionBudget = 2500; EditorUtility.SetDirty(makerL);
        var attractL = Machine("machine_attract_l", "大道芸のステージ", "毎日が祭り。営業売上+20%。", ShopMachineEffectType.RevenueMultiplier, 18000, 4);
        attractL.revenueMultiplierBonus = 0.20f; EditorUtility.SetDirty(attractL);
        var fridgeL = Machine("machine_fridge_l", "特級鮮度ケース", "商品がいつでも輝いて見える。全商品の需要下限+15%。", ShopMachineEffectType.DemandFloorBonus, 12000, 3);
        fridgeL.demandFloorBonus = 0.15f; EditorUtility.SetDirty(fridgeL);

        AssetDatabase.SaveAssets();
        if (settings != null)
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);

        Debug.Log($"[BuildContentSetup] 完了: 新規 {created} 件 / 更新 {updated} 件（レリック17種・マシン、Addressables登録済み）");
    }

    // =====================================================
    // ② 設備画面の追加UI（生産アイテム選択ドロップダウン + 設置済み一覧）
    // =====================================================

    [MenuItem("Tools/TomsLands/設備画面に生産選択UIを配置（TomsShopシーンを開いて実行）")]
    public static void SetupMachineShopUI()
    {
        var view = Object.FindFirstObjectByType<ShopMachineView>(FindObjectsInactive.Include);
        if (view == null)
        {
            Debug.LogError("[BuildContentSetup] ShopMachineView が見つかりません。TomsShop シーンを開いてから実行してください。");
            return;
        }

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/MPLUSRounded1c-Bold SDFPlusPadding.asset");
        var so = new SerializedObject(view);
        var detail = (so.FindProperty("purchaseButton").objectReferenceValue as Button)?.transform.parent;
        if (detail == null)
        {
            Debug.LogError("[BuildContentSetup] Detail パネルが特定できません（purchaseButton 未配線）。");
            return;
        }

        bool changed = false;

        // --- 生産アイテム選択（ドロップダウン） ---
        if (so.FindProperty("producedItemDropdown").objectReferenceValue == null)
        {
            var groupGO = new GameObject("ProducedItemGroup", typeof(RectTransform));
            groupGO.layer = 5;
            groupGO.transform.SetParent(detail, false);
            var grt = groupGO.GetComponent<RectTransform>();
            grt.anchoredPosition = new Vector2(0, -120);
            grt.sizeDelta = new Vector2(580, 60);

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.layer = 5;
            labelGO.transform.SetParent(groupGO.transform, false);
            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = "生産:"; label.fontSize = 24; label.alignment = TextAlignmentOptions.Left;
            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchoredPosition = new Vector2(-240, 0); lrt.sizeDelta = new Vector2(90, 44);

            var ddGO = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            ddGO.name = "ProducedItemDropdown";
            foreach (var t in ddGO.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
            ddGO.transform.SetParent(groupGO.transform, false);
            var drt = ddGO.GetComponent<RectTransform>();
            drt.anchoredPosition = new Vector2(50, 0); drt.sizeDelta = new Vector2(440, 48);
            var dropdown = ddGO.GetComponent<TMP_Dropdown>();
            if (font != null)
            {
                foreach (var tmp in ddGO.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    tmp.font = font;
                    tmp.fontSize = 20;
                }
            }

            so.FindProperty("producedItemGroup").objectReferenceValue = groupGO;
            so.FindProperty("producedItemDropdown").objectReferenceValue = dropdown;
            groupGO.SetActive(false);
            changed = true;
        }

        // --- 設置済み一覧（スクロール） ---
        if (so.FindProperty("placementListParent").objectReferenceValue == null)
        {
            var window = detail.parent; // Window
            var groupGO = new GameObject("PlacementGroup", typeof(RectTransform));
            groupGO.layer = 5;
            groupGO.transform.SetParent(window, false);
            var grt = groupGO.GetComponent<RectTransform>();
            grt.anchoredPosition = new Vector2(-380, -330); // カタログの下
            grt.sizeDelta = new Vector2(620, 130);
            var bg = groupGO.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            var scroll = groupGO.AddComponent<ScrollRect>();
            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.layer = 5;
            viewportGO.transform.SetParent(groupGO.transform, false);
            var vrt = viewportGO.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            viewportGO.AddComponent<RectMask2D>();
            var vImg = viewportGO.AddComponent<Image>(); vImg.color = new Color(1, 1, 1, 0.01f);

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.layer = 5;
            contentGO.transform.SetParent(viewportGO.transform, false);
            var crt = contentGO.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = new Vector2(8, 0); crt.offsetMax = new Vector2(-8, 0);
            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6; vlg.childControlHeight = false; vlg.childControlWidth = true; vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(4, 4, 6, 6);
            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt; scroll.viewport = vrt; scroll.horizontal = false; scroll.vertical = true;

            so.FindProperty("placementGroup").objectReferenceValue = groupGO;
            so.FindProperty("placementListParent").objectReferenceValue = contentGO.transform;
            groupGO.SetActive(false);
            changed = true;
        }

        if (changed)
        {
            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log("[BuildContentSetup] 設備画面のUIを配置してシーンを保存しました。");
        }
        else
        {
            Debug.Log("[BuildContentSetup] 追加UIは配置済みです。");
        }
    }
}
#endif
