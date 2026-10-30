using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 会話チュートリアルのセリフを、ゲームの画面の上に宴のメッセージウィンドウと立ち絵を重ねた 1920x1080 の PNG に書き出す（文言の確認用）。
/// 背景はその会話が流れる画面のプレハブ（占い師・魔王軍支援・情報屋・鍛冶屋・勇者）か店の背景。
/// メッセージウィンドウは ScenarioSystem.prefab の MessageWindowCanvas をそのまま使い、名前と本文の TMP に文字を入れる。
/// 立ち絵は Character.csv の Scale（2048 × Scale、中心が画面中央。宴と同じ置き方）。Play しない・宴のエンジンは動かさない。
/// バッチ（描画が要るので -nographics は付けない）:
///   Unity.exe -batchmode -projectPath &lt;wt&gt; -executeMethod ConversationCapture.RunBatch -captureOut &lt;dir&gt; -logFile &lt;log&gt;
/// 撮る行は Shots（ラベルと、行を絞る文字列。null ならラベルの全行）。
/// </summary>
public static class ConversationCapture
{
    private const string Tag = "[ConversationCapture]";
    private const int Width = 1920;
    private const int Height = 1080;
    private const string ScenarioDir = "Assets/TomsScenario/Scenarios/";
    private const string ScenarioSystemPath = "Assets/TomsScenario/Prefabs/ScenarioSystem.prefab";
    private const string ShopBackgroundPath = "Assets/UI/店/ShopBackground.png";

    private readonly struct Shot
    {
        public readonly string File;
        public readonly string Label;
        public readonly string Filter;
        public readonly string Background; // 画面のプレハブのパス、または背景の画像のパス
        public Shot(string file, string label, string filter, string background)
        {
            File = file; Label = label; Filter = filter; Background = background;
        }
    }

    // 背景はその画面の絵（画面のプレハブは文字が見本のままなので、絵だけを敷く）
    private const string Prophet = "Assets/UI/占い屋-20260426T014819Z-3-001/占い屋/占い師背景.png";
    private const string Support = "Assets/UI/InfoBroker/StreamingBackground/魔王城.png";
    private const string Broker = "Assets/UI/InfoBroker/InfoBack.jpg";
    private const string Smith = ShopBackgroundPath;

    private static readonly Shot[] Shots =
    {
        new("Tutorial.csv", "Tutorial_OpeningShop", "納付日は7日ごと", ShopBackgroundPath),
        new("Tutorial.csv", "Tutorial_Display", "品ごとに1日に来る", ShopBackgroundPath),
        new("Tutorial.csv", "Tutorial_Display", "並べすぎた分", ShopBackgroundPath),
        new("Tutorial.csv", "Tutorial_FundsShortage", "注文が入ったよ", ShopBackgroundPath),
        new("Tutorial.csv", "Tutorial_FundsAdvance", "50,000G", Smith),
        new("Tutorial.csv", "Tutorial_FundsAdvance", "たくさん売れたのに", Smith),
        new("Tutorial.csv", "Tutorial_FundsAdvance", "賢く仕入れる", Smith),
        new("Tutorial.csv", "Tutorial_HeroEquip", "負ければ", ShopBackgroundPath),
        new("CharacterIntro.csv", "Intro_Prophet", null, Prophet),
        new("CharacterIntro.csv", "Intro_ProphetRules", null, Prophet),
        new("CharacterIntro.csv", "Intro_DemonLord", "出資せよ", Support),
        new("CharacterIntro.csv", "Intro_SupportOdds", null, Support),
        new("CharacterIntro.csv", "Intro_SupportFloor", null, Support),
        new("CharacterIntro.csv", "Intro_InfoBroker", "ご名答", Broker),
    };

    private sealed class Line
    {
        public string Speaker;
        public string Face;
        public string Text;
    }

