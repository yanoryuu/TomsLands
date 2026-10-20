using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// ビルド前チェック（2026-10-20）。「エディタでは動くがビルドで壊れる」を出荷前に機械で止める。
///   - Addressables のビルドがあるか／素材より古くないか（プレイヤービルドと一緒にビルドする設定か）
///   - コードが読むシーンが Build Settings に入っているか
///   - 製品ビルドで読む approved の記事テンプレート・埋め草の数（0 本だと旧形式の紙面に落ちる）
///   - ダンジョン SO が FightScene / TomsShop の一覧に入っているか（エディタだけ自動で補われるため）
///   - 同梱（GameConstSettings.asset・EventDatas.csv）と配信用データ（Docs/balance_upload）の差分
/// メニュー: Tools/TomsLands/ビルド前チェック。ビルド時にも自動で走り、エラーがあればビルドを止める。
/// 同梱と配信値の差分だけを見たいときは Tools/TomsLands/リモート設定/同梱と配信値の差分。
/// </summary>
public sealed class BuildPreflight : IPreprocessBuildWithReport
{
    private const string MenuRoot = "Tools/TomsLands/";

    /// <summary>コードに名前は残っているが、使わないと決めたシーン（無くてもエラーにしない）。</summary>
    private static readonly Dictionary<string, string> UnusedScenes = new()
    {
        { "EventScene", "PendingEventData.UseInlineEventPopup が常に true なので遷移しない" },
        { "ItemPopupScene", "ItemPopUpManager.Show を呼ぶ所が無い（2026-10-20 に Build Settings から外した）" },
    };

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        bool dev = (report.summary.options & BuildOptions.Development) != 0;
        var r = Run(productBuild: !dev);
        Debug.Log(r.ToText("ビルド前チェック（ビルド時）"));
        if (r.Errors.Count > 0)
            throw new BuildFailedException($"ビルド前チェックでエラー {r.Errors.Count} 件: {string.Join(" / ", r.Errors)}");
    }

    [MenuItem(MenuRoot + "ビルド前チェック", priority = 50)]
    public static void RunFromMenu()
    {
        var r = Run(productBuild: true);
        string text = r.ToText("ビルド前チェック（製品ビルドとして）");
        if (r.Errors.Count > 0) Debug.LogError(text); else if (r.Warnings.Count > 0) Debug.LogWarning(text); else Debug.Log(text);
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog("ビルド前チェック",
                $"エラー {r.Errors.Count} 件 / 注意 {r.Warnings.Count} 件\n詳しくはコンソールを見てください。", "OK");
    }

    [MenuItem(MenuRoot + "リモート設定/同梱と配信値の差分", priority = 60)]
    public static void DiffFromMenu()
    {
        var r = new Result();
        CheckBundledVsUpload(r);
        string text = r.ToText("同梱と配信値（Docs/balance_upload）の差分");
        if (r.Warnings.Count > 0 || r.Errors.Count > 0) Debug.LogWarning(text); else Debug.Log(text);
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog("同梱と配信値の差分",
                r.Warnings.Count == 0 ? "差分はありません。" : $"差分 {r.Warnings.Count} 件。詳しくはコンソールを見てください。", "OK");
    }

    /// <summary>バッチモード用（-executeMethod BuildPreflight.RunBatch）。エラーがあれば終了コード 1。</summary>
    public static void RunBatch()
    {
        var r = Run(productBuild: true);
        Debug.Log(r.ToText("ビルド前チェック（バッチ）"));
        EditorApplication.Exit(r.Errors.Count > 0 ? 1 : 0);
    }

    public sealed class Result
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public readonly List<string> Infos = new();

        public string ToText(string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[BuildPreflight] {title}: エラー {Errors.Count} / 注意 {Warnings.Count}");
            foreach (var e in Errors) sb.AppendLine("  ✕ " + e);
            foreach (var w in Warnings) sb.AppendLine("  △ " + w);
            foreach (var i in Infos) sb.AppendLine("  ・ " + i);
            return sb.ToString();
        }
    }

    public static Result Run(bool productBuild)
    {
        var r = new Result();
        Guard(r, "Addressables", () => CheckAddressables(r));
        Guard(r, "シーン", () => CheckScenes(r));
        Guard(r, "記事", () => CheckNews(r, productBuild));
        Guard(r, "ダンジョン", () => CheckDungeonLists(r));
        Guard(r, "同梱と配信値", () => CheckBundledVsUpload(r));
        return r;
    }

    private static void Guard(Result r, string label, Action check)
    {
        try { check(); }
        catch (Exception e) { r.Errors.Add($"{label} のチェック中に例外: {e.Message}"); }
    }

    // ---------------------------------------------------------------
    // Addressables
    // ---------------------------------------------------------------

    private static void CheckAddressables(Result r)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { r.Errors.Add("AddressableAssetSettings が見つからない"); return; }

        bool buildsWithPlayer = settings.BuildAddressablesWithPlayerBuild == AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
        r.Infos.Add($"Addressables をプレイヤービルドと一緒にビルド: {settings.BuildAddressablesWithPlayerBuild}");

        string buildPath = UnityEngine.AddressableAssets.Addressables.BuildPath;
        string catalog = Directory.Exists(buildPath)
            ? Directory.GetFiles(buildPath, "catalog*.*", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (catalog == null)
        {
            if (buildsWithPlayer) r.Infos.Add("Addressables のビルドはまだ無い（プレイヤービルド時に作られる）");
            else r.Errors.Add($"Addressables のビルドが無い（{buildPath}）。Window/Asset Management/Addressables/Groups で Build してください");
            return;
        }

        DateTime built = File.GetLastWriteTimeUtc(catalog);
        string newest = null;
        DateTime newestTime = DateTime.MinValue;
        foreach (var f in Directory.GetFiles(Path.Combine(Application.dataPath, "Resources_moved"), "*", SearchOption.AllDirectories))
        {
            if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            var t = File.GetLastWriteTimeUtc(f);
            if (t > newestTime) { newestTime = t; newest = f; }
        }
        if (newest != null && newestTime > built)
        {
            string msg = $"Addressables のビルド（{built.ToLocalTime():MM/dd HH:mm}）が素材より古い（最新: {Path.GetFileName(newest)} {newestTime.ToLocalTime():MM/dd HH:mm}）";
            if (buildsWithPlayer) r.Infos.Add(msg + "。プレイヤービルド時に作り直される");
            else r.Errors.Add(msg + "。Addressables を Build し直してください");
        }
    }

    // ---------------------------------------------------------------
    // シーン
    // ---------------------------------------------------------------

    private static readonly Regex[] SceneNamePatterns =
    {
        new(@"LoadScene(?:Async)?\(\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled),
        new(@"SceneLoader\.Load\(\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled),
        new(@"const\s+string\s+\w*Scene\w*\s*=\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled),
    };

    private static void CheckScenes(Result r)
    {
        var inBuild = new HashSet<string>(EditorBuildSettings.scenes.Where(s => s.enabled)
            .Select(s => Path.GetFileNameWithoutExtension(s.path)));
        foreach (var s in EditorBuildSettings.scenes.Where(s => s.enabled))
            if (!File.Exists(s.path)) r.Errors.Add($"Build Settings のシーンが無い: {s.path}");

        var referenced = new Dictionary<string, string>();
        string scripts = Path.Combine(Application.dataPath, "Scripts");
        foreach (var file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
        {
            string rel = file.Replace('\\', '/');
            if (rel.Contains("/Editor/")) continue;
            string text = File.ReadAllText(file);
            foreach (var re in SceneNamePatterns)
                foreach (Match m in re.Matches(text))
                    referenced.TryAdd(m.Groups[1].Value, Path.GetFileName(file));
        }

        foreach (var kv in referenced.OrderBy(k => k.Key))
        {
            if (inBuild.Contains(kv.Key)) continue;
            if (UnusedScenes.TryGetValue(kv.Key, out var why))
                r.Infos.Add($"シーン {kv.Key} は Build Settings に無い（未使用: {why}）");
            else
                r.Errors.Add($"コードが読むシーン {kv.Key}（{kv.Value}）が Build Settings に無い／無効");
        }
        r.Infos.Add($"Build Settings のシーン {inBuild.Count} 件、コードが名前で読むシーン {referenced.Count} 件");
    }

    // ---------------------------------------------------------------
    // 記事（Docs/News_Spec.md の status）
    // ---------------------------------------------------------------

    private static void CheckNews(Result r, bool productBuild)
    {
        var (tplApproved, tplTotal, approvedEvents) = CountApproved("NewsTemplates.csv", "eventId");
        var (fillApproved, fillTotal, _) = CountApproved("NewsFillers.csv", "fillerId");
        var allEvents = ReadColumn("NewsEvents.csv", "eventId");
        int eventsWithReport = allEvents.Count(e => approvedEvents.Contains(e));

        r.Infos.Add($"記事テンプレート approved {tplApproved}/{tplTotal}、埋め草 approved {fillApproved}/{fillTotal}、" +
                    $"製品ビルドで出る事象 {eventsWithReport}/{allEvents.Count}（approved の報道がある事象だけ）");
        if (!productBuild) return; // 開発ビルドは draft も読む
        if (tplApproved == 0)
            r.Errors.Add("approved の記事テンプレートが0本。製品ビルドの新聞が旧形式に落ちる（実行時は draft を読む保険あり）");
        if (fillApproved == 0)
            r.Warnings.Add("approved の埋め草が0本（実行時は draft を読む保険あり）");
    }

    private static (int approved, int total, HashSet<string> approvedReportEvents) CountApproved(string file, string keyCol)
    {
        var rows = ReadTsv(file);
        int approved = 0, total = 0;
        var events = new HashSet<string>();
        foreach (var row in rows)
        {
            if (!row.TryGetValue(keyCol, out var key) || string.IsNullOrEmpty(key)) continue;
            total++;
            row.TryGetValue("status", out var status);
            if (!string.IsNullOrEmpty(status) && status != "approved") continue;
            approved++;
            row.TryGetValue("kind", out var kind);
            if (string.IsNullOrEmpty(kind) || kind.Equals("report", StringComparison.OrdinalIgnoreCase)) events.Add(key);
        }
        return (approved, total, events);
    }

    private static List<string> ReadColumn(string file, string col) =>
        ReadTsv(file).Select(row => row.TryGetValue(col, out var v) ? v : null).Where(v => !string.IsNullOrEmpty(v)).ToList();

    private static List<Dictionary<string, string>> ReadTsv(string file)
    {
        var list = new List<Dictionary<string, string>>();
        string path = Path.Combine(Application.dataPath, "Resources_moved", file);
        if (!File.Exists(path)) return list;
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) return list;
        var head = lines[0].TrimStart('﻿').Split('\t');
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cols = lines[i].Split('\t');
            var row = new Dictionary<string, string>();
            for (int c = 0; c < head.Length && c < cols.Length; c++) row[head[c].Trim()] = cols[c].Trim();
            list.Add(row);
        }
        return list;
    }

    // ---------------------------------------------------------------
    // ダンジョン SO の一覧（BattleLifetimeScope はエディタでだけ足りない分を補う）
    // ---------------------------------------------------------------

    private static void CheckDungeonLists(Result r)
    {
        string fight = File.ReadAllText("Assets/Scene/FightScene.unity");
        string shop = File.ReadAllText("Assets/Scene/TomsShop.unity");
        foreach (var guid in AssetDatabase.FindAssets("t:DungeonInfoScriptableObj"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!fight.Contains(guid)) r.Errors.Add($"FightScene の BattleLifetimeScope.dungeonInfos に {Path.GetFileName(path)} が無い（ビルドでだけその配信が壊れる）");
            if (!shop.Contains(guid)) r.Errors.Add($"TomsShop の DungeonRepository.dungeonInfos に {Path.GetFileName(path)} が無い");
        }
    }

    // ---------------------------------------------------------------
    // 同梱と配信用データ（Docs/balance_upload）の差分
    // ---------------------------------------------------------------

    private static string UploadFolder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Docs", "balance_upload");

    private static void CheckBundledVsUpload(Result r)
    {
        int before = r.Warnings.Count;
        DiffGameConst(r);
        DiffEvents(r);
        int n = r.Warnings.Count - before;
        r.Infos.Add(n == 0
            ? "同梱と配信用データ（Docs/balance_upload）の差分なし（gameconst・events）"
            : $"同梱と配信用データの差分 {n} 件。どちらが正しいか決めて片方を直す（本番への反映は GAS からアップロード）");
    }

    private static void DiffGameConst(Result r)
    {
        string path = Path.Combine(UploadFolder, "gameconst.json");
        var settings = AssetDatabase.LoadAssetAtPath<GameConstSettings>("Assets/Resources_moved/GameConstSettings.asset");
        if (settings == null || !File.Exists(path)) { r.Warnings.Add("gameconst: 同梱アセットか gameconst.json が見つからない"); return; }

        var upload = JObject.Parse(File.ReadAllText(path))["data"] as JObject;
        var bundled = JObject.Parse(JsonUtility.ToJson(settings.data));
        if (upload == null) { r.Warnings.Add("gameconst.json に data が無い"); return; }

        // 配信に載っている項目だけ比べる（配信に無い項目は同梱の値がそのまま使われる）
        foreach (var prop in upload.Properties())
        {
            var b = bundled[prop.Name];
            if (b == null) { r.Warnings.Add($"gameconst.{prop.Name}: 配信にあるが同梱（GameConstData）に無い項目"); continue; }
            if (!SameValue(b, prop.Value))
                r.Warnings.Add($"gameconst.{prop.Name}: 同梱 {b.ToString(Newtonsoft.Json.Formatting.None)} / 配信 {prop.Value.ToString(Newtonsoft.Json.Formatting.None)}");
        }
    }

    private static void DiffEvents(Result r)
    {
        string path = Path.Combine(UploadFolder, "balance.json");
        if (!File.Exists(path)) return;
        if (!(JObject.Parse(File.ReadAllText(path))["events"] is JArray upload)) return;

        var text = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources_moved/EventDatas.csv");
        if (text == null) { r.Warnings.Add("events: EventDatas.csv が見つからない"); return; }
        var bundled = new Dictionary<string, string[]>();
        foreach (var line in text.text.Split('\n').Skip(1))
        {
            var cols = line.Trim().Split(',');
            if (cols.Length >= 6 && !string.IsNullOrWhiteSpace(cols[0])) bundled[cols[0].Trim()] = cols;
        }

        foreach (var el in upload.OfType<JObject>())
        {
            string id = el.Value<string>("id");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(el.Value<string>("title"))) continue;
            if (!bundled.TryGetValue(id, out var c)) { r.Warnings.Add($"events.{id}: 配信にあるが同梱に無い"); continue; }
            string cmd = el.Value<string>("command1") ?? "", val = el.Value<string>("param1Value1") ?? "";
            if (c[3].Trim() != cmd || c[5].Trim() != val)
                r.Warnings.Add($"events.{id}: 同梱 {c[3].Trim()} {c[5].Trim()} / 配信 {cmd} {val}");
            string desc = (el.Value<string>("description") ?? "").Replace("\u200B", "");
            if (c[2].Trim().Replace("\u200B", "") != desc.Trim()) r.Warnings.Add($"events.{id}: 本文が違う");
        }
    }

    private static bool SameValue(JToken a, JToken b)
    {
        if (a.Type is JTokenType.Integer or JTokenType.Float && b.Type is JTokenType.Integer or JTokenType.Float)
            return Math.Abs(a.Value<double>() - b.Value<double>()) < 1e-6;
        if (a is JArray aa && b is JArray ba)
            return aa.Count == ba.Count && aa.Zip(ba, SameValue).All(x => x);
        if (a.Type == JTokenType.Boolean || b.Type == JTokenType.Boolean)
            return ToBool(a) == ToBool(b);
        return JToken.DeepEquals(a, b);
    }

    private static bool ToBool(JToken t) =>
        t.Type == JTokenType.Boolean ? t.Value<bool>() : t.Type == JTokenType.Integer && t.Value<long>() != 0;
}
