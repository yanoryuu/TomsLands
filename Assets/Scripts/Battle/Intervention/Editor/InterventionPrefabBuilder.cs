#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 介入カード / 必殺技カットインのプレハブを素材から組み立てる（再実行で上書き）。
/// Tools/Intervention/Build Prefabs
/// </summary>
public static class InterventionPrefabBuilder
{
    private const string Dir = "Assets/UI/戦闘/介入/";
    private const string PrefabDir = "Assets/Prefabs/Battle/";
    private const string FontPath = "Assets/Font/MPLUSRounded1c-Bold SDF.asset";
    private const string FontNoOutlinePath = "Assets/Font/MPLUSRounded1c-Bold SDFNoOutLine.asset";
    private const string HeroPortraitPath = "Assets/UI/Character/勇者/勇者_10_真剣.png";

    public const float CardW = 480f, CardH = 330f;

    private static readonly Color WellFront = new(0.29f, 0.19f, 0.12f, 1f);
    private static readonly Color WellBack = new(0.19f, 0.08f, 0.17f, 1f);
    private static readonly Color TabBg = new(0.35f, 0.23f, 0.14f, 1f);

    [MenuItem("Tools/Intervention/Build Prefabs")]
    public static void BuildAll()
    {
        SetupSprites();
        Directory.CreateDirectory(PrefabDir);
        BuildCard();
        BuildCutIn();
        AssetDatabase.SaveAssets();
        Debug.Log("[InterventionPrefabBuilder] built");
    }

    // ------------------------------------------------------------ sprites

    private static void SetupSprites()
    {
        var btnBorder = new Vector4(60, 55, 60, 55); // L B R T
        Import("Card_Front", btnBorder);
        Import("Card_Back", btnBorder);
        foreach (var n in new[] { "Btn_Heal", "Btn_Skill", "Btn_Special", "Btn_Dungeon", "Btn_Gray" }) Import(n, btnBorder);
        Import("SC_Card", new Vector4(16, 16, 16, 16));
        Import("SC_CardRim", new Vector4(16, 16, 16, 16));
        Import("Inset", new Vector4(14, 14, 14, 14));
        Import("Pill", new Vector4(8, 7, 8, 7));
        foreach (var n in new[]
                 {
                     "Btn_Mask", "Circle", "Ring_Gold", "Ring_Plain", "White", "CutIn_Band", "CutIn_SpeedLines",
                     "Icon_Heal", "Icon_Skill", "Icon_Special", "Icon_Trap", "Icon_Curse", "Icon_Reinforce", "Icon_BossBuff",
                     "Tab_Hero", "Tab_Dungeon",
                 })
            Import(n, Vector4.zero);
    }