    public static void RunBatch()
    {
        int code = 0;
        try
        {
            string dir = Arg("-captureOut") ?? @"D:\WorkTree\TomsLands\_scratch_wt04\conversation\shots";
            Directory.CreateDirectory(dir);
            var portraits = LoadPortraits();
            var index = new List<string>();
            foreach (var shot in Shots)
            {
                var lines = ReadLabel(shot.File, shot.Label);
                if (lines.Count == 0) throw new InvalidOperationException($"{shot.Label} が {shot.File} に無い");
                int n = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (shot.Filter != null && !line.Text.Contains(shot.Filter)) continue;
                    string name = $"{shot.Label}_{i + 1:00}.png";
                    Capture(line, portraits, shot.Background, Path.Combine(dir, name));
                    index.Add($"{name}\t{shot.Label}\t{line.Speaker}\t{line.Face}\t{line.Text}");
                    n++;
                }
                if (n == 0) throw new InvalidOperationException($"{shot.Label} に「{shot.Filter}」を含む行が無い");
            }
            File.WriteAllLines(Path.Combine(dir, "index.tsv"), index);
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 失敗: {e}");
            code = 1;
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        EditorApplication.Exit(code);
    }

    // ───────── 会話の CSV ─────────

    private static List<Line> ReadLabel(string file, string label)
    {
        var lines = new List<Line>();
        bool inLabel = false;
        foreach (var raw in File.ReadAllLines(ScenarioDir + file))
        {
            if (raw.StartsWith("*"))
            {
                inLabel = raw.StartsWith($"*{label},");
                continue;
            }
            if (!inLabel) continue;
            if (raw.StartsWith("EndScenario")) break;
            if (raw.StartsWith("//") || !raw.StartsWith(",")) continue;
            var m = Regex.Match(raw, "^,([^,]*),([^,]*),[^,]*,[^,]*,[^,]*,[^,]*,[^,]*,\"([^\"]*)\"");
            if (!m.Success) continue;
            lines.Add(new Line { Speaker = m.Groups[1].Value, Face = m.Groups[2].Value, Text = m.Groups[3].Value.Replace("\\n", "\n") });
        }
        return lines;
    }

