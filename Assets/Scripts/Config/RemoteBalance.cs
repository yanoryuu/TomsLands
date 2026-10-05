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

    private static readonly string[] SingleSections = { "shopEconomy", "gameBalance", "battlePrice", "shopLevel", "finance" };
    // ※ HeroData(SO) は Inspector 割当のフォールバックで、実バトル値は heroLevels(CSV由来) なので
    //   "heroes" 区画は設けない（hero バランスは heroLevels で配信する）。
    private static readonly string[] ListSections =
        { "advertisements", "buzzEffects", "followerMilestones", "enemies", "dungeons", "financialProducts", "shopMachines", "relics", "villageFacilities" };

    public static bool HasAny => _sections.Count > 0 || _lists.Count > 0 || _heroLevels != null || _events != null;

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

        _sections.Clear();
        _lists.Clear();
        _heroLevels = null;
        _events = null;

        // 単一区画
        foreach (var key in SingleSections)
        {
            if (root[key] is JObject obj)
                _sections[key] = obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        // リスト区画（全て "id" 項目で突合。id は実SOフィールドではないので FromJsonOverwrite では無視される）
        foreach (var section in ListSections)
        {
            if (!(root[section] is JArray arr)) continue;
            var map = new Dictionary<string, string>();
            foreach (var el in arr)
            {
                if (!(el is JObject eo)) continue;
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
                if (!(el is JObject eo)) continue;

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

        Debug.Log($"[RemoteBalance] version {version} を適用（single={_sections.Count}, list={_lists.Count}, heroLevels={(_heroLevels?.Count ?? 0)}, events={(_events?.Count ?? 0)}）");
        return version;
    }

    /// <summary>単一設定SOへ上書き。区画が無ければ baked をそのまま返す。</summary>
    public static T ApplyOverwrite<T>(string key, T baked) where T : ScriptableObject
    {
        if (baked == null) return baked;
        if (!_sections.TryGetValue(key, out var raw) || string.IsNullOrEmpty(raw)) return baked;

        var clone = UnityEngine.Object.Instantiate(baked);
        clone.name = baked.name;
        JsonUtility.FromJsonOverwrite(raw, clone); // 区画に無いフィールド＝baked保持、Sprite等も保持
        return clone;
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
                var clone = UnityEngine.Object.Instantiate(item);
                clone.name = item.name;
                JsonUtility.FromJsonOverwrite(raw, clone);
                result.Add(clone);
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

    public static void ResetToDefault()
    {
        _sections.Clear();
        _lists.Clear();
        _heroLevels = null;
        _events = null;
    }
}
