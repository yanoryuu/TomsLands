using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 村シーン（VillageScene）の VillageUI（HUD の看板・村資金・出店準備ボタン・投資パネル）を組み直して保存する。
/// 見た目は案ごと（-uiVariant A/B/C）に変えられる:
///   A = 木の看板と金縁（正統派） / B = 羊皮紙・巻物（明るく軽い） / C = 紺に金（王都らしい豪華版）
/// 本番は案A（ユーザーが選んだ。-uiVariant を省くと A）。B/C は比較用に残してある。
/// あわせて「タイトルへ」・メッセージの帯・帰還ポップ・記録掲示板を同じ見た目にし、
/// マップの格の装飾（VillageRankDecor.prefab）をシーンのルートに置いて遮蔽シルエットの対象に足す。
/// 何度実行しても部品は名前で探して作り直さない（重複しない）。
/// バッチ: Unity.exe -batchmode -projectPath &lt;wt&gt; -executeMethod VillageUIBuilder.BuildBatch [-uiVariant A] -logFile &lt;log&gt;
/// 部品はすべて既存の UI 素材（Assets/UI/Village/Parts は 9 スライス用に複製・加工したもの）。
/// </summary>
public static class VillageUIBuilder
{
    private const string Tag = "[VillageUIBuilder]";
    private const string ScenePath = "Assets/Scene/VillageScene.unity";
    private const string PartsDir = "Assets/UI/Village/Parts/";

    private const string FontOutline = "Assets/Font/MPLUSRounded1c-Bold SDFPlusPadding.asset";
    private const string FontPlain = "Assets/Font/MPLUSRounded1c-Bold SDFPlusPaddingNoOutLine.asset";

    // 色（ui-rules の目安）
    private static readonly Color DarkBrown = Hex("5A3A24");
    private static readonly Color Gold = Hex("E8A33D");
    private static readonly Color BrightGold = Hex("FFD966");
    private static readonly Color Cream = Hex("F5E9D5");
    private static readonly Color Red = Hex("A8332B");

    // ───────── 入口 ─────────

