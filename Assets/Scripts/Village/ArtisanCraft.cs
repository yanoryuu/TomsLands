using System;
using UnityEngine;

/// <summary>職人組合 Lv5「細工」の種類。</summary>
public enum ArtisanCraftKind
{
    None = 0,
    /// <summary>業物: 次の配信先の弱点の属性が武器に付く（勇者が勝ちやすい）。</summary>
    Fine = 1,
    /// <summary>なまくら: 武器の攻撃が下がる（勇者が負けやすい）。</summary>
    Dull = 2,
}

/// <summary>
/// 職人組合 Lv5「細工」（Docs/Village_Meta_Design.md §0-1、PROPOSAL_v3 §4-5 A）。
/// 配信と配信の間に1回、勇者の武器を「業物」か「なまくら」にできる。効くのはその次の配信1回だけ。
///
/// ・業物: 武器の属性が、その配信先の弱点の属性になる（弱点の敵への攻撃が上がる。HeroEquipmentBonus.AttributeAttackMul）
/// ・なまくら: 勇者の攻撃が <see cref="DullAttackMul"/> 倍になる
/// ・<b>クリア確率の計算・表示には入れない</b>（2026-10 ユーザー決定 Q7。属性の戦闘効果と同じ扱い）。勇者画面の攻撃・防御の数字も変えない
/// ・細工は武器の枠に付く（配信の前に武器を替えても、そのとき持っている武器に効く。武器を外すと効かない）
///
/// 使ったかどうかと種類はランのセーブ <see cref="FileName"/> に1件だけ持つ（RunSaveCleaner で消える）。
/// 「どの配信の間か」は次の配信がこのランの何回目か（GameFlowManager.GetStreamProgress の upcoming）で見分ける。
/// FightScene は別の LifetimeScope なので、配信へ移る直前に GameFlowManager が <see cref="PrepareBattle"/> で値を確定し、
/// 勇者の CharacterModel はそれを読むだけにする（RelicBattleEffects と同じ作り）。
/// </summary>
public static class ArtisanCraft
{
    public const string FileName = "artisanCraft.json";

    /// <summary>なまくらにしたときの勇者の攻撃の倍率（0.5 = 半分）。</summary>
    public const float DullAttackMul = 0.5f;

    [Serializable]
    private class SaveData
    {
        public int runSeed;
        /// <summary>細工した「配信の間」（次の配信がこのランの何回目か）。</summary>
        public int streamNumber;
        public int kind;
    }

    // ───────── 店側（勇者画面） ─────────

    /// <summary>その配信の間にした細工（していなければ None）。streamNumber は次の配信がこのランの何回目か（1 始まり）。</summary>
    public static ArtisanCraftKind KindFor(int runSeed, int streamNumber)
    {
        if (streamNumber <= 0) return ArtisanCraftKind.None;
        var data = Load();
        if (data == null || data.runSeed != runSeed || data.streamNumber != streamNumber) return ArtisanCraftKind.None;
        return Normalize(data.kind);
    }

    /// <summary>細工する（配信の間に1回）。保存できたら true。</summary>
    public static bool Apply(int runSeed, int streamNumber, ArtisanCraftKind kind)
    {
        if (kind == ArtisanCraftKind.None || streamNumber <= 0) return false;
        if (KindFor(runSeed, streamNumber) != ArtisanCraftKind.None) return false;
        try
        {
            var data = new SaveData { runSeed = runSeed, streamNumber = streamNumber, kind = (int)kind };
            return SaveFile.WriteAtomic(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArtisanCraft] 保存に失敗しました: {ex.Message}");
            return false;
        }
    }

    // ───────── 配信側（FightScene） ─────────

    /// <summary>これから始まる配信に効く細工（PrepareBattle で確定）。</summary>
    public static ArtisanCraftKind BattleKind { get; private set; }

    /// <summary>業物のとき武器に付く属性（その配信先の弱点）。</summary>
    public static ItemTypeData.ItemAttribute? BattleFineAttribute { get; private set; }

    /// <summary>
    /// 配信へ移る直前に呼ぶ（GameFlowManager.PrepareBattleInput）。streamNumber はその配信がこのランの何回目か、
    /// weakness はその配信先の弱点の属性。職人組合が Lv5 でなければ細工は効かない。
    /// </summary>
    public static void PrepareBattle(int runSeed, int streamNumber, ItemTypeData.ItemAttribute? weakness)
    {
        var kind = VillageEffects.Current.CanCraftHeroWeapon ? KindFor(runSeed, streamNumber) : ArtisanCraftKind.None;
        if (kind == ArtisanCraftKind.Fine && !weakness.HasValue) kind = ArtisanCraftKind.None;
        BattleKind = kind;
        BattleFineAttribute = kind == ArtisanCraftKind.Fine ? weakness : null;
        if (kind != ArtisanCraftKind.None)
            Debug.Log($"[ArtisanCraft] 配信{streamNumber}回目に細工が効く: {kind}{(BattleFineAttribute.HasValue ? $"（{BattleFineAttribute.Value}）" : "")}");
    }

    /// <summary>勇者の武器の属性に細工を掛けたもの（武器が無ければ細工は効かない）。</summary>
    public static ItemTypeData.ItemAttribute? BattleWeaponAttribute(bool hasWeapon, ItemTypeData.ItemAttribute? baseAttribute)
        => WeaponAttributeFor(BattleKind, hasWeapon, baseAttribute, BattleFineAttribute);

    /// <summary>勇者の攻撃に掛ける細工の倍率（武器が無ければ 1）。</summary>
    public static float BattleAttackMul(bool hasWeapon) => AttackMulFor(BattleKind, hasWeapon);

    /// <summary>細工の種類から武器の属性を決める（純粋関数。検証用にも使う）。</summary>
    public static ItemTypeData.ItemAttribute? WeaponAttributeFor(ArtisanCraftKind kind, bool hasWeapon,
        ItemTypeData.ItemAttribute? baseAttribute, ItemTypeData.ItemAttribute? fineAttribute)
        => hasWeapon && kind == ArtisanCraftKind.Fine && fineAttribute.HasValue ? fineAttribute : baseAttribute;

    /// <summary>細工の種類から攻撃の倍率を決める（純粋関数）。</summary>
    public static float AttackMulFor(ArtisanCraftKind kind, bool hasWeapon)
        => hasWeapon && kind == ArtisanCraftKind.Dull ? Mathf.Clamp(DullAttackMul, 0.1f, 1f) : 1f;

    // ───────── 内部 ─────────

    private static ArtisanCraftKind Normalize(int kind) =>
        kind == (int)ArtisanCraftKind.Fine ? ArtisanCraftKind.Fine
        : kind == (int)ArtisanCraftKind.Dull ? ArtisanCraftKind.Dull
        : ArtisanCraftKind.None;

    private static SaveData Load()
    {
        try
        {
            return SaveFile.TryReadJson<SaveData>(SaveSlotManager.GetPath(FileName), out var data) ? data : null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArtisanCraft] 読み込みに失敗しました: {ex.Message}");
            return null;
        }
    }
}
