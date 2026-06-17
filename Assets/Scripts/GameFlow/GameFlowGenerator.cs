using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゲームフロー（GameFlowStack）をモード設定とシードから自動生成する。
/// 純粋関数として実装：同じ (config, settings, dungeons, eventIdPool, seed) なら
/// 必ず同一のフローを返す（= シード保存で「続きから」を再現できる）。
/// </summary>
public static class GameFlowGenerator
{
    /// <summary>
    /// フローを生成する。
    /// </summary>
    /// <param name="config">モード設定（ダンジョン数・Shopターン数・イベント出現率）</param>
    /// <param name="settings">出現率や難易度進行などの全体設定</param>
    /// <param name="dungeons">利用可能なダンジョン一覧（DungeonRepository.GetAll()）</param>
    /// <param name="eventIdPool">挿入候補のイベントID一覧（EventDataLoader由来）</param>
    /// <param name="seed">乱数シード</param>
    public static GameFlow Generate(
        GameModeConfig config,
        GameFlowGenerationSettings settings,
        IReadOnlyList<DungeonData> dungeons,
        IReadOnlyList<string> eventIdPool,
        int seed)
    {
        var flow = ScriptableObject.CreateInstance<GameFlow>();
        var stack = flow.GameFlowStack;

        if (config == null)
        {
            Debug.LogError("[GameFlowGenerator] config is null. 空のフローを返します。");
            return flow;
        }

        var rng = new System.Random(seed);

        // 決定性のため、ダンジョンを key(enum値) でソートしたローカルコピーを使う
        // （DungeonRepository の並び順がセーブ状況で変わっても結果が変わらないように）
        var sortedDungeons = new List<DungeonData>();
        if (dungeons != null)
        {
            foreach (var d in dungeons)
                if (d != null) sortedDungeons.Add(d);
            sortedDungeons.Sort((a, b) => ((int)a.key).CompareTo((int)b.key));
        }

        int dungeonCount = Mathf.Max(0, config.dungeonCount);
        int minShop = Mathf.Max(0, config.minShopTurnsBetweenBattles);
        int maxShop = Mathf.Max(minShop, config.maxShopTurnsBetweenBattles);
        float eventRate = Mathf.Clamp01(config.eventRate);
        bool hasEvents = eventIdPool != null && eventIdPool.Count > 0;

        // Start
        stack.Add(new GameFlowNode { EventType = GameEvent.Start });

        for (int b = 0; b < dungeonCount; b++)
        {
            // Battle間のShopターン（＋確率でEvent挿入）
            int shopTurns = rng.Next(minShop, maxShop + 1);
            for (int s = 0; s < shopTurns; s++)
            {
                stack.Add(new GameFlowNode { EventType = GameEvent.Shop });

                if (hasEvents && rng.NextDouble() < eventRate)
                {
                    string eventId = eventIdPool[rng.Next(eventIdPool.Count)];
                    stack.Add(new GameFlowNode { EventType = GameEvent.Event, EventData = eventId });
                }
            }

            // Battle：重み付き抽選＋難易度進行
            float progress = dungeonCount > 1 ? (float)b / (dungeonCount - 1) : 0f;
            var dungeon = PickDungeon(rng, sortedDungeons, settings, progress);
            stack.Add(new GameFlowNode { EventType = GameEvent.Battle, BattleDungeon = dungeon });
        }

        // End
        stack.Add(new GameFlowNode { EventType = GameEvent.End });

        Debug.Log($"[GameFlowGenerator] Generated flow: mode={config.mode}, seed={seed}, nodes={stack.Count}, dungeons={dungeonCount}");
        return flow;
    }

    /// <summary>
    /// 重み付き抽選でダンジョンを1件選ぶ。進行度から目標難易度を補間し、
    /// 出現率(重み) × 難易度近接度 で確率を決める。
    /// </summary>
    private static DungeonName PickDungeon(
        System.Random rng,
        List<DungeonData> dungeons,
        GameFlowGenerationSettings settings,
        float progress)
    {
        if (dungeons == null || dungeons.Count == 0)
        {
            // フォールバック：enum先頭
            return default;
        }

        float target = settings != null
            ? Mathf.Lerp(settings.earlyTargetDifficulty, settings.lateTargetDifficulty, progress)
            : Mathf.Lerp(1f, 10f, progress);
        float bias = settings != null ? Mathf.Max(0f, settings.difficultyBias) : 0.5f;

        // 各ダンジョンの最終重みを算出
        float totalWeight = 0f;
        var weights = new float[dungeons.Count];
        for (int i = 0; i < dungeons.Count; i++)
        {
            float spawnWeight = settings != null ? settings.GetWeight(dungeons[i].key) : 1f;
            if (spawnWeight < 0f) spawnWeight = 0f;

            float distance = Mathf.Abs(dungeons[i].difficulty - target);
            float proximity = 1f / (1f + bias * distance);

            float w = spawnWeight * proximity;
            weights[i] = w;
            totalWeight += w;
        }

        if (totalWeight <= 0f)
        {
            // すべて重み0なら均等抽選
            return dungeons[rng.Next(dungeons.Count)].key;
        }

        double roll = rng.NextDouble() * totalWeight;
        float acc = 0f;
        for (int i = 0; i < dungeons.Count; i++)
        {
            acc += weights[i];
            if (roll < acc) return dungeons[i].key;
        }
        return dungeons[dungeons.Count - 1].key;
    }
}