    public static void BuildBatch()
    {
        try
        {
            string variant = (Arg("-uiVariant") ?? "A").Trim().ToUpperInvariant();
            Build(variant);
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{Tag} 失敗: {e}");
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/TomsLands/村UIを組み直す（案A）")]
    private static void BuildA() => Build("A");

    public static void Build(string variant)
    {
        ImportParts();
        var style = StyleOf(variant);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "VillageUI");
        if (root == null) throw new System.InvalidOperationException("VillageUI が無い");
        var view = root.GetComponent<VillageView>();
        var so = new SerializedObject(view);

        BuildHud(root.transform, so, style);
        BuildDepart(so, style);
        BuildTitleAndMessage(root.transform, so, style);
        BuildInvestPanel(so, style);
        BuildPopups(so, style);
        // ポップは HUD より手前に描く（HUD の部品を作り足すと後ろに並ぶので、最後に並べ直す）
        foreach (var f in new[] { "investPanel", "conversionPopup", "recordBoardPopup" })
            Ref<GameObject>(so, f)?.transform.SetAsLastSibling();
        so.ApplyModifiedPropertiesWithoutUndo();
        PlaceRankDecor(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"{Tag} 案{variant} で組んで保存した");
    }

    // ───────── 案ごとの見た目 ─────────

    private class Style
    {
        public string Name;
        // 看板
        public Sprite Banner; public Color BannerColor = Color.white; public float BannerPpu = 1f;
        public Sprite BannerTop; // 王都の看板（null なら変えない）
        public Sprite BannerFrame; public float BannerFramePpu = 1f; // 上に重ねる金縁（任意）
        public bool NameOutline = true; public Color NameColor = Color.white; public Color RankColor = BrightGold;
        // 次の格の札
        public Sprite Plate; public Color PlateColor = Color.white; public float PlatePpu = 1f;
        public Sprite PlateFrame; public float PlateFramePpu = 1f;
        public bool PlateOutline = false; public Color PlateTextColor = Cream;
        // 飾り
        public bool PinsAtRank1; public bool FlagsAtRank1 = true; public bool CrestAtRank2; public Sprite TopEmblem;
        // 村資金
        public Sprite FundsBox; public Color FundsBoxColor = Color.white; public float FundsPpu = 1f; public Vector2 FundsSize;
        public Sprite FundsFrame; public float FundsFramePpu = 1f;
        public Sprite FundsCoin; public Vector2 FundsCoinPos; public float FundsTextLeft;
        public Color FundsTextColor = BrightGold;
        // 出店準備
        public Sprite Depart; public float DepartPpu = 1f; public Color DepartColor = Color.white; public Vector2 DepartSize;
        public float DepartPadX = 24f; public bool DepartOutline = true; public Color DepartTextColor = Color.white; public Sprite DepartIcon;
        // 投資パネル
        public Sprite Panel; public Color PanelColor = Color.white; public float PanelPpu = 1f;
        public Sprite PanelFrame; public float PanelFramePpu = 1f;
        public Sprite PanelPin;
        public bool HeadOutline = true; public Color HeadNameColor = Color.white; public Color HeadLevelColor = BrightGold;
        public Sprite Card; public Color CardColor = Color.white; public float CardPpu = 1f; public float CardInset = 16f; // 絵の透明な縁のぶん、文字を内側へ
        public bool CardOutline; public Color CardText = DarkBrown; public Color CardCaption = Hex("8B6244"); public Color CardNext = Hex("2E7D32");
        public bool CostOutline = true; public Color CostCaption = Hex("F5E9D5"); public Color CostFunds = Cream;
        public Color CostOk = Gold; public Color CostShort = Red;
        public Sprite IconFrame; public float IconFramePpu = 1f; public Sprite IconBack; public Color IconBackColor = Color.white;
        public Sprite Button; public Sprite ButtonDisabled; public float ButtonPpu = 1f; public Color ButtonTextColor = Color.white; public bool ButtonOutline = true;
        public Sprite Close; public Vector2 ClosePos = new(-46, -46); public float CloseSize = 64f;
        // タイトルへ（控えめなサブボタン）・メッセージの帯
        public Sprite SubButton; public float SubButtonPpu = 1f; public Color SubButtonTextColor = Cream;
        public Sprite MessageBand; public float MessageBandPpu = 1f; public Color MessageTextColor = Color.white;
        // 帰還ポップ・記録掲示板（null なら投資パネルと同じ）
        public Sprite Popup; public float PopupPpu = 1f; public Color PopupColor = Color.white;
        public Sprite Ledger; public float LedgerPpu = 1f; public Color LedgerColor = Color.white;
    }

    private static Style StyleOf(string v)
    {
        var goldBtn = Load("Assets/UI/新聞/4−0購読する.png");
        var goldBtnOff = Load("Assets/UI/新聞/4−1購読する.png");
        var goldFrame = Load("Assets/UI/Preparation/Prep_GoldFrame.png");
        var coinBox = Load("Assets/UI/基本/3ゴールド.png");
        var coin = Load("Assets/UI/Village/ArrivalFx/arrival_coin.png");
        var crown = Load("Assets/UI/新聞/6紋章_王立官報.png");
        var crest = Load("Assets/UI/レリック/百貨店の理念.png");
        var round = Part("vui_round_white");
        switch (v)
        {
            case "B":
                return new Style
                {
                    Name = "B",
                    Banner = Part("vui_scroll_banner"), BannerPpu = 1.7f, BannerTop = Part("vui_gold_plaque"),
                    NameOutline = false, NameColor = DarkBrown, RankColor = Hex("A0603A"),
                    Plate = Part("vui_sign_box"), PlatePpu = 1f, PlateTextColor = DarkBrown,
                    PinsAtRank1 = true, FlagsAtRank1 = false, CrestAtRank2 = false, TopEmblem = crown,
                    FundsBox = Part("vui_cream_box"), FundsPpu = 0.5f, FundsSize = new Vector2(410, 96),
                    FundsCoin = coin, FundsCoinPos = new Vector2(50, -48), FundsTextLeft = 98,
                    FundsTextColor = Gold,
                    Depart = Part("vui_scroll_banner"), DepartPpu = 2.2f, DepartSize = new Vector2(480, 124),
                    DepartPadX = 64f, DepartOutline = false, DepartTextColor = DarkBrown, DepartIcon = Load("Assets/UI/Preparation/Prep_IconFlyer.png"),
                    Panel = Part("vui_paper"), PanelPpu = 1.2f, PanelPin = Load("Assets/UI/新聞/21留め具.png"),
                    HeadOutline = false, HeadNameColor = DarkBrown, HeadLevelColor = Hex("B8741A"),
                    Card = Part("vui_article_big"), CardPpu = 2f, CardCaption = Hex("8B6244"),
                    CostOutline = false, CostCaption = Hex("8B6244"), CostFunds = Hex("6B4630"),
                    CostOk = Hex("B8741A"), CostShort = Red,
                    IconFrame = Load("Assets/UI/鍛冶屋・道具屋/8−0アイコン枠.png"), IconBack = round, IconBackColor = Hex("EFDFC8"),
                    Button = Part("vui_brown_btn"), ButtonPpu = 0.7f, ButtonTextColor = Cream,
                    Close = Load("Assets/UI/鍛冶屋・道具屋/9−1閉じる.png"),
                };
            case "C":
                var navy = Hex("1F2D52");
                return new Style
                {
                    Name = "C",
                    Banner = round, BannerColor = navy, BannerPpu = 1f, BannerFrame = goldFrame, BannerFramePpu = 0.9f,
                    NameColor = Hex("FFE08A"), RankColor = Cream,
                    Plate = round, PlateColor = Hex("16213D"), PlatePpu = 2f, PlateFrame = goldFrame, PlateFramePpu = 1.6f,
                    PlateTextColor = Hex("F3D9A0"),
                    PinsAtRank1 = false, FlagsAtRank1 = true, CrestAtRank2 = true, TopEmblem = crown,
                    FundsBox = round, FundsBoxColor = navy, FundsPpu = 1.4f, FundsSize = new Vector2(400, 104),
                    FundsFrame = goldFrame, FundsFramePpu = 1.2f,
                    FundsCoin = coin, FundsCoinPos = new Vector2(56, -52), FundsTextLeft = 104,
                    FundsTextColor = BrightGold,
                    Depart = Part("vui_gold_plaque"), DepartPpu = 1.6f, DepartSize = new Vector2(500, 136), DepartPadX = 84f,
                    DepartTextColor = Color.white,
                    Panel = round, PanelColor = navy, PanelPpu = 0.5f, PanelFrame = goldFrame, PanelFramePpu = 0.55f,
                    HeadNameColor = Hex("FFE08A"), HeadLevelColor = Cream,
                    Card = round, CardColor = Hex("2C3E6E"), CardPpu = 1.6f,
                    CardOutline = true, CardText = Color.white, CardCaption = Hex("F3C969"), CardNext = Hex("8FE3A0"),
                    CostCaption = Hex("F3C969"), CostFunds = Hex("D9DEEA"), CostOk = BrightGold, CostShort = Hex("FF6B5B"),
                    IconFrame = goldFrame, IconFramePpu = 0.8f, IconBack = round, IconBackColor = Hex("2C3E6E"),
                    Button = goldBtn, ButtonDisabled = goldBtnOff, ButtonPpu = 1.4f,
                    Close = Load("Assets/UI/鍛冶屋・道具屋/9−0閉じる.png"),
                };
            default:
                return new Style
                {
                    Name = "A",
                    Banner = Part("vui_wood_plate"), BannerPpu = 0.8f, BannerTop = goldBtn,
                    Plate = Part("vui_label_plate"), PlatePpu = 1f, PlateTextColor = Cream, PlateOutline = false,
                    PinsAtRank1 = false, FlagsAtRank1 = true, CrestAtRank2 = false, TopEmblem = crown,
                    FundsBox = coinBox, FundsSize = new Vector2(424, 130),
                    FundsTextLeft = 128,
                    Depart = goldBtn, DepartPpu = 1.2f, DepartSize = new Vector2(470, 136),
                    // 投資パネルの枠は右上の飾りを消した複製（× を置くため）。ポップは元の飾り付き
                    Panel = Part("vui_panel_frame"), PanelPpu = 1.9f,
                    Popup = Load("Assets/UI/新聞/1枠.png"), PopupPpu = 1.9f,
                    Ledger = Part("vui_label_plate"), LedgerPpu = 1f, LedgerColor = Hex("C9B8A6"),
                    SubButton = Part("vui_brown_btn"), SubButtonPpu = 1f, SubButtonTextColor = Cream,
                    MessageBand = Part("vui_wood_plate"), MessageBandPpu = 1.4f,
                    Card = Load("Assets/UI/新聞/13記事ボックス小.png"), CardPpu = 1.8f, CardInset = 46f, CardCaption = Hex("6B4630"),
                    CostCaption = Hex("F5E9D5"), CostFunds = Hex("F5E9D5"), CostOk = BrightGold, CostShort = Hex("FF7A66"),
                    IconFrame = goldFrame, IconFramePpu = 0.8f, IconBack = Load("Assets/UI/鍛冶屋・道具屋/8−1アイコン背景.png"),
                    Button = goldBtn, ButtonDisabled = Part("vui_gold_btn_off"), ButtonPpu = 1.4f,
                    Close = Load("Assets/UI/鍛冶屋・道具屋/9−0閉じる.png"), ClosePos = new Vector2(-74, -74), CloseSize = 56f,
                };
        }
    }

    // ───────── HUD ─────────

    private static void BuildHud(Transform root, SerializedObject so, Style s)
    {
        var fundsText = Ref<TextMeshProUGUI>(so, "villageFundsText");
        var levelText = Ref<TextMeshProUGUI>(so, "villageLevelText");
        var nextText = Ref<TextMeshProUGUI>(so, "rankNextText");

        // 村資金（左上の箱）
        var funds = NewImage("FundsBox", root, s.FundsBox, s.FundsBoxColor, s.FundsPpu);
        Place(funds.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -18), s.FundsSize);
        if (s.FundsFrame != null) Stretch(NewImage("Frame", funds.transform, s.FundsFrame, Color.white, s.FundsFramePpu).rectTransform, 0);
        if (s.FundsCoin != null)
        {
            var c = NewImage("Coin", funds.transform, s.FundsCoin, Color.white, 1f, sliced: false);
            Place(c.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), s.FundsCoinPos, new Vector2(64, 64));
        }
        if (fundsText != null)
        {
            fundsText.transform.SetParent(funds.transform, false);
            var rt = fundsText.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(s.FundsTextLeft, 6); rt.offsetMax = new Vector2(-30, -4);
            SetFont(fundsText, true);
            fundsText.fontSize = 44; fundsText.enableAutoSizing = true; fundsText.fontSizeMin = 24; fundsText.fontSizeMax = 44;
            fundsText.alignment = TextAlignmentOptions.MidlineLeft;
            fundsText.color = s.FundsTextColor;
            fundsText.textWrappingMode = TextWrappingModes.NoWrap;
            fundsText.text = "<size=64%>村資金</size> 0G";
        }

