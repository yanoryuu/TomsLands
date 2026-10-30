using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 勇者の画面（Assets/Prefabs/Screens/Hero.prefab）を案A「着せ替え型」に組み直して保存する。
///   左: 勇者の立ち絵（大）＋手元の武器の枠・胴の防具の枠・職人の仕込み、その下に能力値の板（Lv・HP・MP・攻撃・防御・クリア見込み）
///   右: 品物の棚（武器／防具のタブ・2 列のカード）、その下に作戦のプルダウン（アイコン付き）、いちばん下に勇者のセリフの板
/// 今ある部品（もどる・立ち絵・装備アイコン・説明ボタン・細工・セリフ）は HeroPanelView の参照から探して新しい場所へ移す。
/// 古い武器・防具・作戦のプルダウン（DropDown.prefab の入れ子）と古いパネルの枠は消し、作戦のプルダウンは作り直す。
/// 何度実行しても部品は名前で探して作り直さない（重複しない）。座標は 1920x1080 の左上から（Layout の中）。
/// TomsShop シーンの Hero は位置・名前しか上書きしていないので、シーンは触らない。
/// バッチ: Unity.exe -batchmode -projectPath &lt;wt&gt; -executeMethod HeroUIBuilder.BuildBatch -logFile &lt;log&gt;
/// 配線の記録は Docs/Hero_UnityWiring.md。
/// </summary>
public static class HeroUIBuilder
{
    private const string Tag = "[HeroUIBuilder]";
    public const string PrefabPath = "Assets/Prefabs/Screens/Hero.prefab";
    private const string PartsDir = "Assets/UI/Village/Parts/";
    private const string FontOutline = "Assets/Font/MPLUSRounded1c-Bold SDFPlusPadding.asset";
    private const string FontPlain = "Assets/Font/MPLUSRounded1c-Bold SDFPlusPaddingNoOutLine.asset";

    private static readonly Color Ink = Hex("3A2414");
    private static readonly Color DarkBrown = Hex("5A3A24");
    private static readonly Color MidBrown = Hex("6B4630");
    private static readonly Color Gold = Hex("E9B949");
    private static readonly Color BrightGold = Hex("FFD966");
    private static readonly Color Cream = Hex("FFF3D6");
    private static readonly Color SoftCream = Hex("F1DDB8");
    private static readonly Color Red = Hex("C8402E");
    private static readonly Color BonusGreen = Hex("2E6B1F");

    // ───────── 入口 ─────────

