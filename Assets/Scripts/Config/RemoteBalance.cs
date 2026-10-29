using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 結合配信 balance.json 由来の上書きを保持する静的ファサード。
/// 1ファイルに複数データ（単一設定SO・リストマスター）の区画を含む。
/// Newtonsoft で区画ごとの生JSONへ分割し、適用は JsonUtility.FromJsonOverwrite で行うことで
/// 「前方互換（配信に無いフィールドはベイク値保持）」と「アセット参照(Sprite等)の保持」を両立する。
/// 上書きが無ければ各SOの既定値のまま（Bootを通さないエディタ直接再生でも安全）。
/// </summary>
public static class RemoteBalance
{
    public const int ExpectedSchemaVersion = 1;

    // 単一区画: key -> 生JSON
    private static readonly Dictionary<string, string> _sections = new();
    // リスト区画: section -> ( id値 -> 生JSON )
    private static readonly Dictionary<string, Dictionary<string, string>> _lists = new();
    // heroLevels は純スカラーCSV由来。あれば全置換。
    private static List<HeroLevelData> _heroLevels;
    // events はCSVマスター由来（Dictionaryを含むためJsonUtility不可、JObjectから手動変換）。あれば全置換。
    private static List<TomsEvent> _events;
    // dungeonLevels（ダンジョンのレベルの表の費用・配当。"*" = 全ダンジョン共通）。あれば項目ごとに上書き。
    private static List<DungeonLevelRow> _dungeonLevels;
    // 上書き済みの複製（元のアセット1つにつき1つ。シーンをまたいで使い回す）
    private static readonly OverrideCloneCache _clones = new();

    private static readonly string[] SingleSections = { "shopEconomy", "gameBalance", "battlePrice", "shopLevel", "finance", StreamingInteractionSettings.RemoteKey };
    // ※ HeroData(SO) は Inspector 割当のフォールバックで、実バトル値は heroLevels(CSV由来) なので
    //   "heroes" 区画は設けない（hero バランスは heroLevels で配信する）。
    private static readonly string[] ListSections =
        { "advertisements", "buzzEffects", "followerMilestones", "enemies", "dungeons", "financialProducts", "shopMachines", "machineLevels", "relics", "villageFacilities" };

    public static bool HasAny => _sections.Count > 0 || _lists.Count > 0 || _heroLevels != null || _events != null || _dungeonLevels != null;

    /// <summary>
    /// balance.json を取り込む。schemaVersion 不一致/解析失敗時は -1（不採用）。
    /// </summary>
    public static int OverrideFromBundle(string json)
    {
        if (string.IsNullOrEmpty(json)) return -1;

        JObject root;
        try { root = JObject.Parse(json); }
        catch (Exception e) { Debug.LogError($"[RemoteBalance] 解析失敗: {e.Message}"); return -1; }

        int schema = root.Value<int?>("schemaVersion") ?? -1;
        if (schema != ExpectedSchemaVersion)
        {
            Debug.LogWarning($"[RemoteBalance] schemaVersion 不一致 (expected {ExpectedSchemaVersion}, got {schema})。不採用。");
            return -1;
        }
        int version = root.Value<int?>("version") ?? 0;

        NormalizeStrings(root);

        _sections.Clear();
        _lists.Clear();
        _clones.Clear();
        _heroLevels = null;
        _events = null;
        _dungeonLevels = null;

        // 単一区画
        foreach (var key in SingleSections)
        {
            if (root[key] is JObject obj)
                _sections[key] = obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        // リスト区画（全て "id" 項目で突合。id は実SOフィールドではないので FromJsonOverwrite では無視される）
        foreach (var section in ListSections)
        {
            if (root[section] is not JArray arr) continue;
            var map = new Dictionary<string, string>();
            foreach (var el in arr)
            {
                if (el is not JObject eo) continue;
                var idTok = eo["id"];
                if (idTok == null) continue;
                map[idTok.ToString()] = eo.ToString(Newtonsoft.Json.Formatting.None);
            }
            if (map.Count > 0) _lists[section] = map;
        }

        // heroLevels（全置換用）
        if (root["heroLevels"] is JArray hl && hl.Count > 0)
        {
            var list = new List<HeroLevelData>();
            foreach (var el in hl)
            {
                var data = JsonUtility.FromJson<HeroLevelData>(el.ToString());
                if (data != null) list.Add(data);
            }
            if (list.Count > 0) _heroLevels = list;
        }

        // events（全置換用）
        // 行形式: { id, title, description, command1, param1Key1, param1Value1, command2, ... }
        // TomsEventCommand.parameters が Dictionary のため JsonUtility ではなく JObject から手動変換する
        if (root["events"] is JArray ev && ev.Count > 0)
        {
            var list = new List<TomsEvent>();
            foreach (var el in ev)
            {
                if (el is not JObject eo) continue;

                string id = eo.Value<string>("id");
                string title = eo.Value<string>("title");
                // 空行（idのみ・タイトル無し）はプールに入れない
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) continue;

                var e = new TomsEvent
                {
                    id = id.Trim(),
                    title = title.Trim(),
                    description = eo.Value<string>("description") ?? "",
                    commands = new List<TomsEventCommand>()
                };

                for (int n = 1; n <= 10; n++)
                {
                    string cmdName = eo.Value<string>($"command{n}");
                    if (string.IsNullOrWhiteSpace(cmdName)) break;

                    var cmd = new TomsEventCommand { command = cmdName.Trim() };
                    string key = eo.Value<string>($"param{n}Key1");
                    string val = eo.Value<string>($"param{n}Value1");
                    if (!string.IsNullOrWhiteSpace(key))
                        cmd.parameters[key.Trim()] = val ?? "";

                    e.commands.Add(cmd);
                }

                list.Add(e);
            }
            if (list.Count > 0) _events = list;
        }

        // dungeonLevels（行ごと。不正な行は読み飛ばす）
        _dungeonLevels = ParseDungeonLevels(root["dungeonLevels"]);

        Debug.Log($"[RemoteBalance] version {version} を適用（single={_sections.Count}, list={_lists.Count}, heroLevels={(_heroLevels?.Count ?? 0)}, events={(_events?.Count ?? 0)}, dungeonLevels={(_dungeonLevels?.Count ?? 0)}）");
        return version;
    }