        // 看板（村の名前・格・総合Lv）
        var bannerSize = new Vector2(720, 136);
        var banner = NewImage("RankBanner", root, s.Banner, s.BannerColor, s.BannerPpu);
        Place(banner.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), bannerSize);
        if (s.BannerFrame != null) Stretch(NewImage("Frame", banner.transform, s.BannerFrame, Color.white, s.BannerFramePpu).rectTransform, 0);

        if (levelText != null)
        {
            levelText.transform.SetParent(banner.transform, false);
            Place(levelText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 22), new Vector2(560, 60));
            SetFont(levelText, s.NameOutline);
            levelText.fontSize = 50; levelText.enableAutoSizing = false; levelText.color = s.NameColor;
            levelText.alignment = TextAlignmentOptions.Center;
            levelText.text = "トコの村";
        }
        var rank = NewText("RankText", banner.transform, s.NameOutline, 30, s.RankColor, "町・総合Lv18");
        Place(rank.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(560, 40));
        so.FindProperty("rankText").objectReferenceValue = rank;
        so.FindProperty("rankBanner").objectReferenceValue = banner;
        var bannerSprites = so.FindProperty("rankBannerSprites");
        if (s.BannerTop != null)
        {
            bannerSprites.arraySize = 4;
            for (int i = 0; i < 3; i++) bannerSprites.GetArrayElementAtIndex(i).objectReferenceValue = s.Banner;
            bannerSprites.GetArrayElementAtIndex(3).objectReferenceValue = s.BannerTop;
        }
        else bannerSprites.arraySize = 0;

        // 星（格の印。看板の下の縁に重ねる）
        var stars = NewRect("RankStars", banner.transform);
        Place(stars, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(4 * 44 + 3 * 4, 44));
        var starOn = Load("Assets/UI/新聞/3−1星.png");
        var starOff = Load("Assets/UI/新聞/3−0星.png");
        var starProp = so.FindProperty("rankStars");
        starProp.arraySize = 4;
        for (int i = 0; i < 4; i++)
        {
            var st = NewImage($"Star{i}", stars, i == 0 ? starOn : starOff, Color.white, 1f, sliced: false);
            st.preserveAspect = true;
            Place(st.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 48, 0), new Vector2(44, 44));
            starProp.GetArrayElementAtIndex(i).objectReferenceValue = st;
        }
        so.FindProperty("rankStarOn").objectReferenceValue = starOn;
        so.FindProperty("rankStarOff").objectReferenceValue = starOff;

        // 飾り（格が上がるほど増える）
        var ornaments = new List<(GameObject go, int min, int max)>();
        var green = Load("Assets/UI/追加/緑旗.png");
        var red = Load("Assets/UI/追加/赤旗.png");
        if (s.FlagsAtRank1)
        {
            ornaments.AddRange(Flags(banner.transform, green, "FlagGreen", bannerSize).Select(g => (g, 1, 1)));
            ornaments.AddRange(Flags(banner.transform, red, "FlagRed", bannerSize).Select(g => (g, 2, -1)));
        }
        if (s.PinsAtRank1)
        {
            var pin = Load("Assets/UI/新聞/21留め具.png");
            foreach (var x in new[] { -1f, 1f })
            {
                var p = NewImage(x < 0 ? "PinL" : "PinR", banner.transform, pin, Color.white, 1f, sliced: false);
                p.preserveAspect = true;
                Place(p.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(x * (bannerSize.x * 0.5f - 48), -6), new Vector2(34, 56));
                ornaments.Add((p.gameObject, 1, -1));
            }
            ornaments.AddRange(Flags(banner.transform, red, "FlagRed", bannerSize).Select(g => (g, 2, -1)));
        }
        if (s.CrestAtRank2)
        {
            var crest = NewImage("Crest", banner.transform, Load("Assets/UI/レリック/百貨店の理念.png"), Color.white, 1f, sliced: false);
            crest.preserveAspect = true;
            Place(crest.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(64, 4), new Vector2(104, 104));
            ornaments.Add((crest.gameObject, 2, 2));
        }
        if (s.TopEmblem != null)
        {
            var crown = NewImage("Crown", banner.transform, s.TopEmblem, Color.white, 1f, sliced: false);
            crown.preserveAspect = true;
            Place(crown.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(66, 6), new Vector2(108, 108));
            ornaments.Add((crown.gameObject, 3, -1));
        }
        var ornProp = so.FindProperty("rankOrnaments");
        ornProp.arraySize = ornaments.Count;
        for (int i = 0; i < ornaments.Count; i++)
        {
            var e = ornProp.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("target").objectReferenceValue = ornaments[i].go;
            e.FindPropertyRelative("minRank").intValue = ornaments[i].min;
            e.FindPropertyRelative("maxRank").intValue = ornaments[i].max;
        }

        // 次の格の条件（看板の下の札。文字の幅に合わせて伸びる）
        if (nextText != null)
        {
            var plate = NewImage("RankNextPlate", root, s.Plate, s.PlateColor, s.PlatePpu);
            var prt = plate.rectTransform;
            Place(prt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16 - bannerSize.y - 26), new Vector2(600, 52));
            var hl = GetOrAdd<HorizontalLayoutGroup>(plate.gameObject);
            hl.padding = new RectOffset(32, 32, 6, 8);
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            var fit = GetOrAdd<ContentSizeFitter>(plate.gameObject);
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (s.PlateFrame != null)
            {
                var fr = NewImage("Frame", plate.transform, s.PlateFrame, Color.white, s.PlateFramePpu);
                GetOrAdd<LayoutElement>(fr.gameObject).ignoreLayout = true;
                Stretch(fr.rectTransform, 0);
            }
            nextText.transform.SetParent(plate.transform, false);
            SetFont(nextText, s.PlateOutline);
            nextText.enableAutoSizing = false; nextText.fontSize = 30;
            nextText.color = s.PlateTextColor;
            nextText.alignment = TextAlignmentOptions.Center;
            nextText.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    private static IEnumerable<GameObject> Flags(Transform banner, Sprite flag, string name, Vector2 bannerSize)
    {
        foreach (var x in new[] { -1f, 1f })
        {
            var f = NewImage($"{name}{(x < 0 ? "L" : "R")}", banner, flag, Color.white, 1f, sliced: false);
            f.preserveAspect = true;
            var rt = f.rectTransform;
            Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(x * (bannerSize.x * 0.5f + 34), -bannerSize.y * 0.5f + 6), new Vector2(76, 94));
            rt.localScale = new Vector3(-x, 1, 1); // 旗は看板の外側へなびかせる
            rt.SetAsFirstSibling();
            yield return f.gameObject;
        }
    }

    // ───────── 出店準備ボタン ─────────

    private static void BuildDepart(SerializedObject so, Style s)
    {
        var btn = Ref<Button>(so, "departButton");
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        SetImage(img, s.Depart, s.DepartColor, s.DepartPpu);
        Place((RectTransform)btn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 30), s.DepartSize);
        var label = btn.GetComponentInChildren<TextMeshProUGUI>(true);
        float left = 0;
        if (s.DepartIcon != null)
        {
            var ic = NewImage("Icon", btn.transform, s.DepartIcon, Color.white, 1f, sliced: false);
            ic.preserveAspect = true;
            Place(ic.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(s.DepartPadX + 36, 4), new Vector2(96, 96));
            left = 78;
        }
        if (label != null)
        {
            var rt = label.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(s.DepartPadX + left, 8); rt.offsetMax = new Vector2(-s.DepartPadX, -8);
            SetFont(label, s.DepartOutline);
            label.fontSize = 50; label.enableAutoSizing = true; label.fontSizeMin = 30; label.fontSizeMax = 50;
            label.color = s.DepartTextColor;
            label.alignment = TextAlignmentOptions.Center;
        }
    }

    // ───────── 投資パネル ─────────

    private static void BuildInvestPanel(SerializedObject so, Style s)
    {
        var panelGo = Ref<GameObject>(so, "investPanel");
        if (panelGo == null) return;
        var panel = (RectTransform)panelGo.transform;
        var size = new Vector2(780, 640);
        Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -44), size);
        SetImage(panelGo.GetComponent<Image>(), s.Panel, s.PanelColor, s.PanelPpu);
        if (s.PanelFrame != null)
        {
            var fr = NewImage("Frame", panel, s.PanelFrame, Color.white, s.PanelFramePpu);
            Stretch(fr.rectTransform, 0);
            fr.transform.SetAsFirstSibling();
        }
        if (s.PanelPin != null)
        {
            var pin = NewImage("Pin", panel, s.PanelPin, Color.white, 1f, sliced: false);
            pin.preserveAspect = true;
            Place(pin.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(40, 66));
        }

        Transform Find(string n) => FindDeep(panel, n);
        RectTransform R(string n) => Find(n) as RectTransform;

        // 頭（アイコン・名前・レベル）
        var iconFrame = Find("IconFrame")?.GetComponent<Image>();
        var iconImg = Ref<Image>(so, "detailIcon");
        if (iconFrame != null)
        {
            SetImage(iconFrame, s.IconBack, s.IconBackColor, 1.4f);
            Place(iconFrame.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(40 + 64, -40 - 64), new Vector2(128, 128));
            var fr = iconFrame.transform.Find("Frame");
            if (fr == null && s.IconFrame != null)
            {
                var f = NewImage("Frame", iconFrame.transform, s.IconFrame, Color.white, s.IconFramePpu);
                Stretch(f.rectTransform, -2);
            }
        }
        if (iconImg != null)
        {
            iconImg.transform.SetParent(iconFrame != null ? iconFrame.transform : panel, false);
            Place(iconImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, 100));
            iconImg.transform.SetAsFirstSibling();
            iconImg.raycastTarget = false;
        }
        var nameText = Ref<TextMeshProUGUI>(so, "detailNameText");
        if (nameText != null)
        {
            Place(nameText.rectTransform, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(196, -78), new Vector2(460, 60));
            SetFont(nameText, s.HeadOutline);
            nameText.fontSize = 44; nameText.enableAutoSizing = true; nameText.fontSizeMin = 28; nameText.fontSizeMax = 44;
            nameText.color = s.HeadNameColor;
        }
        var levelText = Ref<TextMeshProUGUI>(so, "detailLevelText");
        if (levelText != null)
        {
            Place(levelText.rectTransform, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(198, -132), new Vector2(460, 40));
            SetFont(levelText, s.HeadOutline);
            levelText.fontSize = 30; levelText.color = s.HeadLevelColor;
        }
        var close = Ref<Button>(so, "investCloseButton");
        if (close != null)
        {
            SetImage(close.GetComponent<Image>(), s.Close, Color.white, 1f, sliced: false);
            close.GetComponent<Image>().preserveAspect = true;
            Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), s.ClosePos, new Vector2(s.CloseSize, s.CloseSize));
            var l = close.GetComponentInChildren<TextMeshProUGUI>(true);
            if (l != null) l.gameObject.SetActive(false); // × の絵があるので文字は出さない
        }

        // 効果の2枚のカード
        var cur = MakeCard("CurrentCard", panel, s, -190, 112);
        var nxt = MakeCard("NextCard", panel, s, -312, 112);
        PutRow(cur, Ref<TextMeshProUGUI>(so, "currentEffectText"), R("CurrentCaption"), s, s.CardText);
        PutRow(nxt, Ref<TextMeshProUGUI>(so, "nextEffectText"), R("NextCaption"), s, s.CardNext);
        var special = Ref<GameObject>(so, "specialFrame");
        if (special != null)
        {
            special.transform.SetParent(nxt, false);
            Stretch((RectTransform)special.transform, -4);
            special.transform.SetAsFirstSibling();
            special.GetComponent<Image>().pixelsPerUnitMultiplier = 0.9f;
        }

        // 費用（村資金と並べて、足りる/足りないを色で）
        var costCaption = Find("CostCaption") as RectTransform;
        var costText = Ref<TextMeshProUGUI>(so, "detailCostText");
        if (costCaption != null)
        {
            Place(costCaption, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(70, -456), new Vector2(110, 44));
            var t = costCaption.GetComponent<TextMeshProUGUI>();
            SetFont(t, s.CostOutline); t.fontSize = 28; t.color = s.CostCaption; t.enableAutoSizing = false;
        }
        if (costText != null)
        {
            Place(costText.rectTransform, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(180, -456), new Vector2(330, 56));
            SetFont(costText, true);
            costText.fontSize = 42; costText.enableAutoSizing = true; costText.fontSizeMin = 26; costText.fontSizeMax = 42;
            costText.alignment = TextAlignmentOptions.MidlineLeft;
        }
        var fundsLine = Find("FundsText")?.GetComponent<TextMeshProUGUI>();
        if (fundsLine == null) fundsLine = NewText("FundsText", panel, s.CostOutline, 28, s.CostFunds, "村資金 0G");
        Place(fundsLine.rectTransform, new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-70, -456), new Vector2(290, 44));
        fundsLine.alignment = TextAlignmentOptions.MidlineRight;
        fundsLine.enableAutoSizing = true; fundsLine.fontSizeMin = 18; fundsLine.fontSizeMax = 28;
        so.FindProperty("detailFundsText").objectReferenceValue = fundsLine;
        so.FindProperty("costAffordableColor").colorValue = s.CostOk;
        so.FindProperty("costShortColor").colorValue = s.CostShort;

        // 投資ボタン
        var invest = Ref<Button>(so, "investButton");
        if (invest != null)
        {
            SetImage(invest.GetComponent<Image>(), s.Button, Color.white, s.ButtonPpu);
            Place((RectTransform)invest.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(400, 104));
            if (s.ButtonDisabled != null)
            {
                invest.transition = Selectable.Transition.SpriteSwap;
                var st = invest.spriteState;
                st.disabledSprite = s.ButtonDisabled;
                invest.spriteState = st;
            }
            var l = Ref<TextMeshProUGUI>(so, "investButtonLabel");
            if (l != null)
            {
                var rt = l.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(24, 6); rt.offsetMax = new Vector2(-24, -6);
                SetFont(l, s.ButtonOutline);
                l.fontSize = 38; l.enableAutoSizing = true; l.fontSizeMin = 22; l.fontSizeMax = 38;
                l.color = s.ButtonTextColor;
            }
        }

        // 施設アイコン（マスターに icon が無いので View 側で facilityId ごとに持つ）
        var icons = new (string id, string path)[]
        {
            (VillageEffects.HallId, "Assets/UI/レリック/百貨店の理念.png"),
            (VillageEffects.GuildId, "Assets/UI/新聞/6紋章_冒険者ギルド報.png"),
            (VillageEffects.TavernId, "Assets/UI/新聞/6紋章_街角かわら版.png"),
            (VillageEffects.BankId, "Assets/UI/レリック/子豚の貯金箱.png"),
            (VillageEffects.ArtisanId, "Assets/UI/新聞/6紋章_職人季報.png"),
            (VillageEffects.PlazaId, "Assets/UI/Preparation/Prep_IconFlyer.png"),
        };
        var prop = so.FindProperty("facilityIcons");
        prop.arraySize = icons.Length;
        for (int i = 0; i < icons.Length; i++)
        {
            var e = prop.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("facilityId").stringValue = icons[i].id;
            e.FindPropertyRelative("icon").objectReferenceValue = Load(icons[i].path);
        }
    }

    private static RectTransform MakeCard(string name, RectTransform panel, Style s, float top, float height)
    {
        var card = NewImage(name, panel, s.Card, s.CardColor, s.CardPpu);
        Place(card.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, top), new Vector2(680, height));
        // 見出し・本文より奥、パネル直下の背景の上に置く
        card.transform.SetSiblingIndex(Mathf.Min(1, panel.childCount - 1));
        return card.rectTransform;
    }

    private static void PutRow(RectTransform card, TextMeshProUGUI body, RectTransform caption, Style s, Color bodyColor)
    {
        if (caption != null)
        {
            caption.SetParent(card, false);
            Place(caption, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(s.CardInset + 18, 0), new Vector2(120, 44));
            var t = caption.GetComponent<TextMeshProUGUI>();
            SetFont(t, s.CardOutline);
            t.fontSize = 28; t.enableAutoSizing = true; t.fontSizeMin = 16; t.fontSizeMax = 28;
            t.color = s.CardCaption;
            t.alignment = TextAlignmentOptions.MidlineLeft;
        }
        if (body != null)
        {
            body.transform.SetParent(card, false);
            var rt = body.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(s.CardInset + 146, 12); rt.offsetMax = new Vector2(-s.CardInset - 18, -12);
            SetFont(body, s.CardOutline);
            body.fontSize = 28; body.enableAutoSizing = true; body.fontSizeMin = 18; body.fontSizeMax = 28;
            body.color = bodyColor;
            body.alignment = TextAlignmentOptions.MidlineLeft;
            body.textWrappingMode = TextWrappingModes.Normal;
        }
    }

    // ───────── タイトルへ・メッセージの帯 ─────────

    private static void BuildTitleAndMessage(Transform root, SerializedObject so, Style s)
    {
        // タイトルへ: 右上の控えめなサブボタン（出店準備より小さく、茶色の板にクリームの文字）
        var titleImg = NewImage("TitleButton", root, s.SubButton ?? s.Button, Color.white, s.SubButtonPpu);
        titleImg.raycastTarget = true;
        Place(titleImg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -26), new Vector2(210, 66));
        var title = GetOrAdd<Button>(titleImg.gameObject);
        title.targetGraphic = titleImg;
        title.transition = Selectable.Transition.ColorTint;
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.93f, 0.8f, 1f);
        colors.selectedColor = new Color(1f, 0.93f, 0.8f, 1f);
        colors.pressedColor = new Color(0.82f, 0.76f, 0.7f, 1f);
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 1f); // 透かさずグレーに
        title.colors = colors;
        var titleLabel = NewText("Label", titleImg.transform, true, 30, s.SubButtonTextColor, "タイトルへ");
        Stretch(titleLabel.rectTransform, 0);
        titleLabel.rectTransform.offsetMin = new Vector2(16, 4); titleLabel.rectTransform.offsetMax = new Vector2(-16, -4);
        titleLabel.enableAutoSizing = true; titleLabel.fontSizeMin = 20; titleLabel.fontSizeMax = 30;
        so.FindProperty("titleButton").objectReferenceValue = title;

        // メッセージ: 画面下中央の帯（出店準備ボタンの左。文字が空なら帯ごと隠す）
        var band = NewImage("MessageBand", root, s.MessageBand, Color.white, s.MessageBandPpu);
        Place(band.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-80, 28), new Vector2(1000, 96));
        var msg = Ref<TextMeshProUGUI>(so, "messageText");
        if (msg == null) msg = NewText("MessageText", band.transform, true, 30, s.MessageTextColor, "");
        msg.transform.SetParent(band.transform, false);
        msg.gameObject.name = "MessageText";
        Stretch(msg.rectTransform, 0);
        msg.rectTransform.offsetMin = new Vector2(48, 10); msg.rectTransform.offsetMax = new Vector2(-48, -10);
        SetFont(msg, true);
        msg.color = s.MessageTextColor;
        msg.fontSize = 30; msg.enableAutoSizing = true; msg.fontSizeMin = 20; msg.fontSizeMax = 30;
        msg.alignment = TextAlignmentOptions.Center;
        msg.textWrappingMode = TextWrappingModes.Normal;
        msg.raycastTarget = false;
        msg.text = "稼いだお金で村に投資しよう。施設に近づいて調べると詳細が見られる。";
        so.FindProperty("messageText").objectReferenceValue = msg;
        so.FindProperty("messageBand").objectReferenceValue = band.gameObject;
    }

    // ───────── 帰還ポップ（格上げも兼用）・記録掲示板 ─────────

    private const float ConversionBaseHeight = 400f;

    private static void BuildPopups(SerializedObject so, Style s)
    {
        var popupSprite = s.Popup ?? s.Panel;
        float popupPpu = s.Popup != null ? s.PopupPpu : s.PanelPpu;
        var top = new Vector2(0.5f, 1);
        var bottom = new Vector2(0.5f, 0);

        // 帰還ポップ: 見出し・儲け・村へ（上寄せ）、内訳の枠とボタン（下寄せ）。内訳の高さだけ View が縦に伸ばす
        var conv = Ref<GameObject>(so, "conversionPopup");
        if (conv != null)
        {
            var rt = (RectTransform)conv.transform;
            Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(820, ConversionBaseHeight));
            SetImage(conv.GetComponent<Image>(), popupSprite, s.PopupColor, popupPpu);

            var title = Ref<TextMeshProUGUI>(so, "conversionTitleText");
            if (title != null)
            {
                Place(title.rectTransform, top, new Vector2(0.5f, 0.5f), new Vector2(0, -68), new Vector2(700, 60));
                StyleText(title, s.HeadOutline, 44, 28, s.HeadNameColor);
            }
            var earned = Ref<TextMeshProUGUI>(so, "conversionEarnedText");
            if (earned != null)
            {
                Place(earned.rectTransform, top, new Vector2(0.5f, 0.5f), new Vector2(0, -140), new Vector2(700, 46));
                StyleText(earned, true, 32, 20, Cream);
            }
            var converted = Ref<TextMeshProUGUI>(so, "conversionConvertedText");
            if (converted != null)
            {
                Place(converted.rectTransform, top, new Vector2(0.5f, 0.5f), new Vector2(0, -208), new Vector2(700, 72));
                StyleText(converted, true, 56, 30, BrightGold);
            }
            StyleGoldButton(Ref<Button>(so, "conversionCloseButton"), s, bottom, new Vector2(0, 36), new Vector2(360, 100));

            var area = NewImage("BreakdownArea", rt, s.Ledger, s.LedgerColor, s.LedgerPpu);
            Place(area.rectTransform, bottom, new Vector2(0.5f, 0), new Vector2(0, 36 + 100 + 20), new Vector2(720, 180));
            area.gameObject.SetActive(false);
            so.FindProperty("conversionBreakdownArea").objectReferenceValue = area.rectTransform;
        }

        // 記録掲示板: 見出し・記録の板・閉じる
        var board = Ref<GameObject>(so, "recordBoardPopup");
        if (board != null)
        {
            var rt = (RectTransform)board.transform;
            Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(820, 580));
            SetImage(board.GetComponent<Image>(), popupSprite, s.PopupColor, popupPpu);
            var title = Ref<TextMeshProUGUI>(so, "recordBoardTitleText");
            if (title != null)
            {
                Place(title.rectTransform, top, new Vector2(0.5f, 0.5f), new Vector2(0, -68), new Vector2(700, 60));
                StyleText(title, s.HeadOutline, 44, 28, s.HeadNameColor);
            }
            var box = NewImage("BodyBox", rt, s.Ledger, s.LedgerColor, s.LedgerPpu);
            Place(box.rectTransform, top, new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(720, 290));
            box.transform.SetSiblingIndex(0);
            var body = Ref<TextMeshProUGUI>(so, "recordBoardBodyText");
            if (body != null)
            {
                body.transform.SetParent(box.transform, false);
                Stretch(body.rectTransform, 0);
                body.rectTransform.offsetMin = new Vector2(32, 20); body.rectTransform.offsetMax = new Vector2(-32, -20);
                StyleText(body, true, 34, 18, Cream);
                body.textWrappingMode = TextWrappingModes.Normal;
                body.lineSpacing = 20;
            }
            StyleGoldButton(Ref<Button>(so, "recordBoardCloseButton"), s, bottom, new Vector2(0, 36), new Vector2(360, 100));
        }
    }

    private static void StyleText(TextMeshProUGUI t, bool outline, float max, float min, Color color)
    {
        SetFont(t, outline);
        t.fontSize = max; t.enableAutoSizing = true; t.fontSizeMin = min; t.fontSizeMax = max;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
    }

    /// <summary>投資ボタンと同じ金の板のボタンにする（押せないときはグレーの板に差し替え）。</summary>
    private static void StyleGoldButton(Button b, Style s, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        if (b == null) return;
        SetImage(b.GetComponent<Image>(), s.Button, Color.white, s.ButtonPpu);
        Place((RectTransform)b.transform, anchor, new Vector2(0.5f, 0), pos, size);
        if (s.ButtonDisabled != null)
        {
            b.transition = Selectable.Transition.SpriteSwap;
            var st = b.spriteState;
            st.disabledSprite = s.ButtonDisabled;
            b.spriteState = st;
        }
        var l = b.GetComponentInChildren<TextMeshProUGUI>(true);
        if (l != null)
        {
            var rt = l.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(24, 6); rt.offsetMax = new Vector2(-24, -6);
            SetFont(l, s.ButtonOutline);
            l.fontSize = 38; l.enableAutoSizing = true; l.fontSizeMin = 22; l.fontSizeMax = 38;
            l.color = s.ButtonTextColor;
            l.alignment = TextAlignmentOptions.Center;
        }
    }

    // ───────── マップの格の装飾 ─────────

    /// <summary>
    /// VillageRankDecor.prefab をシーンのルートに (0,0,0) で置き、遮蔽シルエット（OccluderStencilSetup.roots）に足す。
    /// 既に置いてあれば置き直さない・二重に足さない。
    /// </summary>
    private static void PlaceRankDecor(UnityEngine.SceneManagement.Scene scene)
    {
        var decor = scene.GetRootGameObjects()
            .Select(g => g.GetComponentInChildren<VillageRankDecor>(true)).FirstOrDefault(d => d != null);
        if (decor == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VillageRankDecorBuilder.PrefabPath);
            if (prefab == null) { Debug.LogWarning($"{Tag} {VillageRankDecorBuilder.PrefabPath} が無いので装飾は置かない"); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            decor = go.GetComponent<VillageRankDecor>();
            Debug.Log($"{Tag} 格の装飾を置いた");
        }
        decor.transform.SetParent(null, false);
        decor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var occluder = scene.GetRootGameObjects()
            .Select(g => g.GetComponentInChildren<OccluderStencilSetup>(true)).FirstOrDefault(o => o != null);
        if (occluder == null) Debug.LogWarning($"{Tag} OccluderStencilSetup が無い");
        else
        {
            var oso = new SerializedObject(occluder);
            var roots = oso.FindProperty("roots");
            bool has = false;
            for (int i = 0; i < roots.arraySize; i++)
                if (roots.GetArrayElementAtIndex(i).objectReferenceValue == decor.transform) has = true;
            if (!has)
            {
                roots.arraySize++;
                roots.GetArrayElementAtIndex(roots.arraySize - 1).objectReferenceValue = decor.transform;
                oso.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"{Tag} 遮蔽シルエットの対象に格の装飾を足した（{roots.arraySize} 個）");
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ───────── 部品の取り込み ─────────

    private static readonly Dictionary<string, Vector4> PartBorders = new()
    {
        // x=左 y=下 z=右 w=上（px）
        { "vui_wood_plate", new Vector4(72, 42, 72, 42) },
        { "vui_scroll_banner", new Vector4(100, 64, 100, 64) },
        { "vui_cream_box", new Vector4(16, 14, 16, 14) },
        { "vui_paper", new Vector4(70, 70, 70, 70) },
        { "vui_label_plate", new Vector4(24, 24, 24, 24) },
        { "vui_sign_box", new Vector4(20, 20, 20, 20) },
        { "vui_article_big", new Vector4(60, 60, 60, 60) },
        { "vui_brown_btn", new Vector4(30, 30, 30, 30) },
        { "vui_gold_plaque", new Vector4(110, 110, 110, 110) },
        { "vui_round_white", new Vector4(44, 44, 44, 44) },
        { "vui_panel_frame", new Vector4(342, 258, 330, 243) }, // 新聞/1枠.png の右上の飾りを左上の鏡写しで消したもの
        { "vui_gold_btn_off", new Vector4(169, 70, 169, 70) },  // 新聞/4−0購読する.png をグレーにしたもの（押せないとき）
    };

    private static void ImportParts()
    {
        AssetDatabase.Refresh();
        foreach (var kv in PartBorders)
        {
            string path = PartsDir + kv.Key + ".png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Debug.LogWarning($"{Tag} 部品が無い: {path}"); continue; }
            bool changed = imp.textureType != TextureImporterType.Sprite || imp.spriteImportMode != SpriteImportMode.Single
                           || imp.spriteBorder != kv.Value || imp.mipmapEnabled || !imp.alphaIsTransparency;
            if (!changed) continue;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spriteBorder = kv.Value;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
        }
    }

    // ───────── 小物 ─────────

    private static Sprite Part(string name) => Load(PartsDir + name + ".png");

    private static T GetOrAdd<T>(GameObject go) where T : Component
        => go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();

    private static Transform FindDeep(Transform t, string name)
    {
        foreach (Transform ch in t)
        {
            if (ch.name == name) return ch;
            var r = FindDeep(ch, name);
            if (r != null) return r;
        }
        return null;
    }

    private static Sprite Load(string path)
    {
        var s = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (s == null) Debug.LogWarning($"{Tag} スプライトが無い: {path}");
        return s;
    }

    private static T Ref<T>(SerializedObject so, string field) where T : Object
    {
        var o = so.FindProperty(field)?.objectReferenceValue;
        if (o is T t) return t;
        if (typeof(T) == typeof(GameObject) && o is Component c) return c.gameObject as T;
        return null;
    }

    /// <summary>名前の同じ子があればそれを使う（何度組み直しても重複しない）。</summary>
    private static RectTransform NewRect(string name, Transform parent)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite, Color color, float ppu, bool sliced = true)
    {
        var rt = NewRect(name, parent);
        GetOrAdd<CanvasRenderer>(rt.gameObject);
        var img = GetOrAdd<Image>(rt.gameObject);
        SetImage(img, sprite, color, ppu, sliced);
        img.raycastTarget = false;
        return img;
    }

    private static void SetImage(Image img, Sprite sprite, Color color, float ppu, bool sliced = true)
    {
        if (img == null) return;
        img.sprite = sprite;
        img.color = color;
        bool canSlice = sliced && sprite != null && sprite.border != Vector4.zero;
        img.type = canSlice ? Image.Type.Sliced : Image.Type.Simple;
        img.pixelsPerUnitMultiplier = ppu;
        img.fillCenter = true;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, bool outline, float size, Color color, string text)
    {
        var rt = NewRect(name, parent);
        GetOrAdd<CanvasRenderer>(rt.gameObject);
        var t = GetOrAdd<TextMeshProUGUI>(rt.gameObject);
        SetFont(t, outline);
        t.fontSize = size; t.color = color; t.text = text;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        return t;
    }

    private static void SetFont(TMP_Text t, bool outline)
    {
        if (t == null) return;
        var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outline ? FontOutline : FontPlain);
        if (f == null) return;
        t.font = f;
        t.fontSharedMaterial = f.material;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        rt.localScale = Vector3.one;
    }

    private static Color Hex(string h) => ColorUtility.TryParseHtmlString("#" + h, out var c) ? c : Color.magenta;

    private static string Arg(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, System.StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    // ───────── 調査用 ─────────

    public static void DumpBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != "VillageUI") continue;
            var sb = new StringBuilder();
            Dump(root.transform, 0, sb);
            System.IO.File.WriteAllText(@"D:\WorkTree\TomsLands\_scratch_wt04\village_ui\dump.txt", sb.ToString());
        }
        EditorApplication.Exit(0);
    }

    private static void Dump(Transform t, int depth, StringBuilder sb)
    {
        sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [off]");
        if (t is RectTransform rt)
            sb.Append($" a({rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##})-({rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##}) p({rt.pivot.x:0.##},{rt.pivot.y:0.##}) pos{rt.anchoredPosition} size{rt.sizeDelta}");
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null || c is Transform) continue;
            sb.Append(" |").Append(c.GetType().Name);
            if (c is Image img) sb.Append($"(spr={(img.sprite ? AssetDatabase.GetAssetPath(img.sprite) + ":" + img.sprite.name : "none")},{img.type},col={ColorUtility.ToHtmlStringRGBA(img.color)})");
            if (c is TMP_Text tx) sb.Append($"(\"{tx.text.Replace("\n", "\\n")}\",{tx.fontSize},{tx.font?.name},col={ColorUtility.ToHtmlStringRGBA(tx.color)},{tx.alignment})");
        }
        sb.AppendLine();
        foreach (Transform ch in t) Dump(ch, depth + 1, sb);
    }
}
