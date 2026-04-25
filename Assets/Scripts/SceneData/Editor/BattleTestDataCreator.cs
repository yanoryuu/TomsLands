#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// テスト用の EnemyData アセットを生成し、
/// dungeonMonsters が空のダンジョンSOに自動設定するエディタツール。
/// メニュー: Tools > Battle > Setup Test Enemies
/// </summary>
public static class BattleTestDataCreator
{
    private const string EnemyDataFolder = "Assets/Resources/EnemyData";

    [MenuItem("Tools/Battle/Setup Test Enemies")]
    public static void SetupTestEnemies()
    {
        // 1) テスト用 EnemyData を作成
        var slime = CreateEnemyIfNotExists("Slime", "スライム", hp: 30, atk: 5, def: 2, isBoss: false);
        var goblin = CreateEnemyIfNotExists("Goblin", "ゴブリン", hp: 50, atk: 8, def: 3, isBoss: false);
        var wolf = CreateEnemyIfNotExists("Wolf", "狼", hp: 40, atk: 10, def: 2, isBoss: false);
        var boss = CreateEnemyIfNotExists("BossOgre", "ボスオーガ", hp: 200, atk: 20, def: 10, isBoss: true);

        var normalEnemies = new List<EnemyData> { slime, goblin, wolf };

        // 2) DungeonIndoData 内のダンジョンSOで dungeonMonsters が空のものに自動設定
        var guids = AssetDatabase.FindAssets("t:DungeonInfoScriptableObj", new[] { "Assets/DungeonIndoData" });
        int updated = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<DungeonInfoScriptableObj>(path);
            if (so == null) continue;

            // levelDataList を初期化（nullや空なら5要素で確保）
            if (so.levelDataList == null || so.levelDataList.Length < 5)
            {
                so.levelDataList = new DungeonLevelData[5];
            }

            bool needsUpdate = false;
            for (int lv = 0; lv < 5; lv++)
            {
                if (so.levelDataList[lv] == null)
                    so.levelDataList[lv] = new DungeonLevelData();

                if (so.levelDataList[lv].monsters == null || so.levelDataList[lv].monsters.Count == 0)
                {
                    so.levelDataList[lv].monsters = new List<EnemyData>(normalEnemies);
                    so.levelDataList[lv].monsters.Add(boss);
                    so.levelDataList[lv].bossName = boss.enemyName;
                    needsUpdate = true;
                }
            }

            if (needsUpdate)
            {
                EditorUtility.SetDirty(so);
                updated++;
                Debug.Log($"[BattleTestDataCreator] Updated dungeon: {so.key} ({so.dungeonName}) with level-based enemy data");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BattleTestDataCreator] Done. Created enemy assets and updated {updated} dungeons.");
    }

    private static EnemyData CreateEnemyIfNotExists(string fileName, string displayName, int hp, int atk, int def, bool isBoss)
    {
        // フォルダ作成
        if (!AssetDatabase.IsValidFolder(EnemyDataFolder))
        {
            var parts = EnemyDataFolder.Replace("\\", "/").Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        var assetPath = $"{EnemyDataFolder}/{fileName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<EnemyData>(assetPath);
        if (existing != null)
        {
            Debug.Log($"[BattleTestDataCreator] Already exists: {assetPath}");
            return existing;
        }

        var enemy = ScriptableObject.CreateInstance<EnemyData>();
        enemy.enemyId = fileName;
        enemy.enemyName = displayName;
        enemy.hp = hp;
        enemy.attackPower = atk;
        enemy.defensePower = def;
        enemy.isBoss = isBoss;
        enemy.skills = new List<SkillData>();

        AssetDatabase.CreateAsset(enemy, assetPath);
        Debug.Log($"[BattleTestDataCreator] Created: {assetPath}");
        return enemy;
    }
}
#endif