    /// <summary>
    /// 配信の文字列を整える（その場で書き換える）。
    /// - シートに「\n」（バックスラッシュ＋n の2文字）と書いた改行は、JSON では "\\n" のまま届き、画面に「\n」が見える
    ///   （ダンジョン説明。2026-10-20 時点の balance.json）。本物の改行にする。
    /// - ゼロ幅スペース（U+200B）を取り除く（イベント本文に貼り付けの名残が混ざっていた）。
    /// GAS 側（Docs/gas/GameConstGas.gs の cleanText_）でも同じ整形をするが、古い配信値・キャッシュのためにこちらでも行う。
    /// </summary>
    private static void NormalizeStrings(JToken token)
    {
        switch (token)
        {
            case JValue v when v.Type == JTokenType.String:
                var s = (string)v.Value;
                if (!string.IsNullOrEmpty(s) && (s.Contains("\\n") || s.Contains("\u200B")))
                    v.Value = s.Replace("\\n", "\n").Replace("\u200B", "");
                break;
            case JContainer c:
                foreach (var child in c.Children()) NormalizeStrings(child);
                break;
        }
    }

    /// <summary>単一設定SOへ上書き。区画が無ければ baked をそのまま返す。</summary>
    public static T ApplyOverwrite<T>(string key, T baked) where T : ScriptableObject
    {
        if (baked == null) return baked;
        if (!_sections.TryGetValue(key, out var raw) || string.IsNullOrEmpty(raw)) return baked;

        return _clones.GetOrCreate(key, baked, clone =>
        {
            JsonUtility.FromJsonOverwrite(raw, clone); // 区画に無いフィールド＝baked保持、Sprite等も保持
            return true;
        });
    }

    /// <summary>
    /// リストSOへ id 突合で部分上書き。一致要素のみ Instantiate クローンへ上書き、非一致は元参照のまま。
    /// </summary>
    public static List<T> ApplyList<T>(string section, List<T> baked, Func<T, string> idSelector) where T : ScriptableObject
    {
        if (baked == null) return baked;
        if (!_lists.TryGetValue(section, out var map) || map.Count == 0) return baked;

        var result = new List<T>(baked.Count);
        foreach (var item in baked)
        {
            if (item == null) continue;
            var id = idSelector(item);
            if (!string.IsNullOrEmpty(id) && map.TryGetValue(id, out var raw))
            {
                result.Add(_clones.GetOrCreate(section, item, clone =>
                {
                    JsonUtility.FromJsonOverwrite(raw, clone);
                    // 突合キー（ダンジョンの key など）まで書き換わると、ゲーム側の検索で見つからなくなる。
                    // その要素は配信値を諦めてベイク済みを使う
                    if (idSelector(clone) == id) return true;
                    Debug.LogWarning($"[RemoteBalance] '{section}' の '{id}' が配信で識別キーを '{idSelector(clone)}' に変えようとしたため、この要素の配信値は使いません。");
                    return false;
                }));
            }
            else
            {
                result.Add(item);
            }
        }
        return result;
    }

    /// <summary>HeroLevelData は純スカラー。配信があれば全置換、無ければ baked。</summary>
    public static List<HeroLevelData> ApplyHeroLevels(List<HeroLevelData> baked)
        => _heroLevels != null ? new List<HeroLevelData>(_heroLevels) : baked;

    /// <summary>イベントマスター。配信があれば全置換、無ければ baked（CSV由来）。</summary>
    public static List<TomsEvent> ApplyEvents(List<TomsEvent> baked)
        => _events != null ? new List<TomsEvent>(_events) : baked;

    // ---- dungeonLevels（シート dungeonLevels。魔王軍支援の費用と防衛の配当） ----

