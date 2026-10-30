using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI アニメーションの共通部品（Assets/Scripts/UI/Motion）を、録画やセーブに触れずに確認するためのエディタ側。
/// ・<see cref="UIMotionEditorHooks"/>: 録画中（GameplayAutoRecorder.IsRecording）は UIMotion を「見せる再生」にする
/// ・<see cref="UIMotionCapture"/>: 16 種の動きを見本の UI で再生して連番 PNG に撮る（GIF 化は tools/ui_motion_gif.py）
/// </summary>
[InitializeOnLoad]
public static class UIMotionEditorHooks
{
    static UIMotionEditorHooks()
    {
        // 録画（人格マラソンの録画ラン・実況の撮影プリセット・手動の自動録画）は見せるための再生なので、
        // オートプレイが省略を要求していても通常の演出で撮る
        UIMotion.ShowcaseProbe = () => GameplayAutoRecorder.IsRecording;
    }
}

/// <summary>
/// 16 種の UI アニメを撮る。新しい一時シーン（保存しない）に見本の UI を組み、DOTween を手動で 1 コマずつ進めて描く。
///   Unity.exe -batchmode -projectPath &lt;wt&gt; -executeMethod UIMotionCapture.RunBatch -out &lt;dir&gt; [-only 1,5,...] [-fps 30] -logFile &lt;log&gt;
/// 出力: &lt;dir&gt;/&lt;番号&gt;_&lt;名前&gt;/f_000.png …（1280x720）。描画が要るので -nographics は付けない。
/// 既存のシーン・プレハブ・セーブには触れない（撮影の後は空のシーンに切り替えて終わる）。
/// </summary>
public static class UIMotionCapture
{
    private const string Tag = "[UIMotionCapture]";
    private const int Width = 1280;
    private const int Height = 720;

    private sealed class Demo
    {
        public int No;
        public string Name;
        public float Seconds;
        public Action<Ctx> Build;
    }

    /// <summary>見本づくりの道具と、時刻つきの操作の予定表。</summary>
    private sealed class Ctx
    {
        public RectTransform Root;
        public readonly List<(float at, Action action)> Schedule = new();
        public readonly List<Action<float, float>> PerFrame = new();

        public void At(float t, Action a) => Schedule.Add((t, a));
        public void Every(Action<float, float> f) => PerFrame.Add(f);
    }

    private static readonly Demo[] Demos =
    {
        new() { No = 1, Name = "open_close", Seconds = 2.4f, Build = BuildOpenClose },
        new() { No = 2, Name = "money", Seconds = 2.6f, Build = BuildMoney },
        new() { No = 3, Name = "stagger", Seconds = 1.6f, Build = BuildStagger },
        new() { No = 4, Name = "button_feel", Seconds = 3.2f, Build = BuildButtons },
        new() { No = 5, Name = "celebration", Seconds = 1.9f, Build = BuildCelebration },
        new() { No = 6, Name = "curtain", Seconds = 2.6f, Build = BuildCurtain },
        new() { No = 7, Name = "tabs", Seconds = 2.4f, Build = BuildTabs },
        new() { No = 8, Name = "idle", Seconds = 2.8f, Build = BuildIdle },
        new() { No = 9, Name = "toast", Seconds = 3.4f, Build = BuildToast },
        new() { No = 10, Name = "trail_gauge", Seconds = 2.8f, Build = BuildGauge },
        new() { No = 11, Name = "card_flip", Seconds = 2.4f, Build = BuildCards },
        new() { No = 12, Name = "price_change", Seconds = 2.6f, Build = BuildPrice },
        new() { No = 13, Name = "drag_drop", Seconds = 2.2f, Build = BuildDrag },
        new() { No = 14, Name = "damage_number", Seconds = 2.2f, Build = BuildDamage },
        new() { No = 15, Name = "comment_highlight", Seconds = 3.0f, Build = BuildComments },
        new() { No = 16, Name = "flip_counter", Seconds = 2.2f, Build = BuildFlipCounter },
        new() { No = 17, Name = "door", Seconds = 1.7f, Build = BuildDoor },
    };

