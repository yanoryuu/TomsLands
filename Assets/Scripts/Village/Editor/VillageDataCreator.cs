#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 村施設マスター（VillageFacilityData 13種）を一括生成するエディタツール。
/// メニュー: Tools > TomsLands > データ生成 > 村施設データ生成（全部入り）
/// 既存アセットはスキップするので安全に再実行できる。
/// 生成後は「Addressables一括登録（Resources_moved）」でラベル VillageFacilityData を付与すること。
/// 数値の出典: Docs/Village_Meta_Design.md §11-B（2026-08-30確定表）
/// </summary>
public static class VillageDataCreator
{
    private const string BasePath = "Assets/Resources_moved/Village";

    [MenuItem("Tools/TomsLands/データ生成/村施設データ生成（全部入り）")]
    private static void CreateAll()
    {
        EnsureFolder();

        Create("hall", "領主館", "村の中心。ここが立派になると村が広がる。", 0,
            (4000, "全施設の Lv2 への拡張を解禁"),
            (18000, "全施設の Lv3 への拡張と、祠の建設を解禁"),
            (40000, "産業の土地スロット +1（産業投資は今後実装）"));

        Create("guild", "冒険者ギルド", "腕利きが集まればお宝も集まる。", 0,
            (4000, "レリック Tier1（5種）が報酬の抽選に加わる"),
            (10000, "レリック Tier2（4種）が加わる"),
            (20000, "レリック Tier3（4種）が加わる"));

        Create("antique", "骨董品店", "掘り出し物と目利きの店。", 0,
            (5000, "スターターレリックの候補に Rare が並ぶ"),
            (13000, "レリック3択を辞退したときの買取額 +50%"));

        Create("shrine", "祠", "商売の神は選択肢を増やしてくれる。", 2,
            (12000, "レリック報酬の選択肢が 3 → 4 になる"));

        Create("bank", "銀行", "信用は建物で示すもの。", 0,
            (5000, "借入枠の最終段階（20,000G）の拡張を解放"),
            (12000, "借入の利息 50% → 45%"),
            (22000, "借入の利息 45% → 40%"));

        Create("warehouse", "倉庫", "持てるだけ持っていけ。", 0,
            (6000, "持ち込み枠 +1（計3）"),
            (14000, "持ち込み枠 +1（計4）"),
            (24000, "持ち込み枠 +1（計5）"));

        Create("road", "街道整備", "道が良ければ商いも早い。", 0,
            (2000, "ラン開始時の初期資金 +1,000G"),
            (6000, "初期資金 さらに +1,000G（計+2,000G）"),
            (13000, "初期資金 さらに +1,000G（計+3,000G）"));

        Create("press", "印刷所", "噂は刷って広める時代。", 0,
            (4000, "開始時フォロワー +500・注目 +10"),
            (9000, "さらにフォロワー +500・注目 +10（計+1,000/+20）"));

        Create("artisan", "職人組合", "村の鍛冶が店を支える。", 0,
            (7000, "ラン開始時から鍛冶屋 Lv2"),
            (16000, "鍛冶屋の開発費 -15%"));

        Create("tavern", "酒場（情報局）", "酔った口は市場より正直。", 0,
            (7000, "ラン開始時から情報屋 Lv2（金融商品が最初から使える）"),
            (16000, "取引所の売買手数料 -1%"));

        Create("workshop", "工房区画", "機械の音は繁栄の音。", 0,
            (6000, "マシン設置枠 +1"),
            (15000, "マシンの購入費 -15%"));

        Create("farm", "農場", "村の実りが店の朝を支える。", 0,
            (3000, "毎朝 +50G（朝レポート「村からの収穫」）"),
            (7000, "毎朝 +100G"),
            (14000, "毎朝 +150G"));

        Create("training", "訓練所", "鍛えた勇者はよく稼ぐ。", 0,
            (5000, "配信の売上 +5%"),
            (12000, "配信の売上 +10%"));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[VillageDataCreator] 村施設マスター（13種）を作成しました: {BasePath}\n" +
                  "続けて Tools > TomsLands > データ生成 > Addressables一括登録（Resources_moved） を実行してください。");
    }

    private static void Create(string id, string name, string description, int requiredHallLevel,
        params (int cost, string effect)[] levels)
    {
        string path = $"{BasePath}/Village_{id}.asset";
        if (AssetDatabase.LoadAssetAtPath<VillageFacilityData>(path) != null)
        {
            Debug.Log($"[VillageDataCreator] 既存のアセットをスキップ: {path}");
            return;
        }

        var data = ScriptableObject.CreateInstance<VillageFacilityData>();
        data.facilityId = id;
        data.facilityName = name;
        data.description = description;
        data.requiredHallLevel = requiredHallLevel;
        data.levels = new List<FacilityLevelEntry>();
        foreach (var (cost, effect) in levels)
        {
            data.levels.Add(new FacilityLevelEntry { cost = cost, effectText = effect });
        }

        AssetDatabase.CreateAsset(data, path);
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources_moved"))
            AssetDatabase.CreateFolder("Assets", "Resources_moved");
        if (!AssetDatabase.IsValidFolder(BasePath))
            AssetDatabase.CreateFolder("Assets/Resources_moved", "Village");
    }
}
#endif