    public static void BuildBatch()
    {
        try
        {
            Build();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{Tag} 失敗: {e}");
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/TomsLands/勇者の画面を組み直す（案A 着せ替え型）")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = root.GetComponentInChildren<HeroPanelView>(true);
            if (view == null) throw new System.InvalidOperationException("HeroPanelView が無い");
            var so = new SerializedObject(view);
            var panel = (RectTransform)view.transform;

            var layout = NewRect("Layout", panel);
            Place(layout, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
            layout.SetSiblingIndex(Mathf.Min(1, panel.childCount - 1)); // 背景のすぐ上

            var oldInner = FindChild(panel, "HeroPanel"); // 古いパネル（中身を移したあと消す）

            BuildFigure(layout, so);
            var weaponSlot = BuildSlot(layout, so, true, "WeaponSlot", 36, 430);
            var armorSlot = BuildSlot(layout, so, false, "ArmorSlot", 566, 322);
            BuildCraft(layout, so);
            BuildBack(layout, so);
            BuildStats(layout, so);
            BuildShelf(layout, so);
            BuildTactics(layout, so);
            BuildDialogue(layout, so, panel);

            // 描く順: 立ち絵 → 立ち絵の上の枠 → 板・棚 → プルダウン（開いた一覧が棚の上に出る）
            foreach (var n in new[] { "HeroFigure", "HeroDropArea", "WeaponSlot", "ArmorSlot", "CraftRoot", "Back", "StatsBoard", "ShelfPanel", "DialogueBoard", "TacticsDropdown" })
            {
                var t = layout.Find(n);
                if (t != null) t.SetAsLastSibling();
            }

            // パッドの最初の選択は武器の枠（実行時は棚の最初のカードに差し替える）
            var scope = view.GetComponent<UIFocusScope>();
            if (scope != null)
            {
                var sso = new SerializedObject(scope);
                sso.FindProperty("firstSelected").objectReferenceValue = weaponSlot.GetComponent<Button>();
                sso.ApplyModifiedPropertiesWithoutUndo();
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            // 古いプルダウン（DropDown.prefab の入れ子）と古いパネルを消す
            var keepDropdown = Ref<TMP_Dropdown>(so, "tacticsDropdown");
            foreach (var dd in panel.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                if (dd == keepDropdown) continue;
                var go = PrefabUtility.IsPartOfPrefabInstance(dd) ? PrefabUtility.GetOutermostPrefabInstanceRoot(dd) : dd.gameObject;
                Debug.Log($"{Tag} 古いプルダウンを消す: {go.name}");
                Object.DestroyImmediate(go);
            }
            if (oldInner != null)
            {
                Debug.Log($"{Tag} 古いパネルを消す（残っていた子: {string.Join(", ", oldInner.Cast<Transform>().Select(c => c.name))}）");
                Object.DestroyImmediate(oldInner.gameObject);
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"{Tag} 組んで保存した: {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ───────── 立ち絵 ─────────

    private static void BuildFigure(RectTransform layout, SerializedObject so)
    {
        var chara = Ref<Button>(so, "characterButton");
        if (chara != null)
        {
            var rt = (RectTransform)chara.transform;
            rt.SetParent(layout, false);
            rt.name = "HeroFigure";
            // 3240 四方の絵（人物は縦 0〜94%・横 19〜90%）。顔が HUD の下、足は能力値の板の後ろ
            PlaceTL(rt, 52, 188, 1000, 1000);
            var img = chara.GetComponent<Image>();
            if (img != null)
            {
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = Color.white;
            }
        }
        // ここで離しても装備する（体のあたり）
        var drop = NewRect("HeroDropArea", layout);
        PlaceTL(drop, 170, 200, 560, 580);
        so.FindProperty("heroDropArea").objectReferenceValue = drop;
    }

    // ───────── 装備の枠 ─────────

    private static RectTransform BuildSlot(RectTransform layout, SerializedObject so, bool weapon, string name, float x, float y)
    {
        const float w = 196f, frameSize = 164f;
        var slotRt = NewRect(name, layout);
        PlaceTL(slotRt, x, y, w, 250);
        var slot = GetOrAdd<HeroEquipSlot>(slotRt.gameObject);

        // 種類の札（武器／防具）。文字は説明ボタン（ホバーで説明）
        var plate = NewImage("KindPlate", slotRt, Part("vui_label_plate"), Color.white, 1.6f);
        PlaceTL(plate.rectTransform, (w - 132) / 2, 0, 132, 44);
        var info = Ref<Button>(so, weapon ? "weaponInfoButton" : "armorInfoButton");
        if (info != null)
        {
            var irt = (RectTransform)info.transform;
            irt.SetParent(plate.transform, false);
            irt.name = weapon ? "WeaponInfo" : "ArmorInfo";
            Stretch(irt, 0);
            var t = info.GetComponent<TextMeshProUGUI>();
            if (t != null)
            {
                StyleText(t, true, 28, 18, BrightGold);
                t.text = weapon ? "武器" : "防具";
                t.raycastTarget = true;
            }
        }

        // 枠（光らせる・つぶす単位）
        var frame = NewRect("Frame", slotRt);
        PlaceTL(frame, (w - frameSize) / 2, 46, frameSize, frameSize);
        // 持ち上げ中だけ後ろで光る（置き先の目印。枠の外へ広がる柔らかい光）
        var carryGlow = NewImage("CarryGlow", frame, Load("Assets/UI/Village/ArrivalFx/arrival_glow.png"), new Color(1f, 0.86f, 0.38f, 1f), 1f, sliced: false);
        Place(carryGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(frameSize + 230, frameSize + 230));
        carryGlow.transform.SetAsFirstSibling();
        carryGlow.gameObject.SetActive(false);
        var back = NewImage("Back", frame, Part("vui_round_white"), new Color(0.16f, 0.1f, 0.06f, 0.82f), 2.2f);
        Stretch(back.rectTransform, 0);
        back.raycastTarget = true;
        var button = GetOrAdd<Button>(slotRt.gameObject);
        button.targetGraphic = back;
        SetTint(button);

        // 今の装備のアイコン（古い「Icon」一式を移す。細工の印も中にある）
        var iconImage = Ref<Image>(so, weapon ? "weaponIconImage" : "armorIconImage");
        var iconGroup = FindIconGroup(iconImage);
        if (iconGroup != null)
        {
            iconGroup.SetParent(frame, false);
            iconGroup.name = "Icon";
            Place(iconGroup, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(148, 148));
            iconGroup.localScale = Vector3.one * 0.98f;
        }
        var rim = NewImage("GoldFrame", frame, Load("Assets/UI/Preparation/Prep_GoldFrame.png"), Color.white, 1.4f);
        Stretch(rim.rectTransform, -4);
        rim.transform.SetAsLastSibling();

        // 名前の板
        var namePlate = NewImage("NamePlate", slotRt, Part("vui_wood_plate"), Color.white, 3.2f);
        PlaceTL(namePlate.rectTransform, 0, 206, w, 44);
        var nameText = NewText("Name", namePlate.transform, true, 24, Cream, weapon ? "鋼の剣" : "鎖かたびら");
        Stretch(nameText.rectTransform, 0);
        nameText.rectTransform.offsetMin = new Vector2(14, 2);
        nameText.rectTransform.offsetMax = new Vector2(-14, -2);
        StyleText(nameText, true, 24, 14, Cream);

        var sso = new SerializedObject(slot);
        sso.FindProperty("isWeapon").boolValue = weapon;
        sso.FindProperty("button").objectReferenceValue = button;
        sso.FindProperty("glowTarget").objectReferenceValue = frame;
        sso.FindProperty("iconRoot").objectReferenceValue = frame;
        sso.FindProperty("nameText").objectReferenceValue = nameText;
        sso.FindProperty("carryGlow").objectReferenceValue = carryGlow.gameObject;
        sso.ApplyModifiedPropertiesWithoutUndo();
        so.FindProperty(weapon ? "weaponSlot" : "armorSlot").objectReferenceValue = slot;
        return slotRt;
    }

    /// <summary>装備アイコンの Image から、古い「Icon」一式（枠・背景・マスク・アイコン）の根を探す。</summary>
    private static RectTransform FindIconGroup(Image iconImage)
    {
        if (iconImage == null) return null;
        var t = iconImage.transform;
        while (t.parent != null && t.parent.name != "HeroPanel" && t.parent.name != "Frame")
            t = t.parent;
        return t as RectTransform;
    }

    private static void BuildCraft(RectTransform layout, SerializedObject so)
    {
        var craft = Ref<GameObject>(so, "craftRoot");
        if (craft == null) return;
        var rt = (RectTransform)craft.transform;
        rt.SetParent(layout, false);
        rt.name = "CraftRoot";
        PlaceTL(rt, 30, 692, 280, 58);
        var hl = craft.GetComponent<HorizontalLayoutGroup>();
        if (hl != null)
        {
            hl.spacing = 12;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = true;
        }
        foreach (var b in new[] { Ref<Button>(so, "craftFineButton"), Ref<Button>(so, "craftDullButton") })
        {
            if (b == null) continue;
            var le = GetOrAdd<LayoutElement>(b.gameObject);
            le.preferredWidth = 134; le.preferredHeight = 58;
            var l = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (l != null)
            {
                SetFont(l, true);
                l.enableAutoSizing = true; l.fontSizeMin = 16; l.fontSizeMax = 26;
            }
        }
    }

    private static void BuildBack(RectTransform layout, SerializedObject so)
    {
        var back = Ref<Button>(so, "closeButton");
        if (back == null) return;
        var rt = (RectTransform)back.transform;
        rt.SetParent(layout, false);
        rt.name = "Back";
        PlaceTL(rt, 26, 206, 146, 152);
    }

    // ───────── 能力値の板 ─────────

    private static void BuildStats(RectTransform layout, SerializedObject so)
    {
        var board = NewImage("StatsBoard", layout, Part("vui_panel_frame"), Color.white, 3.4f);
        board.raycastTarget = true; // 後ろの立ち絵を押さない
        PlaceTL(board.rectTransform, 22, 776, 756, 284);
        var b = board.rectTransform;

        // 左の列: Lv・HP・MP
        var lv = Adopt<TextMeshProUGUI>(so, "heroLevelText", b, "Lv");
        if (lv != null)
        {
            PlaceTL(lv.rectTransform, 44, 30, 330, 58);
            StyleText(lv, true, 40, 22, BrightGold, TextAlignmentOptions.Left);
            lv.text = "Lv 12  <size=62%>EXP 30/100</size>";
        }
        StatRow(b, so, "HP", "heroHpText", "HPNum", 44, 98, "840");
        StatRow(b, so, "MP", "heroMpText", "MPNum", 44, 164, "120");
        Chip(b, so, "HpChip", "hpDeltaText", 248, 102);

        // 右の列: 攻撃・防御・クリア見込み
        var atk = Adopt<TextMeshProUGUI>(so, "heroStatusText", b, "Attack");
        if (atk != null)
        {
            PlaceTL(atk.rectTransform, 396, 30, 196, 58);
            StyleText(atk, true, 36, 20, Cream, TextAlignmentOptions.Left);
            atk.text = "攻撃 86";
        }
        var def = NewText("Defense", b, true, 36, Cream, "防御 54");
        PlaceTL(def.rectTransform, 396, 98, 196, 58);
        StyleText(def, true, 36, 20, Cream, TextAlignmentOptions.Left);
        so.FindProperty("defenseText").objectReferenceValue = def;
        Chip(b, so, "AttackChip", "attackDeltaText", 596, 36);
        Chip(b, so, "DefenseChip", "defenseDeltaText", 596, 104);

        // クリア見込み（生成りの札。段階の色は生成りの上で読める濃さ）
        var box = NewImage("ClearBox", b, Part("vui_sign_box"), Color.white, 1f);
        PlaceTL(box.rectTransform, 388, 164, 330, 78);
        var clearLabel = Ref<Button>(so, "clearProbabilityInfoButton");
        if (clearLabel != null)
        {
            var crt = (RectTransform)clearLabel.transform;
            crt.SetParent(box.transform, false);
            crt.name = "ClearLabel";
            PlaceTL(crt, 16, 6, 170, 30);
            var t = clearLabel.GetComponent<TextMeshProUGUI>();
            if (t != null)
            {
                StyleText(t, false, 22, 16, MidBrown, TextAlignmentOptions.Left);
                t.text = "クリア見込み";
                t.raycastTarget = true;
            }
        }
        var clearNum = Adopt<TextMeshProUGUI>(so, "clearProbabilityText", box.rectTransform, "ClearNum");
        if (clearNum != null)
        {
            PlaceTL(clearNum.rectTransform, 16, 34, 160, 38);
            StyleText(clearNum, false, 30, 18, Ink, TextAlignmentOptions.Left);
            clearNum.text = "<color=#6E9E2E>有利</color>";
        }
        var pips = so.FindProperty("clearMeterPips");
        pips.arraySize = 5;
        for (int i = 0; i < 5; i++)
        {
            var pip = NewImage($"Pip{i}", box.transform, Part("vui_round_white"), i < 4 ? Hex("6E9E2E") : Hex("4A3020"), 0.5f);
            PlaceTL(pip.rectTransform, 176 + i * 29, 44, 24, 24);
            pips.GetArrayElementAtIndex(i).objectReferenceValue = pip;
        }
    }

    private static void StatRow(RectTransform board, SerializedObject so, string label, string field, string name, float x, float y, string sample)
    {
        var l = FindDeep(board, label + "Label")?.GetComponent<TextMeshProUGUI>();
        if (l == null)
        {
            // 古い静的ラベル（「HP」「MP」）があれば使う
            var old = FindDeepUnder(so, label);
            l = old != null ? old.GetComponent<TextMeshProUGUI>() : null;
            if (l == null) l = NewText(label + "Label", board, true, 28, BrightGold, label);
            l.transform.SetParent(board, false);
            l.name = label + "Label";
        }
        PlaceTL(l.rectTransform, x, y + 6, 64, 50);
        StyleText(l, true, 28, 18, BrightGold, TextAlignmentOptions.Left);
        l.text = label;
        var num = Adopt<TextMeshProUGUI>(so, field, board, name);
        if (num == null) return;
        PlaceTL(num.rectTransform, x + 66, y, 136, 58);
        StyleText(num, true, 36, 20, Cream, TextAlignmentOptions.Left);
        num.text = sample;
    }

    /// <summary>付けた後の差の札（緑の「+12」など）。文字の親（名前が …Chip）が札の板。</summary>
    private static void Chip(RectTransform board, SerializedObject so, string name, string field, float x, float y)
    {
        var chip = NewImage(name, board, Part("vui_round_white"), new Color(0.1f, 0.06f, 0.03f, 0.78f), 1.4f);
        PlaceTL(chip.rectTransform, x, y, 112, 46);
        var t = NewText("Value", chip.transform, true, 28, Hex("7CD36A"), "+12");
        Stretch(t.rectTransform, 0);
        t.rectTransform.offsetMin = new Vector2(8, 2);
        t.rectTransform.offsetMax = new Vector2(-8, -2);
        StyleText(t, true, 28, 16, Hex("7CD36A"));
        so.FindProperty(field).objectReferenceValue = t;
    }

    // ───────── 品物の棚 ─────────

    private const float CardW = 494f, CardH = 100f;

    private static void BuildShelf(RectTransform layout, SerializedObject so)
    {
        var shelf = NewImage("ShelfPanel", layout, Part("vui_panel_frame"), Color.white, 2.6f);
        shelf.raycastTarget = true;
        PlaceTL(shelf.rectTransform, 796, 214, 1100, 584);
        var s = shelf.rectTransform;

        var title = NewText("Title", s, true, 40, Cream, "品物の棚");
        PlaceTL(title.rectTransform, 46, 30, 230, 64);
        StyleText(title, true, 40, 24, Cream, TextAlignmentOptions.Left);

        var wTab = Tab(s, "WeaponTab", Load("Assets/UI/鍛冶屋・道具屋/1−0武器.png"), 286);
        var aTab = Tab(s, "ArmorTab", Load("Assets/UI/鍛冶屋・道具屋/2−0防具.png"), 430);
        so.FindProperty("weaponTabButton").objectReferenceValue = wTab;
        so.FindProperty("armorTabButton").objectReferenceValue = aTab;
        so.FindProperty("weaponTabOn").objectReferenceValue = wTab.transform.Find("On").gameObject;
        so.FindProperty("armorTabOn").objectReferenceValue = aTab.transform.Find("On").gameObject;

        var hint = NewText("Hint", s, true, 22, SoftCream, "品を勇者へドラッグして装備（2 回押しても付く）");
        PlaceTL(hint.rectTransform, 590, 40, 470, 44);
        StyleText(hint, true, 22, 14, SoftCream, TextAlignmentOptions.Right);

        // スクロールする棚
        var scrollImg = NewImage("Scroll", s, Part("vui_round_white"), new Color(0.12f, 0.07f, 0.04f, 0.45f), 1.2f);
        scrollImg.raycastTarget = true; // ホイールを受ける
        PlaceTL(scrollImg.rectTransform, 38, 116, 1024, 436);
        var scroll = GetOrAdd<ScrollRect>(scrollImg.gameObject);
        GetOrAdd<ScrollRectAutoFollow>(scrollImg.gameObject);
        var viewport = NewRect("Viewport", scrollImg.transform);
        Stretch(viewport, 0);
        viewport.offsetMax = new Vector2(-20, 0);
        GetOrAdd<RectMask2D>(viewport.gameObject);
        var content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, 400); content.localScale = Vector3.one;
        var grid = GetOrAdd<GridLayoutGroup>(content.gameObject);
        grid.cellSize = new Vector2(CardW, CardH);
        grid.spacing = new Vector2(12, 14);
        grid.padding = new RectOffset(8, 0, 16, 12);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        var fitter = GetOrAdd<ContentSizeFitter>(content.gameObject);
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // スクロールバー（細い金）
        var bar = NewImage("Scrollbar", scrollImg.transform, Part("vui_round_white"), new Color(0.1f, 0.06f, 0.03f, 0.7f), 4f);
        var brt = bar.rectTransform;
        brt.anchorMin = new Vector2(1, 0); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(1, 0.5f);
        brt.anchoredPosition = new Vector2(-4, 0); brt.sizeDelta = new Vector2(12, -16); brt.localScale = Vector3.one;
        bar.raycastTarget = true;
        var area = NewRect("Sliding Area", bar.transform);
        Stretch(area, 0);
        var handle = NewImage("Handle", area, Part("vui_round_white"), Gold, 4f);
        Stretch(handle.rectTransform, 0);
        handle.raycastTarget = true;
        var sb = GetOrAdd<Scrollbar>(bar.gameObject);
        sb.handleRect = handle.rectTransform;
        sb.targetGraphic = handle;
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.navigation = new Navigation { mode = Navigation.Mode.None };

        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40;
        scroll.verticalScrollbar = sb;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        so.FindProperty("shelfScroll").objectReferenceValue = scroll;
        so.FindProperty("shelfContent").objectReferenceValue = content;

        var card = BuildCardTemplate(s, so);
        so.FindProperty("cardTemplate").objectReferenceValue = card;
    }

    private static Button Tab(RectTransform shelf, string name, Sprite sprite, float x)
    {
        var img = NewImage(name, shelf, sprite, Color.white, 1f, sliced: false);
        img.raycastTarget = true;
        img.preserveAspect = true;
        PlaceTL(img.rectTransform, x, 18, 134, 86);
        var b = GetOrAdd<Button>(img.gameObject);
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None; // 選んでいるかは View が色で出す
        var on = NewImage("On", img.transform, Load("Assets/UI/Preparation/Prep_GoldFrame.png"), Color.white, 2.2f);
        Stretch(on.rectTransform, -2);
        on.rectTransform.offsetMin = new Vector2(-3, 1);
        on.rectTransform.offsetMax = new Vector2(3, -1);
        return b;
    }

    private static HeroEquipmentCard BuildCardTemplate(RectTransform shelf, SerializedObject so)
    {
        var bg = NewImage("CardTemplate", shelf, Part("vui_article_big"), Color.white, 4f);
        bg.raycastTarget = true;
        PlaceTL(bg.rectTransform, 40, 120, CardW, CardH);
        var c = bg.rectTransform;
        var card = GetOrAdd<HeroEquipmentCard>(bg.gameObject);
        var button = GetOrAdd<Button>(bg.gameObject);
        button.targetGraphic = bg;
        SetTint(button);
        var oldGroup = bg.GetComponent<CanvasGroup>();
        if (oldGroup != null) Object.DestroyImmediate(oldGroup); // 薄くするのは幕（Dim）で。CanvasGroup は棚の並ぶ演出が使う

        var weakFrame = NewImage("WeakFrame", c, Load("Assets/UI/Preparation/Prep_GoldFrame.png"), Hex("FF5A40"), 2.4f);
        Stretch(weakFrame.rectTransform, -3);

        var iconBox = NewImage("IconBox", c, Load("Assets/UI/鍛冶屋・道具屋/8−1アイコン背景.png"), Color.white, 1f, sliced: false);
        PlaceTL(iconBox.rectTransform, 10, 8, 84, 84);
        var icon = NewImage("Icon", iconBox.transform, null, Color.white, 1f, sliced: false);
        Stretch(icon.rectTransform, 12);
        icon.preserveAspect = true;
        var iconFrame = NewImage("Frame", iconBox.transform, Load("Assets/UI/鍛冶屋・道具屋/8−0アイコン枠.png"), Color.white, 1f, sliced: false);
        Stretch(iconFrame.rectTransform, 0);

        var nameText = NewText("Name", c, false, 28, Ink, "炎の剣");
        PlaceTL(nameText.rectTransform, 106, 8, 264, 40);
        StyleText(nameText, false, 28, 18, Ink, TextAlignmentOptions.Left);
        var info = NewText("Info", c, false, 21, MidBrown, "在庫12・1,240G");
        PlaceTL(info.rectTransform, 106, 48, 264, 26);
        StyleText(info, false, 21, 14, MidBrown, TextAlignmentOptions.Left);
        var bonus = NewText("Bonus", c, false, 21, BonusGreen, "攻撃+10%");
        PlaceTL(bonus.rectTransform, 106, 72, 264, 24);
        StyleText(bonus, false, 21, 14, BonusGreen, TextAlignmentOptions.Left);

        var attr = NewImage("Attribute", c, Part("vui_round_white"), Hex("5A3420"), 1.6f);
        PlaceTL(attr.rectTransform, 378, 12, 100, 38);
        var attrIcon = NewImage("Icon", attr.transform, null, Color.white, 1f, sliced: false);
        PlaceTL(attrIcon.rectTransform, 4, 3, 32, 32);
        attrIcon.preserveAspect = true;
        var attrText = NewText("Text", attr.transform, true, 24, Cream, "火");
        PlaceTL(attrText.rectTransform, 38, 0, 58, 38);
        StyleText(attrText, true, 24, 16, Cream);

        var equipped = NewImage("EquippedTag", c, Part("vui_round_white"), Gold, 1.6f);
        PlaceTL(equipped.rectTransform, 378, 56, 100, 34);
        var eqText = NewText("Text", equipped.transform, false, 20, Ink, "装備中");
        Stretch(eqText.rectTransform, 0);
        StyleText(eqText, false, 20, 14, Ink);

        // 在庫 0・持ち上げ中に重ねる生成りの幕（View が濃さを決める）
        var dim = NewImage("Dim", c, Part("vui_article_big"), new Color(1f, 0.96f, 0.88f, 0.5f), 4f);
        Stretch(dim.rectTransform, 0);
        dim.gameObject.SetActive(false);

        var weakTag = NewImage("WeakTag", c, Part("vui_round_white"), Red, 1.6f);
        PlaceTL(weakTag.rectTransform, -6, -12, 66, 30);
        var wt = NewText("Text", weakTag.transform, true, 20, Color.white, "弱点");
        Stretch(wt.rectTransform, 0);
        StyleText(wt, true, 20, 14, Color.white);
        weakTag.transform.SetAsLastSibling();

        var cso = new SerializedObject(card);
        cso.FindProperty("button").objectReferenceValue = button;
        cso.FindProperty("iconBox").objectReferenceValue = iconBox.rectTransform;
        cso.FindProperty("icon").objectReferenceValue = icon;
        cso.FindProperty("nameText").objectReferenceValue = nameText;
        cso.FindProperty("infoText").objectReferenceValue = info;
        cso.FindProperty("bonusText").objectReferenceValue = bonus;
        cso.FindProperty("attributeRoot").objectReferenceValue = attr.gameObject;
        cso.FindProperty("attributeIcon").objectReferenceValue = attrIcon;
        cso.FindProperty("attributeText").objectReferenceValue = attrText;
        cso.FindProperty("weakTag").objectReferenceValue = weakTag.gameObject;
        cso.FindProperty("weakFrame").objectReferenceValue = weakFrame.gameObject;
        cso.FindProperty("equippedTag").objectReferenceValue = equipped.gameObject;
        cso.FindProperty("dimOverlay").objectReferenceValue = dim;
        cso.ApplyModifiedPropertiesWithoutUndo();

        bg.gameObject.SetActive(false);
        return card;
    }

    // ───────── 作戦のプルダウン ─────────

    private const float TacticsRowH = 92f;
    private const float TacticsPad = 26f; // 開いた一覧の枠の上下の余白（行は Content の真ん中に置き、上下に同じだけ空ける）

    private static void BuildTactics(RectTransform layout, SerializedObject so)
    {
        var box = NewImage("TacticsDropdown", layout, Part("vui_wood_plate"), Color.white, 1.5f);
        box.raycastTarget = true;
        PlaceTL(box.rectTransform, 796, 806, 1100, 102);
        var b = box.rectTransform;
        var dd = GetOrAdd<TMP_Dropdown>(box.gameObject);
        dd.targetGraphic = box;
        SetTint(dd);

        var ring = NewImage("IconRing", b, Load("Assets/UI/戦闘/介入/Ring_Gold.png"), Color.white, 1f, sliced: false);
        PlaceTL(ring.rectTransform, 24, 11, 80, 80);
        var capIcon = NewImage("Icon", ring.transform, null, Color.white, 1f, sliced: false);
        Stretch(capIcon.rectTransform, 12);
        capIcon.preserveAspect = true;

        var caption = NewText("Caption", b, true, 32, Cream, "作戦: 攻撃重視（武器↑）");
        PlaceTL(caption.rectTransform, 120, 10, 840, 46);
        StyleText(caption, true, 32, 20, Cream, TextAlignmentOptions.Left);
        var desc = NewText("Desc", b, true, 22, SoftCream, "敵を倒すたびの武器の値上がりと、話題性が強くなる");
        PlaceTL(desc.rectTransform, 122, 56, 840, 34);
        StyleText(desc, true, 22, 14, SoftCream, TextAlignmentOptions.Left);
        so.FindProperty("tacticsDescText").objectReferenceValue = desc;

        // 説明（？）: ホバーで作戦の説明。古い「作戦」の文字ボタンを移す
        var infoPlate = NewImage("InfoPlate", b, Part("vui_round_white"), Hex("3A2414"), 1f);
        PlaceTL(infoPlate.rectTransform, 972, 27, 48, 48);
        var info = Ref<Button>(so, "tacticsInfoButton");
        if (info != null)
        {
            var irt = (RectTransform)info.transform;
            irt.SetParent(infoPlate.transform, false);
            irt.name = "TacticsInfo";
            Stretch(irt, 0);
            var t = info.GetComponent<TextMeshProUGUI>();
            if (t != null)
            {
                StyleText(t, true, 30, 18, BrightGold);
                t.text = "？";
                t.raycastTarget = true;
            }
        }
        var arrow = NewText("Arrow", b, true, 30, BrightGold, "▼");
        PlaceTL(arrow.rectTransform, 1030, 26, 44, 50);
        StyleText(arrow, true, 30, 18, BrightGold);

        // 開いた一覧（上に開く。5 行が全部入る）
        var template = NewImage("Template", b, Part("vui_panel_frame"), Color.white, 3.2f);
        var trt = template.rectTransform;
        trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 0);
        trt.anchoredPosition = new Vector2(0, 6);
        trt.sizeDelta = new Vector2(0, TacticsRowH * 5 + TacticsPad * 2 + 8); // 開くとき TMP_Dropdown が行の数に合わせて縮める
        trt.localScale = Vector3.one;
        template.raycastTarget = true;
        var tscroll = GetOrAdd<ScrollRect>(template.gameObject);
        var vp = NewRect("Viewport", trt);
        Stretch(vp, 0);
        // 上下は余白を付けない（TMP_Dropdown は一覧の高さを Content の高さに縮めるので、余白は Content の中に持つ）
        vp.offsetMin = new Vector2(26, 0); vp.offsetMax = new Vector2(-26, 0);
        GetOrAdd<RectMask2D>(vp.gameObject);
        var content = NewRect("Content", vp);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, TacticsRowH + TacticsPad * 2); content.localScale = Vector3.one;
        tscroll.content = content;
        tscroll.viewport = vp;
        tscroll.horizontal = false;
        tscroll.vertical = true;
        tscroll.movementType = ScrollRect.MovementType.Clamped;
        tscroll.scrollSensitivity = TacticsRowH * 0.5f;

        var item = NewRect("Item", content);
        item.anchorMin = new Vector2(0, 0.5f); item.anchorMax = new Vector2(1, 0.5f); item.pivot = new Vector2(0.5f, 0.5f);
        item.anchoredPosition = Vector2.zero; item.sizeDelta = new Vector2(0, TacticsRowH); item.localScale = Vector3.one;
        var toggle = GetOrAdd<Toggle>(item.gameObject);
        var itemBg = NewImage("Item Background", item, Part("vui_round_white"), Hex("4A2C18"), 1.2f);
        Stretch(itemBg.rectTransform, 0);
        itemBg.raycastTarget = true;
        var check = NewImage("Item Checkmark", item, Load("Assets/UI/Preparation/Prep_GoldFrame.png"), Color.white, 2.4f);
        Stretch(check.rectTransform, 0);
        var itemIcon = NewImage("Item Icon", item, null, Color.white, 1f, sliced: false);
        Place(itemIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(72, 72));
        itemIcon.preserveAspect = true;
        var itemLabel = NewText("Item Label", item, true, 30, Cream, "攻撃重視（武器↑）\n<size=70%>説明</size>");
        var lrt = itemLabel.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.offsetMin = new Vector2(104, 4); lrt.offsetMax = new Vector2(-18, -4); lrt.localScale = Vector3.one;
        StyleText(itemLabel, true, 30, 16, Cream, TextAlignmentOptions.Left);
        itemLabel.lineSpacing = -6;
        toggle.targetGraphic = itemBg;
        toggle.graphic = check;
        toggle.isOn = true;
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.25f, 1.1f, 1f); // 茶色の行を明るく
        colors.selectedColor = new Color(1.35f, 1.25f, 1.1f, 1f);
        colors.pressedColor = new Color(0.85f, 0.8f, 0.75f, 1f);
        colors.colorMultiplier = 1.4f;
        toggle.colors = colors;
        template.gameObject.SetActive(false);

        dd.template = trt;
        // 閉じた枠の文字とアイコンは View が出す（TMP_Dropdown に任せると一覧の 2 行の表記で上書きされる）
        dd.captionText = null;
        dd.captionImage = null;
        so.FindProperty("tacticsCaptionText").objectReferenceValue = caption;
        so.FindProperty("tacticsCaptionIcon").objectReferenceValue = capIcon;
        dd.itemText = itemLabel;
        dd.itemImage = itemIcon;
        dd.alphaFadeSpeed = 0.12f;

        so.FindProperty("tacticsDropdown").objectReferenceValue = dd;
        var icons = so.FindProperty("tacticsIcons");
        var sprites = new[]
        {
            Load("Assets/UI/戦闘/介入/Icon_Special.png"),      // 攻撃重視: 炎の剣
            Load("Assets/UI/レリック/百貨店の理念.png"),        // 防御重視: 紋章の盾
            Load("Assets/UI/戦闘/介入/Tab_Hero_Portrait.png"), // バランス: 勇者
            Load("Assets/UI/Preparation/Prep_IconAppraisal.png"), // 慎重: 虫めがね
            Load("Assets/UI/戦闘/介入/Icon_Skill.png"),         // 属性重視: 魔法の剣
        };
        icons.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++) icons.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
    }

