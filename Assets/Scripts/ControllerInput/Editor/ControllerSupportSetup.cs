#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// パッド対応基盤のセットアップ（再実行可能）。
///   Tools/Controller/Configure Sprites      … Assets/UI/Controller の Sprite 設定（9スライス境界）
///   Tools/Controller/Build Overlay Prefab   … Assets/Prefabs/ControllerOverlay.prefab（選択枠）を生成し直す
///   Tools/Controller/Wire Scene EventSystems … 全シーンの InputSystemUIInputModule を InputSystem_Actions に揃え、
///                                             ControllerOverlayBootstrap を付ける
/// </summary>
public static class ControllerSupportSetup
{
    public const string SpriteDir = "Assets/UI/Controller/";
    public const string PrefabPath = "Assets/Prefabs/ControllerOverlay.prefab";
    public const string ActionsPath = "Assets/InputSystem_Actions.inputactions";

    public static readonly string[] Scenes =
    {
        "Assets/Scene/BootScene.unity",
        "Assets/Scene/TitleScene.unity",
        "Assets/Scene/TomsShop.unity",
        "Assets/Scene/VillageScene.unity",
        "Assets/Scene/PreparationScene.unity",
        "Assets/Scene/EventScene.unity",
        "Assets/Scene/FightScene.unity",
        "Assets/Scene/ResultScene.unity",
        "Assets/Scene/GameOver.unity",
    };

    // ------------------------------------------------------------------ Sprites

    [MenuItem("Tools/Controller/Configure Sprites")]
    public static void ConfigureSprites()
    {
        Configure("ctrl_focus_frame.png", new Vector4(36, 36, 36, 36));
        Configure("ctrl_focus_glow.png", new Vector4(44, 44, 44, 44));
        AssetDatabase.Refresh();
    }

    private static void Configure(string file, Vector4 border)
    {
        var path = SpriteDir + file;
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"[ControllerSetup] {path} not found"); return; }
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.spriteBorder = border;
        ti.SaveAndReimport();
    }

    private static Sprite LoadSprite(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + file);

    // ------------------------------------------------------------------ Prefab

    [MenuItem("Tools/Controller/Build Overlay Prefab")]
    public static void BuildPrefab()
    {
        var frameSprite = LoadSprite("ctrl_focus_frame.png");
        var glowSprite = LoadSprite("ctrl_focus_glow.png");

        var root = new GameObject("ControllerOverlay", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // --- 選択枠
        var frameRoot = NewUI("FocusFrame", root.transform);
        var frt = frameRoot.GetComponent<RectTransform>();
        frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0.5f);
        frt.sizeDelta = new Vector2(240, 96);
        var frameGroup = frameRoot.AddComponent<CanvasGroup>();
        frameGroup.blocksRaycasts = false;
        frameGroup.interactable = false;

        var glow = NewImage("Glow", frameRoot.transform, glowSprite, new Color(1f, 0.88f, 0.5f, 1f), Image.Type.Sliced);
        glow.pixelsPerUnitMultiplier = 1f;
        Stretch(glow.rectTransform, 14);
        var frame = NewImage("Frame", frameRoot.transform, frameSprite, Color.white, Image.Type.Sliced);
        frame.pixelsPerUnitMultiplier = 1.1f;
        Stretch(frame.rectTransform, 0);

        var hl = root.AddComponent<ControllerHighlight>();
        var hlSo = new SerializedObject(hl);
        hlSo.FindProperty("frame").objectReferenceValue = frt;
        hlSo.FindProperty("frameGroup").objectReferenceValue = frameGroup;
        hlSo.ApplyModifiedPropertiesWithoutUndo();

        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ControllerSetup] Built {PrefabPath}");
    }

    private static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite, Color color, Image.Type type)
    {
        var go = NewUI(name, parent);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.type = type;
        img.raycastTarget = false;
        return img;
    }

    private static void Stretch(RectTransform rt, float outset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(-outset, -outset);
        rt.offsetMax = new Vector2(outset, outset);
    }

    // ------------------------------------------------------------------ Scenes

    [MenuItem("Tools/Controller/Wire Scene EventSystems")]
    public static void WireAllScenes()
    {
        foreach (var path in Scenes) WireScene(path);
    }

    public static string WireScene(string path)
    {
        var scene = SceneManager.GetSceneByPath(path);
        bool openedHere = false;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            openedHere = true;
        }

        var refs = AssetDatabase.LoadAllAssetsAtPath(ActionsPath).OfType<InputActionReference>().Where(r => AssetDatabase.IsSubAsset(r))
            .GroupBy(r => r.name).Select(g => g.First())
            .ToDictionary(r => r.name, r => r);
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        var overlay = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var module in root.GetComponentsInChildren<InputSystemUIInputModule>(true))
            {
                var so = new SerializedObject(module);
                so.FindProperty("m_ActionsAsset").objectReferenceValue = asset;
                Set(so, refs, "m_PointAction", "UI/Point");
                Set(so, refs, "m_MoveAction", "UI/Navigate");
                Set(so, refs, "m_SubmitAction", "UI/Submit");
                Set(so, refs, "m_CancelAction", "UI/Cancel");
                Set(so, refs, "m_LeftClickAction", "UI/Click");
                Set(so, refs, "m_MiddleClickAction", "UI/MiddleClick");
                Set(so, refs, "m_RightClickAction", "UI/RightClick");
                Set(so, refs, "m_ScrollWheelAction", "UI/ScrollWheel");
                Set(so, refs, "m_TrackedDevicePositionAction", "UI/TrackedDevicePosition");
                Set(so, refs, "m_TrackedDeviceOrientationAction", "UI/TrackedDeviceOrientation");
                so.ApplyModifiedPropertiesWithoutUndo();

                var boot = module.GetComponent<ControllerOverlayBootstrap>();
                if (boot == null) boot = module.gameObject.AddComponent<ControllerOverlayBootstrap>();
                var bso = new SerializedObject(boot);
                bso.FindProperty("overlayPrefab").objectReferenceValue = overlay;
                bso.ApplyModifiedPropertiesWithoutUndo();
                count++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedHere) EditorSceneManager.CloseScene(scene, true);
        var msg = $"{path}: modules={count}";
        Debug.Log("[ControllerSetup] " + msg);
        return msg;
    }

    private static void Set(SerializedObject so, Dictionary<string, InputActionReference> refs, string prop, string action)
    {
        var p = so.FindProperty(prop);
        if (p == null) return;
        p.objectReferenceValue = refs.TryGetValue(action, out var r) ? r : null;
    }
}
#endif
