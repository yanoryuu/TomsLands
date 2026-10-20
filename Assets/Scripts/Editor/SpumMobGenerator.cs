using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// SPUM のパーツ（Addons/*/Index.json）を組み合わせて、町の客・村人用のモブユニットを生成するツール。
///
/// - Tools > TomsLands > SPUM > モブユニットを生成            : Units/Mobs/SPUM_Mob_XX.prefab を（再）生成
/// - Tools > TomsLands > SPUM > モブをシーンに配線             : カタログ作成＋FightScene/VillageScene へ設定
/// - Tools > TomsLands > SPUM > モブを生成して配置（まとめて）  : 上の2つを続けて実行
/// - Tools > TomsLands > SPUM > 町人だけ村に配線              : VillageScene の町人枠だけ作り直す（カタログはそのまま）
/// - Tools > TomsLands > SPUM > 追加モブ（23〜42）だけ生成     : 2回目のまとめだけ（再）生成（01..22 は触らない）
/// - Tools > TomsLands > SPUM > カタログにモブを反映            : カタログだけ更新（シーンは触らない）
/// - Tools > TomsLands > SPUM > 勇者ユニットを生成             : Tom.prefab を元に Prefabs/Hero_SPUM.prefab を（再）生成
///
/// 村の町人は TownsfolkSlotCount 人ぶんの枠を「引っ越してくる順」に作り、見た目・家・セリフを枠ごとに固定する。
/// 何人出すかは実行時に VillagePopulationView が村の総合Lvから決める。
///
/// 生成は固定シードなので何度実行しても同じ見た目になる（既存ファイルは上書きされ GUID は維持）。
/// プレハブの骨組みは SPUM エディタで作った既存ユニットを複製し、SPUM_Manager.SetSprite と同じ規則で
/// パーツを差し替え、SPUM_Prefabs.PopulateAnimationLists でアニメーションリストを埋める。
/// </summary>
public static class SpumMobGenerator
{
    private const string MenuRoot = "Tools/TomsLands/SPUM/";
    private const string TemplatePrefabPath = "Assets/SPUM/Resources/Units/SPUM_20260503012752912.prefab";
    private const string MobFolder = "Assets/SPUM/Resources/Units/Mobs";
    private const string CatalogPath = "Assets/Prefabs/Mobs/MobAppearanceCatalog.asset";
    private const string TomPrefabPath = "Assets/Prefabs/Tom.prefab";
    private const string FightScenePath = "Assets/Scene/FightScene.unity";
    private const string VillageScenePath = "Assets/Scene/VillageScene.unity";
    private const string ExtraVillagerSource = "Villager_Wanderer";
    private const string ExtraVillagerPrefix = "Villager_Townsfolk_";

    // 1回目のまとめ（SPUM_Mob_01..22）。見た目を変えないため、この数・Seed・乱数の使い方は変えないこと
    public const int MobCount = 22;
    // 2回目のまとめ（SPUM_Mob_23..42）。1回目とは別の固定シードで、1回目で使わなかった髪型などを優先する
    public const int SecondBatchCount = 20;
    public const int TotalMobCount = MobCount + SecondBatchCount;
    private const int SecondBatchSeed = 20261017;
    private const string PopulationObjectName = "Townsfolk";

    // 町人の枠数（名前付き5人＋この人数＝村が最大まで育ったときの住民数）。何Lvで1人増えるかは VillagePopulationView 側で調整
    public const int TownsfolkSlotCount = 25;
    private const float TownsfolkSpacing = 2.1f;     // 町人の家どうし・他の村人との最小間隔
    private const float TownsfolkClearance = 0.45f;  // 障害物から空ける半径
    private const float TownsfolkPlotMargin = 0.3f;  // 区画の「調べる」範囲からさらに空ける幅
    private const float TownsfolkWanderRadius = 3.5f;
    private const int Seed = 20261016;

    // ───────────────────────── パーツ候補（町人らしいもの） ─────────────────────────

    private static readonly (string pkg, string name, int weight)[] BodyChoices =
    {
        ("Legacy", "Human_1", 3), ("Legacy", "Human_2", 3), ("Legacy", "Human_3", 3),
        ("Legacy", "Human_4", 3), ("Legacy", "Human_5", 3), ("Legacy", "Elf_1", 1), ("Legacy", "Elf_2", 1),
    };

    // 2回目: エルフと肌の色の違う人間を多めに（オーク・骸骨などは町人らしくないので使わない）
    private static readonly (string pkg, string name, int weight)[] SecondBatchBodyChoices =
    {
        ("Legacy", "Human_1", 2), ("Legacy", "Human_2", 2), ("Legacy", "Human_3", 2),
        ("Legacy", "Human_4", 2), ("Legacy", "Human_5", 2), ("Legacy", "Elf_1", 2), ("Legacy", "Elf_2", 2),
    };

    // 2回目: 1回目で出にくかった色（灰・白・赤毛・金髪）を厚めに入れる（HairColors の添字）
    private static readonly int[] SecondBatchHairColors = { 8, 9, 8, 9, 6, 5, 4, 0, 1, 2, 3, 6, 5, 7, 10, 11, 8, 9, 0, 4 };

    private const double SecondBatchFaceHairChance = 0.4;

    private static readonly string[] EyeChoices =
        { "Eye0", "Eye1", "Eye2", "Eye3", "Eye4", "Eye5", "Eye6", "Eye7", "Eye8", "Eye9", "Eye10", "Eye11" };

    private static readonly (string pkg, string prefix)[] HairPackages =
        { ("Legacy", "Hair_"), ("Ver121", "Normal_Hair"), ("Ver300", "New_Hair_") };

    private static readonly (string pkg, string prefix)[] ClothPackages =
        { ("Legacy", "Cloth_"), ("Ver121", "Normal_Cloth"), ("Ver300", "New_Cloth_") };

    private static readonly (string pkg, string prefix)[] PantPackages =
        { ("Legacy", "Foot_"), ("Ver300", "New_Pant_") };

    private static readonly (string pkg, string prefix)[] FaceHairPackages =
        { ("Legacy", "FaceHair_"), ("Ver121", "Normal_Mustache") };

