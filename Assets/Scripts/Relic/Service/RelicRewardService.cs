using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;

/// <summary>
/// レリックの報酬抽選と「配信リザルト3択」の保留管理。
/// - 抽選はレア度の重み付き・所持済みと呪いを除外。村の冒険者ギルドのレベルまでに解禁されたものだけ（unlockGuildLevel）
/// - 選択肢の数・辞退の買取額も冒険者ギルドで増える（VillageEffects）
/// - 配信勝利時に BattleResultHandler が QueueBattleReward() を呼び、
///   TomsShopPresenter がホーム復帰時に PendingChoices を3択UIで表示する
/// （保留は RelicInventoryModel 経由で relics.json に保存する: 選ぶ前に終了しても続きからで出し直す）
/// </summary>
public class RelicRewardService
{
    private readonly RelicInventoryModel inventory;

    /// <summary>表示待ちの3択（空なら保留なし）。</summary>
    public List<RelicDefinition> PendingChoices { get; } = new();

    public RelicRewardService(RelicInventoryModel inventory)
    {
        this.inventory = inventory;
        // 保存済みの保留を戻す。以後もロード・ニューゲームの Clear のたびに作り直す
        // （本サービスと在庫モデルはどちらもシーンの Singleton で寿命が同じなので購読は解除しない）
        RebuildPendingFromInventory();
        inventory.PendingChoicesReloaded.Subscribe(_ => RebuildPendingFromInventory());
    }

    /// <summary>在庫モデルに保存された保留の relicId から、表示用の PendingChoices を作り直す。</summary>
    private void RebuildPendingFromInventory()
    {
        PendingChoices.Clear();
        foreach (var id in inventory.PendingChoiceIds)
        {
            var def = inventory.GetDefinition(id);
            if (def != null) PendingChoices.Add(def);
        }
    }

    /// <summary>PendingChoices の今の中身を保存する（積んだ・選んだ・辞退したとき）。</summary>
    private void SavePending() => inventory.SetPendingChoices(PendingChoices.Select(c => c.relicId));

    /// <summary>レア度重み付きで count 個の候補を抽選する（重複・所持済み・呪い除外）。</summary>
    public List<RelicDefinition> PickChoices(int count)
    {
        int unlockedTier = VillageEffects.Current.RelicUnlockTier;
        var pool = inventory.AllRelics
            .Where(r => !string.IsNullOrEmpty(r.relicId) && !r.isCurse && !inventory.Has(r.relicId)
                        && r.unlockGuildLevel <= unlockedTier)
            .ToList();

        var result = new List<RelicDefinition>();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            var picked = PickWeighted(pool);
            if (picked == null) break;
            result.Add(picked);
            pool.Remove(picked);
        }
        return result;
    }

    private RelicDefinition PickWeighted(List<RelicDefinition> pool)
    {
        float total = pool.Sum(r => RarityWeight(r.rarity));
        if (total <= 0f) return null;

        float roll = Random.Range(0f, total);
        foreach (var relic in pool)
        {
            roll -= RarityWeight(relic.rarity);
            if (roll <= 0f) return relic;
        }
        return pool[pool.Count - 1];
    }

    private static float RarityWeight(RelicRarity rarity) => rarity switch
    {
        RelicRarity.Common => GameConst.RelicCommonWeight,
        RelicRarity.Rare => GameConst.RelicRareWeight,
        RelicRarity.Epic => GameConst.RelicEpicWeight,
        _ => 1f,
    };

    /// <summary>
    /// レリック報酬の3択を保留に積む（既に保留があれば何もしない）。
    /// ホーム復帰時に TomsShopPresenter が選択UIを表示する。
    /// </summary>
    public void QueueReward(string reason)
    {
        if (PendingChoices.Count > 0) return;

        var choices = PickChoices(VillageEffects.Current.RelicChoiceCount(GameConst.RelicRewardChoiceCount));
        if (choices.Count == 0)
        {
            Debug.Log("[Relic] 報酬候補がありません（全所持 or マスター未登録）");
            return;
        }
        PendingChoices.AddRange(choices);
        SavePending();
        Debug.Log($"[Relic] {reason}の3択を保留: {string.Join(", ", choices.Select(c => c.relicName))}");
    }

    /// <summary>配信勝利報酬の3択を保留に積む。</summary>
    public void QueueBattleReward() => QueueReward("配信報酬");

    /// <summary>保留中の3択から1つ選んで獲得する。</summary>
    public bool ChoosePending(int index, int currentTurn)
    {
        if (index < 0 || index >= PendingChoices.Count) return false;
        var chosen = PendingChoices[index];
        PendingChoices.Clear();
        SavePending();
        return inventory.Add(chosen.relicId, currentTurn, GameConst.RelicMaxEquipSlots);
    }

    /// <summary>保留中の3択を辞退したときにもらえるゴールド（選択肢の最高レア度基準）。保留なしは0。</summary>
    public int GetDeclineGold()
    {
        if (PendingChoices.Count == 0) return 0;
        var best = PendingChoices.Max(c => c.rarity);
        return VillageEffects.Current.ApplyDeclineGold(GameConst.RelicDeclineGold(best));
    }

    /// <summary>保留中の3択を辞退してゴールドに換える。もらえる額を返す（保留なしは0、入金は呼び出し側）。</summary>
    public int DeclineForGold()
    {
        int gold = GetDeclineGold();
        PendingChoices.Clear();
        SavePending();
        return gold;
    }
}
