using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 旧方式のダンジョン構成（monsters + bossName）をフェーズ構成（phases）へ一括変換するツール。
/// 通常敵を3体ずつのフェーズに分割し、ボス（isBoss または bossName 一致）を最終フェーズに置く。
/// phases が既に設定されているレベルはスキップする（手動編集を上書きしない）。
/// </summary>
public static class DungeonPhaseMigrationTool
{
    [MenuItem("Tools/Dungeon/旧モンスター構成をフェーズへ一括変換")]
    public static void MigrateAll()
    {
        int convertedLevels = 0;
        int skippedLevels = 0;
        int dungeonCount = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:DungeonInfoScriptableObj"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/.claude/")) continue; // worktreeコピーを除外

            var so = AssetDatabase.LoadAssetAtPath<DungeonInfoScriptableObj>(path);
            if (so == null || so.levelDataList == null) continue;

            bool dirty = false;
            for (int i = 0; i < so.levelDataList.Length; i++)
            {
                var level = so.levelDataList[i];
                if (level == null) continue;

                bool hasPhases = level.phases != null &&
                                 level.phases.Any(p => p?.enemies != null && p.enemies.Any(e => e != null));
                if (hasPhases) { skippedLevels++; continue; }

                if (level.monsters == null || level.monsters.Count == 0) continue;

                level.phases = DungeonPhaseBuilder.BuildFromLegacy(level);
                convertedLevels++;
                dirty = true;
                Debug.Log($"[DungeonPhaseMigration] {so.dungeonName} Lv{i + 1}: {level.phases.Count}フェーズに変換" +
                          $"（{string.Join(" / ", level.phases.Select((p, n) => $"P{n + 1}:{string.Join(",", p.enemies.Select(e => e.enemyName))}"))}）");
            }

            if (dirty)
            {
                EditorUtility.SetDirty(so);
                dungeonCount++;
            }
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("フェーズ変換",
            $"変換完了\nダンジョン: {dungeonCount}件\n変換したレベル: {convertedLevels}件\nスキップ（設定済み）: {skippedLevels}件\n\n詳細はConsoleログを確認してください。", "OK");
    }
}
