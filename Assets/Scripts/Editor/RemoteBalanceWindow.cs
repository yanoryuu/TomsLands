using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 結合配信 balance.json を確認するエディタウィンドウ。
/// version / schemaVersion / updatedAt と各区画（単一設定・リストマスター）を表示する。
/// </summary>
public class RemoteBalanceWindow : EditorWindow
{
    private const string UrlPrefKey = "TomsLands.BalanceUrl";
    private const string DefaultUrl =
        "https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/balance.json";

    private string _url;
    private JObject _root;
    private string _status = "「サーバーから取得」または「キャッシュ読込」を押してください。";
    private MessageType _statusType = MessageType.Info;
    private Vector2 _scroll;
    private readonly Dictionary<string, bool> _foldouts = new();

    private UnityWebRequest _req;
    private bool _fetching;

    [MenuItem("Tools/Balance/リモート確認ウィンドウ")]
    public static void Open()
    {
        var w = GetWindow<RemoteBalanceWindow>("Balance");
        w.minSize = new Vector2(540, 480);
        w.Show();
    }

    private void OnEnable() => _url = EditorPrefs.GetString(UrlPrefKey, DefaultUrl);
    private void OnDisable() => StopPoll();

    private void OnGUI()
    {
        EditorGUILayout.LabelField("配信URL（balance.json）", EditorStyles.boldLabel);
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

        if (_root == null) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            int schema = _root.Value<int?>("schemaVersion") ?? -1;
            EditorGUILayout.LabelField($"version : {_root.Value<int?>("version") ?? 0}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"schemaVersion : {schema}" +
                (schema != RemoteBalance.ExpectedSchemaVersion ? "  ⚠ 期待値と不一致(適用されません)" : ""));
            EditorGUILayout.LabelField($"updatedAt : {_root.Value<string>("updatedAt")}");
        }

        EditorGUILayout.Space();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var prop in _root.Properties())
        {
            if (prop.Name == "version" || prop.Name == "schemaVersion" || prop.Name == "updatedAt") continue;
            DrawSection(prop.Name, prop.Value);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawSection(string name, JToken token)
    {
        if (!_foldouts.ContainsKey(name)) _foldouts[name] = false;

        string summary = token is JArray arr ? $"（{arr.Count}件）"
                       : token is JObject obj ? $"（{obj.Count}項目）" : "";
        _foldouts[name] = EditorGUILayout.Foldout(_foldouts[name], $"{name} {summary}", true);
        if (!_foldouts[name]) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            // 内容の高さに合わせて伸ばす（MaxHeightで頭打ちにせず、外側のScrollViewで全文スクロール）
            var content = new GUIContent(token.ToString(Newtonsoft.Json.Formatting.Indented));
            float width = Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 40f);
            float height = EditorStyles.textArea.CalcHeight(content, width);
            EditorGUILayout.SelectableLabel(content.text, EditorStyles.textArea,
                GUILayout.Height(height), GUILayout.ExpandHeight(false));
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
        var path = Path.Combine(Application.persistentDataPath, "remoteconfig", "balance.json");
        if (!File.Exists(path))
        {
            _root = null;
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
            _root = JObject.Parse(json);
            SetStatus(okMsg, okType);
        }
        catch (Exception e)
        {
            _root = null;
            SetStatus($"解析失敗: {e.Message}", MessageType.Error);
        }
    }

    private void SetStatus(string msg, MessageType type) { _status = msg; _statusType = type; }
}
