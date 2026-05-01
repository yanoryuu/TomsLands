using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Assets/Sounds フォルダをスキャンして SoundLibrary を自動設定する Editor ツール。
/// Tools > Sound > SoundLibrary 自動設定 から開く。
/// </summary>
public class SoundLibrarySetupTool : EditorWindow
{
    private const string BGMFolder          = "Assets/Sounds/BGM";
    private const string SEFolder           = "Assets/Sounds/SE";
    private const string DefaultLibraryPath = "Assets/Resources/SoundLibrary.asset";

    private SoundLibrary _library;
    private string       _libraryPath = DefaultLibraryPath;
    private Vector2      _scrollPos;

    // プレビュー用
    private List<(string key, string path, bool isBGM)> _preview;

    [MenuItem("Tools/Sound/SoundLibrary 自動設定")]
    public static void ShowWindow()
        => GetWindow<SoundLibrarySetupTool>("SoundLibrary 自動設定");

    // ---- GUI ----

    private void OnGUI()
    {
        GUILayout.Label("SoundLibrary 自動設定ツール", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"BGM フォルダ : {BGMFolder}\nSE  フォルダ : {SEFolder}\n\n" +
            "各ファイルを Addressables に登録し、SoundLibrary のキーと参照を一括設定します。",
            MessageType.Info);

        EditorGUILayout.Space(8);

        _library = (SoundLibrary)EditorGUILayout.ObjectField(
            "対象 SoundLibrary", _library, typeof(SoundLibrary), false);

        if (_library == null)
        {
            using (new EditorGUI.IndentLevelScope())
                _libraryPath = EditorGUILayout.TextField("新規作成パス", _libraryPath);
        }

        EditorGUILayout.Space(8);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("プレビュー",    GUILayout.Height(28))) Preview();
            if (GUILayout.Button("自動設定を実行", GUILayout.Height(28))) Execute();
        }

        DrawPreview();
    }

    private void DrawPreview()
    {
        if (_preview == null) return;

        EditorGUILayout.Space(8);

        int bgmCount = _preview.FindAll(p => p.isBGM).Count;
        int seCount  = _preview.Count - bgmCount;
        GUILayout.Label($"検出クリップ : BGM {bgmCount} 件 / SE {seCount} 件", EditorStyles.boldLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

        string section = "";
        foreach (var (key, path, isBGM) in _preview)
        {
            string newSection = isBGM ? "BGM" : "SE";
            if (newSection != section)
            {
                EditorGUILayout.Space(4);
                GUILayout.Label($"── {newSection} ──", EditorStyles.boldLabel);
                section = newSection;
            }
            EditorGUILayout.LabelField(key, path, EditorStyles.miniLabel);
        }

        EditorGUILayout.EndScrollView();
    }

    // ---- Actions ----

    private void Preview()
    {
        _preview = new List<(string, string, bool)>();
        CollectClips(BGMFolder, BGMFolder, isBGM: true,  result: _preview);
        CollectClips(SEFolder,  SEFolder,  isBGM: false, result: _preview);
        Repaint();
    }

    private void Execute()
    {
        // Addressables 初期化確認
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EditorUtility.DisplayDialog("エラー",
                "Addressables が未初期化です。\n" +
                "Window > Asset Management > Addressables > Groups を開いて初期化してください。",
                "OK");
            return;
        }

        // SoundLibrary の取得 or 新規作成
        if (_library == null)
        {
            _library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(_libraryPath);
            if (_library == null)
            {
                EnsureDirectory(_libraryPath);
                _library = CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(_library, _libraryPath);
                Debug.Log($"[SoundLibrarySetupTool] SoundLibrary を新規作成: {_libraryPath}");
            }
        }

        var group = settings.DefaultGroup;

        _library.bgmList = BuildEntries(BGMFolder, BGMFolder, settings, group);
        _library.seList  = BuildEntries(SEFolder,  SEFolder,  settings, group);

        EditorUtility.SetDirty(_library);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Addressablesグループの変更を通知
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);

        Preview(); // プレビューも更新

        int bgm = _library.bgmList.Count;
        int se  = _library.seList.Count;
        Debug.Log($"[SoundLibrarySetupTool] 設定完了 — BGM: {bgm} 件, SE: {se} 件");
        EditorUtility.DisplayDialog("完了",
            $"SoundLibrary を設定しました。\n\nBGM : {bgm} 件\nSE  : {se} 件", "OK");
    }

    // ---- Helpers ----

    private static void CollectClips(
        string folder, string root, bool isBGM,
        List<(string key, string path, bool isBGM)> result)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            result.Add((ToKey(path, root), path, isBGM));
        }
    }

    private static List<SoundLibrary.Entry> BuildEntries(
        string folder, string root,
        AddressableAssetSettings settings, AddressableAssetGroup group)
    {
        var entries = new List<SoundLibrary.Entry>();

        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var key  = ToKey(path, root);

            // Addressables グループに追加してアドレスを設定
            var addrEntry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
            addrEntry.address = key;

            entries.Add(new SoundLibrary.Entry
            {
                key     = key,
                clipRef = new AssetReferenceT<AudioClip>(guid)
            });

            Debug.Log($"[SoundLibrarySetupTool] 登録: key='{key}'  path='{path}'");
        }

        return entries;
    }

    /// <summary>フォルダルートからの相対パス（拡張子なし）をキーにする</summary>
    private static string ToKey(string assetPath, string rootFolder)
    {
        var relative = assetPath.Substring(rootFolder.Length).TrimStart('/', '\\');
        return Path.ChangeExtension(relative, null).Replace('\\', '/').Trim();
    }

    private static void EnsureDirectory(string assetPath)
    {
        var dir = Path.GetDirectoryName(assetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
