using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 配信中のアイテムマスター(items.json)を確認するエディタウィンドウ。
/// version / schemaVersion / updatedAt と各アイテムの上書き値を一覧表示する。
/// SOアセットは値を保持しない（サーバー/シート管理）ため、確認はこのウィンドウで行う。
/// </summary>
public class ItemMasterWindow : EditorWindow
{
    private const string UrlPrefKey = "TomsLands.ItemMasterUrl";
    private const string DefaultUrl =
        "https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/items.json";

    private string _url;
    private string _search = "";
    private ItemMasterEnvelope _envelope;
    private string _status = "「サーバーから取得」または「キャッシュ読込」を押してください。";
    private MessageType _statusType = MessageType.Info;
    private Vector2 _scroll;

    private UnityWebRequest _req;
    private bool _fetching;

    // itemId → アイコン（SOアセットから引く。items.jsonにスプライトは含まれないため）
    private Dictionary<string, Sprite> _iconLookup;

    [MenuItem("Tools/ItemMaster/Item一覧ウィンドウ")]
    public static void Open()
    {
        var w = GetWindow<ItemMasterWindow>("Item Master");
        w.minSize = new Vector2(520, 420);
        w.Show();
    }

    private void OnEnable()
    {
        _url = EditorPrefs.GetString(UrlPrefKey, DefaultUrl);
        BuildIconLookup();
    }
    private void OnDisable() => StopPoll();

    /// <summary>プロジェクト内の全 ItemData から itemId→アイコン の対応表を作る。</summary>
    private void BuildIconLookup()
    {
        _iconLookup = new Dictionary<string, Sprite>();
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (so != null && !string.IsNullOrEmpty(so.itemId) && !_iconLookup.ContainsKey(so.itemId))
                _iconLookup[so.itemId] = so.itemIcon;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("配信URL（items.json）", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _url = EditorGUILayout.TextField(_url);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(UrlPrefKey, _url);

        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledScope(_fetching))
        {
            if (GUILayout.Button("サーバーから取得")) StartFetch();
            if (GUILayout.Button("キャッシュ読込")) LoadFromCache();
            if (GUILayout.Button("URLを既定に戻す")) { _url = DefaultUrl; EditorPrefs.SetString(UrlPrefKey, _url); }
            if (GUILayout.Button("アイコン再読込")) BuildIconLookup();
        }

        EditorGUILayout.HelpBox(_status, _statusType);
        EditorGUILayout.Space();

        if (_envelope == null) return;

        // --- メタ情報 ---
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField($"version : {_envelope.version}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"schemaVersion : {_envelope.schemaVersion}" +
                (_envelope.schemaVersion != ItemMaster.ExpectedSchemaVersion ? "  ⚠ 期待値と不一致(適用されません)" : ""));
            EditorGUILayout.LabelField($"updatedAt : {_envelope.updatedAt}");
            int n = _envelope.items != null ? _envelope.items.Length : 0;
            EditorGUILayout.LabelField($"アイテム数 : {n}");
        }

        EditorGUILayout.Space();
        _search = EditorGUILayout.TextField("検索 (id/名前)", _search);
        EditorGUILayout.Space();

        // --- アイテム一覧 ---
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_envelope.items != null)
        {
            foreach (var it in _envelope.items)
            {
                if (it == null) continue;
                if (!string.IsNullOrEmpty(_search) && !Matches(it, _search)) continue;
                DrawItem(it);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static bool Matches(ItemOverride it, string q)
    {
        q = q.ToLowerInvariant();
        return (it.itemId != null && it.itemId.ToLowerInvariant().Contains(q))
            || (it.itemName != null && it.itemName.ToLowerInvariant().Contains(q));
    }

    private void DrawItem(ItemOverride it)
    {
        Sprite icon = null;
        if (_iconLookup != null && !string.IsNullOrEmpty(it.itemId))
            _iconLookup.TryGetValue(it.itemId, out icon);

        using (new EditorGUILayout.VerticalScope("box"))
        using (new EditorGUILayout.HorizontalScope())
        {
            // アイコン（SOから取得）
            Rect iconRect = GUILayoutUtility.GetRect(56, 56, GUILayout.Width(56), GUILayout.Height(56));
            if (icon != null) DrawSprite(iconRect, icon);
            else EditorGUI.LabelField(iconRect, "(no\nicon)", EditorStyles.centeredGreyMiniLabel);

            // テキスト情報
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField($"{it.itemId}    {it.itemName}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"価格 {it.basePrice:N0}G    在庫 {it.initialStock}/{it.maxStock}    陳列 {it.initialDisplayStock}");
                EditorGUILayout.LabelField($"種類 {it.itemType}    属性 {it.itemAttribute}    必要Lv {it.requiredLevel}    売れやすさ ×{it.salesRate}");
                if (!string.IsNullOrEmpty(it.description))
                    EditorGUILayout.LabelField(it.description, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }

    /// <summary>Spriteを正しいサブ矩形で矩形内に収めて描画する。</summary>
    private static void DrawSprite(Rect position, Sprite sprite)
    {
        if (sprite == null) return;
        var tex = sprite.texture;
        if (tex == null) return;

        var tr = sprite.textureRect;
        var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);

        // アスペクト比を保って収める
        float aspect = tr.width / Mathf.Max(1f, tr.height);
        var fit = position;
        if (aspect >= 1f) { fit.height = position.width / aspect; fit.y += (position.height - fit.height) * 0.5f; }
        else { fit.width = position.height * aspect; fit.x += (position.width - fit.width) * 0.5f; }

        GUI.DrawTextureWithTexCoords(fit, tex, uv);
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
        var path = Path.Combine(Application.persistentDataPath, "remoteconfig", "items.json");
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
            _envelope = JsonUtility.FromJson<ItemMasterEnvelope>(json);
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
