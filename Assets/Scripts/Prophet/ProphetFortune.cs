using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 占い師の独自の情報（2026-10 グループ I-11・Q20）: 「この先 HorizonDays 日で、相場が大きく上がる品・下がる品」を占う。
///
/// ・中身は新聞の<b>本当の</b>効果（誤報は効果が無いので出ない。誇張は半分）。記事を読んで「上がる」と思った品が
///   占いに出なければ誤報を疑える＝新聞の裏取りになる（Docs/News_Spec.md §2 C5 の「有料観測」）
/// ・占いは外れることがある。出した行ごとに (1 − Accuracy) の確率で、動かない品にすり替わる（シード×ターン×行で決まる。同じ日は何度見ても同じ）
/// ・1日1回 Cost で買う。買った日はその日の中なら何度でも見られる（その日のターンを保存する）
/// </summary>
public static class ProphetFortune
{
    /// <summary>占いの値段（ユーザー決定 2026-10-20）。</summary>
    public const int Cost = 15000;
    /// <summary>何日先までの効果を見るか。</summary>
    public const int HorizonDays = 3;
    /// <summary>出す行の数（上がる・下がるそれぞれ）。</summary>
    public const int MaxRows = 3;
    /// <summary>この先 HorizonDays 日の効果の合計がこれ以上なら「上がる」と出す（小の記事 0.3 × 1日 を拾う）。</summary>
    public const float Threshold = 0.25f;
    /// <summary>1行が当たる確率。static は AutoPlay の計測で上書きするため。</summary>
    public static float Accuracy = 0.8f;

    public struct Reading
    {
        public RuntimeItemData item;
        /// <summary>+1 上がる / -1 下がる。</summary>
        public int direction;
        /// <summary>強さ（効果の合計の絶対値。すり替わった行は仮の値）。</summary>
        public float strength;
        /// <summary>すり替わった（外れの）行か。画面には出さない（計測用）。</summary>
        public bool wrong;
    }

    /// <summary>
    /// 解放済み（requiredLevel ≦ maxLevel）の品を占う。上がる品を強い順に最大 MaxRows 件、下がる品を最大 MaxRows 件。
    /// </summary>
    public static (List<Reading> ups, List<Reading> downs) Read(IReadOnlyList<RuntimeItemData> items, NewsEffectResolver news, int turn, int seed, int maxLevel)
    {
        var ups = new List<Reading>();
        var downs = new List<Reading>();
        if (items == null) return (ups, downs);

        var pool = new List<(RuntimeItemData item, float sum)>();
        foreach (var r in items)
        {
            if (r == null || r.RequiredLevel == null || r.RequiredLevel.Value > maxLevel) continue;
            float sum = 0f;
            if (news != null)
                for (int t = turn + 1; t <= turn + HorizonDays; t++) sum += news.TrendBias(r, t);
            pool.Add((r, sum));
        }

        pool.Sort((a, b) => b.sum.CompareTo(a.sum));
        var calm = pool.FindAll(p => Mathf.Abs(p.sum) < Threshold);
        var used = new HashSet<string>();

        void Take(List<Reading> into, IEnumerable<(RuntimeItemData item, float sum)> src, int dir)
        {
            int row = 0;
            foreach (var p in src)
            {
                if (into.Count >= MaxRows) break;
                if (dir > 0 ? p.sum < Threshold : p.sum > -Threshold) continue;
                var reading = new Reading { item = p.item, direction = dir, strength = Mathf.Abs(p.sum) };
                // 外れ: 動かない品にすり替える
                if (Unit(seed, turn, dir * 10 + row) > Accuracy && calm.Count > 0)
                {
                    int pick = Mathf.Min(calm.Count - 1, (int)(Unit(seed, turn, 100 + dir * 10 + row) * calm.Count));
                    var c = calm[pick];
                    if (!used.Contains(c.item.ItemId))
                        reading = new Reading { item = c.item, direction = dir, strength = reading.strength, wrong = true };
                }
                row++;
                if (used.Add(reading.item.ItemId)) into.Add(reading);
            }
        }

        Take(ups, pool, +1);
        var rev = new List<(RuntimeItemData item, float sum)>(pool);
        rev.Reverse();
        Take(downs, rev, -1);
        return (ups, downs);
    }

    /// <summary>シード・ターン・番号から 0〜1（同じ入力なら同じ値）。</summary>
    private static float Unit(int seed, int turn, int salt)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)seed) * 16777619u;
            h = (h ^ (uint)turn) * 16777619u;
            h = (h ^ (uint)salt) * 16777619u;
            h = (h ^ 0x5bd1e995u) * 16777619u;
            h ^= h >> 16; h *= 0x85ebca6bu; h ^= h >> 13; h *= 0xc2b2ae35u; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    // ---------------- 買った日の保存 ----------------

    [Serializable]
    private class Save { public int runSeed; public int boughtTurn = -1; }

    public const string FileName = "prophetFortune.json";

    /// <summary>このランのこのターンに占いを買ってあるか。</summary>
    public static bool IsBought(int runSeed, int turn)
    {
        try
        {
            string path = SaveSlotManager.GetPath(FileName);
            return SaveFile.TryReadJson<Save>(path, out var d) && d.runSeed == runSeed && d.boughtTurn == turn;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ProphetFortune] 読み込みに失敗: {e.Message}");
            return false;
        }
    }

    public static void MarkBought(int runSeed, int turn)
    {
        try
        {
            SaveFile.WriteAtomic(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(new Save { runSeed = runSeed, boughtTurn = turn }, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ProphetFortune] 保存に失敗: {e.Message}");
        }
    }
}