    private static void Import(string name, Vector4 border)
    {
        var path = Dir + name + ".png";
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning("missing " + path); return; }
        bool dirty = ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single
                     || ti.spriteBorder != border || ti.mipmapEnabled || ti.alphaIsTransparency == false;
        if (!dirty) return;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spriteBorder = border;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
    }

    private static Sprite S(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Dir + name + ".png");

    private static Sprite HeroPortrait()
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(HeroPortraitPath))
            if (o is Sprite sp) return sp;
        return null;
    }

    // ------------------------------------------------------------ helpers

    private static RectTransform NewRT(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(-r, -t);
    }

    private static Image Img(RectTransform rt, Sprite sp, Color c, Image.Type type = Image.Type.Simple, float ppum = 1f,
        bool raycast = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sp;
        img.color = c;
        img.type = type;
        img.pixelsPerUnitMultiplier = ppum;
        img.raycastTarget = raycast;
        return img;
    }

    private static TextMeshProUGUI Text(RectTransform rt, bool outline, float size, TextAlignmentOptions align)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outline ? FontPath : FontNoOutlinePath);
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.text = "";
        return t;
    }

    private static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"field {field} not found on {target}"); return; }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(Object target, string field, Object[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"field {field} not found"); return; }
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------ card

    private static void BuildCard()
    {
        var root = new GameObject("InterventionCard", typeof(RectTransform));
        root.layer = 5;
        var rootRt = (RectTransform)root.transform;
        rootRt.sizeDelta = new Vector2(CardW, CardH);
        var view = root.AddComponent<InterventionCardView>();

        var flip = NewRT("FlipRoot", rootRt);
        Stretch(flip);
        var flipGroup = flip.gameObject.AddComponent<CanvasGroup>();

        // ---- 表
        var hero = NewRT("FaceHero", flip);
        Stretch(hero);
        Img(NewRTStretch("Bg", hero), S("Card_Front"), Color.white, Image.Type.Sliced, 1f, true);

        var chatWell = NewRT("ChatWell", hero);
        Stretch(chatWell, 18, 18, 18, 124);
        Img(chatWell, S("Inset"), WellFront, Image.Type.Sliced, 1f, true);
        chatWell.gameObject.AddComponent<RectMask2D>();

        var chat = NewRT("ChatContent", chatWell);
        chat.anchorMin = new Vector2(0, 0);
        chat.anchorMax = new Vector2(1, 0);
        chat.pivot = new Vector2(0.5f, 0f);
        chat.offsetMin = new Vector2(0, 0);
        chat.offsetMax = new Vector2(0, 0);
        var vlg = chat.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 5;
        vlg.childAlignment = TextAnchor.LowerCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var csf = chat.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scTemplate = BuildSuperChatTemplate(chat);

        var heroRow = ButtonRow("Buttons", hero, 3);
        var heal = BuildButton("Heal", heroRow, "Btn_Heal", "Icon_Heal", 0, 2.1f);
        var skill = BuildButton("Skill", heroRow, "Btn_Skill", "Icon_Skill", 0, 2.1f);
        var special = BuildButton("Special", heroRow, "Btn_Special", "Icon_Special", 2, 2.1f);

        // ---- 裏
        var dun = NewRT("FaceDungeon", flip);
        Stretch(dun);
        Img(NewRTStretch("Bg", dun), S("Card_Back"), Color.white, Image.Type.Sliced, 1f, true);

        var enemyWell = NewRT("EnemyWell", dun);
        Stretch(enemyWell, 18, 18, 18, 124);
        Img(enemyWell, S("Inset"), WellBack, Image.Type.Sliced, 1f, true);

        var row = NewRT("EnemyRow", enemyWell);
        Stretch(row, 8, 8, 8, 8);
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 18;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        var badgeTemplate = BuildEnemyBadge(row);

        var dunRow = ButtonRow("Buttons", dun, 4);
        var trap = BuildButton("Trap", dunRow, "Btn_Dungeon", "Icon_Trap", 0, 2.4f);
        var curse = BuildButton("Curse", dunRow, "Btn_Dungeon", "Icon_Curse", 0, 2.4f);
        var reinforce = BuildButton("Reinforce", dunRow, "Btn_Dungeon", "Icon_Reinforce", 0, 2.4f);
        var boss = BuildButton("BossBuff", dunRow, "Btn_Dungeon", "Icon_BossBuff", 1, 2.4f);
        dun.gameObject.SetActive(false);

        // ---- タブ
        var tab = NewRT("Tab", flip);
        tab.anchorMin = tab.anchorMax = new Vector2(1, 1);
        tab.pivot = new Vector2(0.5f, 0.5f);
        tab.sizeDelta = new Vector2(70, 70);
        tab.anchoredPosition = new Vector2(-14, -14);
        var tabBg = Img(tab, S("Circle"), TabBg, Image.Type.Simple, 1f, true);
        var tabBtn = tab.gameObject.AddComponent<Button>();
        tabBtn.targetGraphic = tabBg;
        SetupTint(tabBtn);
        var tabIcon = NewRT("Icon", tab);
        Stretch(tabIcon, 9, 9, 9, 9);
        var tabIconImg = Img(tabIcon, S("Tab_Dungeon"), Color.white);
        tabIconImg.preserveAspect = true;
        var tabRing = NewRT("Ring", tab);
        Stretch(tabRing);
        Img(tabRing, S("Ring_Gold"), Color.white);
        var dot = NewRT("NotifyDot", tab);
        dot.anchorMin = dot.anchorMax = new Vector2(0.15f, 0.85f);
        dot.sizeDelta = new Vector2(22, 22);
        Img(dot, S("Circle"), new Color(0.35f, 0.2f, 0.1f, 1f));
        var dotIn = NewRT("Fill", dot);
        Stretch(dotIn, 3, 3, 3, 3);
        Img(dotIn, S("Circle"), new Color(0.93f, 0.3f, 0.2f, 1f));
        dot.gameObject.SetActive(false);

        // ---- 配線
        Set(view, "flipRoot", flip);
        Set(view, "flipGroup", flipGroup);
        Set(view, "heroFace", hero.gameObject);
        Set(view, "dungeonFace", dun.gameObject);
        Set(view, "tabButton", tabBtn);
        Set(view, "tabRoot", tab);
        Set(view, "tabIcon", tabIconImg);
        Set(view, "tabIconOnHeroFace", S("Tab_Dungeon"));
        Set(view, "tabIconOnDungeonFace", S("Tab_Hero"));
        Set(view, "tabNotifyDot", dot.gameObject);
        Set(view, "healButton", heal);
        Set(view, "skillButton", skill);
        Set(view, "specialButton", special);
        Set(view, "trapButton", trap);
        Set(view, "curseButton", curse);
        Set(view, "reinforceButton", reinforce);
        Set(view, "bossBuffButton", boss);
        Set(view, "chatContent", chat);
        Set(view, "superChatTemplate", scTemplate);
        Set(view, "enemyRow", row);
        Set(view, "enemyBadgeTemplate", badgeTemplate);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "InterventionCard.prefab");
        Object.DestroyImmediate(root);
    }

    private static RectTransform NewRTStretch(string name, Transform parent)
    {
        var rt = NewRT(name, parent);
        Stretch(rt);
        return rt;
    }

    private static RectTransform ButtonRow(string name, RectTransform face, int count)
    {
        var row = NewRT(name, face);
        row.anchorMin = new Vector2(0, 0);
        row.anchorMax = new Vector2(1, 0);
        row.pivot = new Vector2(0.5f, 0f);
        row.offsetMin = new Vector2(18, 22);
        row.offsetMax = new Vector2(-18, 22 + 94);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = count >= 4 ? 6 : 8;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        return row;
    }

    private static void SetupTint(Button b)
    {
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 0.97f, 0.9f, 1f);
        cb.pressedColor = new Color(0.8f, 0.78f, 0.74f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = Color.white;
        cb.fadeDuration = 0.06f;
        b.colors = cb;
        var nav = b.navigation;
        nav.mode = Navigation.Mode.None;
        b.navigation = nav;
    }

    private static InterventionButtonWidget BuildButton(string name, RectTransform row, string bgSprite, string iconSprite,
        int dots, float ppum)
    {
        var rt = NewRT(name, row);
        var bg = Img(rt, S(bgSprite), Color.white, Image.Type.Sliced, ppum, true);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        SetupTint(btn);
        var mask = rt.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var icon = NewRT("Icon", rt);
        icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 1f);
        icon.pivot = new Vector2(0.5f, 1f);
        icon.sizeDelta = new Vector2(50, 50);
        icon.anchoredPosition = new Vector2(0, -8);
        var iconImg = Img(icon, S(iconSprite), Color.white);
        iconImg.preserveAspect = true;

        var price = NewRT("Price", rt);
        price.anchorMin = new Vector2(0, 0);
        price.anchorMax = new Vector2(1, 0);
        price.pivot = new Vector2(0.5f, 0f);
        price.offsetMin = new Vector2(12, 13);
        price.offsetMax = new Vector2(-12, 13 + 26);
        var priceText = Text(price, true, 22, TextAlignmentOptions.Center);
        priceText.enableAutoSizing = true;
        priceText.fontSizeMin = 13;
        priceText.fontSizeMax = 22;
        priceText.text = "0G";

        Image[] dotImgs = new Image[dots];
        if (dots > 0)
        {
            var dr = NewRT("Uses", rt);
            dr.anchorMin = dr.anchorMax = new Vector2(0, 1);
            dr.pivot = new Vector2(0, 1);
            dr.anchoredPosition = new Vector2(11, -10);
            dr.sizeDelta = new Vector2(dots * 19 + 2, 18);
            var h = dr.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 3;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = false;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            for (int i = 0; i < dots; i++)
            {
                var ring = NewRT("Dot" + i, dr);
                ring.sizeDelta = new Vector2(16, 16);
                Img(ring, S("Circle"), new Color(0.23f, 0.14f, 0.08f, 1f));
                var fill = NewRT("Fill", ring);
                Stretch(fill, 2.5f, 2.5f, 2.5f, 2.5f);
                dotImgs[i] = Img(fill, S("Circle"), new Color(1f, 0.82f, 0.36f, 1f));
            }
        }

        var fl = NewRT("Flash", rt);
        Stretch(fl);
        var flImg = Img(fl, S("White"), new Color(1f, 0.93f, 0.75f, 0f));
        flImg.enabled = false;

        var cd = NewRT("Cooldown", rt);
        Stretch(cd);
        var cdImg = Img(cd, S("White"), new Color(0.05f, 0.02f, 0.02f, 0.6f), Image.Type.Filled);
        cdImg.fillMethod = Image.FillMethod.Radial360;
        cdImg.fillOrigin = (int)Image.Origin360.Top;
        cdImg.fillClockwise = false;
        cdImg.fillAmount = 0f;
        cdImg.enabled = false;

        var w = rt.gameObject.AddComponent<InterventionButtonWidget>();
        Set(w, "button", btn);
        Set(w, "background", bg);
        Set(w, "icon", iconImg);
        Set(w, "priceLabel", priceText);
        Set(w, "cooldownFill", cdImg);
        Set(w, "flashOverlay", flImg);
        SetArray(w, "usesDots", dotImgs);
        Set(w, "normalSprite", S(bgSprite));
        Set(w, "disabledSprite", S("Btn_Gray"));
        return w;
    }

    private static SuperChatCardWidget BuildSuperChatTemplate(RectTransform parent)
    {
        var rt = NewRT("SuperChatTemplate", parent);
        var body = Img(rt, S("SC_Card"), Color.white, Image.Type.Sliced, 1.4f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(5, 10, 5, 5);
        h.spacing = 8;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 38;

        var pill = NewRT("Amount", rt);
        var pillImg = Img(pill, S("SC_Card"), Color.white, Image.Type.Sliced, 1.6f);
        var ph = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        ph.padding = new RectOffset(10, 10, 2, 3);
        ph.childAlignment = TextAnchor.MiddleCenter;
        ph.childControlWidth = ph.childControlHeight = true;
        ph.childForceExpandWidth = ph.childForceExpandHeight = false;
        var ple = pill.gameObject.AddComponent<LayoutElement>();
        ple.minHeight = 28;
        var amt = NewRT("Text", pill);
        var amtText = Text(amt, true, 21, TextAlignmentOptions.Center);
        amtText.overflowMode = TextOverflowModes.Overflow;
        amtText.text = "10,000G";

        var msg = NewRT("Message", rt);
        var msgText = Text(msg, false, 20, TextAlignmentOptions.MidlineLeft);
        msgText.text = "がんばれ！";
        var mle = msg.gameObject.AddComponent<LayoutElement>();
        mle.flexibleWidth = 1;
        mle.minWidth = 0;

        var rim = NewRT("Rim", rt);
        rim.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch(rim, -2, -2, -2, -2);
        var rimImg = Img(rim, S("SC_CardRim"), Color.white, Image.Type.Sliced, 1.2f);
        rim.gameObject.SetActive(false);

        var w = rt.gameObject.AddComponent<SuperChatCardWidget>();
        Set(w, "body", body);
        Set(w, "amountPill", pillImg);
        Set(w, "amountLabel", amtText);
        Set(w, "messageLabel", msgText);
        Set(w, "rim", rimImg);
        rt.gameObject.SetActive(false);
        return w;
    }

    private static EnemyBadgeWidget BuildEnemyBadge(RectTransform parent)
    {
        var rt = NewRT("EnemyBadgeTemplate", parent);
        rt.sizeDelta = new Vector2(96, 118);

        var face = NewRT("Face", rt);
        face.anchorMin = face.anchorMax = new Vector2(0.5f, 1f);
        face.pivot = new Vector2(0.5f, 1f);
        face.sizeDelta = new Vector2(94, 94);
        face.anchoredPosition = Vector2.zero;
        var backdrop = Img(face, S("Circle"), new Color(0.2f, 0.1f, 0.22f, 1f));
        face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var icon = NewRT("Icon", face);
        Stretch(icon, -16, -16, -12, -20);
        var iconImg = Img(icon, null, Color.white);
        iconImg.preserveAspect = true;

        var ring = NewRT("Ring", rt);
        ring.anchorMin = ring.anchorMax = new Vector2(0.5f, 1f);
        ring.pivot = new Vector2(0.5f, 1f);
        ring.sizeDelta = new Vector2(100, 100);
        ring.anchoredPosition = new Vector2(0, 3);
        var ringImg = Img(ring, S("Ring_Plain"), new Color(0.78f, 0.62f, 0.86f, 1f));

        var hpBg = NewRT("Hp", rt);
        hpBg.anchorMin = hpBg.anchorMax = new Vector2(0.5f, 0f);
        hpBg.pivot = new Vector2(0.5f, 0f);
        hpBg.sizeDelta = new Vector2(80, 13);
        hpBg.anchoredPosition = new Vector2(0, 2);
        Img(hpBg, S("Pill"), new Color(0.1f, 0.04f, 0.08f, 1f), Image.Type.Sliced, 1.3f);
        var fill = NewRT("Fill", hpBg);
        Stretch(fill, 2, 2, 2, 2);
        var fillImg = Img(fill, S("White"), new Color(0.45f, 0.82f, 0.36f, 1f), Image.Type.Filled);
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;

        var w = rt.gameObject.AddComponent<EnemyBadgeWidget>();
        Set(w, "backdrop", backdrop);
        Set(w, "icon", iconImg);
        Set(w, "ring", ringImg);
        Set(w, "hpFill", fillImg);
        Set(w, "ringNormal", S("Ring_Plain"));
        Set(w, "ringBoss", S("Ring_Gold"));
        rt.gameObject.SetActive(false);
        return w;
    }

    // ------------------------------------------------------------ cut-in

    private static void BuildCutIn()
    {
        var root = new GameObject("SpecialMoveCutIn", typeof(RectTransform));
        root.layer = 5;
        var rt = (RectTransform)root.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        var cg = root.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        var view = root.AddComponent<SpecialMoveCutInView>();

        var dim = NewRT("Dim", rt);
        Stretch(dim);
        var dimImg = Img(dim, S("White"), new Color(0.08f, 0.02f, 0.02f, 0f));

        var lines = NewRT("SpeedLines", rt);
        lines.anchorMin = lines.anchorMax = new Vector2(0.5f, 0.5f);
        lines.sizeDelta = new Vector2(2600, 1460);
        Img(lines, S("CutIn_SpeedLines"), new Color(1f, 0.92f, 0.85f, 0f));

        var band = NewRT("Band", rt);
        band.anchorMin = band.anchorMax = new Vector2(0.5f, 0.5f);
        band.sizeDelta = new Vector2(2600, 340);
        band.anchoredPosition = new Vector2(0, 10);
        band.localRotation = Quaternion.Euler(0, 0, -7f);
        Img(band, S("CutIn_Band"), Color.white);
        band.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var portrait = NewRT("Portrait", band);
        portrait.anchorMin = portrait.anchorMax = new Vector2(0.5f, 0.5f);
        portrait.sizeDelta = new Vector2(1700, 1700);
        portrait.localRotation = Quaternion.Euler(0, 0, 7f);
        var pImg = Img(portrait, HeroPortrait(), Color.white);
        pImg.preserveAspect = true;

        var flash = NewRT("Flash", rt);
        Stretch(flash);
        var flashImg = Img(flash, S("White"), new Color(1f, 0.95f, 0.85f, 0f));

        Set(view, "root", cg);
        Set(view, "dim", dimImg);
        Set(view, "speedLines", lines);
        Set(view, "band", band);
        Set(view, "portrait", portrait);
        Set(view, "flash", flashImg);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "SpecialMoveCutIn.prefab");
        Object.DestroyImmediate(root);
    }
}
#endif
