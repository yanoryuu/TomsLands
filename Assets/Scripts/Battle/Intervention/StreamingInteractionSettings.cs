using System;
using UnityEngine;

/// <summary>
/// 配信中の介入（勇者側スパチャ3種・ダンジョン側4種）と視聴者ランダムスパチャの調整値。
/// Docs/Streaming_Redesign.md §4・§5。効果量は仮値（プレイテストで調整）。
///
/// ロード: Addressable "StreamingInteractionSettings"（Assets/Resources_moved/StreamingInteractionSettings.asset）。
/// サーバー上書きは RemoteBalance の区画 "streamingInteraction"。どちらも無ければ既定値のインスタンス。
/// </summary>
[CreateAssetMenu(fileName = "StreamingInteractionSettings", menuName = "ScriptableObjects/Battle/StreamingInteractionSettings")]
public class StreamingInteractionSettings : ScriptableObject
{
    public const string Address = "StreamingInteractionSettings";
    public const string RemoteKey = "streamingInteraction";

    // ───────── 勇者側（表）: プレイヤー払いスパチャ ─────────
    [Header("緑スパ: 回復")]
    [Min(0)] public int healPrice = 10000;
    [Tooltip("勇者の最大HPに対する回復割合。0.3 = +30%")]
    [Range(0f, 1f)] public float healRatio = 0.30f;
    [Tooltip("実行時の配信熱")]
    public float healHeat = 0f;

    [Header("青スパ: スキル")]
    [Min(0)] public int skillPrice = 50000;
    [Tooltip("次の勇者の攻撃に掛ける倍率")]
    [Min(1f)] public float skillMultiplier = 1.5f;
    public float skillHeat = 10f;
    [Tooltip("装備中の武器（武器種）の需要UP量")]
    public float skillWeaponDemandUp = 0.10f;

    [Header("赤スパ: 必殺技")]
    [Min(0)] public int specialPrice = 100000;
    [Min(1f)] public float specialMultiplier = 3f;
    public float specialHeat = 20f;
    [Tooltip("武器種の需要UP量")]
    public float specialWeaponDemandUp = 0.20f;
    [Tooltip("武器種の価格倍率（「その剣なに？」）。1 = 変化なし")]
    [Min(0f)] public float specialWeaponPriceMul = 1.05f;
    [Tooltip("必殺技の上限（1配信。視聴者の赤スパ起点も含む）")]
    [Min(0)] public int specialMaxPerStream = 2;

    // ───────── ダンジョン側（裏） ─────────
    [Header("罠: 勇者の次の被弾に追加ダメージ")]
    [Min(0)] public int trapPrice = 10000;
    [Tooltip("追加ダメージ = 勇者の最大HP × この割合（防御無視）")]
    [Range(0f, 1f)] public float trapBonusDamageRatio = 0.30f;

    [Header("呪い: 勇者の防御力を一時ダウン（2026-10-05 攻撃ダウンから変更）")]
    [Min(0)] public int cursePrice = 30000;
    [Tooltip("勇者の防御力に掛ける倍率（魔物の攻撃を受けるとき）。ダメージ = max(1, 攻撃 − 防御×この値)")]
    [Range(0f, 1f)] public float curseDefenseMul = 0.5f;
    [Tooltip("効果が続く勇者の被弾回数（魔物の攻撃1回 = 1）")]
    [Min(1)] public int curseHeroHits = 9;

    [Header("増援: 現在フェーズのキューに魔物を追加")]
    [Min(0)] public int reinforcePrice = 50000;
    [Min(1)] public int reinforceCount = 1;

    [Header("ボス強化（その配信中のみ）")]
    [Min(0)] public int bossBuffPrice = 100000;
    [Tooltip("ボスの攻撃力倍率")]
    [Min(1f)] public float bossBuffAttackMul = 1.25f;
    [Tooltip("ボスが受ける勇者の攻撃力に掛ける倍率（実質の耐久UP）")]
    [Range(0.1f, 1f)] public float bossBuffDamageTakenMul = 0.8f;
    [Tooltip("防御貫通: ボスの攻撃が勇者の防御のこの割合を無視する（0 = 無視しない / 1 = 完全に無視）")]
    [Range(0f, 1f)] public float bossBuffDefensePierce = 0.5f;
    [Min(0)] public int bossBuffMaxPerStream = 1;

    [Header("ダンジョン側の実行時の配信熱")]
    public float dungeonInterventionHeat = 5f;

    // ───────── 共通 ─────────
    [Header("共通")]
    [Tooltip("指示を出した後のクールダウン（秒。両面共通）。一時停止中は進まない")]
    [Min(0f)] public float cooldownSeconds = 6f;

    // ───────── 視聴者ランダムスパチャ（収入にしない・演出のみ） ─────────
    [Header("視聴者スパチャ: ON/OFF（OFF にすると介入なしの配信が改修前と完全に同じになる）")]
    [Tooltip("視聴者ランダムスパチャを発生させる（熱・同接に効く）")]
    public bool viewerSuperChatEnabled = true;
    [Tooltip("視聴者の赤スパで勇者の必殺技を自動で出す（必殺技の上限に含む）")]
    public bool viewerRedTriggersSpecial = true;