    [MenuItem("Tools/TomsLands/UI アニメを撮る（連番 PNG）")]
    private static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "UIMotionCapture"));
        int code = Run(dir, null, 30);
        EditorUtility.RevealInFinder(dir);
        Debug.Log($"{Tag} 終了（{code}）");
    }

    /// <summary>バッチモード用。終わったら Exit(0)、失敗は Exit(1)。</summary>
    public static void RunBatch()
    {
        int code;
        try
        {
            string dir = Arg("-out") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "UIMotionCapture"));
            var only = (Arg("-only") ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Select(int.Parse).ToArray();
            int fps = int.TryParse(Arg("-fps"), out int f) ? Mathf.Clamp(f, 5, 60) : 30;
            code = Run(dir, only.Length > 0 ? only : null, fps);
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 失敗: {e}");
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    private static int Run(string outDir, int[] only, int fps)
    {
        Directory.CreateDirectory(outDir);
        var assets = UIMotionAssets.Instance;
        Debug.Log($"{Tag} 素材: coin={assets.coin != null} sparkle={assets.sparkle != null} glow={assets.glow != null} " +
                  $"rays={assets.rays != null} goldPlate={assets.goldPlate != null} roundWhite={assets.roundWhite != null}");
        // シーンを作り直すたびに使われていないアセットが片付けられるので、消されないようにしておく
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave };
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
        int failures = 0;
        try
        {
            UIMotion.ManualUpdate = true;
            UIMotion.ForcedMode = UIMotionMode.Normal;
            foreach (var demo in Demos)
            {
                if (only != null && Array.IndexOf(only, demo.No) < 0) continue;
                try
                {
                    int frames = Capture(demo, outDir, fps, rt, tex);
                    Debug.Log($"{Tag} #{demo.No} {demo.Name}: {frames} コマ");
                }
                catch (Exception e)
                {
                    failures++;
                    Debug.LogError($"{Tag} #{demo.No} {demo.Name} で失敗: {e}");
                }
            }
        }
        finally
        {
            DOTween.KillAll();
            UIMotion.ManualUpdate = false;
            UIMotion.ForcedMode = null;
            UIMotion.CaptureCamera = null;
            UIMotionFx.ResetLayers();
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            // 撮影で作ったものは保存せずに捨てる
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        Debug.Log($"{Tag} 終了: 失敗 {failures} 件 → {outDir}");
        return failures == 0 ? 0 : 1;
    }

    private static int Capture(Demo demo, string outDir, int fps, RenderTexture rt, Texture2D tex)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        DOTween.KillAll();
        UIMotionFx.ResetLayers();

        var camGo = new GameObject("CaptureCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(0x1d, 0x14, 0x0e, 0xff);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 500f;
        cam.targetTexture = rt;
        UIMotion.CaptureCamera = cam;

        var ctx = new Ctx { Root = CreateRootCanvas(cam) };
        AddBackdrop(ctx.Root);
        RenderFrame(cam); // Canvas を RenderTexture の大きさに合わせる
        Canvas.ForceUpdateCanvases();
        demo.Build(ctx);
        Canvas.ForceUpdateCanvases();

        string dir = Path.Combine(outDir, $"{demo.No:00}_{demo.Name}");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        float dt = 1f / fps;
        int frames = Mathf.CeilToInt(demo.Seconds * fps);
        var schedule = ctx.Schedule.OrderBy(s => s.at).ToList();
        int next = 0;
        for (int i = 0; i < frames; i++)
        {
            float t = i * dt;
            if (i > 0) DOTween.ManualUpdate(dt, dt);
            while (next < schedule.Count && schedule[next].at <= t + 1e-4f) schedule[next++].action();
            foreach (var f in ctx.PerFrame) f(t, dt);
            CommentHighlight.SyncAll();
            Canvas.ForceUpdateCanvases();
            RenderFrame(cam);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, $"f_{i:000}.png"), tex.EncodeToPNG());
        }
        DOTween.KillAll();
        return frames;
    }

    private static void RenderFrame(Camera cam)
    {
        cam.Render();
    }

    private static RectTransform CreateRootCanvas(Camera cam)
    {
        var go = new GameObject("DemoCanvas", typeof(RectTransform));
        go.layer = 5;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 100f;
        canvas.sortingOrder = 0;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return (RectTransform)go.transform;
    }

    // =====================================================
    // 見本の部品（ゲームの素材を使う）
    // =====================================================

    private const string PanelFrame = "Assets/UI/Village/Parts/vui_panel_frame.png";
    private const string PanelFrameGold = "Assets/UI/Village/Parts/vui_panel_frame_gold.png";
    private const string BrownBtn = "Assets/UI/Village/Parts/vui_brown_btn.png";
    private const string GoldBtnOff = "Assets/UI/Village/Parts/vui_gold_btn_off.png";
    private const string WoodPlate = "Assets/UI/Village/Parts/vui_wood_plate.png";
    private const string WoodPlateGold = "Assets/UI/Village/Parts/vui_wood_plate_gold.png";
    private const string CreamBox = "Assets/UI/Village/Parts/vui_cream_box.png";
    private const string FundsBox = "Assets/UI/Village/Parts/vui_funds_box.png";
    private const string LabelPlate = "Assets/UI/Village/Parts/vui_label_plate.png";
    private const string Paper = "Assets/UI/Village/Parts/vui_paper.png";
    private const string ShopBg = "Assets/UI/店/ShopBackground.png";
    /// <summary>#6 の幕が開いた後の場面（村の広場の絵。出店準備の背景をそのまま使う）。</summary>
    private const string VillageBg = "Assets/UI/Preparation/PreparationBackground.png";
    /// <summary>#17 の扉の奥の場面（鍛冶屋の背景）。</summary>
    private const string BlackSmithBg = "Assets/UI/鍛冶屋・道具屋/Blacksmith.jpg";
    private const string Coin = "Assets/UI/Village/ArrivalFx/arrival_coin.png";
    private const string Monster = "Assets/Layer Lab/2D Pixel-RPGMonstersIcon/default size/no shadow/1 Undead/undead_101.png";
    private static readonly string[] Icons =
    {
        "Assets/UI/レリック/命知らずのサイコロ.png",
        "Assets/UI/レリック/商人ギルドの証.png",
        "Assets/UI/レリック/呪われた帳簿.png",
        "Assets/UI/レリック/ギルドの減免状.png",
        "Assets/UI/Preparation/Prep_IconFlyer.png",
    };

    private static readonly Dictionary<string, Sprite> SpriteCache = new();

    private static Sprite Load(string path)
    {
        if (SpriteCache.TryGetValue(path, out var s) && s != null) return s;
        s = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (s == null)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null)
            {
                s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
                s.hideFlags = HideFlags.DontSave;
            }
        }
        if (s == null) Debug.LogWarning($"{Tag} 絵が見つからない: {path}");
        SpriteCache[path] = s;
        return s;
    }

    private static void AddBackdrop(RectTransform root)
    {
        var bg = UIMotionFx.NewImage("Backdrop", root, Load(ShopBg), Color.white, Vector2.zero);
        bg.type = Image.Type.Simple;
        UIMotionFx.Stretch(bg.rectTransform);
        var dim = UIMotionFx.NewImage("Dim", root, UIMotionFx.WhiteSprite, new Color(0.08f, 0.05f, 0.03f, 0.45f), Vector2.zero);
        UIMotionFx.Stretch(dim.rectTransform);
    }

    private static Image Panel(Transform parent, string sprite, Vector2 size, Vector2 pos, float ppu = 2.2f)
    {
        var img = UIMotionFx.NewImage("Panel", parent, Load(sprite), Color.white, size);
        if (img.type == Image.Type.Sliced) img.pixelsPerUnitMultiplier = ppu;
        img.rectTransform.anchoredPosition = pos;
        return img;
    }

    private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 box = default)
    {
        var t = UIMotionFx.NewText("Text", parent, text, size, color, box == default ? new Vector2(900f, size * 1.6f) : box);
        t.rectTransform.anchoredPosition = pos;
        // 暗い字は縁取りなしのフォントで（既定のフォントは縁取り付きで、暗い字だと潰れる）
        if (color.grayscale < 0.5f)
        {
            var plain = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PlainFont);
            if (plain != null) t.font = plain;
        }
        return t;
    }

    private const string PlainFont = "Assets/Font/MPLUSRounded1c-Bold SDFPlusPaddingNoOutLine.asset";

    private static Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, string sprite = BrownBtn)
    {
        var img = UIMotionFx.NewImage("Button_" + label, parent, Load(sprite), Color.white, size);
        if (img.type == Image.Type.Sliced) img.pixelsPerUnitMultiplier = 1f;
        img.raycastTarget = true;
        img.rectTransform.anchoredPosition = pos;
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var t = Text(img.transform, label, 40f, UIMotionFx.Cream, Vector2.zero, size);
        t.fontSharedMaterial = UIMotionFx.OutlinedMaterial(t.font);
        return b;
    }

    private static Image Icon(Transform parent, int index, Vector2 pos, float size = 120f)
    {
        var img = UIMotionFx.NewImage("Icon", parent, Load(Icons[index % Icons.Length]), Color.white, new Vector2(size, size));
        img.preserveAspect = true;
        img.rectTransform.anchoredPosition = pos;
        return img;
    }

    private static Image Slot(Transform parent, Vector2 pos, float size = 170f)
    {
        var img = UIMotionFx.NewImage("Slot", parent, Load(CreamBox), Color.white, new Vector2(size, size));
        if (img.type == Image.Type.Sliced) img.pixelsPerUnitMultiplier = 0.5f;
        img.rectTransform.anchoredPosition = pos;
        return img;
    }

    private static void Caption(Ctx c, string text)
    {
        var t = Text(c.Root, text, 34f, UIMotionFx.Cream, new Vector2(0f, -490f), new Vector2(1800f, 60f));
        t.fontSharedMaterial = UIMotionFx.OutlinedMaterial(t.font);
    }

    // =====================================================
    // 16 種の見本
    // =====================================================

    private static void BuildOpenClose(Ctx c)
    {
        Caption(c, "#1 ぽよん開閉  UIMotion.Open / UIMotion.Close");
        var panelGo = new GameObject("Popup", typeof(RectTransform));
        var panel = (RectTransform)panelGo.transform;
        panel.SetParent(c.Root, false);
        UIMotionFx.Stretch(panel);
        var dim = UIMotionFx.NewImage("Dim", panel, UIMotionFx.WhiteSprite, new Color(0f, 0f, 0f, 0.5f), Vector2.zero);
        UIMotionFx.Stretch(dim.rectTransform);
        var win = Panel(panel, PanelFrameGold, new Vector2(900f, 560f), new Vector2(0f, 20f), 1.6f);
        Text(win.transform, "両替所", 60f, UIMotionFx.Ink, new Vector2(0f, 170f));
        Text(win.transform, "村の資金を 10,000G 両替しますか？", 40f, UIMotionFx.Ink, new Vector2(0f, 40f));
        MakeButton(win.transform, "両替する", new Vector2(-170f, -150f), new Vector2(280f, 92f));
        MakeButton(win.transform, "やめる", new Vector2(170f, -150f), new Vector2(280f, 92f));
        panelGo.SetActive(false);
        c.At(0.15f, () => UIMotion.Open(panelGo, win.rectTransform));
        c.At(1.6f, () => UIMotion.Close(panelGo, null, win.rectTransform));
    }

    private static void BuildMoney(Ctx c)
    {
        Caption(c, "#2 お金が飛んで数字が回る  MoneyFx.Gain");
        var wallet = Panel(c.Root, FundsBox, new Vector2(424f, 130f), new Vector2(660f, 410f));
        var coin = UIMotionFx.NewImage("Coin", wallet.transform, Load(Coin), Color.white, new Vector2(52f, 72f));
        coin.rectTransform.anchoredPosition = new Vector2(-130f, 0f);
        var money = Text(wallet.transform, "12,400G", 54f, UIMotionFx.Cream, new Vector2(40f, 0f), new Vector2(300f, 80f));
        money.fontSharedMaterial = UIMotionFx.OutlinedMaterial(money.font);
        var card = Panel(c.Root, PanelFrame, new Vector2(420f, 300f), new Vector2(-380f, -60f), 2.6f);
        Icon(card.transform, 0, new Vector2(0f, 30f), 130f);
        Text(card.transform, "売れた！", 44f, UIMotionFx.Cream, new Vector2(0f, -95f));
        c.At(0.25f, () => MoneyFx.Gain(card.rectTransform, wallet.rectTransform, money, 12400, 12900, v => $"{v:N0}G"));
    }

    private static void BuildStagger(Ctx c)
    {
        Caption(c, "#3 順番に並んで出る  UIMotion.Stagger");
        var panel = Panel(c.Root, Paper, new Vector2(760f, 860f), new Vector2(0f, 20f), 1.2f);
        Text(panel.transform, "今日の売上", 52f, UIMotionFx.Ink, new Vector2(0f, 330f));
        var listGo = new GameObject("List", typeof(RectTransform));
        var list = (RectTransform)listGo.transform;
        list.SetParent(panel.transform, false);
        list.sizeDelta = new Vector2(620f, 560f);
        list.anchoredPosition = new Vector2(0f, -40f);
        var layout = listGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childAlignment = TextAnchor.UpperCenter;
        string[] rows = { "鉄の剣  ×3   +1,800G", "回復薬  ×12  +2,400G", "木の盾  ×2   +900G", "魔法の巻物 ×1 +1,500G", "合計          +6,600G" };
        for (int i = 0; i < rows.Length; i++)
        {
            var row = UIMotionFx.NewImage("Row", list, Load(WoodPlate), Color.white, new Vector2(620f, 96f));
            if (row.type == Image.Type.Sliced) row.pixelsPerUnitMultiplier = 1.4f;
            var t = Text(row.transform, rows[i], 38f, UIMotionFx.Cream, Vector2.zero, new Vector2(580f, 90f));
            t.fontSharedMaterial = UIMotionFx.OutlinedMaterial(t.font);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(list);
        // 最初は見えない（Stagger が alpha 0 から出す）
        foreach (Transform r in list) UIMotionFxGroup(r.gameObject).alpha = 0f;
        c.At(0.2f, () => UIMotion.Stagger(list));
    }

    private static CanvasGroup UIMotionFxGroup(GameObject go)
    {
        if (!go.TryGetComponent<CanvasGroup>(out var cg)) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private static void BuildButtons(Ctx c)
    {
        Caption(c, "#4 ボタンの手ざわり  ButtonFeel（主ボタンの脈打ち・押す・押せない）");
        var main = MakeButton(c.Root, "出店する", new Vector2(0f, 200f), new Vector2(460f, 130f), WoodPlateGold);
        var normal = MakeButton(c.Root, "仕入れ", new Vector2(-260f, -80f), new Vector2(340f, 110f));
        var off = MakeButton(c.Root, "改装（資金不足）", new Vector2(260f, -80f), new Vector2(400f, 110f));
        off.interactable = false;
        ((Image)off.targetGraphic).color = new Color(0.6f, 0.6f, 0.6f, 1f);
        ButtonFeel.SetPrimary(main);
        var nf = normal.gameObject.AddComponent<ButtonFeel>();
        var of = off.gameObject.AddComponent<ButtonFeel>();
        c.At(0.6f, () => nf.OnPointerDown(null));
        c.At(0.75f, () => nf.OnPointerUp(null));
        c.At(1.3f, () => of.OnPointerClick(null));
        c.At(2.4f, () => nf.OnPointerDown(null));
        c.At(2.55f, () => nf.OnPointerUp(null));
    }

    private static void BuildCelebration(Ctx c)
    {
        Caption(c, "#5 ここ一番の祝い  Celebration.Play");
        var panel = Panel(c.Root, PanelFrame, new Vector2(900f, 600f), Vector2.zero, 1.6f);
        Text(panel.transform, "評価", 56f, UIMotionFx.Cream, new Vector2(0f, 200f));
        c.At(0.15f, () => Celebration.Play("ランク S！"));
    }

    private static void BuildCurtain(Ctx c)
    {
        // 場面 A: 店（共通の背景＝店の絵）に看板だけ。場面 B: 村の広場の絵（出店準備の背景）に村の看板。
        var sceneAGo = new GameObject("SceneA", typeof(RectTransform));
        var sceneA = (RectTransform)sceneAGo.transform;
        sceneA.SetParent(c.Root, false);
        UIMotionFx.Stretch(sceneA);
        ScenePlate(sceneA, "トムトムの店");

        var sceneBGo = new GameObject("SceneB", typeof(RectTransform));
        var sceneB = (RectTransform)sceneBGo.transform;
        sceneB.SetParent(c.Root, false);
        UIMotionFx.Stretch(sceneB);
        var bgB = UIMotionFx.NewImage("BgB", sceneB, Load(VillageBg), Color.white, Vector2.zero);
        bgB.type = Image.Type.Simple;
        UIMotionFx.Stretch(bgB.rectTransform);
        ScenePlate(sceneB, "トコの村");
        sceneBGo.SetActive(false);

        Caption(c, "#6 幕で場面切替  LoadingView（SceneLoader はそのまま）");

        // LoadingOverlay と同じく 1 枚の Canvas の上に幕と文言（右下）を置く
        var overlayGo = new GameObject("Overlay", typeof(RectTransform));
        var overlay = (RectTransform)overlayGo.transform;
        overlay.SetParent(c.Root, false);
        UIMotionFx.Stretch(overlay);
        var curtain = SceneCurtain.Create(overlay, 0);
        var status = Text(overlay, "読み込み中……", 40f, UIMotionFx.Cream, Vector2.zero, new Vector2(640f, 64f));
        status.rectTransform.anchorMin = status.rectTransform.anchorMax = new Vector2(1f, 0f);
        status.rectTransform.anchoredPosition = new Vector2(-292f, 64f);
        status.fontSharedMaterial = UIMotionFx.OutlinedMaterial(status.font);
        var sg = UIMotionFxGroup(status.gameObject);
        sg.alpha = 0f;
        c.At(0.15f, () => curtain.BeginClose());
        c.At(0.55f, () => UIMotion.Run(sg.DOFade(1f, 0.15f), status.gameObject, true));
        c.At(0.9f, () =>
        {
            sceneAGo.SetActive(false);
            sceneBGo.SetActive(true);
        });
        c.At(1.25f, () => UIMotion.Run(sg.DOFade(0f, 0.12f), status.gameObject, true));
        c.At(1.3f, () => curtain.BeginOpen());
    }

    /// <summary>#6 の場面の看板（村の HUD と同じ木の板に、場所の名前）。</summary>
    private static void ScenePlate(RectTransform scene, string label)
    {
        var plate = Panel(scene, WoodPlate, new Vector2(760f, 150f), new Vector2(0f, 400f), 1.2f);
        var t = Text(plate.transform, label, 72f, UIMotionFx.Cream, new Vector2(0f, 4f), new Vector2(700f, 120f));
        t.fontSharedMaterial = UIMotionFx.OutlinedMaterial(t.font);
    }

    private static void BuildTabs(Ctx c)
    {
        Caption(c, "#7 タブ切替  TabSwitcher.MoveUnderline / SlideContent");
        var panel = Panel(c.Root, PanelFrame, new Vector2(1100f, 700f), new Vector2(0f, 0f), 1.6f);
        var tabs = new RectTransform[3];
        string[] names = { "武器", "防具", "道具" };
        for (int i = 0; i < 3; i++)
        {
            var tab = UIMotionFx.NewImage("Tab", panel.transform, Load(LabelPlate), Color.white, new Vector2(260f, 80f));
            tab.rectTransform.anchoredPosition = new Vector2(-300f + 300f * i, 250f);
            var t = Text(tab.transform, names[i], 40f, UIMotionFx.Cream, Vector2.zero, new Vector2(240f, 80f));
            t.fontSharedMaterial = UIMotionFx.OutlinedMaterial(t.font);
            tabs[i] = tab.rectTransform;
        }
        var underline = TabSwitcher.CreateUnderline(tabs[0], 8f);
        var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(panel.transform, false);
        content.sizeDelta = new Vector2(900f, 420f);
        content.anchoredPosition = new Vector2(0f, -60f);
        var icons = new Image[3];
        for (int i = 0; i < 3; i++) icons[i] = Icon(content, i, new Vector2(-280f + 280f * i, 20f), 170f);
        var label = Text(content, "武器の棚", 44f, UIMotionFx.Cream, new Vector2(0f, -150f));
        void Show(int tab)
        {
            for (int i = 0; i < 3; i++) icons[i].sprite = Load(Icons[(i + tab * 2) % Icons.Length]);
            label.text = names[tab] + "の棚";
        }
        c.At(0.4f, () => { TabSwitcher.MoveUnderline(underline, tabs[1]); Show(1); TabSwitcher.SlideContent(content, +1); });
        c.At(1.3f, () => { TabSwitcher.MoveUnderline(underline, tabs[2]); Show(2); TabSwitcher.SlideContent(content, +1); });
        c.At(1.95f, () => { TabSwitcher.MoveUnderline(underline, tabs[0]); Show(0); TabSwitcher.SlideContent(content, -1); });
    }

    private static void BuildIdle(Ctx c)
    {
        Caption(c, "#8 アイドル  IdleBob / IconGlint（レア）/ NewBadge");
        for (int i = 0; i < 4; i++)
        {
            var slot = Slot(c.Root, new Vector2(-480f + 320f * i, 40f), 220f);
            var icon = Icon(slot.transform, i, Vector2.zero, 150f);
            c.At(0f, () => IdleBob.Attach(icon.rectTransform));
            if (i == 1) c.At(0f, () => IconGlint.Attach(icon.rectTransform, 1.4f));
            if (i == 3) c.At(0f, () => NewBadge.Attach(slot.rectTransform));
            Text(slot.transform, i == 1 ? "レア" : i == 3 ? "新商品" : "商品", 32f, UIMotionFx.Ink, new Vector2(0f, -140f));
        }
    }

    private static void BuildToast(Ctx c)
    {
        Caption(c, "#9 トースト  Toast.Show");
        Panel(c.Root, PanelFrame, new Vector2(1000f, 520f), new Vector2(0f, -120f), 1.6f);
        c.At(0.1f, () => Toast.Show("新しい商品「魔法の巻物」が入荷しました", Load(Icons[4])));
        c.At(0.8f, () => Toast.Show("実績「はじめての大儲け」を解除！", Load(Icons[1]), ToastKind.Good));
        c.At(1.4f, () => Toast.Show("回復薬が売り切れました", null, ToastKind.Warn, 1.6f));
    }

    private static void BuildGauge(Ctx c)
    {
        Caption(c, "#10 ゲージの残像  TrailGauge.SetValue（減る→増える）");
        var monster = UIMotionFx.NewImage("Monster", c.Root, Load(Monster), Color.white, new Vector2(440f, 440f));
        monster.preserveAspect = true;
        monster.rectTransform.anchoredPosition = new Vector2(0f, 120f);
        var frame = Panel(c.Root, WoodPlate, new Vector2(760f, 90f), new Vector2(0f, -120f), 1.4f);
        var back = UIMotionFx.NewImage("Back", frame.transform, UIMotionFx.WhiteSprite, new Color(0.15f, 0.08f, 0.05f, 1f), new Vector2(680f, 40f));
        var fill = UIMotionFx.NewImage("Fill", back.transform, UIMotionFx.WhiteSprite, UIMotionFx.Red, new Vector2(680f, 40f));
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 0.8f;
        TrailGauge gauge = null;
        c.At(0f, () => gauge = TrailGauge.Attach(fill));
        c.At(0.25f, () => { gauge.SetValue(0.45f); DamageNumber.Spawn(monster.rectTransform, 350); });
        c.At(1.6f, () => { gauge.SetValue(0.7f); DamageNumber.Spawn(monster.rectTransform, 250, DamageKind.Heal); });
    }

    private static void BuildCards(Ctx c)
    {
        Caption(c, "#11 カードめくり  CardFlip.FlipAll（3 枚目がレア）");
        var cards = new List<RectTransform>();
        var faces = new List<(Image body, Image icon, TextMeshProUGUI label)>();
        for (int i = 0; i < 3; i++)
        {
            var card = Panel(c.Root, PanelFrame, new Vector2(300f, 420f), new Vector2(-380f + 380f * i, 30f), 3f);
            var icon = Icon(card.transform, i + 1, new Vector2(0f, 40f), 170f);
            icon.enabled = false;
            var label = Text(card.transform, "？", 110f, UIMotionFx.Gold2, new Vector2(0f, 20f));
            label.fontSharedMaterial = UIMotionFx.OutlinedMaterial(label.font);
            cards.Add(card.rectTransform);
            faces.Add((card, icon, label));
        }
        string[] names = { "商人の証", "呪いの帳簿", "★減免状" };
        c.At(0.25f, () => CardFlip.FlipAll(cards, i =>
        {
            var f = faces[i];
            f.body.sprite = Load(i == 2 ? PanelFrameGold : Paper);
            f.icon.enabled = true;
            f.label.text = names[i];
            f.label.fontSize = 40f;
            f.label.color = UIMotionFx.Ink;
            var plain = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PlainFont);
            if (plain != null) f.label.font = plain;
            f.label.fontSharedMaterial = f.label.font.material;
            f.label.rectTransform.anchoredPosition = new Vector2(0f, -140f);
        }, i => i == 2));
    }

    private static void BuildPrice(Ctx c)
    {
        Caption(c, "#12 値段の変化  PriceChangeFx.Play（上がる・下がる）");
        TextMeshProUGUI MakeRow(string name, string price, float y, out Image bg)
        {
            bg = UIMotionFx.NewImage("Row", c.Root, Load(CreamBox), Color.white, new Vector2(800f, 130f));
            if (bg.type == Image.Type.Sliced) bg.pixelsPerUnitMultiplier = 0.6f;
            bg.rectTransform.anchoredPosition = new Vector2(0f, y);
            Icon(bg.transform, name == "鉄の剣" ? 0 : 2, new Vector2(-310f, 0f), 100f);
            Text(bg.transform, name, 44f, UIMotionFx.Ink, new Vector2(-90f, 0f), new Vector2(300f, 80f));
            var p = Text(bg.transform, price, 56f, UIMotionFx.Ink, new Vector2(200f, 0f), new Vector2(260f, 80f));
            p.alignment = TextAlignmentOptions.MidlineRight;
            return p;
        }
        var up = MakeRow("鉄の剣", "1,200G", 140f, out var bgUp);
        var down = MakeRow("回復薬", "300G", -100f, out var bgDown);
        c.At(0.3f, () => { up.text = "1,450G"; PriceChangeFx.Play(up, +1, bgUp); });
        c.At(1.2f, () => { down.text = "240G"; PriceChangeFx.Play(down, -1, bgDown); });
    }

    private static void BuildDrag(Ctx c)
    {
        Caption(c, "#13 つかんで置く  DragFeel.Lift / SetSlotGlow / Drop");
        var from = Slot(c.Root, new Vector2(-360f, 40f), 210f);
        var to = Slot(c.Root, new Vector2(360f, 40f), 210f);
        Text(from.transform, "倉庫", 34f, UIMotionFx.Ink, new Vector2(0f, -140f));
        Text(to.transform, "棚", 34f, UIMotionFx.Ink, new Vector2(0f, -140f));
        var item = Icon(c.Root, 0, from.rectTransform.anchoredPosition, 150f);
        Vector2 a = from.rectTransform.anchoredPosition;
        Vector2 b = to.rectTransform.anchoredPosition;
        const float moveFrom = 0.35f, moveTo = 1.05f;
        c.At(0.2f, () => { DragFeel.Lift(item.rectTransform); DragFeel.SetSlotGlow(to.rectTransform, true); });
        c.Every((t, dt) =>
        {
            if (t < moveFrom || t > moveTo + 0.01f) return;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(moveFrom, moveTo, t));
            item.rectTransform.anchoredPosition = Vector2.Lerp(a, b, k) + new Vector2(0f, Mathf.Sin(k * Mathf.PI) * 60f);
        });
        c.At(moveTo + 0.05f, () => { DragFeel.SetSlotGlow(to.rectTransform, false); DragFeel.Drop(item.rectTransform); });
    }

    private static void BuildDamage(Ctx c)
    {
        Caption(c, "#14 ダメージ数字  DamageNumber.Spawn（ふつう・クリティカル・回復）");
        var monster = UIMotionFx.NewImage("Monster", c.Root, Load(Monster), Color.white, new Vector2(560f, 560f));
        monster.preserveAspect = true;
        monster.rectTransform.anchoredPosition = new Vector2(0f, 40f);
        c.At(0.1f, () => { DamageNumber.Spawn(monster.rectTransform, 120); UIMotion.Shake(monster.rectTransform, 10f); });
        c.At(0.5f, () => { DamageNumber.Spawn(monster.rectTransform, 480, DamageKind.Critical); UIMotion.Shake(monster.rectTransform, 16f); });
        c.At(0.9f, () => DamageNumber.Spawn(monster.rectTransform, 95));
        c.At(1.3f, () => DamageNumber.Spawn(monster.rectTransform, 60, DamageKind.Heal));
    }

    private static void BuildComments(Ctx c)
    {
        Caption(c, "#15 配信コメントの強調  CommentHighlight.Apply（大きなコメント・スパチャだけ）");
        var area = new GameObject("CommentArea", typeof(RectTransform)).GetComponent<RectTransform>();
        area.SetParent(c.Root, false);
        area.sizeDelta = new Vector2(1700f, 800f);
        area.anchoredPosition = new Vector2(0f, 40f);
        area.gameObject.AddComponent<RectMask2D>();
        var monster = UIMotionFx.NewImage("Monster", area, Load(Monster), new Color(1f, 1f, 1f, 0.6f), new Vector2(560f, 560f));
        monster.preserveAspect = true;

        var lines = new (float at, string text, int lane, float size, CommentHighlightStyle? style)[]
        {
            (0.0f, "わこつ", 0, 38f, null),
            (0.1f, "その剣いくら？", 3, 38f, null),
            (0.3f, "8888888", 5, 38f, null),
            (0.45f, "神回きたああああ", 2, 54f, CommentHighlightStyle.Big),
            (0.7f, "草", 6, 38f, null),
            (1.0f, "【500G】応援してます！", 4, 46f, CommentHighlightStyle.SuperChat),
            (1.3f, "強すぎｗ", 1, 38f, null),
            (1.6f, "がんばれー", 6, 38f, null),
        };
        const float flow = 4f;
        float width = area.sizeDelta.x;
        foreach (var l in lines)
        {
            var label = UIMotionFx.NewText("NicoComment", area, l.text, l.size, Color.white, Vector2.zero);
            label.fontSharedMaterial = UIMotionFx.OutlinedMaterial(label.font);
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            float w = Mathf.Ceil(label.GetPreferredValues(l.text, 4000f, 100f).x) + 8f;
            rt.sizeDelta = new Vector2(w, Mathf.Max(50f, l.size * 1.3f));
            float y = -56f - l.lane * 100f;
            float speed = (width + w) / flow;
            label.gameObject.SetActive(false);
            var entry = l;
            c.At(entry.at, () =>
            {
                label.gameObject.SetActive(true);
                rt.anchoredPosition = new Vector2(width * 0.75f, y);
                if (entry.style.HasValue) CommentHighlight.Apply(rt, entry.style.Value);
            });
            c.Every((t, dt) =>
            {
                if (t <= entry.at || !label.gameObject.activeSelf) return;
                rt.anchoredPosition += new Vector2(-speed * dt, 0f);
            });
        }
    }

    private static void BuildFlipCounter(Ctx c)
    {
        Caption(c, "#16 日付めくり  FlipCounter.Flip");
        var plate = Panel(c.Root, WoodPlateGold, new Vector2(520f, 220f), new Vector2(0f, 60f), 0.8f);
        var day = Text(plate.transform, "3日目", 96f, UIMotionFx.Cream, Vector2.zero, new Vector2(460f, 180f));
        day.fontSharedMaterial = UIMotionFx.OutlinedMaterial(day.font);
        c.At(0.3f, () => FlipCounter.Flip(day, "4日目"));
        c.At(1.3f, () => FlipCounter.Flip(day, "5日目"));
    }

    private static void BuildDoor(Ctx c)
    {
        // 場面 A: 店のホーム（店の絵に看板と施設のボタン）。場面 B: 鍛冶屋の背景。GamePanelManager と同じく即座に切り替え、扉はその上にかぶせる
        var sceneAGo = new GameObject("ShopHome", typeof(RectTransform));
        var sceneA = (RectTransform)sceneAGo.transform;
        sceneA.SetParent(c.Root, false);
        UIMotionFx.Stretch(sceneA);
        ScenePlate(sceneA, "トムトムの店");
        string[] names = { "鍛冶屋", "情報屋", "勇者", "占い師" };
        Button smith = null;
        for (int i = 0; i < names.Length; i++)
        {
            var b = MakeButton(sceneA, names[i], new Vector2(-480f + 320f * i, -250f), new Vector2(280f, 110f), i == 0 ? GoldBtnOff : BrownBtn);
            if (i == 0) smith = b;
        }

        var sceneBGo = new GameObject("BlackSmith", typeof(RectTransform));
        var sceneB = (RectTransform)sceneBGo.transform;
        sceneB.SetParent(c.Root, false);
        UIMotionFx.Stretch(sceneB);
        var bgB = UIMotionFx.NewImage("BgB", sceneB, Load(BlackSmithBg), Color.white, Vector2.zero);
        bgB.type = Image.Type.Simple;
        UIMotionFx.Stretch(bgB.rectTransform);
        sceneBGo.SetActive(false);

        Caption(c, "#17 両開きの扉  DoorTransition.Play(\"鍛冶屋\")（店から施設に入るとき）");
        c.At(0.25f, () => UIMotion.Bump(smith.transform, 0.92f, 0.16f, true));
        c.At(0.45f, () => DoorTransition.Play(() =>
        {
            sceneAGo.SetActive(false);
            sceneBGo.SetActive(true);
        }, "鍛冶屋"));
    }

    // =====================================================
    // 省略モードの確認（-executeMethod UIMotionCapture.RunSelfTest。-nographics で可）
    // =====================================================

    /// <summary>省略モードと通常モード（最後まで進めたとき）で最終状態が同じかを確かめる。</summary>
    public static void RunSelfTest()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            if (ok) Debug.Log($"{Tag} OK: {what}");
            else { failures++; Debug.LogError($"{Tag} NG: {what}"); }
        }
        try
        {
            UIMotion.ManualUpdate = true;
            foreach (var mode in new[] { UIMotionMode.Skip, UIMotionMode.Normal, UIMotionMode.Fast })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                DOTween.KillAll();
                UIMotionFx.ResetLayers();
                UIMotion.ForcedMode = mode;
                string m = mode.ToString();
                var root = new GameObject("Root", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
                var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
                panel.SetParent(root, false);
                panel.gameObject.SetActive(false);
                var text = UIMotionFx.NewText("Money", root, "100", 30f, Color.white, new Vector2(200f, 50f));
                var rows = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
                rows.SetParent(root, false);
                for (int i = 0; i < 3; i++) UIMotionFx.NewImage("Row", rows, UIMotionFx.WhiteSprite, Color.white, new Vector2(100f, 20f)).rectTransform.anchoredPosition = new Vector2(0f, -30f * i);
                var fillImg = UIMotionFx.NewImage("Fill", root, UIMotionFx.WhiteSprite, Color.red, new Vector2(100f, 10f));
                fillImg.type = Image.Type.Filled;
                fillImg.fillAmount = 1f;
                var gauge = TrailGauge.Attach(fillImg);
                var day = UIMotionFx.NewText("Day", root, "3日目", 30f, Color.white, new Vector2(200f, 50f));
                bool closedCb = false, celebrated = false, flipped = false, flipDone = false;
                var card = UIMotionFx.NewImage("Card", root, UIMotionFx.WhiteSprite, Color.white, new Vector2(100f, 140f)).rectTransform;

                UIMotion.Open(panel.gameObject);
                UIMotion.CountTo(text, 100, 900, v => v + "G");
                UIMotion.Stagger(rows);
                gauge.SetValue(0.3f);
                FlipCounter.Flip(day, "4日目");
                Celebration.Play("S", null, () => celebrated = true);
                CardFlip.Flip(card, () => flipped = true, 0f, true, () => flipDone = true);
                for (int i = 0; i < 120; i++) DOTween.ManualUpdate(1f / 30f, 1f / 30f);

                Check(panel.gameObject.activeSelf && panel.localScale == Vector3.one && panel.GetComponent<CanvasGroup>().alpha == 1f, $"{m} Open → 表示・等倍・alpha 1");
                Check(text.text == "900G", $"{m} CountTo → 900G（{text.text}）");
                Check(rows.Cast<Transform>().All(r => r.GetComponent<CanvasGroup>().alpha == 1f), $"{m} Stagger → 全行 alpha 1");
                Check(((RectTransform)rows.GetChild(2)).anchoredPosition == new Vector2(0f, -60f), $"{m} Stagger → 位置が元どおり");
                Check(Mathf.Approximately(fillImg.fillAmount, 0.3f) && Mathf.Approximately(gauge.Value, 0.3f), $"{m} TrailGauge → 0.3");
                Check(day.text == "4日目" && day.alpha == 1f && day.rectTransform.Find("FlipPage") == null, $"{m} FlipCounter → 4日目・複製なし");
                Check(celebrated, $"{m} Celebration → onDone");
                Check(flipped && flipDone && card.localRotation == Quaternion.identity, $"{m} CardFlip → 表・回転なし");

                UIMotion.Close(panel.gameObject, () => closedCb = true);
                if (mode == UIMotionMode.Skip) Check(!panel.gameObject.activeSelf, $"{m} Close → 即 SetActive(false)");
                else Check(UIMotion.IsClosing(panel.gameObject), $"{m} Close → 閉じ演出中");
                for (int i = 0; i < 30; i++) DOTween.ManualUpdate(1f / 30f, 1f / 30f);
                Check(!panel.gameObject.activeSelf && closedCb && panel.localScale == Vector3.one && panel.GetComponent<CanvasGroup>().alpha == 1f,
                    $"{m} Close → 非表示・onDone・拡縮と alpha は元どおり");

                RegressionChecks(root, m, Check);
                DoorChecks(m, mode, Check);
            }
            // 省略要求と見せる再生の優先順
            UIMotion.ForcedMode = null;
            UIMotion.SetSkip("SelfTest", true);
            Check(UIMotion.IsSkip, "SetSkip → Skip");
            UIMotion.SetShowcase("SelfTest", true);
            Check(!UIMotion.IsSkip, "SetShowcase は SetSkip より優先");
            UIMotion.SetShowcase("SelfTest", false);
            UIMotion.SetSkip("SelfTest", false);
            Check(!UIMotion.IsSkip, "要求を外すと通常／速い");
        }
        catch (Exception e)
        {
            failures++;
            Debug.LogError($"{Tag} 自己テストで例外: {e}");
        }
        finally
        {
            DOTween.KillAll();
            UIMotion.ManualUpdate = false;
            UIMotion.ForcedMode = null;
            UIMotionFx.ResetLayers();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        Debug.Log($"{Tag} 自己テスト終了: NG {failures} 件");
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    /// <summary>#17 扉: 画面の切り替え（onClosed）は呼んだ瞬間に同期で行い、扉は入力を受けず、最後は何も残らない。</summary>
    private static void DoorChecks(string m, UIMotionMode mode, Action<bool, string> Check)
    {
        void Step(int frames) { for (int i = 0; i < frames; i++) DOTween.ManualUpdate(1f / 30f, 1f / 30f); }
        bool switched = false;
        DoorTransition.Play(() => switched = true, "鍛冶屋");
        Check(switched, $"{m} DoorTransition → onClosed は同期で呼ばれる");
        if (mode == UIMotionMode.Skip)
        {
            Check(!DoorTransition.IsPlaying, $"{m} DoorTransition → 省略では扉を出さない");
            return;
        }
        Check(DoorTransition.IsPlaying, $"{m} DoorTransition → 扉が出ている");
        var layer = UIMotionFx.Layer(DoorTransition.SortingOrder);
        Check(layer.GetComponent<GraphicRaycaster>() == null && layer.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget),
            $"{m} DoorTransition → 入力を受けない（GraphicRaycaster なし・raycastTarget なし）");
        var leaves = layer.GetComponentsInChildren<DoorLeafGraphic>(true);
        Check(leaves.Length == 2 && leaves.All(l => l.TryGetComponent<CanvasRenderer>(out _)), $"{m} DoorTransition → 扉 2 枚が描かれる（CanvasRenderer あり）");
        // 扉の途中でもう一度（連続で別の施設）→ 扉は 1 組だけ
        Step(6);
        DoorTransition.Play("情報屋");
        Check(layer.childCount == 1 && DoorTransition.IsPlaying, $"{m} DoorTransition（途中で 2 回目）→ 扉は 1 組（{layer.childCount}）");
        float total = DoorTransition.Duration / UIMotion.Speed;
        Step(Mathf.FloorToInt(total * 30f) - 2);
        Check(DoorTransition.IsPlaying, $"{m} DoorTransition → {total:0.##} 秒の手前ではまだ出ている");
        Step(4);
        Check(!DoorTransition.IsPlaying && layer.childCount == 0, $"{m} DoorTransition → {total:0.##} 秒で消える（残り {layer.childCount}）");
        DoorTransition.Play("勇者");
        DoorTransition.Stop();
        Check(!DoorTransition.IsPlaying && layer.childCount == 0, $"{m} DoorTransition.Stop → すぐ消える");
    }

    /// <summary>
    /// 2 回開く・途中で打ち切る・一覧を作り直す、で位置と大きさが元に戻るか（2026-10-30 の Play の報告の再発防止）。
    /// ・店の設備／情報屋／鍛冶屋／魔王軍支援: 一覧を作り直すと Destroy した古い行がフレームの終わりまで残り、新しい行が一瞬下に並ぶ。
    ///   その位置を Stagger が覚えて戻していたので、2 回目に開くと行が下にずれて押せなくなった
    /// ・窓がパネルと別の矩形のとき、開きかけに開き直すと縮んだ大きさを基準として覚えていた
    /// </summary>
    private static void RegressionChecks(RectTransform root, string m, Action<bool, string> Check)
    {
        void Step(int frames) { for (int i = 0; i < frames; i++) DOTween.ManualUpdate(1f / 30f, 1f / 30f); }
        bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 0.01f;
        bool NearScale(Vector3 a) => (a - Vector3.one).sqrMagnitude < 1e-6f;

        // --- Stagger: レイアウトの中で作り直す（古い行が残った状態で並べ、あとで消えてレイアウトが直す） ---
        var list = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
        list.SetParent(root, false);
        list.sizeDelta = new Vector2(300f, 600f);
        var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = vlg.childControlWidth = false;
        vlg.childForceExpandHeight = vlg.childForceExpandWidth = false;
        vlg.spacing = 10f;
        List<RectTransform> MakeRows(int n)
        {
            var made = new List<RectTransform>();
            for (int i = 0; i < n; i++) made.Add(UIMotionFx.NewImage("Row", list, UIMotionFx.WhiteSprite, Color.white, new Vector2(200f, 40f)).rectTransform);
            return made;
        }
        for (int open = 1; open <= 3; open++)
        {
            var old = list.Cast<Transform>().Select(t => t.gameObject).ToList(); // 前回の行（Destroy はフレームの終わりまで残る）
            var rows = MakeRows(4);
            UIMotion.Stagger(rows, 0.05f);
            Step(2);
            foreach (var go in old) UnityEngine.Object.DestroyImmediate(go); // フレームの終わりに消える
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);              // 次の描画でレイアウトが直す
            if (open == 2)
            {
                Step(2);
                UIMotion.Stagger(rows, 0.05f); // 途中でもう一度（開き直し）
            }
            if (open == 3)
            {
                Step(2);
                list.gameObject.SetActive(false); // 途中で閉じる
                list.gameObject.SetActive(true);
            }
            Step(60);
            var shown = rows.Select(r => r.anchoredPosition).ToList();
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            bool same = rows.Select(r => r.anchoredPosition).Zip(shown, Near).All(x => x);
            Check(same && rows.All(r => r.GetComponent<CanvasGroup>().alpha == 1f),
                $"{m} Stagger（レイアウトの作り直し {open} 回目{(open == 2 ? "・途中で開き直し" : open == 3 ? "・途中で閉じる" : "")}）→ レイアウトどおりの位置・alpha 1（{shown[0]} / {rows[0].anchoredPosition}）");
        }
        UnityEngine.Object.DestroyImmediate(list.gameObject);

        // --- Stagger: レイアウトの外。途中で打ち切っても元の位置 ---
        var free = UIMotionFx.NewImage("Free", root, UIMotionFx.WhiteSprite, Color.white, new Vector2(80f, 30f)).rectTransform;
        free.anchoredPosition = new Vector2(15f, -40f);
        UIMotion.Stagger(new List<RectTransform> { free });
        Step(2);
        UIMotion.Stagger(new List<RectTransform> { free });
        Step(2);
        DOTween.Kill(free.gameObject);
        Check(Near(free.anchoredPosition, new Vector2(15f, -40f)), $"{m} Stagger（2 回・途中で Kill）→ 元の位置（{free.anchoredPosition}）");

        // --- Open: 窓がパネルと別の矩形。開きかけに開き直す・閉じる ---
        var popup = new GameObject("Popup", typeof(RectTransform)).GetComponent<RectTransform>();
        popup.SetParent(root, false);
        popup.anchorMin = Vector2.zero;
        popup.anchorMax = Vector2.one;
        var window = UIMotionFx.NewImage("Window", popup, UIMotionFx.WhiteSprite, Color.white, new Vector2(400f, 300f)).rectTransform;
        popup.gameObject.SetActive(false);
        UIMotion.Open(popup.gameObject, window);
        Step(2);
        UIMotion.Open(popup.gameObject, window); // 開きかけにもう一度
        Step(30);
        Check(NearScale(window.localScale), $"{m} Open（開きかけに 2 回目）→ 窓は等倍（{window.localScale.x:0.###}）");
        UIMotion.Open(popup.gameObject, window);
        Step(2);
        UIMotion.Close(popup.gameObject, null, window); // 開きかけに閉じる
        Step(30);
        Check(!popup.gameObject.activeSelf && NearScale(window.localScale), $"{m} Open→途中で Close → 非表示・窓は等倍（{window.localScale.x:0.###}）");
        UIMotion.Open(popup.gameObject, window);
        Step(30);
        Check(popup.gameObject.activeSelf && NearScale(window.localScale) && popup.GetComponent<CanvasGroup>().alpha == 1f, $"{m} もう一度 Open → 等倍・alpha 1");
        UIMotion.Close(popup.gameObject, null, window);
        Step(2);
        UIMotion.Open(popup.gameObject, window); // 閉じかけに開き直す
        Step(30);
        Check(popup.gameObject.activeSelf && NearScale(window.localScale), $"{m} 閉じかけに Open → 表示・等倍（{window.localScale.x:0.###}）");

        // --- TabSwitcher.SlideContent: 2 回・途中で位置が書き直される ---
        var content = UIMotionFx.NewImage("Content", root, UIMotionFx.WhiteSprite, Color.white, new Vector2(300f, 200f)).rectTransform;
        content.anchoredPosition = new Vector2(10f, 20f);
        TabSwitcher.SlideContent(content, 1);
        Step(2);
        TabSwitcher.SlideContent(content, -1);
        Step(30);
        Check(Near(content.anchoredPosition, new Vector2(10f, 20f)), $"{m} SlideContent（途中で逆向き）→ 元の位置（{content.anchoredPosition}）");
        TabSwitcher.SlideContent(content, 1);
        Step(2);
        content.anchoredPosition = new Vector2(50f, -5f); // レイアウト・他のコードが動かした
        Step(30);
        Check(Near(content.anchoredPosition, new Vector2(50f, -5f)), $"{m} SlideContent（途中で位置が変わる）→ 新しい位置のまま（{content.anchoredPosition}）");

        // --- Shake: 2 回続けて ---
        var btn = UIMotionFx.NewImage("Btn", root, UIMotionFx.WhiteSprite, Color.white, new Vector2(100f, 40f)).rectTransform;
        btn.anchoredPosition = new Vector2(-30f, 5f);
        UIMotion.Shake(btn);
        Step(2);
        UIMotion.Shake(btn);
        Step(30);
        Check(Near(btn.anchoredPosition, new Vector2(-30f, 5f)), $"{m} Shake（2 回）→ 元の位置（{btn.anchoredPosition}）");

        // --- DragFeel: 持ち上げ・置くを繰り返しても大きさは等倍に戻る／光る枠は隣と重ならない ---
        var ghost = UIMotionFx.NewImage("Ghost", root, UIMotionFx.WhiteSprite, Color.white, new Vector2(90f, 90f)).rectTransform;
        for (int k = 0; k < 3; k++)
        {
            DragFeel.Lift(ghost);
            Step(k); // 0〜2 フレームで次へ（持ち上げの途中）
            DragFeel.Lift(ghost);
            Step(1);
            DragFeel.Drop(ghost, placed: k % 2 == 0);
            Step(30);
        }
        Check(NearScale(ghost.localScale) && ghost.localRotation == Quaternion.identity, $"{m} DragFeel（Lift 2 回→Drop を 3 回）→ 等倍・傾きなし（{ghost.localScale.x:0.###}）");
        DragFeel.Lift(ghost);
        Step(10);
        Check(m == nameof(UIMotionMode.Skip) ? NearScale(ghost.localScale) : Mathf.Abs(ghost.localScale.x - 1.12f) < 0.01f,
            $"{m} DragFeel.Lift → 1.12 倍（省略は等倍のまま）（{ghost.localScale.x:0.###}）");
        ghost.gameObject.SetActive(false);
        DragFeel.Drop(ghost);

        var sale = new GameObject("SaleRow", typeof(RectTransform)).GetComponent<RectTransform>();
        sale.SetParent(root, false);
        var slots = new List<RectTransform>();
        for (int i = 0; i < 3; i++)
        {
            var s = UIMotionFx.NewImage("Slot", sale, UIMotionFx.WhiteSprite, Color.white, new Vector2(154f, 160f)).rectTransform;
            s.anchoredPosition = new Vector2(-123f + 123f * i, 0f); // 売り場と同じ: 154 幅を 123 間隔（矩形は重なり、絵の余白で間が空く）
            slots.Add(s);
        }
        foreach (var s in slots) DragFeel.SetSlotGlow(s, true);
        Step(20);
        var g0 = (RectTransform)slots[0].Find("DropGlow");
        var g1 = (RectTransform)slots[1].Find("DropGlow");
        var c0 = new Vector3[4];
        var c1 = new Vector3[4];
        g0.GetWorldCorners(c0);
        g1.GetWorldCorners(c1);
        Check(c0[2].x <= c1[0].x + 0.01f, $"{m} SetSlotGlow → 隣の枠の光と重ならない（右端 {sale.InverseTransformPoint(c0[2]).x:0.#} ≤ 左端 {sale.InverseTransformPoint(c1[0]).x:0.#}）");
        var lone = UIMotionFx.NewImage("LoneSlot", root, UIMotionFx.WhiteSprite, Color.white, new Vector2(100f, 100f)).rectTransform;
        DragFeel.SetSlotGlow(lone, true);
        Step(20);
        var lg = (RectTransform)lone.Find("DropGlow");
        lg.GetWorldCorners(c0);
        lone.GetWorldCorners(c1);
        Check(c0[0].x >= c1[0].x - 0.01f && c0[2].x <= c1[2].x + 0.01f && c0[0].y >= c1[0].y - 0.01f && c0[2].y <= c1[2].y + 0.01f,
            $"{m} SetSlotGlow → 枠の内側に収まる");
        foreach (var s in slots) DragFeel.SetSlotGlow(s, false);
        DragFeel.SetSlotGlow(lone, false);
        Check(slots.All(s => s.Find("DropGlow") == null) && lone.Find("DropGlow") == null, $"{m} SetSlotGlow(false) → 光が消える");
    }

    // =====================================================

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