    private static readonly Color[] HairColors =
    {
        new(0.20f, 0.13f, 0.09f), // 黒褐色
        new(0.12f, 0.11f, 0.13f), // 黒
        new(0.36f, 0.22f, 0.12f), // こげ茶
        new(0.55f, 0.33f, 0.16f), // 栗色
        new(0.75f, 0.55f, 0.30f), // 明るい茶
        new(0.95f, 0.82f, 0.48f), // 金髪
        new(0.72f, 0.28f, 0.14f), // 赤毛
        new(0.44f, 0.15f, 0.15f), // SPUM 既定色
        new(0.66f, 0.66f, 0.70f), // 灰色
        new(0.93f, 0.93f, 0.90f), // 白髪
        new(0.40f, 0.50f, 0.80f), // 青（たまに）
        new(0.90f, 0.58f, 0.68f), // 桃色（たまに）
    };

    private static readonly Color[] EyeColors =
    {
        new(0.28f, 0.10f, 0.10f), new(0.15f, 0.12f, 0.12f), new(0.20f, 0.35f, 0.65f),
        new(0.22f, 0.45f, 0.25f), new(0.42f, 0.30f, 0.18f), new(0.40f, 0.42f, 0.46f),
    };

    // 服・ズボンの色味（白=元の色のまま）
    private static readonly Color[] ClothTints =
    {
        Color.white, Color.white, Color.white,
        new(0.92f, 0.82f, 0.66f), new(0.75f, 0.85f, 1.00f), new(1.00f, 0.82f, 0.82f),
        new(0.82f, 0.95f, 0.78f), new(0.95f, 0.95f, 0.75f), new(0.85f, 0.78f, 0.95f),
    };

    // ───────────────────────── メニュー ─────────────────────────

    [MenuItem(MenuRoot + "モブを生成して配置（まとめて）")]
    public static void GenerateAndWire()
    {
        GenerateMobs();
        WireScenes();
    }

    [MenuItem(MenuRoot + "モブユニットを生成")]
    public static void GenerateMobs() => GenerateMobsCore(saveFirstBatch: true);

    /// <summary>2回目のまとめ（SPUM_Mob_23..42）だけ作る。1回目のプレハブには触らない（乱数は同じ順で回すので結果は全生成と同じ）。</summary>
    [MenuItem(MenuRoot + "追加モブ（23〜42）だけ生成")]
    public static void GenerateSecondBatchOnly() => GenerateMobsCore(saveFirstBatch: false);

    private static void GenerateMobsCore(bool saveFirstBatch)
    {
        var packages = LoadPackages();
        var template = AssetDatabase.LoadAssetAtPath<SPUM_Prefabs>(TemplatePrefabPath);
        if (template == null) throw new Exception("テンプレートの SPUM ユニットが見つかりません: " + TemplatePrefabPath);

        EnsureFolder(MobFolder);
        var rng = new Random(Seed);

        var hairs = Shuffle(rng, CollectGroups(packages, "Hair", HairPackages));
        var cloths = Shuffle(rng, CollectGroups(packages, "Cloth", ClothPackages));
        var pants = Shuffle(rng, CollectGroups(packages, "Pant", PantPackages));
        var faceHairs = CollectGroups(packages, "FaceHair", FaceHairPackages);
        var hairColors = Shuffle(rng, Enumerable.Range(0, HairColors.Length).ToList());

        var log = new StringBuilder();
        var usage = new Dictionary<TexGroup, int>();
        void Use(TexGroup g) => usage[g] = usage.TryGetValue(g, out int n) ? n + 1 : 1;
        var usedCombos = new HashSet<string>();

        for (int i = 0; i < MobCount; i++)
        {
            string name = $"SPUM_Mob_{i + 1:00}";
            var parts = new List<PreviewMatchingElement>();

            var body = PickWeighted(rng, BodyChoices);
            parts.AddRange(BuildElements(FindGroup(packages, "Body", body.pkg, body.name), "", Color.white, 0));

            var eye = FindGroup(packages, "Eye", "Legacy", EyeChoices[rng.Next(EyeChoices.Length)]);
            Color eyeColor = EyeColors[rng.Next(EyeColors.Length)];
            // 白目（Back）は色を付けない（SPUM エディタと同じ）
            parts.AddRange(BuildElements(eye, "", eyeColor, 0, sub => sub == "Back"));

            var hair = hairs[i % hairs.Count];
            // 青・桃色はたまにだけ（シャッフル順で後ろの方に来たら普通の色に寄せる）
            Color hairColor = HairColors[hairColors[i % hairColors.Count]];
            parts.AddRange(BuildElements(hair, "", hairColor, 0));

            string faceHairName = "-";
            if (rng.NextDouble() < 0.25)
            {
                var fh = faceHairs[rng.Next(faceHairs.Count)];
                faceHairName = fh.Name;
                parts.AddRange(BuildElements(fh, "", hairColor, 0));
            }

            var cloth = cloths[i % cloths.Count];
            Color clothTint = ClothTints[rng.Next(ClothTints.Length)];
            parts.AddRange(BuildElements(cloth, "", clothTint, 0));

            var pant = pants[i % pants.Count];
            Color pantTint = ClothTints[rng.Next(ClothTints.Length)];
            parts.AddRange(BuildElements(pant, "", pantTint, 0));

            Use(hair); Use(cloth); Use(pant);
            usedCombos.Add($"{body.name}|{hair.Name}|{cloth.Name}|{pant.Name}");

            string path = $"{MobFolder}/{name}.prefab";
            if (saveFirstBatch) SaveUnit(template, parts, name, path);
            log.AppendLine($"{name}: body={body.name} eye={eye.Name} hair={hair.Package}/{hair.Name} faceHair={faceHairName} cloth={cloth.Package}/{cloth.Name} pant={pant.Package}/{pant.Name}{(saveFirstBatch ? "" : " (skip)")}");
        }

        // ── 2回目のまとめ：別シード。1回目で使っていない／使用回数の少ないパーツから順に使う ──
        var rng2 = new Random(SecondBatchSeed);
        List<TexGroup> LeastUsedFirst(List<TexGroup> all) =>
            Shuffle(rng2, all).OrderBy(g => usage.TryGetValue(g, out int n) ? n : 0).ToList(); // OrderBy は安定ソート
        var hairs2 = LeastUsedFirst(hairs);
        var cloths2 = LeastUsedFirst(cloths);
        var pants2 = LeastUsedFirst(pants);

        for (int k = 0; k < SecondBatchCount; k++)
        {
            int index = MobCount + k;
            string name = $"SPUM_Mob_{index + 1:00}";
            var parts = new List<PreviewMatchingElement>();

            var body = PickWeighted(rng2, SecondBatchBodyChoices);
            parts.AddRange(BuildElements(FindGroup(packages, "Body", body.pkg, body.name), "", Color.white, 0));

            var eye = FindGroup(packages, "Eye", "Legacy", EyeChoices[rng2.Next(EyeChoices.Length)]);
            Color eyeColor = EyeColors[rng2.Next(EyeColors.Length)];
            parts.AddRange(BuildElements(eye, "", eyeColor, 0, sub => sub == "Back"));

            var hair = hairs2[k % hairs2.Count];
            int hairColorIndex = SecondBatchHairColors[k % SecondBatchHairColors.Length];
            Color hairColor = HairColors[hairColorIndex];
            parts.AddRange(BuildElements(hair, "", hairColor, 0));

            // 灰・白髪の人はひげ率を上げて年配っぽく
            bool older = hairColorIndex == 8 || hairColorIndex == 9;
            string faceHairName = "-";
            if (rng2.NextDouble() < (older ? 0.7 : SecondBatchFaceHairChance))
            {
                var fh = faceHairs[rng2.Next(faceHairs.Count)];
                faceHairName = fh.Name;
                parts.AddRange(BuildElements(fh, "", hairColor, 0));
            }

            var cloth = cloths2[k % cloths2.Count];
            var pant = pants2[k % pants2.Count];
            // 同じ体・髪・服・ズボンの組み合わせは避ける（ズボンをずらす）
            for (int shift = 1; usedCombos.Contains($"{body.name}|{hair.Name}|{cloth.Name}|{pant.Name}") && shift < pants2.Count; shift++)
                pant = pants2[(k + shift) % pants2.Count];
            usedCombos.Add($"{body.name}|{hair.Name}|{cloth.Name}|{pant.Name}");

            Color clothTint = ClothTints[rng2.Next(ClothTints.Length)];
            parts.AddRange(BuildElements(cloth, "", clothTint, 0));
            Color pantTint = ClothTints[rng2.Next(ClothTints.Length)];
            parts.AddRange(BuildElements(pant, "", pantTint, 0));

            SaveUnit(template, parts, name, $"{MobFolder}/{name}.prefab");
            log.AppendLine($"{name}: body={body.name} eye={eye.Name} hair={hair.Package}/{hair.Name} hairColor={hairColorIndex} faceHair={faceHairName} cloth={cloth.Package}/{cloth.Name} pant={pant.Package}/{pant.Name}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SpumMobGenerator] モブユニットを生成しました（1回目 {MobCount} 体{(saveFirstBatch ? "" : "は既存のまま")} + 2回目 {SecondBatchCount} 体）\n{log}");
    }

    /// <summary>カタログだけ更新する（FightScene は触らない）。カタログ中のモブ以外の既存ユニットはそのまま残し、モブを全部入れ直す。</summary>
    [MenuItem(MenuRoot + "カタログにモブを反映（シーンは触らない）")]
    public static void UpdateCatalogOnly()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<MobAppearanceCatalog>(CatalogPath);
        var existing = catalog == null ? new List<SPUM_Prefabs>()
            : catalog.Units.Where(u => u != null && !AssetDatabase.GetAssetPath(u).StartsWith(MobFolder + "/")).ToList();
        var units = existing.Concat(LoadMobs()).Distinct().ToList();
        CreateOrUpdateCatalog(units);
        Debug.Log($"[SpumMobGenerator] カタログ {units.Count} 種（既存 {existing.Count} + モブ {units.Count - existing.Count}）");
    }

