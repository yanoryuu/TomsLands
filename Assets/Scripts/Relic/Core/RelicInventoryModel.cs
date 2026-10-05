using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using R3;
using UnityEngine;

/// <summary>
/// ラン中に所持しているレリックの保持と永続化。
/// 重複所持は不可（同一レリックは1個まで）。装備枠は maxEquipSlots（0=無制限）。
/// セーブは slot_N/relics.json。ラン終了（ニューゲーム）で消滅する。
/// </summary>
public class RelicInventoryModel
{
    private const string FileName = "relics.json";

    private readonly List<RelicDefinition> masterRelics;

    /// <summary>所持レリック（獲得順）。</summary>
    public List<OwnedRelic> Owned { get; } = new();

    /// <summary>所持変化の通知（UI再描画用）。</summary>
    public Subject<Unit> OnChanged { get; } = new();

    public RelicInventoryModel(List<RelicDefinition> masterRelics)
    {
        this.masterRelics = masterRelics ?? new List<RelicDefinition>();
        LoadData();
    }

    public List<RelicDefinition> AllRelics => masterRelics;

    public RelicDefinition GetDefinition(string relicId) =>
        masterRelics.FirstOrDefault(r => r.relicId == relicId);

    public bool Has(string relicId) => Owned.Any(o => o.RelicId == relicId);

    /// <summary>所持レリックの定義を列挙する（Resolver / Hook 用）。</summary>
    public IEnumerable<RelicDefinition> OwnedDefinitions()
    {
        foreach (var owned in Owned)
        {
            var def = GetDefinition(owned.RelicId);
            if (def != null) yield return def;
        }
    }

    /// <summary>
    /// レリックを獲得する。重複・装備枠上限（maxEquipSlots、0=無制限）は失敗。
    /// </summary>
    public bool Add(string relicId, int acquiredTurn, int maxEquipSlots = 0)
    {
        var def = GetDefinition(relicId);
        if (def == null)
        {
            Debug.LogWarning($"[Relic] 未知のレリックID: {relicId}");
            return false;
        }
        if (Has(relicId)) return false;
        if (maxEquipSlots > 0 && Owned.Count >= maxEquipSlots) return false;

        Owned.Add(new OwnedRelic { RelicId = relicId, AcquiredTurn = acquiredTurn });
        SaveData();
        OnChanged.OnNext(Unit.Default);
        Debug.Log($"[Relic] 獲得: {def.relicName}");
        return true;
    }

    public bool Remove(string relicId)
    {
        var owned = Owned.FirstOrDefault(o => o.RelicId == relicId);
        if (owned == null) return false;
        Owned.Remove(owned);
        SaveData();
        OnChanged.OnNext(Unit.Default);
        return true;
    }

    // ========================================
    // トリガー型効果用の汎用カウンタ（relicId → key → 値）
    // ========================================

    public float GetCounter(string relicId, string key)
    {
        var owned = Owned.FirstOrDefault(o => o.RelicId == relicId);
        var counter = owned?.Counters.FirstOrDefault(c => c.key == key);
        return counter?.value ?? 0f;
    }

    public void SetCounter(string relicId, string key, float value)
    {
        var owned = Owned.FirstOrDefault(o => o.RelicId == relicId);
        if (owned == null) return;
        var counter = owned.Counters.FirstOrDefault(c => c.key == key);
        if (counter == null)
        {
            counter = new RelicCounter { key = key };
            owned.Counters.Add(counter);
        }
        counter.value = value;
    }

    // ========================================
    // 永続化
    // ========================================

    public void SaveData()
    {
        var data = new RelicSaveData
        {
            owned = Owned.Select(o => new OwnedRelicPlain
            {
                relicId = o.RelicId,
                acquiredTurn = o.AcquiredTurn,
                counters = new List<RelicCounter>(o.Counters),
            }).ToList()
        };
        File.WriteAllText(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true));
    }

    public void LoadData()
    {
        Owned.Clear();
        string path = SaveSlotManager.GetPath(FileName);
        if (File.Exists(path))
        {
            var data = JsonUtility.FromJson<RelicSaveData>(File.ReadAllText(path));
            if (data?.owned != null)
            {
                foreach (var plain in data.owned)
                {
                    if (string.IsNullOrEmpty(plain.relicId) || Has(plain.relicId)) continue;
                    Owned.Add(new OwnedRelic
                    {
                        RelicId = plain.relicId,
                        AcquiredTurn = plain.acquiredTurn,
                        Counters = new List<RelicCounter>(plain.counters ?? new List<RelicCounter>()),
                    });
                }
            }
        }
        OnChanged.OnNext(Unit.Default);
    }

    /// <summary>ニューゲーム用リセット（レリックはラン中限定）。</summary>
    public void Clear()
    {
        Owned.Clear();
        OnChanged.OnNext(Unit.Default);
    }
}

public class OwnedRelic
{
    public string RelicId;
    public int AcquiredTurn;
    public List<RelicCounter> Counters = new();
}

[Serializable]
public class OwnedRelicPlain
{
    public string relicId;
    public int acquiredTurn;
    public List<RelicCounter> counters = new();
}

[Serializable]
public class RelicCounter
{
    public string key;
    public float value;
}

[Serializable]
public class RelicSaveData
{
    public List<OwnedRelicPlain> owned = new();
}