    /// <summary>dungeonLevels の 1 行。dungeonId "*" は全ダンジョン共通。値が無い項目は null（同梱の値のまま）。</summary>
    public readonly struct DungeonLevelRow
    {
        public readonly string DungeonId;
        public readonly int Level;
        public readonly int? LevelUpCost;
        public readonly int? RewardGold;

        public DungeonLevelRow(string dungeonId, int level, int? levelUpCost, int? rewardGold)
        {
            DungeonId = dungeonId;
            Level = level;
            LevelUpCost = levelUpCost;
            RewardGold = rewardGold;
        }
    }

    public const string AllDungeons = "*";

    private static List<DungeonLevelRow> ParseDungeonLevels(JToken token)
    {
        if (token is not JArray arr || arr.Count == 0) return null;
        var list = new List<DungeonLevelRow>();
        foreach (var el in arr)
        {
            if (el is not JObject eo) continue;
            string id = (eo.Value<string>("dungeonId") ?? eo.Value<string>("id"))?.Trim();
            int? level = ReadNonNegativeInt(eo["level"]);
            if (string.IsNullOrEmpty(id) || level == null || level < 1)
            {
                Debug.LogWarning($"[RemoteBalance] dungeonLevels の行を読み飛ばします（dungeonId / level が不正）: {eo.ToString(Newtonsoft.Json.Formatting.None)}");
                continue;
            }
            var cost = ReadNonNegativeInt(eo["levelUpCost"]);
            var reward = ReadNonNegativeInt(eo["rewardGold"]);
            if (cost == null && reward == null) continue;
            list.Add(new DungeonLevelRow(id, level.Value, cost, reward));
        }
        return list.Count > 0 ? list : null;
    }

    // 整数（0 以上）として読めれば値、空・文字・負・小数は null
    private static int? ReadNonNegativeInt(JToken t)
    {
        if (t == null || t.Type == JTokenType.Null) return null;
        if (t.Type == JTokenType.Integer)
        {
            long v = t.Value<long>();
            return v >= 0 && v <= int.MaxValue ? (int)v : null;
        }
        if (t.Type == JTokenType.Float)
        {
            double d = t.Value<double>();
            return d >= 0 && d <= int.MaxValue && Math.Abs(d - Math.Round(d)) < 1e-9 ? (int)Math.Round(d) : null;
        }
        if (t.Type == JTokenType.String && int.TryParse(((string)t).Trim(), out var s) && s >= 0) return s;
        return null;
    }

    /// <summary>
    /// ダンジョンの levelUpCost / rewardGold を dungeonLevels で上書きする（"dungeons" 区画を当てた後に呼ぶ）。
    /// 項目ごとに「そのダンジョンの行 → "*" の行 → 同梱の値」の順で決める。当てる行が無いダンジョンは元のまま。
    /// 敵・フェーズ・ボス名などほかの項目は触らない（アセットのまま）。
    /// </summary>
    public static List<DungeonInfoScriptableObj> ApplyDungeonLevels(List<DungeonInfoScriptableObj> baked)
    {
        if (baked == null || _dungeonLevels == null) return baked;

        var result = new List<DungeonInfoScriptableObj>(baked.Count);
        foreach (var item in baked)
        {
            if (item == null) continue;
            string id = item.key.ToString();
            if (!_dungeonLevels.Exists(r => r.DungeonId == AllDungeons || r.DungeonId == id))
            {
                result.Add(item);
                continue;
            }
            result.Add(_clones.GetOrCreate("dungeonLevels", item, clone =>
            {
                ApplyDungeonLevelRows(clone, id);
                return true;
            }));
        }
        return result;
    }

    private static void ApplyDungeonLevelRows(DungeonInfoScriptableObj so, string id)
    {
        if (so.levelDataList == null) return;
        for (int i = 0; i < so.levelDataList.Length; i++)
        {
            var data = so.levelDataList[i];
            if (data == null) continue;
            int level = i + 1;
            int? cost = null, reward = null, sharedCost = null, sharedReward = null;
            // 同じキーの行が 2 つあれば下の行
            foreach (var r in _dungeonLevels)
            {
                if (r.Level != level) continue;
                if (r.DungeonId == id)
                {
                    if (r.LevelUpCost != null) cost = r.LevelUpCost;
                    if (r.RewardGold != null) reward = r.RewardGold;
                }
                else if (r.DungeonId == AllDungeons)
                {
                    if (r.LevelUpCost != null) sharedCost = r.LevelUpCost;
                    if (r.RewardGold != null) sharedReward = r.RewardGold;
                }
            }
            data.levelUpCost = cost ?? sharedCost ?? data.levelUpCost;
            data.rewardGold = reward ?? sharedReward ?? data.rewardGold;
        }
    }

    public static void ResetToDefault()
    {
        _sections.Clear();
        _lists.Clear();
        _clones.Clear();
        _heroLevels = null;
        _events = null;
        _dungeonLevels = null;
    }
}
