using UnityEngine;

[CreateAssetMenu(fileName = "DungeonInfo", menuName = "ScriptableObjects/DungeonInfo", order = 1)]
public class DungeonInfoScriptableObj : ScriptableObject
{
    // ----------------------
    // 識別・基本情報
    // ----------------------
    [Header("基本情報")]
    [Tooltip("ダンジョンを一意に識別するID（セーブやDB用）")]
    public DungeonName key;

    [Tooltip("ダンジョン名（表示用）")]
    public string dungeonName;


    // ----------------------
    // 表示用情報
    // ----------------------
    [Header("表示情報")]
    [Tooltip("ダンジョンの説明文（UI表示用）")]
    [TextArea]
    public string dungeonDescription;

    [Tooltip("ダンジョンの背景画像（FightScene用）")]
    public Sprite dungeonImage;

    [Tooltip("ダンジョンのアイコン画像（マップ・情報画面用）")]
    public Sprite dungeonIcon;

    [Tooltip("ダンジョン名の画像（ダンジョン情報パネル用）")]
    public Sprite dungeonNameImage;


    // ----------------------
    // 難易度・レベル関連
    // ----------------------
    [Header("レベル・難易度")]
    [Tooltip("初期レベル（基準値）")]
    public int initDungeonLevel;

    [Tooltip("推奨レベル（挑戦目安）")]
    public int recommendedLevel;

    [Tooltip("難易度（1～10）")]
    [Range(1, 10)]
    public int difficulty;


    // ----------------------
    // 入場条件
    // ----------------------
    [Header("入場条件")]
    [Tooltip("入場に必要な属性")]
    public ItemTypeData.ItemAttribute requiredAttribute;


    // ----------------------
    // 敵データ（レベル別）
    // ----------------------
    [Header("敵データ（レベル別）")]
    [Tooltip("レベル1～5ごとの出現モンスター・ボスデータ")]
    public DungeonLevelData[] levelDataList = new DungeonLevelData[5];

    /// <summary>
    /// 指定レベル（1～5）のデータを取得する。範囲外はクランプ。
    /// </summary>
    public DungeonLevelData GetLevelData(int level)
    {
        int index = Mathf.Clamp(level, 1, 5) - 1;
        if (levelDataList == null || levelDataList.Length == 0) return null;
        if (index >= levelDataList.Length) return null;
        return levelDataList[index];
    }

    // ----------------------
    // 魔王軍が勝った時の報酬（※レベルごとに DungeonLevelData.rewardGold で設定）
    // ----------------------

    /// <summary>
    /// 指定レベルの防衛報酬（勇者敗北時に受け取る配当）。Lv0（魔王軍支援で未出資）は 0。
    /// Lv0 の戦闘（敵の出方）は Lv1 と同じ（GetLevelData がクランプする）。
    /// </summary>
    public int GetDefenseReward(int level) => DungeonLevels.DefenseReward(GetLevelData(level), level);

    /// <summary>このダンジョンを level にするまでの魔王軍支援の出資の合計（DungeonLevels.InvestedTotal）。</summary>
    public int GetInvestedTotal(int level) => DungeonLevels.InvestedTotal(GetLevelData, initDungeonLevel, level);

    /// <summary>
    /// 配信値を当てる（"dungeons" の部分上書き → "dungeonLevels" のレベルごとの費用・配当の順）。
    /// マップ（DungeonRepository）・戦闘（BattleLifetimeScope）・自動プレイのヘッドレスで同じものを使う。
    /// </summary>
    public static System.Collections.Generic.List<DungeonInfoScriptableObj> ApplyRemoteBalance(
        System.Collections.Generic.List<DungeonInfoScriptableObj> baked)
    {
        var list = RemoteBalance.ApplyList("dungeons", baked, d => d.key.ToString());
        return RemoteBalance.ApplyDungeonLevels(list);
    }
}

/// <summary>
/// ダンジョンレベルの約束事。Lv0 = 魔王軍支援で未出資（配当なし・戦闘は Lv1 と同じ）、出資すると Lv1。
/// </summary>
public static class DungeonLevels
{
    /// <summary>未出資のレベル。</summary>
    public const int Unfunded = 0;

    /// <summary>防衛報酬。未出資（Lv0 以下）は 0。</summary>
    public static int DefenseReward(DungeonLevelData data, int level) => level <= Unfunded ? 0 : data?.rewardGold ?? 0;

    /// <summary>
    /// 出資の合計: 初期Lv（0 未満は 0）→ level までの魔王軍支援の費用（levelUpCost は「そのLvから次のLvへ」の費用）の合計。
    /// Lv0→Lv1（最初の出資）は Lv1 の行の費用（GetLevelData のクランプ）。ダンジョンLvは魔王軍支援でしか上がらないので、払った額と一致する。
    /// 配当通知書の「支援額」と、配当の下限（SupportOdds.Payout の stake）はこの 1 か所から取る。
    /// </summary>
    public static int InvestedTotal(System.Func<int, DungeonLevelData> getLevelData, int initLevel, int level)
    {
        if (getLevelData == null) return 0;
        long total = 0;
        for (int lv = System.Math.Max(Unfunded, initLevel); lv < level; lv++)
            total += System.Math.Max(0, getLevelData(lv)?.levelUpCost ?? 0);
        return (int)System.Math.Min(int.MaxValue, total);
    }

    /// <summary>表示用「Lv.0（未出資）」「Lv.3」。</summary>
    public static string Label(int level, string prefix = "Lv.") => level <= Unfunded ? $"{prefix}0（未出資）" : $"{prefix}{level}";
}