using System;

/// <summary>
/// ゲームフロー自動生成（ローグライト）の設定群。
/// JsonUtility でシリアライズできるよう、Dictionary を使わず配列＋構造体で表現する
/// （将来のサーバー配信ペイロードとしてそのまま使える）。
/// <see cref="GameConstData"/> に内包され、Inspector / JSON で編集できる。
/// </summary>
[Serializable]
public class GameFlowGenerationSettings
{
    /// <summary>手動(SO) / 自動生成 の既定トグル。タイトルUIで上書きされうる。</summary>
    public bool useAutoGeneration = true;

    /// <summary>0 = 起動ごとにランダム、非0 = 固定シード（再現可能・テスト用）。</summary>
    public int randomSeed = 0;

    /// <summary>3モード分の設定。</summary>
    public GameModeConfig[] modes =
    {
        new GameModeConfig { mode = GameModeId.Short,  dungeonCount = 2, minShopTurnsBetweenBattles = 2, maxShopTurnsBetweenBattles = 3, eventRate = 0.3f },
        new GameModeConfig { mode = GameModeId.Medium, dungeonCount = 4, minShopTurnsBetweenBattles = 2, maxShopTurnsBetweenBattles = 4, eventRate = 0.3f },
        new GameModeConfig { mode = GameModeId.Long,   dungeonCount = 6, minShopTurnsBetweenBattles = 3, maxShopTurnsBetweenBattles = 5, eventRate = 0.35f },
    };

    /// <summary>ダンジョン別の出現率（重み）。未設定のダンジョンは重み1として扱う。</summary>
    public DungeonSpawnWeight[] dungeonWeights = Array.Empty<DungeonSpawnWeight>();

    // --- 難易度進行（重み付き抽選時のバイアス）---
    /// <summary>序盤(progress=0)で狙う目標難易度。</summary>
    public int earlyTargetDifficulty = 1;
    /// <summary>終盤(progress=1)で狙う目標難易度。</summary>
    public int lateTargetDifficulty = 10;
    /// <summary>
    /// 目標難易度からの距離に対する重み減衰の鋭さ。大きいほど目標難易度付近に強く偏る。
    /// 最終重み = spawnWeight / (1 + difficultyBias * |difficulty - target|)
    /// </summary>
    public float difficultyBias = 0.5f;

    /// <summary>指定モードの設定を返す。見つからなければ先頭、配列が空なら既定値。</summary>
    public GameModeConfig GetMode(GameModeId id)
    {
        if (modes != null)
        {
            foreach (var m in modes)
                if (m != null && m.mode == id) return m;
            if (modes.Length > 0 && modes[0] != null) return modes[0];
        }
        return new GameModeConfig { mode = id, dungeonCount = 3, minShopTurnsBetweenBattles = 2, maxShopTurnsBetweenBattles = 3, eventRate = 0.3f };
    }

    /// <summary>指定ダンジョンの出現重みを返す。未設定なら 1。</summary>
    public float GetWeight(DungeonName dungeon)
    {
        if (dungeonWeights != null)
        {
            foreach (var w in dungeonWeights)
                if (w != null && w.dungeon == dungeon) return w.weight;
        }
        return 1f;
    }

    /// <summary>配列まで含めた深いコピー（ベイク済みアセットを実行時に汚染しないため）。</summary>
    public GameFlowGenerationSettings Clone()
    {
        var clone = (GameFlowGenerationSettings)MemberwiseClone();

        if (modes != null)
        {
            clone.modes = new GameModeConfig[modes.Length];
            for (int i = 0; i < modes.Length; i++)
                clone.modes[i] = modes[i]?.Clone();
        }
        else
        {
            clone.modes = Array.Empty<GameModeConfig>();
        }

        if (dungeonWeights != null)
        {
            clone.dungeonWeights = new DungeonSpawnWeight[dungeonWeights.Length];
            for (int i = 0; i < dungeonWeights.Length; i++)
                clone.dungeonWeights[i] = dungeonWeights[i]?.Clone();
        }
        else
        {
            clone.dungeonWeights = Array.Empty<DungeonSpawnWeight>();
        }

        return clone;
    }
}

/// <summary>1モード分の生成設定。</summary>
[Serializable]
public class GameModeConfig
{
    public GameModeId mode;
    /// <summary>このモードで攻略するダンジョン（=Battle）数。</summary>
    public int dungeonCount = 3;
    /// <summary>Battle間に挟むShopターン数の下限。</summary>
    public int minShopTurnsBetweenBattles = 2;
    /// <summary>Battle間に挟むShopターン数の上限。</summary>
    public int maxShopTurnsBetweenBattles = 3;
    /// <summary>各Shopターンでイベントを挿入する確率（出現率）。0〜1。</summary>
    public float eventRate = 0.3f;

    public GameModeConfig Clone() => (GameModeConfig)MemberwiseClone();
}

/// <summary>ダンジョン別の出現率（重み）。</summary>
[Serializable]
public class DungeonSpawnWeight
{
    public DungeonName dungeon;
    public float weight = 1f;

    public DungeonSpawnWeight Clone() => (DungeonSpawnWeight)MemberwiseClone();
}