    /// <summary>Character.csv から (話者, 表情) → (画像のパス, Scale)。</summary>
    private static Dictionary<(string, string), (string path, float scale)> LoadPortraits()
    {
        var map = new Dictionary<(string, string), (string, float)>();
        string current = null;
        foreach (var raw in File.ReadAllLines(ScenarioDir + "Character.csv").Skip(1))
        {
            if (raw.StartsWith("//")) continue;
            var c = raw.Split(',');
            if (c.Length < 6) continue;
            if (!string.IsNullOrEmpty(c[0])) current = c[0];
            if (current == null || string.IsNullOrEmpty(c[5])) continue;
            string file = c[5].Trim();
            string path = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(file) + " t:Texture2D")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileName(p) == file);
            if (path == null) continue;
            float.TryParse(c[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float scale);
            map[(current, c[2])] = (path, scale > 0 ? scale : 1f);
        }
        return map;
    }

    // ───────── 撮影 ─────────

    private static void Capture(Line line, Dictionary<(string, string), (string path, float scale)> portraits, string background, string outPath)
    {
        UIMotion.ForcedMode = UIMotionMode.Skip;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("CaptureCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.orthographic = true;
        camGo.transform.position = new Vector3(0, 0, -10);

        // 背景（画面のプレハブ、または画像）
        if (background.EndsWith(".prefab"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(background);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.SetActive(true);
            var common = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Screens/CommonCanvas.prefab");
            if (common != null) ((GameObject)PrefabUtility.InstantiatePrefab(common, scene)).SetActive(true);
        }
        else
        {
            var bg = NewCanvas("Background", -100);
            var bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(background);
            if (bgSprite != null) AddImage(bg.transform, bgSprite, Vector2.zero, new Vector2(Width, Height), false);
            else
            {
                var raw = new GameObject("Raw", typeof(RectTransform), typeof(RawImage));
                raw.transform.SetParent(bg.transform, false);
                var rrt = (RectTransform)raw.transform;
                rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one; rrt.sizeDelta = Vector2.zero;
                raw.GetComponent<RawImage>().texture = AssetDatabase.LoadAssetAtPath<Texture2D>(background);
            }
        }

        // 立ち絵（宴と同じ: 2048 × Scale、中心が画面中央）
        if (portraits.TryGetValue((line.Speaker, line.Face), out var p) || portraits.TryGetValue((line.Speaker, "01_通常"), out p))
        {
            var layer = NewCanvas("CharacterLayer", 0);
            float size = 2048f * p.scale;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p.path);
            if (sprite != null) AddImage(layer.transform, sprite, Vector2.zero, new Vector2(size, size), true);
        }

        // 宴のメッセージウィンドウ
        var system = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScenarioSystemPath), scene);
        // 宴の本文は AdvEngine/UI（Canvas）/MessageWindowManager/MessageWindow の下にある。AdvEngine は既定で切ってあるので、
        // 本文と名前までの道を出し、UI の Canvas より上の段では道の外（メニュー・バックログ・エンジン本体など）を隠す
        var message = SetTmp(system.transform, "MessageText", line.Text);
        var nameText = SetTmp(system.transform, "NameText", line.Speaker);
        if (message == null) throw new InvalidOperationException("宴のメッセージウィンドウの本文が見つからない");
        var windowCanvas = message.GetComponentInParent<Canvas>(true);
        var keep = new HashSet<Transform>();
        foreach (var tmp in new[] { message, nameText })
            for (var t = tmp != null ? tmp.transform : null; t != null; t = t.parent)
            {
                keep.Add(t);
                t.gameObject.SetActive(true);
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
            }
        for (var t = windowCanvas.transform; t != null && t != system.transform; t = t.parent)
        {
            // UI の Canvas の直下（MessageWindowManager の横）と、それより上の段
            foreach (Transform sibling in t.parent)
                if (!keep.Contains(sibling)) sibling.gameObject.SetActive(false);
        }
        foreach (Transform child in windowCanvas.transform)
            if (!keep.Contains(child)) child.gameObject.SetActive(false);
        windowCanvas.sortingOrder = 1000;
        // 送りのアイコンは文字送りの途中の位置に残るので隠す
        foreach (var icon in system.GetComponentsInChildren<Transform>(true).Where(t => t.name == "IconNext" || t.name == "IconBrPage"))
            icon.gameObject.SetActive(false);

        foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))
                     .Where(c => c.transform.parent == null || c.transform.parent.GetComponentInParent<Canvas>(true) == null))
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = canvas == windowCanvas ? 2 : canvas.name == "CharacterLayer" ? 4 : 10;
        }

        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        cam.targetTexture = rt;
        try
        {
            for (int i = 0; i < 3; i++) Refresh(scene, cam);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log($"{Tag} 書き出し: {outPath}");
        }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UIMotion.ForcedMode = null;
        }
    }

    private static TMP_Text SetTmp(Transform root, string objectName, string text)
    {
        // 同じ名前がバックログなどにもあるので、TMP を持つものを選ぶ（宴の TextMeshProNovelText は同じ物の TMP に書く）
        var tmp = root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(x => x.name == objectName && x.GetComponentInParent<Canvas>(true) != null
            && x.transform.GetComponentsInParent<Transform>(true).Any(a => a.name == "MessageWindow"));
        if (tmp == null)
        {
            var names = string.Join(", ", root.GetComponentsInChildren<TMP_Text>(true).Select(x => x.name));
            Debug.LogWarning($"{Tag} TMP の一覧: {names}");
        }
        if (tmp == null)
        {
            Debug.LogWarning($"{Tag} {objectName} の TMP が無い");
            return null;
        }
        tmp.gameObject.SetActive(true);
        tmp.text = text;
        tmp.maxVisibleCharacters = 99999;
        return tmp;
    }

    private static Canvas NewCanvas(string name, int order)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.sortingOrder = order;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Width, Height);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static void AddImage(Transform parent, Sprite sprite, Vector2 pos, Vector2 size, bool preserveAspect)
    {
        var go = new GameObject("Image", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = preserveAspect;
    }

    private static void Refresh(UnityEngine.SceneManagement.Scene scene, Camera cam)
    {
        cam.Render();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var scaler in root.GetComponentsInChildren<CanvasScaler>(true))
            {
                if (!scaler.isActiveAndEnabled) continue;
                scaler.enabled = false;
                scaler.enabled = true;
            }
            foreach (var r in root.GetComponentsInChildren<RectTransform>(false).Where(r => r.GetComponent<LayoutGroup>() != null))
                LayoutRebuilder.ForceRebuildLayoutImmediate(r);
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                if (tmp.isActiveAndEnabled) tmp.ForceMeshUpdate(true);
        }
        Canvas.ForceUpdateCanvases();
    }

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
