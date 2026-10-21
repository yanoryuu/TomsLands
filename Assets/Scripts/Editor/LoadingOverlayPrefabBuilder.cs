using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 常駐ローディング画面のプレハブ（Assets/Resources/LoadingOverlay.prefab）を組み立てる。
/// SceneLoader が Resources.Load("LoadingOverlay") で読むので、パス・名前は変えないこと。
/// 見た目を手で調整した後に再実行すると上書きされる点に注意（Docs/LoadingScreen_UnityWiring.md）。
/// </summary>
public static class LoadingOverlayPrefabBuilder
{
    private const string PrefabPath = "Assets/Resources/LoadingOverlay.prefab";
    private const string HeroPrefabPath = "Assets/Prefabs/Hero_SPUM.prefab";
    private const string FontPath = "Assets/Font/MPLUSRounded1c-Bold SDFPlusPadding.asset";
    /// <summary>宴の会話UI（2000）・チュートリアル（32000）・コントローラ選択枠（32000）より上。</summary>
    private const int SortingOrder = 32700;

    [MenuItem("Tools/Loading/Build Loading Overlay Prefab")]
    public static void Build()
    {
        var root = new GameObject("LoadingOverlay", typeof(RectTransform));
        try
        {
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            // 全面の黒（裏の画面を隠し、遷移中のクリックを止める）
            var bg = CreateChild("Background", root.transform);
            Stretch(bg);
            var bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = Color.black;
            bgImage.raycastTarget = true;

            // 右下で歩く勇者（足元＝下端中央）
            var hero = CreateChild("Hero", root.transform);
            hero.anchorMin = hero.anchorMax = new Vector2(1f, 0f);
            hero.pivot = new Vector2(0.5f, 0f);
            hero.sizeDelta = new Vector2(220f, 220f);
            hero.anchoredPosition = new Vector2(-170f, 48f);
            var raw = hero.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false; // 撮影が始まるまで白い四角を出さない
            var stage = hero.gameObject.AddComponent<LoadingHeroStage>();

            // 「読み込み中……」（勇者の左）
            var status = CreateChild("Status", root.transform);
            status.anchorMin = status.anchorMax = new Vector2(1f, 0f);
            status.pivot = new Vector2(1f, 0f);
            status.sizeDelta = new Vector2(640f, 64f);
            status.anchoredPosition = new Vector2(-292f, 64f);
            var text = status.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = "読み込み中……";
            text.fontSize = 40f;
            text.alignment = TextAlignmentOptions.BottomRight;
            text.color = new Color(1f, 0.94f, 0.78f, 1f); // 木と金のトーンに合わせた生成り色
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null) text.font = font;
            else Debug.LogWarning($"[LoadingOverlayPrefabBuilder] フォントが見つかりません: {FontPath}");

            var view = root.AddComponent<LoadingView>();

            var heroPrefab = AssetDatabase.LoadAssetAtPath<SPUM_Prefabs>(HeroPrefabPath);
            if (heroPrefab == null)
                Debug.LogWarning($"[LoadingOverlayPrefabBuilder] 勇者プレハブが見つかりません: {HeroPrefabPath}");

            var stageSo = new SerializedObject(stage);
            stageSo.FindProperty("view").objectReferenceValue = raw;
            stageSo.FindProperty("heroPrefab").objectReferenceValue = heroPrefab;
            stageSo.ApplyModifiedPropertiesWithoutUndo();

            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("canvasGroup").objectReferenceValue = group;
            viewSo.FindProperty("statusText").objectReferenceValue = text;
            viewSo.FindProperty("heroStage").objectReferenceValue = stage;
            viewSo.FindProperty("heroRect").objectReferenceValue = hero;
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? "Assets/Resources");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
            if (success) Debug.Log($"[LoadingOverlayPrefabBuilder] 保存しました: {PrefabPath}");
            else Debug.LogError($"[LoadingOverlayPrefabBuilder] 保存に失敗しました: {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static RectTransform CreateChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