    [MenuItem(MenuRoot + "モブをシーンに配線")]
    public static void WireScenes()
    {
        var mobs = LoadMobs();
        if (mobs.Count == 0) throw new Exception("モブユニットがありません。先に「モブユニットを生成」を実行してください");

        var tomGuids = new HashSet<string>(AssetDatabase.GetDependencies(TomPrefabPath, true).Select(AssetDatabase.AssetPathToGUID));

        // ── FightScene（客） ──
        var scene = EditorSceneManager.OpenScene(FightScenePath, OpenSceneMode.Single);
        var existingLooks = new List<SPUM_Prefabs>();
        var customers = Object.FindObjectsByType<CustomerCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
        foreach (var c in customers)
        {
            var so = new SerializedObject(c);
            var spum = so.FindProperty("spumCharacter").objectReferenceValue as SPUM_Prefabs;
            var src = spum != null ? PrefabUtility.GetCorrespondingObjectFromSource(spum) : null;
            so.FindProperty("appearanceSource").objectReferenceValue = src;
            so.ApplyModifiedPropertiesWithoutUndo();
            AddExistingLook(existingLooks, src, tomGuids);
        }

        var catalogUnits = existingLooks.Concat(mobs).Distinct().ToList();
        var catalog = CreateOrUpdateCatalog(catalogUnits);

        var managers = Object.FindObjectsByType<CustomerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var m in managers)
        {
            var so = new SerializedObject(m);
            so.FindProperty("appearanceCatalog").objectReferenceValue = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[SpumMobGenerator] FightScene: 客 {customers.Length} 人 / CustomerManager {managers.Length} 個にカタログ（{catalogUnits.Count} 種：既存 {existingLooks.Count} + 新規 {mobs.Count}）を設定");

        // ── VillageScene（村人） ──
        WireVillageScene(existingLooks.Select(AssetDatabase.GetAssetPath).ToList(), updateCatalog: true);
    }

