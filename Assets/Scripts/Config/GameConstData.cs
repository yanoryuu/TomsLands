using System;

/// <summary>
/// ゲーム全体の調整値（旧 GameConst の定数群）を保持するシリアライズ可能なデータ。
/// JsonUtility でそのままシリアライズできるため、将来のサーバー配信
/// （SpreadSheet → サーバー → ダウンロードした JSON を適用）にそのまま使える。
/// Inspector 編集用には <see cref="GameConstSettings"/> が内包する。
/// </summary>
[Serializable]
public class GameConstData
{
    // --- 上限値 ---
    public int maxDungeonLevel = 5;
    public int maxBlackSmithLevel = 5;
    public int maxToolShopLevel = 5;
    public int maxInfoBrokerLevel = 5;
    public int maxItemStock = 99;
    public int minItemStock = 0;

    // --- 所持金 ---
    public int initMoney = 10000;

    // --- 借金 ---
    public int debtPaymentInterval = 10;   // 何ターンごとに強制返済か
    public int debtBaseAmount = 5000;       // 1回目（cycle=1）の返済額
    public float debtMultiplier = 1.8f;     // 返済のたびに前回額へ掛ける倍率

    // --- ヒーロー経験値 ---
    public int heroExpPerMob = 10;
    public int heroExpPerBoss = 100;
    public int heroBaseExpToNextLevel = 100;

    // --- 鍛冶屋レベルアップコスト（index = 現在レベル → 次レベルへの費用） ---
    public int[] blackSmithLevelUpCosts = { 0, 3000, 6000, 12000, 20000 };

    // --- ゲームフロー自動生成（ローグライト） ---
    public GameFlowGenerationSettings flowGeneration = new GameFlowGenerationSettings();

    /// <summary>
    /// 配列まで含めた深いコピーを返す（ベイク済みアセットを実行時に汚染しないため）。
    /// </summary>
    public GameConstData Clone()
    {
        var clone = (GameConstData)MemberwiseClone();
        clone.blackSmithLevelUpCosts = blackSmithLevelUpCosts != null
            ? (int[])blackSmithLevelUpCosts.Clone()
            : Array.Empty<int>();
        clone.flowGeneration = flowGeneration != null
            ? flowGeneration.Clone()
            : new GameFlowGenerationSettings();
        return clone;
    }
}