    // ───────── セリフ ─────────

    private static void BuildDialogue(RectTransform layout, SerializedObject so, RectTransform panel)
    {
        var text = Ref<TextMeshProUGUI>(so, "dialogueText");
        if (text == null) return;
        // 古い「Message」の枠（長いメッセージボックス）を板にする
        var board = text.transform.parent as RectTransform;
        if (board == null || board == panel || board == layout) board = NewRect("DialogueBoard", layout);
        board.SetParent(layout, false);
        board.name = "DialogueBoard";
        PlaceTL(board, 796, 922, 1100, 138);
        var img = GetOrAdd<Image>(board.gameObject);
        SetImage(img, Part("vui_wood_plate"), Color.white, 1.5f);
        img.raycastTarget = false;
        var rt = text.rectTransform;
        rt.SetParent(board, false);
        rt.name = "Messagetext";
        Stretch(rt, 0);
        rt.offsetMin = new Vector2(44, 18);
        rt.offsetMax = new Vector2(-44, -18);
        StyleText(text, true, 28, 16, Cream, TextAlignmentOptions.Left);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.text = "「炎の剣か。今回のダンジョンにはこいつが効くはずだ」";
    }

    // ───────── 小物 ─────────

    private static T Adopt<T>(SerializedObject so, string field, Transform parent, string name) where T : Component
    {
        var c = Ref<T>(so, field);
        if (c == null) return null;
        c.transform.SetParent(parent, false);
        c.name = name;
        return c;
    }

