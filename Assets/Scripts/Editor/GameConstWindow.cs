using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 配信中の GameConst 調整値(gameconst.json)を確認するエディタウィンドウ。
/// version / schemaVersion / updatedAt と GameConstData の各値を一覧表示する。
/// （GameConstSettings アセットはベイク済みデフォルト。実際の配信値はこのウィンドウで確認する）
/// </summary>
public class GameConstWindow : EditorWindow
{
    private const string UrlPrefKey = "TomsLands.GameConstUrl";
    private const string DefaultUrl =
        "https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/gameconst.json";

    private string _url;
    private GameConstEnvelope _envelope;
    private string _status = "「サーバーから取得」または「キャッシュ読込」を押してください。";
    private MessageType _statusType = MessageType.Info;
    private Vector2 _scroll;

    private UnityWebRequest _req;
    private bool _fetching;

    [MenuItem("Tools/TomsLands/リモート設定/GameConst確認ウィンドウ")]
    public static void Open()
    {
        var w = GetWindow<GameConstWindow>("GameConst");
        w.minSize = new Vector2(520, 460);
        w.Show();
    }

    private void OnEnable() => _url = EditorPrefs.GetString(UrlPrefKey, DefaultUrl);
    private void OnDisable() => StopPoll();

    private void OnGUI()
    {
        EditorGUILayout.LabelField("配信URL（gameconst.json）", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _url = EditorGUILayout.TextField(_url);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(UrlPrefKey, _url);

        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledScope(_fetching))
        {
            if (GUILayout.Button("サーバーから取得")) StartFetch();
            if (GUILayout.Button("キャッシュ読込")) LoadFromCache();
            if (GUILayout.Button("URLを既定に戻す")) { _url = DefaultUrl; EditorPrefs.SetString(UrlPrefKey, _url); }
        }

        EditorGUILayout.HelpBox(_status, _statusType);
        EditorGUILayout.Space();

        if (_envelope == null) return;

        // --- メタ情報 ---
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField($"version : {_envelope.version}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"schemaVersion : {_envelope.schemaVersion}" +
                (_envelope.schemaVersion != GameConst.ExpectedSchemaVersion ? "  ⚠ 期待値と不一致(適用されません)" : ""));
            EditorGUILayout.LabelField($"updatedAt : {_envelope.updatedAt}");
        }

        EditorGUILayout.Space();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawData(_envelope.data);
        EditorGUILayout.EndScrollView();
    }

    private void DrawData(GameConstData d)
    {
        if (d == null)
        {
            EditorGUILayout.HelpBox("data がありません。", MessageType.Warning);
            return;
        }

        Section("上限値", () =>
        {
            Row("maxDungeonLevel", d.maxDungeonLevel);
            Row("maxBlackSmithLevel", d.maxBlackSmithLevel);
            Row("maxToolShopLevel", d.maxToolShopLevel);
            Row("maxInfoBrokerLevel", d.maxInfoBrokerLevel);
            Row("maxItemStock", d.maxItemStock);
            Row("minItemStock", d.minItemStock);
        });

        Section("所持金", () => Row("initMoney", $"{d.initMoney:N0}G"));

        Section("税金", () =>
        {
            Row("debtPaymentInterval", d.debtPaymentInterval);
            Row("debtBaseAmount", $"{d.debtBaseAmount:N0}G");
            Row("debtMultiplier", d.debtMultiplier);
        });

        Section("ヒーロー経験値", () =>
        {
            Row("heroExpPerMob", d.heroExpPerMob);
            Row("heroExpPerBoss", d.heroExpPerBoss);
            Row("heroBaseExpToNextLevel", d.heroBaseExpToNextLevel);
        });

        Section("鍛冶屋レベルアップコスト", () =>
        {
            string costs = d.blackSmithLevelUpCosts != null
                ? string.Join(", ", d.blackSmithLevelUpCosts)
                : "(なし)";
            Row("blackSmithLevelUpCosts", costs);
        });

        // flowGeneration は現フラット配信には含まれない（ベイク済み値が使われる）
        Section("フロー自動生成 (flowGeneration)", () =>
        {
            if (d.flowGeneration == null)
            {
                EditorGUILayout.LabelField("（配信に含まれず：ベイク済みデフォルトを使用）", EditorStyles.miniLabel);
                return;
            }
            var f = d.flowGeneration;
            Row("useAutoGeneration", f.useAutoGeneration);
            Row("randomSeed", f.randomSeed);
            Row("earlyTargetDifficulty", f.earlyTargetDifficulty);
            Row("lateTargetDifficulty", f.lateTargetDifficulty);
            Row("difficultyBias", f.difficultyBias);

            if (f.modes != null)
            {
                EditorGUILayout.LabelField("modes:", EditorStyles.boldLabel);
                foreach (var m in f.modes)
                {
                    if (m == null) continue;
                    EditorGUILayout.LabelField($"  {m.mode}: dungeonCount={m.dungeonCount}, shop={m.minShopTurnsBetweenBattles}-{m.maxShopTurnsBetweenBattles}, eventRate={m.eventRate}");
                }
            }
            if (f.dungeonWeights != null && f.dungeonWeights.Length > 0)
            {
                EditorGUILayout.LabelField("dungeonWeights:", EditorStyles.boldLabel);
                foreach (var w in f.dungeonWeights)
                {
                    if (w == null) continue;
                    EditorGUILayout.LabelField($"  {w.dungeon}: {w.weight}");
                }
            }
        });
    }

    private static void Section(string title, Action body)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            body();
        }
        EditorGUILayout.Space(2);
    }

    private static void Row(string label, object value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(220));
            EditorGUILayout.LabelField(value != null ? value.ToString() : "");
        }
    }

    // ---- 取得 ----
    private void StartFetch()
    {
        StopPoll();
        SetStatus("取得中...", MessageType.Info);
        _req = UnityWebRequest.Get(_url);
        _req.SendWebRequest();
        _fetching = true;
        EditorApplication.update += PollFetch;
    }

    private void PollFetch()
    {
        if (_req == null) { StopPoll(); return; }
        if (!_req.isDone) return;

        if (_req.result == UnityWebRequest.Result.Success)
            ParseJson(_req.downloadHandler.text, "サーバー取得成功", MessageType.Info);
        else
            SetStatus($"取得失敗: {_req.result} / {_req.error}", MessageType.Error);

        StopPoll();
        Repaint();
    }

    private void StopPoll()
    {
        EditorApplication.update -= PollFetch;
        if (_req != null) { _req.Dispose(); _req = null; }
        _fetching = false;
    }

    private void LoadFromCache()
    {
        var path = Path.Combine(Application.persistentDataPath, "remoteconfig", "gameconst.json");
        if (!File.Exists(path))
        {
            _envelope = null;
            SetStatus($"キャッシュがありません:\n{path}", MessageType.Warning);
            return;
        }
        try { ParseJson(File.ReadAllText(path), $"キャッシュ読込:\n{path}", MessageType.Info); }
        catch (Exception e) { SetStatus($"キャッシュ読込失敗: {e.Message}", MessageType.Error); }
        Repaint();
    }

    private void ParseJson(string json, string okMsg, MessageType okType)
    {
        try
        {
            _envelope = JsonUtility.FromJson<GameConstEnvelope>(json);
            if (_envelope == null) { SetStatus("解析結果が空です。", MessageType.Warning); return; }
            SetStatus(okMsg, okType);
        }
        catch (Exception e)
        {
            _envelope = null;
            SetStatus($"解析失敗: {e.Message}", MessageType.Error);
        }
    }

    private void SetStatus(string msg, MessageType type) { _status = msg; _statusType = type; }
}