    [MenuItem(MenuRoot + "町人だけ村に配線（FightScene は触らない）")]
    public static void WireVillageOnly()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<MobAppearanceCatalog>(CatalogPath);
        if (catalog == null) throw new Exception("カタログがありません。先に「モブをシーンに配線」を実行してください: " + CatalogPath);
        WireVillageScene(catalog.Units.Where(u => u != null).Select(AssetDatabase.GetAssetPath).ToList(), updateCatalog: false);
    }

    /// <summary>
    /// VillageScene に町人の枠（TownsfolkSlotCount 人）を作り直す。
    /// 枠 N の見た目・家・セリフは固定（モブを番号順に使い、名前付き村人と被る見た目は飛ばす。足りなければ既存ユニットで補う）。
    /// 表示人数は実行時に VillagePopulationView が総合Lvから決める（シーン上は全員非表示で保存）。
    /// </summary>
    private static void WireVillageScene(List<string> existingLookPaths, bool updateCatalog)
    {
        var tomGuids = new HashSet<string>(AssetDatabase.GetDependencies(TomPrefabPath, true).Select(AssetDatabase.AssetPathToGUID));
        var scene = EditorSceneManager.OpenScene(VillageScenePath, OpenSceneMode.Single);
        // シーンを開くと未使用アセットがアンロードされ、読み込み済みの参照が無効になるので読み直す
        var mobs = LoadMobs();
        var existingLooks = existingLookPaths.Select(AssetDatabase.LoadAssetAtPath<SPUM_Prefabs>).Where(x => x != null).ToList();

        // 以前の実行で作った町人・まとめ役は作り直す
        foreach (var pop in Object.FindObjectsByType<VillagePopulationView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(pop.gameObject);
        var villagers = Object.FindObjectsByType<VillagerCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID).ToList();
        foreach (var v in villagers.Where(v => v.name.StartsWith(ExtraVillagerPrefix)).ToList())
        {
            villagers.Remove(v);
            Object.DestroyImmediate(v.gameObject);
        }

        // 名前付き村人の見た目（町人では使わない）
        var namedLooks = new HashSet<SPUM_Prefabs>();
        foreach (var v in villagers)
        {
            var spum = new SerializedObject(v).FindProperty("spumCharacter").objectReferenceValue as SPUM_Prefabs;
            var src = spum != null ? PrefabUtility.GetCorrespondingObjectFromSource(spum) : null;
            if (src != null) namedLooks.Add(src);
            AddExistingLook(existingLooks, src, tomGuids);
        }
        if (updateCatalog)
        {
            var catalogUnits = existingLooks.Concat(mobs).Distinct().ToList();
            CreateOrUpdateCatalog(catalogUnits);
            Debug.Log($"[SpumMobGenerator] カタログ {catalogUnits.Count} 種（既存 {existingLooks.Count} + 新規 {mobs.Count}）");
        }

        // 町人はモブ（SPUM_Mob_01..）だけを番号順に使う。足りないときだけ既存ユニットで補う
        var looks = mobs.Where(u => !namedLooks.Contains(u)).ToList();
        if (looks.Count < TownsfolkSlotCount)
            looks = looks.Concat(existingLooks).Distinct().Where(u => !namedLooks.Contains(u)).ToList();
        if (looks.Count == 0) throw new Exception("町人に使える見た目がありません");
        if (looks.Count < TownsfolkSlotCount)
            Debug.LogWarning($"[SpumMobGenerator] 見た目が {looks.Count} 種しかないため、{TownsfolkSlotCount} 人のうち一部は見た目が重複します");

        var source = villagers.FirstOrDefault(v => v.name == ExtraVillagerSource) ?? villagers.FirstOrDefault(IsWanderer);
        if (source == null) throw new Exception("複製元の村人が見つかりません: " + ExtraVillagerSource);

        var homes = FindTownsfolkHomes(source, villagers, TownsfolkSlotCount);

        // まとめ役（引っ越し順に町人を持つ）
        var popGo = new GameObject(PopulationObjectName);
        popGo.transform.SetParent(source.transform.parent, false);
        popGo.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
        var population = popGo.AddComponent<VillagePopulationView>();

        var lineRng = new Random(Seed + 2);
        var slots = new List<VillagerCharacter>();
        var report = new StringBuilder();
        for (int i = 0; i < homes.Count; i++)
        {
            Vector3 pos = homes[i];
            var go = Object.Instantiate(source.gameObject, popGo.transform);
            go.name = $"{ExtraVillagerPrefix}{i + 1:00}";
            go.transform.position = pos;
            var villager = go.GetComponent<VillagerCharacter>();

            var so = new SerializedObject(villager);
            // 子のプレハブインスタンス等への参照が複製元を指したままのことがあるので、複製側の同じ場所に付け替える
            foreach (var prop in new[] { "spumCharacter", "lineBubble", "lineText" })
            {
                var sp = so.FindProperty(prop);
                sp.objectReferenceValue = RemapToClone(sp.objectReferenceValue, source.transform, go.transform);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            so.Update();
            var oldSpum = so.FindProperty("spumCharacter").objectReferenceValue as SPUM_Prefabs;
            if (oldSpum == null || !oldSpum.transform.IsChildOf(go.transform)) oldSpum = go.GetComponentInChildren<SPUM_Prefabs>(true);
            var look = looks[i % looks.Count];
            so.FindProperty("spumCharacter").objectReferenceValue = ReplaceInEditor(oldSpum, look);
            var lines = so.FindProperty("lines");
            var picked = PickLines(lineRng, 3);
            lines.arraySize = picked.Length;
            for (int l = 0; l < picked.Length; l++) lines.GetArrayElementAtIndex(l).stringValue = picked[l];
            so.FindProperty("wanderRadius").floatValue = TownsfolkWanderRadius;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false); // 実行時に VillagePopulationView が総合Lvに応じて出す
            slots.Add(villager);
            report.AppendLine($"{go.name} look={look.name} pos=({pos.x:F2},{pos.y:F2})");
        }

        var popSo = new SerializedObject(population);
        var arr = popSo.FindProperty("townsfolk");
        arr.arraySize = slots.Count;
        for (int i = 0; i < slots.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        popSo.ApplyModifiedPropertiesWithoutUndo();

        foreach (var view in Object.FindObjectsByType<VillageView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var vso = new SerializedObject(view);
            vso.FindProperty("population").objectReferenceValue = population;
            vso.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SpumMobGenerator] VillageScene: 名前付き村人 {villagers.Count} 人はそのまま、町人の枠 {slots.Count} 人を作成（見た目候補 {looks.Count} 種）\n{report}");
    }

    // ───────────────────────── 生成の中身 ─────────────────────────

    private class TexGroup
    {
        public string Package;
        public string PartType;
        public string Name;
        public List<SpumTextureData> Items;
    }

    private static List<SpumPackage> LoadPackages()
    {
        var list = new List<SpumPackage>();
        foreach (var asset in Resources.LoadAll<TextAsset>(""))
        {
            if (asset == null || asset.name != "Index") continue;
            var pkg = JsonUtility.FromJson<SpumPackage>(asset.text);
            if (pkg != null && !string.IsNullOrEmpty(pkg.Name)) list.Add(pkg);
        }
        if (list.Count == 0) throw new Exception("SPUM パッケージ（Resources/Addons/*/Index.json）が読み込めません");
        return list;
    }

    private static List<TexGroup> CollectGroups(List<SpumPackage> packages, string partType, (string pkg, string prefix)[] filters)
    {
        var result = new List<TexGroup>();
        foreach (var (pkgName, prefix) in filters)
        {
            var pkg = packages.FirstOrDefault(p => p.Name == pkgName);
            if (pkg == null) continue;
            result.AddRange(pkg.SpumTextureData
                .Where(t => t.UnitType == "Unit" && t.PartType == partType && t.Name.StartsWith(prefix))
                .GroupBy(t => t.Name)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new TexGroup { Package = pkgName, PartType = partType, Name = g.Key, Items = g.ToList() }));
        }
        if (result.Count == 0) throw new Exception($"パーツ候補がありません: {partType}");
        return result;
    }

    private static TexGroup FindGroup(List<SpumPackage> packages, string partType, string pkgName, string name)
    {
        var pkg = packages.First(p => p.Name == pkgName);
        var items = pkg.SpumTextureData.Where(t => t.UnitType == "Unit" && t.PartType == partType && t.Name == name).ToList();
        if (items.Count == 0) throw new Exception($"パーツが見つかりません: {pkgName}/{partType}/{name}");
        return new TexGroup { Package = pkgName, PartType = partType, Name = name, Items = items };
    }

    /// <summary>SPUM_Manager.SetPartRandom と同じ規則で PreviewMatchingElement を作る。</summary>
    private static IEnumerable<PreviewMatchingElement> BuildElements(TexGroup group, string dir, Color color, int mask, Func<string, bool> keepWhite = null)
    {
        foreach (var item in group.Items)
        {
            string structure = item.SubType == item.Name ? group.PartType : item.SubType;
            yield return new PreviewMatchingElement
            {
                UnitType = "Unit",
                PartType = group.PartType,
                PartSubType = item.PartSubType ?? "",
                Dir = dir,
                ItemPath = item.Path,
                Structure = structure,
                MaskIndex = mask,
                Color = keepWhite != null && keepWhite(structure) ? Color.white : color,
            };
        }
    }

    private static void SaveUnit(SPUM_Prefabs template, List<PreviewMatchingElement> parts, string code, string path)
    {
        var go = (GameObject)Object.Instantiate(template.gameObject);
        try
        {
            go.name = code;
            var unit = go.GetComponent<SPUM_Prefabs>();
            var tables = go.GetComponentsInChildren<SPUM_MatchingList>(true).SelectMany(m => m.matchingTables).ToList();

            // いったん全パーツを外す
            foreach (var me in tables)
            {
                if (me.renderer == null) continue;
                me.renderer.sprite = null;
                me.renderer.color = Color.white;
                me.renderer.maskInteraction = SpriteMaskInteraction.None;
                me.ItemPath = "";
                me.Color = Color.white;
            }

            int applied = 0;
            foreach (var me in tables)
            {
                if (me.renderer == null) continue;
                var e = parts.FirstOrDefault(p =>
                    p.UnitType == me.UnitType && p.PartType == me.PartType &&
                    Norm(p.Dir) == Norm(me.Dir) && p.Structure == me.Structure &&
                    Norm(p.PartSubType) == Norm(me.PartSubType));
                if (e == null) continue;

                var sprite = LoadSprite(e.ItemPath, e.Structure);
                if (sprite == null) Debug.LogWarning($"[SpumMobGenerator] スプライトが見つかりません: {e.ItemPath} ({e.Structure})");
                me.renderer.sprite = sprite;
                me.renderer.maskInteraction = (SpriteMaskInteraction)e.MaskIndex;
                me.renderer.color = e.Color;
                me.ItemPath = e.ItemPath;
                me.Color = e.Color;
                applied++;
            }
            if (applied == 0) throw new Exception($"{code}: パーツが1つも適用できませんでした");

            unit.ImageElement = parts;
            unit._code = code;
            unit.UnitType = "Unit";
            go.transform.localScale = Vector3.one;

            var templateLists = new[] { unit.IDLE_List, unit.MOVE_List, unit.ATTACK_List, unit.DAMAGED_List, unit.DEBUFF_List, unit.DEATH_List, unit.OTHER_List }
                .Select(l => new List<AnimationClip>(l)).ToArray();
            unit.PopulateAnimationLists();
            if (!unit.allListsHaveItemsExist())
            {
                Debug.LogWarning($"[SpumMobGenerator] {code}: PopulateAnimationLists で空のリストがあったためテンプレートのリストを使います");
                unit.IDLE_List = templateLists[0]; unit.MOVE_List = templateLists[1]; unit.ATTACK_List = templateLists[2];
                unit.DAMAGED_List = templateLists[3]; unit.DEBUFF_List = templateLists[4]; unit.DEATH_List = templateLists[5];
                unit.OTHER_List = templateLists[6];
            }

            PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
            if (!ok) throw new Exception("プレハブの保存に失敗: " + path);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static Sprite LoadSprite(string path, string spriteName)
    {
        var sprites = Resources.LoadAll<Sprite>(path);
        if (sprites == null || sprites.Length == 0) return null;
        return Array.Find(sprites, s => s.name == spriteName) ?? sprites[0];
    }

    private static string Norm(string s) => s ?? "";

    // ───────────────────────── 配線の中身 ─────────────────────────

    private static List<SPUM_Prefabs> LoadMobs()
    {
        return AssetDatabase.FindAssets("t:Prefab SPUM_Mob_", new[] { MobFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => AssetDatabase.LoadAssetAtPath<SPUM_Prefabs>(p))
            .Where(p => p != null)
            .ToList();
    }

    private static MobAppearanceCatalog CreateOrUpdateCatalog(List<SPUM_Prefabs> units)
    {
        EnsureFolder(Path.GetDirectoryName(CatalogPath).Replace('\\', '/'));
        var catalog = AssetDatabase.LoadAssetAtPath<MobAppearanceCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<MobAppearanceCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.EditorSetUnits(units);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    /// <summary>シーンで使われている既存ユニットをカタログ候補に加える（Tom と、アニメーションリストが空の古いユニットは除く）。</summary>
    private static void AddExistingLook(List<SPUM_Prefabs> list, SPUM_Prefabs src, HashSet<string> excludedGuids)
    {
        if (src == null || list.Contains(src)) return;
        if (excludedGuids.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(src)))) return;
        if (!src.allListsHaveItemsExist()) return;
        list.Add(src);
    }

    private static bool IsWanderer(VillagerCharacter v) => new SerializedObject(v).FindProperty("wander").boolValue;

    /// <summary>
    /// 町人の家（初期位置）を count 個決める。道の内側（上下左右も道）で、建物・障害物（全段階の建物を出した状態で判定）・
    /// 区画の「調べる」範囲・他の村人やプレイヤーから離れた地点を、村の中心（区画の重心）に近い順に間隔を空けて選ぶ。
    /// 先に引っ越してくる人ほど中心寄り＝村が外へ広がっていく並びになる。乱数は使わないので常に同じ結果。
    /// </summary>
    private static List<Vector3> FindTownsfolkHomes(VillagerCharacter source, List<VillagerCharacter> named, int count)
    {
        var tilemap = new SerializedObject(source).FindProperty("walkableTilemap").objectReferenceValue as Tilemap;
        if (tilemap == null) throw new Exception("複製元の村人に walkableTilemap が設定されていません");
        float z = source.transform.position.z;

        var plots = Object.FindObjectsByType<FacilityPlot>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
        // 将来の建物（未解禁の段階）も障害物として扱うため、判定中だけ全段階を出す
        var toggled = new List<GameObject>();
        foreach (var plot in plots)
        {
            var stages = new SerializedObject(plot).FindProperty("stageObjects");
            for (int i = 0; i < stages.arraySize; i++)
            {
                var go = stages.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go == null || go.activeSelf) continue;
                go.SetActive(true);
                toggled.Add(go);
            }
        }

        try
        {
            Physics2D.SyncTransforms();
            var plotZones = plots.Select(p => p.GetComponent<BoxCollider2D>()).Where(c => c != null)
                .Select(c => { var b = c.bounds; b.Expand(TownsfolkPlotMargin * 2f); return b; }).ToList();
            var reserved = named.Select(v => (Vector2)v.transform.position).ToList();
            var player = Object.FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include);
            if (player != null) reserved.Add(player.transform.position);

            Vector2 center = plots.Length > 0
                ? plots.Aggregate(Vector2.zero, (acc, p) => acc + (Vector2)p.transform.position) / plots.Length
                : (Vector2)source.transform.position;

            var candidates = new List<Vector2>();
            var cb = tilemap.cellBounds;
            foreach (var cell in cb.allPositionsWithin)
            {
                if (!IsInnerWalkable(tilemap, cell)) continue;
                Vector2 p = tilemap.GetCellCenterWorld(cell);
                if (IsBlockedForTownsfolk(p, plotZones)) continue;
                if (reserved.Any(q => (q - p).sqrMagnitude < TownsfolkSpacing * TownsfolkSpacing)) continue;
                candidates.Add(p);
            }

            var ordered = candidates
                .OrderBy(p => Mathf.Round((p - center).sqrMagnitude * 100f))
                .ThenBy(p => p.x).ThenBy(p => p.y)
                .ToList();
            var homes = new List<Vector2>();
            foreach (var p in ordered)
            {
                if (homes.Count >= count) break;
                if (homes.Any(q => (q - p).sqrMagnitude < TownsfolkSpacing * TownsfolkSpacing)) continue;
                homes.Add(p);
            }
            if (homes.Count < count)
                Debug.LogWarning($"[SpumMobGenerator] 町人の置き場所が {homes.Count}/{count} か所しか見つかりませんでした（候補 {candidates.Count}）");
            Debug.Log($"[SpumMobGenerator] 町人の家: 候補 {candidates.Count} マス / 中心 ({center.x:F1},{center.y:F1}) / 最遠 {(homes.Count > 0 ? homes.Max(h => (h - center).magnitude) : 0f):F1}");
            return homes.Select(h => new Vector3(h.x, h.y, z)).ToList();
        }
        finally
        {
            foreach (var go in toggled) go.SetActive(false);
            Physics2D.SyncTransforms();
        }
    }

    /// <summary>そのマスと上下左右のマスがすべて道か（道の縁ぎりぎりや行き止まりの先端を避ける）。</summary>
    private static bool IsInnerWalkable(Tilemap tilemap, Vector3Int cell)
    {
        return tilemap.HasTile(cell)
               && tilemap.HasTile(cell + Vector3Int.left) && tilemap.HasTile(cell + Vector3Int.right)
               && tilemap.HasTile(cell + Vector3Int.up) && tilemap.HasTile(cell + Vector3Int.down);
    }

    /// <summary>非Triggerのコライダー（建物・柵など）に重なる、または区画の「調べる」範囲内なら true。</summary>
    private static bool IsBlockedForTownsfolk(Vector2 p, List<Bounds> plotZones)
    {
        if (plotZones.Any(b => b.Contains(new Vector3(p.x, p.y, b.center.z)))) return true;
        return Physics2D.OverlapCircleAll(p, TownsfolkClearance).Any(c => c != null && !c.isTrigger);
    }

    /// <summary>複製元の階層内を指す参照を、複製側の同じ相対パスのオブジェクトに付け替える。</summary>
    private static Object RemapToClone(Object reference, Transform srcRoot, Transform cloneRoot)
    {
        Transform t = reference is Component c ? c.transform : reference is GameObject g ? g.transform : null;
        if (t == null || !t.IsChildOf(srcRoot)) return reference;

        var names = new List<string>();
        for (var cur = t; cur != srcRoot; cur = cur.parent) names.Insert(0, cur.name);
        Transform target = cloneRoot;
        foreach (var n in names)
        {
            target = target.Find(n);
            if (target == null) return reference;
        }
        if (reference is GameObject) return target.gameObject;
        return target.GetComponent(reference.GetType());
    }

    /// <summary>エディタ上で村人の SPUM 子オブジェクトをプレハブインスタンスとして差し替える。</summary>
    private static SPUM_Prefabs ReplaceInEditor(SPUM_Prefabs old, SPUM_Prefabs prefab)
    {
        var oldT = old.transform;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, oldT.parent);
        var t = go.transform;
        t.SetSiblingIndex(oldT.GetSiblingIndex());
        t.localPosition = oldT.localPosition;
        t.localRotation = oldT.localRotation;
        t.localScale = oldT.localScale;
        foreach (var c in go.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = old.gameObject.layer;

        var og = old.GetComponent<SortingGroup>();
        var ng = go.GetComponent<SortingGroup>();
        if (og != null && ng != null)
        {
            ng.sortingLayerID = og.sortingLayerID;
            ng.sortingOrder = og.sortingOrder;
        }
        var or = old.GetComponentInChildren<SpriteRenderer>(true);
        if (or != null)
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.sortingLayerID = or.sortingLayerID;

        Object.DestroyImmediate(old.gameObject);
        return go.GetComponent<SPUM_Prefabs>();
    }

    private static readonly string[] TownsfolkLines =
    {
        "トコちゃんのお店、最近にぎわってるね",
        "今日はいい天気だなあ。散歩日和だ",
        "掘り出し物が入ったら教えてね",
        "村もだんだん大きくなってきたね",
        "配信、いつも見てるよ！",
        "市場の値段、また変わったみたい",
        "勇者さまたち、今日も出かけていったよ",
        "うちの子がトコちゃんの店に行きたがってるの",
        "新しい建物ができると、ワクワクするね",
        "今日の晩ごはん、何にしようかな",
        "鍛冶屋の音が聞こえると、なんだか安心する",
        "借金？ ……がんばってね",
        "この村に越してきて正解だったよ",
        "道が広くなって歩きやすくなったね",
        "最近、知らない顔が増えてきたなあ",
        "井戸端で話すのが毎日の楽しみなんだ",
        "トコちゃんのおかげで村が明るくなった",
        "お店に並ぶ品、次は何かな",
        "夜になると星がよく見えるんだよ",
        "子どもの頃はもっと寂しい村だったんだ",
        "次はどんな建物が建つんだろう",
        "勇者さまたちが無事に帰ってきますように",
        "そのうち自分の店も持ってみたいな",
        "広場でひと休みするのが好きなんだ",
    };

    private static string[] PickLines(Random rng, int count)
    {
        return TownsfolkLines.OrderBy(_ => rng.Next()).Take(count).ToArray();
    }

    // ───────────────────────── 勇者（トコと同じ作り） ─────────────────────────

    private const string HeroPrefabPath = "Assets/Prefabs/Hero_SPUM.prefab";
    private const string SpriteRoot = "Addons/Legacy/0_Unit/0_Sprite/";
    private const string TomHairPath = SpriteRoot + "0_Hair/Tom_Hair";
    private const string TomBodyPath = SpriteRoot + "2_Cloth/Tom_Body";
    private const string HeroHairPath = SpriteRoot + "0_Hair/Hero_Hair";
    private const string HeroBodyPath = SpriteRoot + "2_Cloth/Hero_Body";
    private const string HeroLegPath = SpriteRoot + "3_Pant/Hero_Leg";

    /// <summary>
    /// Tom.prefab を元に勇者の SPUM ユニット（Hero_SPUM.prefab）を作る。Tom.prefab 自体は変更しない。
    /// 髪＝Hero_Hair（頭全体）、服＝Hero_Body（Body/Left/Right）、足＝Hero_Leg（L_Foot/R_Foot、素足の代わり）。
    /// 足は脚全体の絵なので、トコが使っているズボン（Orc_Pant_02）は外す。顔・目・素体・アニメーションはトコのまま。
    /// プレイヤー操作用の部品（PlayerMove 等）は外す（戦闘の勇者表示 Battle/Prefabs/Hero.prefab の子として使う）。
    /// 何度実行しても同じ結果（既存ファイルは GUID を維持して上書き）。
    /// </summary>
    [MenuItem(MenuRoot + "勇者ユニットを生成（Tom.prefab から）")]
    public static void GenerateHero()
    {
        Sprite Load(string path, string name) =>
            Resources.LoadAll<Sprite>(path).FirstOrDefault(s => s.name == name)
            ?? throw new Exception($"スプライトが見つかりません: {path} ({name})");

        var tomHair = Resources.LoadAll<Sprite>(TomHairPath);
        var tomBody = Resources.LoadAll<Sprite>(TomBodyPath);
        var heroLegL = Load(HeroLegPath, "L_Foot");
        var heroLegR = Load(HeroLegPath, "R_Foot");

        var root = PrefabUtility.LoadPrefabContents(TomPrefabPath);
        try
        {
            root.name = "Hero";
            var log = new StringBuilder();
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.sprite == null) continue;
                Sprite before = sr.sprite;
                if (tomHair.Contains(sr.sprite)) { sr.sprite = Load(HeroHairPath, sr.sprite.name); sr.color = Color.white; }
                else if (tomBody.Contains(sr.sprite)) sr.sprite = Load(HeroBodyPath, sr.sprite.name);
                else if (sr.name == "_3L_Foot") sr.sprite = heroLegL;   // 画像の左側の足（トコも同じ並び）
                else if (sr.name == "_12R_Foot") sr.sprite = heroLegR;
                else if (sr.name == "_2L_Cloth" || sr.name == "_11R_Cloth") sr.sprite = null; // ズボンは脚の絵に含まれる
                else continue;
                log.AppendLine($"  {sr.name}: {before.name} -> {(sr.sprite != null ? sr.sprite.name : "(なし)")}");
            }

            foreach (var me in root.GetComponentsInChildren<SPUM_MatchingList>(true).SelectMany(m => m.matchingTables))
            {
                if (me.PartType == "Pant") { me.ItemPath = ""; continue; }
                if (me.ItemPath == TomHairPath) { me.ItemPath = HeroHairPath; me.Color = Color.white; }
                else if (me.ItemPath == TomBodyPath) me.ItemPath = HeroBodyPath;
            }

            var unit = root.GetComponent<SPUM_Prefabs>();
            unit.ImageElement = unit.ImageElement.Where(e => e.PartType != "Pant").ToList();
            foreach (var e in unit.ImageElement)
            {
                if (e.ItemPath == TomHairPath) { e.ItemPath = HeroHairPath; e.Color = Color.white; }
                else if (e.ItemPath == TomBodyPath) e.ItemPath = HeroBodyPath;
            }
            unit._code = "SPUM_Hero";

            // Tom.prefab はプレイヤー操作用の部品（移動・入力・物理）と村での配置位置を持っているので外す。見た目とアニメーションだけのユニットにする
            foreach (var c in root.GetComponents<PlayerMove>()) Object.DestroyImmediate(c);
            foreach (var c in root.GetComponents<UnityEngine.InputSystem.PlayerInput>()) Object.DestroyImmediate(c);
            foreach (var c in root.GetComponents<Rigidbody2D>()) Object.DestroyImmediate(c);
            foreach (var c in root.GetComponents<BoxCollider2D>()) Object.DestroyImmediate(c);
            root.transform.localPosition = Vector3.zero;
            if (root.transform is RectTransform rt) rt.anchoredPosition = Vector2.zero;

            PrefabUtility.SaveAsPrefabAsset(root, HeroPrefabPath, out bool ok);
            if (!ok) throw new Exception("プレハブの保存に失敗: " + HeroPrefabPath);
            Debug.Log($"[SpumMobGenerator] 勇者ユニットを生成しました: {HeroPrefabPath}\n{log}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ───────────────────────── 確認用 ─────────────────────────

    /// <summary>batchmode 用: 生成→配線→確認を続けて行う。</summary>
    public static void BatchGenerateWireVerify()
    {
        GenerateAndWire();
        Verify();
        VerifyVillage();
    }

    /// <summary>batchmode 用: 村の町人枠だけ作り直して確認する（FightScene・モブユニットは触らない）。</summary>
    public static void BatchWireVillageVerify()
    {
        WireVillageOnly();
        VerifyVillage();
    }

    /// <summary>batchmode 用: 追加モブ（23〜42）を生成→カタログ更新→村の町人枠を作り直し→確認（FightScene・1回目のモブは触らない）。</summary>
    public static void BatchSecondBatchVerify()
    {
        GenerateSecondBatchOnly();
        UpdateCatalogOnly();
        Verify();
        BatchWireVillageVerify();
    }

    /// <summary>batchmode 確認用: 村の町人枠（見た目・家・障害物との重なり）と、総合Lvごとの人数をログに出す。</summary>
    public static void VerifyVillage()
    {
        EditorSceneManager.OpenScene(VillageScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();
        var pops = Object.FindObjectsByType<VillagePopulationView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.AppendLine($"populationViews={pops.Length}");
        if (pops.Length != 1) { Debug.LogError("[SpumVillageVerify] VillagePopulationView が1つではありません\n" + sb); return; }
        var pop = pops[0];
        var views = Object.FindObjectsByType<VillageView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.AppendLine($"villageView.population wired={views.All(v => new SerializedObject(v).FindProperty("population").objectReferenceValue == pop)}");

        var arr = new SerializedObject(pop).FindProperty("townsfolk");
        var slots = Enumerable.Range(0, arr.arraySize).Select(i => arr.GetArrayElementAtIndex(i).objectReferenceValue as VillagerCharacter).ToList();
        var named = Object.FindObjectsByType<VillagerCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
            .Where(v => !slots.Contains(v)).ToList();
        SPUM_Prefabs LookOf(VillagerCharacter v)
        {
            var spum = new SerializedObject(v).FindProperty("spumCharacter").objectReferenceValue as SPUM_Prefabs;
            return spum != null ? PrefabUtility.GetCorrespondingObjectFromSource(spum) : null;
        }
        var namedLooks = named.Select(LookOf).Where(l => l != null).ToList();
        sb.AppendLine($"named={named.Count} [{string.Join(", ", named.Select(v => $"{v.name}:{LookOf(v)?.name}"))}]");
        sb.AppendLine($"slots={slots.Count} null={slots.Count(s => s == null)}");

        var tilemap = new SerializedObject(named.First()).FindProperty("walkableTilemap").objectReferenceValue as Tilemap;
        var plots = Object.FindObjectsByType<FacilityPlot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var toggled = new List<GameObject>();
        foreach (var plot in plots)
        {
            var stages = new SerializedObject(plot).FindProperty("stageObjects");
            for (int i = 0; i < stages.arraySize; i++)
            {
                var go = stages.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go == null || go.activeSelf) continue;
                go.SetActive(true);
                toggled.Add(go);
            }
        }
        Physics2D.SyncTransforms();
        var plotZones = plots.Select(p => p.GetComponent<BoxCollider2D>()).Where(c => c != null).Select(c => c.bounds).ToList();

        int problems = 0;
        var seenLooks = new HashSet<SPUM_Prefabs>();
        var positions = new List<Vector2>();
        foreach (var v in slots.Where(s => s != null))
        {
            var look = LookOf(v);
            Vector2 p = v.transform.position;
            bool onTile = tilemap != null && tilemap.HasTile(tilemap.WorldToCell(p));
            bool overlaps = Physics2D.OverlapCircleAll(p, TownsfolkClearance).Any(c => c != null && !c.isTrigger && !c.transform.IsChildOf(v.transform));
            bool inPlot = plotZones.Any(b => b.Contains(new Vector3(p.x, p.y, b.center.z)));
            bool dupLook = look == null || !seenLooks.Add(look);
            bool namedLook = namedLooks.Contains(look);
            float nearest = positions.Concat(named.Select(n => (Vector2)n.transform.position)).Select(q => (q - p).magnitude).DefaultIfEmpty(99f).Min();
            positions.Add(p);
            bool notMob = look == null || !AssetDatabase.GetAssetPath(look).StartsWith(MobFolder + "/");
            bool bad = !onTile || overlaps || inPlot || dupLook || namedLook || notMob || v.gameObject.activeSelf;
            if (bad) problems++;
            sb.AppendLine($"  {v.name} look={look?.name} pos=({p.x:F2},{p.y:F2}) onTile={onTile} overlapsObstacle={overlaps} inPlotZone={inPlot} dupLook={dupLook} namedLook={namedLook} notMob={notMob} nearest={nearest:F2} activeInScene={v.gameObject.activeSelf}{(bad ? "  <-- NG" : "")}");
        }
        foreach (var go in toggled) go.SetActive(false);

        foreach (int lv in new[] { 0, 1, 2, 5, 10, 20, 25, 31 })
            sb.AppendLine($"  villageLevel={lv} -> townsfolk={pop.CountFor(lv)} (total villagers={named.Count + pop.CountFor(lv)})");
        sb.AppendLine($"problems={problems}");
        Debug.Log("[SpumVillageVerify]\n" + sb);
    }

    /// <summary>batchmode 確認用: 生成したユニットとカタログの状態をログに出す。</summary>
    public static void Verify()
    {
        var sb = new StringBuilder();
        var mobs = LoadMobs();
        sb.AppendLine($"mobs={mobs.Count}");
        foreach (var m in mobs)
        {
            int sprites = m.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.sprite != null);
            sb.AppendLine($"  {m.name} lists={m.allListsHaveItemsExist()} sprites={sprites} anim={(m._anim != null && m._anim.runtimeAnimatorController != null)} parts={m.ImageElement.Count}");
        }
        var catalog = AssetDatabase.LoadAssetAtPath<MobAppearanceCatalog>(CatalogPath);
        sb.AppendLine($"catalog={(catalog != null ? catalog.Units.Count.ToString() : "none")}");
        if (catalog != null)
            foreach (var u in catalog.Units) sb.AppendLine($"  {(u != null ? AssetDatabase.GetAssetPath(u) : "null")} lists={(u != null && u.allListsHaveItemsExist())}");
        Debug.Log("[SpumMobVerify]\n" + sb);
    }

    // ───────────────────────── 汎用 ─────────────────────────

    private static List<T> Shuffle<T>(Random rng, List<T> list)
    {
        var l = new List<T>(list);
        for (int i = l.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (l[i], l[j]) = (l[j], l[i]);
        }
        return l;
    }

    private static (string pkg, string name) PickWeighted(Random rng, (string pkg, string name, int weight)[] choices)
    {
        int total = choices.Sum(c => c.weight);
        int r = rng.Next(total);
        foreach (var c in choices)
        {
            if (r < c.weight) return (c.pkg, c.name);
            r -= c.weight;
        }
        return (choices[0].pkg, choices[0].name);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