    /// <summary>古いパネル（HeroPanel の中）か、新しい板の中から名前で探す。</summary>
    private static Transform FindDeepUnder(SerializedObject so, string name)
    {
        var view = (Component)so.targetObject;
        var inner = FindChild(view.transform, "HeroPanel");
        return inner != null ? FindChild(inner, name) : null;
    }

    private static Transform FindChild(Transform t, string name)
    {
        foreach (Transform c in t)
            if (c.name == name) return c;
        return null;
    }

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

    private static void SetTint(Selectable b)
    {
        b.transition = Selectable.Transition.ColorTint;
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.93f, 0.8f, 1f);
        colors.selectedColor = new Color(1f, 0.93f, 0.8f, 1f);
        colors.pressedColor = new Color(0.82f, 0.76f, 0.7f, 1f);
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        b.colors = colors;
    }

    private static void StyleText(TextMeshProUGUI t, bool outline, float max, float min, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        SetFont(t, outline);
        t.fontSize = max; t.enableAutoSizing = true; t.fontSizeMin = min; t.fontSizeMax = max;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.richText = true;
    }

    private static Sprite Part(string name) => Load(PartsDir + name + ".png");

    private static T GetOrAdd<T>(GameObject go) where T : Component
        => go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();

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
        if (o is GameObject g && typeof(Component).IsAssignableFrom(typeof(T))) return g.GetComponent(typeof(T)) as T;
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

    /// <summary>親の左上から (x, y) に幅 w・高さ h で置く（y は下向き）。</summary>
    private static void PlaceTL(RectTransform rt, float x, float y, float w, float h)
        => Place(rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    private static Color Hex(string h) => ColorUtility.TryParseHtmlString("#" + h, out var c) ? c : Color.magenta;

    // ───────── 調査用 ─────────

    /// <summary>プレハブの階層を書き出す（-executeMethod HeroUIBuilder.DumpBatch -dumpOut &lt;file&gt;）。</summary>
    public static void DumpBatch()
    {
        string path = @"D:\WorkTree\TomsLands\_scratch_wt04\hero\prefab_dump.txt";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-dumpOut") path = args[i + 1];
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var sb = new StringBuilder();
            Dump(root.transform, 0, sb);
            System.IO.File.WriteAllText(path, sb.ToString());
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        EditorApplication.Exit(0);
    }

    private static void Dump(Transform t, int depth, StringBuilder sb)
    {
        sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [off]");
        if (t is RectTransform rt)
            sb.Append($" a({rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##})-({rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##}) pos{rt.anchoredPosition} size{rt.sizeDelta}");
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null || c is Transform) continue;
            sb.Append(" |").Append(c.GetType().Name);
            if (c is Image img) sb.Append($"(spr={(img.sprite ? img.sprite.name : "none")})");
            if (c is TMP_Text tx) sb.Append($"(\"{tx.text.Replace("\n", "\\n")}\")");
        }
        sb.AppendLine();
        foreach (Transform ch in t) Dump(ch, depth + 1, sb);
    }
}