    [Header("視聴者スパチャ: 発生率  期待件数/分 = 同接 / viewersPerUnit × (heatBase + 熱/100)")]
    [Min(1f)] public float viewersPerUnit = 100f;
    public float heatBase = 0.5f;
    [Tooltip("1分あたりの期待件数の上限（同接が極端に多いときの暴発防止）")]
    [Min(0f)] public float maxPerMinute = 8f;
    [Tooltip("配信開始から最初のスパチャが来うるまでの秒数")]
    [Min(0f)] public float startDelaySeconds = 4f;

    [Header("視聴者スパチャ: 色の重み（青/水/緑/黄/橙/桃/赤）")]
    public float[] colorWeights = { 30f, 25f, 18f, 12f, 8f, 5f, 2f };
    [Tooltip("赤の重みを増やす同接の基準。赤の重み × (1 + 同接 / この値)")]
    [Min(1f)] public float redViewerReference = 400f;
    [Tooltip("桃・橙も同接で少し増やす割合（赤の半分の伸び）")]
    [Range(0f, 1f)] public float warmColorViewerScale = 0.5f;
    [Tooltip("超バズ中の赤の重み倍率")]
    [Min(1f)] public float superBuzzRedMul = 3f;

    [Header("視聴者スパチャ: 表示金額の範囲（G。精算には入らない）")]
    public Vector2Int[] amountRanges =
    {
        new Vector2Int(100, 199),     // Blue
        new Vector2Int(200, 499),     // Cyan
        new Vector2Int(500, 999),     // Green
        new Vector2Int(1000, 1999),   // Yellow
        new Vector2Int(2000, 4999),   // Orange
        new Vector2Int(5000, 9999),   // Magenta
        new Vector2Int(10000, 50000), // Red
    };

    [Header("視聴者スパチャ: 盛り上がりへの効果")]
    [Tooltip("受信時の配信熱（色の段階 0〜6 に応じて heatPerTier × (段階+1)）")]
    public float heatPerTier = 0.5f;
    [Tooltip("受信時の同接の瞬間流入（割合。色の段階に比例）")]
    public float viewerSpikePerTier = 0.004f;
    [Tooltip("視聴者の赤スパで必殺技が上限に達していたときの熱UP")]
    public float redOverLimitHeat = 8f;

    [Header("視聴者スパチャ: 添えるコメント（空文字なら金額だけ）")]
    public string[] lowMessages = { "", "", "わこつ", "応援してます", "がんばれー", "初見です", "{hero}かわいい" };
    public string[] midMessages = { "", "ナイスファイト", "{hero}いけー！", "店長いつもありがとう", "その装備ほしい", "ボス倒して！" };
    public string[] redMessages = { "必殺技見せて！", "{hero}ぶちかませ！", "赤スパで殴れ", "全財産ぶっこむ", "必殺技いけええ" };

    [Header("プレイヤーのスパチャに添えるコメント")]
    public string playerHealMessage = "回復して！";
    public string playerSkillMessage = "スキルいけ！";
    public string playerSpecialMessage = "必殺技だ！";

    public int GetPrice(HeroInterventionType type) => type switch
    {
        HeroInterventionType.Heal => healPrice,
        HeroInterventionType.Skill => skillPrice,
        HeroInterventionType.Special => specialPrice,
        _ => 0,
    };

    public int GetPrice(DungeonInterventionType type) => type switch
    {
        DungeonInterventionType.Trap => trapPrice,
        DungeonInterventionType.Curse => cursePrice,
        DungeonInterventionType.Reinforce => reinforcePrice,
        DungeonInterventionType.BossBuff => bossBuffPrice,
        _ => 0,
    };

    public static SuperChatColor ColorOf(HeroInterventionType type) => type switch
    {
        HeroInterventionType.Heal => SuperChatColor.Green,
        HeroInterventionType.Skill => SuperChatColor.Blue,
        _ => SuperChatColor.Red,
    };

    /// <summary>Addressable → RemoteBalance 上書き → 既定値 の順で解決する。</summary>
    public static StreamingInteractionSettings Load()
    {
        StreamingInteractionSettings baked = null;
        try
        {
            baked = AddressableLoader.Load<StreamingInteractionSettings>(Address);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StreamingInteractionSettings] Addressable '{Address}' を読めませんでした（未登録？）: {e.Message}");
        }

        if (baked == null)
        {
            baked = CreateInstance<StreamingInteractionSettings>();
            baked.name = "StreamingInteractionSettings(Default)";
            Debug.LogWarning("[StreamingInteractionSettings] アセットが無いため既定値を使います。メニュー「Tools/TomsLands/Addressables一括登録（Resources_moved）」で登録してください。");
        }
        return RemoteBalance.ApplyOverwrite(RemoteKey, baked);
    }
}
