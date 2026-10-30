using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 占い師の画面（Assets/Prefabs/Screens/FortuneTeller.prefab）を「上げる枠・下げる枠」（Docs/Prophet_Intervention.md）に組み直して保存する。
///   左: 占い師（立ち絵・セリフの吹き出し・下に力の玉）
///   右上: ▲ 上げる枠（緑）・▼ 下げる枠（赤）
///   右下: 品のカードの棚（絞り込み・並べ替え・スクロール）
/// 今ある部品（もどる・立ち絵・セリフの文字）は ProphetView の参照から探して新しい場所へ移す。古い占いの一式（Trend の下の
/// 荒れ相場・高値・おすすめ・次のダンジョン・占うボタン）と古いメッセージ枠は消す。
/// 何度実行しても部品は名前で探して作り直さない（重複しない）。座標は 1920x1080 の左上から（Layout の中）。
/// TomsShop シーンの FortuneTeller は位置・名前しか上書きしていないので、シーンは触らない。
/// バッチ: Unity.exe -batchmode -projectPath &lt;wt&gt; -executeMethod ProphetUIBuilder.BuildBatch -logFile &lt;log&gt;
/// </summary>
public static class ProphetUIBuilder
{
    private const string Tag = "[ProphetUIBuilder]";
    public const string PrefabPath = "Assets/Prefabs/Screens/FortuneTeller.prefab";
    private const string PartsDir = "Assets/UI/Village/Parts/";
    private const string FontOutline = "Assets/Font/MPLUSRounded1c-Bold SDFPlusPadding.asset";
    private const string FontPlain = "Assets/Font/MPLUSRounded1c-Bold SDFPlusPaddingNoOutLine.asset";
    private const string GoldFrame = "Assets/UI/Preparation/Prep_GoldFrame.png";
    private const string Glow = "Assets/UI/Village/ArrivalFx/arrival_glow.png";
    private const string IconBack = "Assets/UI/鍛冶屋・道具屋/8−1アイコン背景.png";
    private const string IconFrame = "Assets/UI/鍛冶屋・道具屋/8−0アイコン枠.png";

    private static readonly Color Ink = Hex("3A2414");
    private static readonly Color MidBrown = Hex("6B4630");
    private static readonly Color Gold = Hex("E9B949");
    private static readonly Color BrightGold = Hex("FFD970");
    private static readonly Color Cream = Hex("FFF3D6");
    private static readonly Color Lavender = Hex("E6D4FF");
    private static readonly Color Up2 = Hex("9BE38B");
    private static readonly Color Down2 = Hex("FF9C86");

    public const float CardW = 182f, CardH = 170f;

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

    [MenuItem("Tools/TomsLands/占い師の画面を組み直す（上げる枠・下げる枠）")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = root.GetComponentInChildren<ProphetView>(true);
            if (view == null) throw new System.InvalidOperationException("ProphetView が無い");
            // 前回の組み直しで型が引けずに外れたスクリプト（m_Script 0）を掃除する（同じ名前の部品に付け直すため）
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            var so = new SerializedObject(view);
            var panel = (RectTransform)view.transform;

            var layout = NewRect("Layout", panel);
            Place(layout, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
            var bg = FindChild(panel, "BackGround");
            layout.SetSiblingIndex(bg != null ? bg.GetSiblingIndex() + 1 : 0);

            // 古いセリフの文字は先に退避（古いメッセージ枠ごと消すため）
            var dialogue = FindDeep(panel, "Messagetext")?.GetComponent<TextMeshProUGUI>();
            if (dialogue == null) dialogue = FindDeep(layout, "Dialogue")?.GetComponent<TextMeshProUGUI>();

            BuildSeer(layout, so, dialogue);
            var up = BuildSlot(layout, so, ProphetDirection.Up, "UpSlot", 650);
            BuildSlot(layout, so, ProphetDirection.Down, "DownSlot", 1284);
            var hint = NewText("Hint", layout, false, 22, Lavender, "品のカードを上の枠へ（押してから枠を押しても入る）");
            PlaceTL(hint.rectTransform, 656, 542, 1240, 34);
            StyleText(hint, true, 22, 14, Lavender, TextAlignmentOptions.Left);
            so.FindProperty("hintText").objectReferenceValue = hint;
            BuildShelf(layout, so);
            BuildBack(layout, so);

            foreach (var n in new[] { "SeerPanel", "UpSlot", "DownSlot", "Hint", "ShelfPanel", "Back" })
            {
                var t = layout.Find(n);
                if (t != null) t.SetAsLastSibling();
            }

            var scope = view.GetComponent<UIFocusScope>();
            if (scope != null)
            {
                var sso = new SerializedObject(scope);
                var p = sso.FindProperty("firstSelected");
                if (p != null) p.objectReferenceValue = up.GetComponent<Button>();
                sso.ApplyModifiedPropertiesWithoutUndo();
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            // 古い占いの一式（荒れ相場・高値・おすすめ・次のダンジョン・占うボタン）と古いメッセージ枠を消す
            foreach (var name in new[] { "Trend", "Message" })
            {
                var old = FindChild(panel, name);
                if (old == null) continue;
                Debug.Log($"{Tag} 古い部品を消す: {name}（子: {string.Join(", ", old.Cast<Transform>().Select(c => c.name))}）");
                Object.DestroyImmediate(old.gameObject);
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"{Tag} 組んで保存した: {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ───────── 左: 占い師 ─────────

    private static void BuildSeer(RectTransform layout, SerializedObject so, TextMeshProUGUI dialogue)
    {
        // 占い師には枠を付けない（ユーザー指示）: 板は透明にして、立ち絵は縁をぼかして切る
        var panelImg = NewImage("SeerPanel", layout, Part("vui_round_white"), new Color(0f, 0f, 0f, 0f), 1.4f);
        panelImg.raycastTarget = true;
        PlaceTL(panelImg.rectTransform, 146, 192, 486, 868);
        var p = panelImg.rectTransform;

        var mask = NewRect("FigureMask", p);
        Stretch(mask, 6);
        var rectMask = GetOrAdd<RectMask2D>(mask.gameObject);
        rectMask.softness = new Vector2Int(48, 0);
        var chara = Ref<Button>(so, "characterButton");
        if (chara != null)
        {
            var rt = (RectTransform)chara.transform;
            rt.SetParent(mask, false);
            rt.name = "Figure";
            // 3240 四方の絵（人物は横 25〜75%・頭が上 2%）。上半身を吹き出しの上に見せる
            PlaceTL(rt, -262, 2, 1100, 1100);
            var img = chara.GetComponent<Image>();
            if (img != null)
            {
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = Color.white;
            }
        }

        var rim = NewImage("Rim", p, Load(GoldFrame), Color.white, 1.4f);
        Stretch(rim.rectTransform, -3);
        rim.gameObject.SetActive(false); // 枠は出さない

        // 吹き出し（しっぽは上向き）
        var tail = NewImage("BubbleTail", p, Part("vui_round_white"), Hex("FFF8E6"), 4f, sliced: false);
        PlaceTL(tail.rectTransform, 224, 452, 36, 36);
        tail.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var bubble = NewImage("SayBubble", p, Part("vui_round_white"), Hex("FFF8E6"), 1.6f);
        PlaceTL(bubble.rectTransform, 16, 466, 454, 228);
        if (dialogue != null)
        {
            var rt = dialogue.rectTransform;
            rt.SetParent(bubble.transform, false);
            rt.name = "Dialogue";
            Stretch(rt, 0);
            rt.offsetMin = new Vector2(22, 16);
            rt.offsetMax = new Vector2(-22, -16);
            StyleText(dialogue, false, 26, 16, Ink, TextAlignmentOptions.TopLeft);
            dialogue.textWrappingMode = TextWrappingModes.Normal;
            dialogue.text = "上げたい品は緑の枠へ、下げたい品は赤の枠へ。力は配信 1 回で 1 つ戻るよ。";
            so.FindProperty("dialogueText").objectReferenceValue = dialogue;
        }

        // 力の玉
        var box = NewImage("PowerBox", p, Part("vui_round_white"), new Color(0f, 0f, 0f, 0.55f), 1.6f);
        PlaceTL(box.rectTransform, 16, 708, 454, 146);
        var boxRim = NewImage("Rim", box.transform, Load(GoldFrame), Color.white, 1.8f);
        Stretch(boxRim.rectTransform, -2);
        var power = NewText("PowerText", box.transform, true, 30, Cream, "占い師の力 2 / 2");
        PlaceTL(power.rectTransform, 22, 10, 410, 44);
        StyleText(power, true, 30, 18, Cream, TextAlignmentOptions.Left);
        so.FindProperty("powerText").objectReferenceValue = power;
        var gems = so.FindProperty("powerGems");
        gems.arraySize = 2;
        for (int i = 0; i < 2; i++)
        {
            var holder = NewRect($"Gem{i}", box.transform);
            PlaceTL(holder, 24 + i * 74, 56, 56, 56);
            var glow = NewImage("Glow", holder, Load(Glow), new Color(0.79f, 0.66f, 1f, 0.75f), 1f, sliced: false);
            Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(104, 104));
            var gem = NewImage("Gem", holder, Part("vui_round_white"), new Color(0.84f, 0.72f, 1f, 1f), 1.8f);
            Stretch(gem.rectTransform, 4);
            var shine = NewImage("Shine", gem.transform, Part("vui_round_white"), new Color(1f, 1f, 1f, 0.6f), 5.5f);
            PlaceTL(shine.rectTransform, 9, 8, 15, 15);
            gems.GetArrayElementAtIndex(i).objectReferenceValue = gem;
        }
        var note = NewText("Note", box.transform, false, 22, Lavender, "配信 1 回で 1 つ回復（最大 2）");
        PlaceTL(note.rectTransform, 182, 66, 256, 40);
        StyleText(note, true, 22, 14, Lavender, TextAlignmentOptions.Left);
    }

    private static void BuildBack(RectTransform layout, SerializedObject so)
    {
        var back = Ref<Button>(so, "closeButton");
        if (back == null) return;
        var rt = (RectTransform)back.transform;
        rt.SetParent(layout, false);
        rt.name = "Back";
        PlaceTL(rt, 12, 198, 124, 128);
    }

    // ───────── 右上: 上げる枠・下げる枠 ─────────

    private static RectTransform BuildSlot(RectTransform layout, SerializedObject so, ProphetDirection dir, string name, float x)
    {
        const float w = 616f, h = 344f;
        bool up = dir == ProphetDirection.Up;
        Color accent = up ? Up2 : Down2;
        var bgImg = NewImage(name, layout, Part("vui_round_white"), up ? new Color(0.16f, 0.33f, 0.12f, 0.55f) : new Color(0.4f, 0.14f, 0.1f, 0.55f), 1.2f);
        bgImg.raycastTarget = true;
        PlaceTL(bgImg.rectTransform, x, 192, w, h);
        var s = bgImg.rectTransform;
        var slot = GetOrAdd<ProphetSlotView>(s.gameObject);
        var button = GetOrAdd<Button>(s.gameObject);
        button.targetGraphic = bgImg;
        SetTint(button);

        var dashed = NewImage("DashedFrame", s, Load(GoldFrame), new Color(accent.r, accent.g, accent.b, 0.55f), 2.2f);
        Stretch(dashed.rectTransform, -2);
        var solid = NewImage("SolidFrame", s, Load(GoldFrame), accent, 1.3f);
        Stretch(solid.rectTransform, -4);

        var head = NewText("Head", s, true, 36, accent, up ? "▲ 上げる" : "▼ 下げる");
        PlaceTL(head.rectTransform, 26, 14, 260, 52);
        StyleText(head, true, 36, 22, accent, TextAlignmentOptions.Left);

        var lockMark = NewImage("Lock", s, Part("vui_wood_plate"), Color.white, 3f);
        PlaceTL(lockMark.rectTransform, w - 26 - 322, 18, 322, 46);
        var lockIcon = NewImage("Badge", lockMark.transform, Part("vui_round_white"), Gold, 3f, sliced: false);
        PlaceTL(lockIcon.rectTransform, 10, 7, 32, 32);
        var lockGlyph = NewText("Glyph", lockIcon.transform, false, 20, Ink, "鍵");
        Stretch(lockGlyph.rectTransform, 0);
        StyleText(lockGlyph, false, 20, 12, Ink);
        var lockText = NewText("Text", lockMark.transform, true, 20, BrightGold, "切れるまで出し入れ不可");
        PlaceTL(lockText.rectTransform, 48, 4, 264, 38);
        StyleText(lockText, true, 20, 12, BrightGold, TextAlignmentOptions.Left);

        // 空き
        var empty = NewRect("Empty", s);
        Stretch(empty, 0);
        var emptyText = NewText("Text", empty, true, 32, Cream, "ここに品のカードを入れる");
        PlaceTL(emptyText.rectTransform, 20, 128, w - 40, 50);
        StyleText(emptyText, true, 32, 18, Cream);
        var emptySub = NewText("Sub", empty, true, 23, Hex("D9CBB4"), up ? "需要が 3 ターン上がる・力を 1 つ使う" : "需要が 3 ターン下がる・力を 1 つ使う");
        PlaceTL(emptySub.rectTransform, 20, 184, w - 40, 38);
        StyleText(emptySub, true, 23, 14, Hex("D9CBB4"));

        // 持ってきたときの確かめ
        var preview = NewImage("Preview", s, Part("vui_round_white"), new Color(0.06f, 0.04f, 0.1f, 0.82f), 1.6f);
        PlaceTL(preview.rectTransform, 58, 104, w - 116, 150);
        var previewText = NewText("Text", preview.transform, true, 30, Cream, "需要 ▲ 3 ターン／力を 1 つ使う\n<size=80%>離すと入る</size>");
        Stretch(previewText.rectTransform, 12);
        StyleText(previewText, true, 30, 16, Cream);
        previewText.textWrappingMode = TextWrappingModes.Normal;
        preview.gameObject.SetActive(false);

        // 効いている
        var active = NewRect("Active", s);
        Stretch(active, 0);
        var card = NewRect("Card", active);
        PlaceTL(card, 30, 82, 176, 236);
        var cardBg = NewImage("Face", card, Part("vui_article_big"), Color.white, 4f);
        Stretch(cardBg.rectTransform, 0);
        var iconBox = NewImage("IconBox", card, Load(IconBack), Color.white, 1f, sliced: false);
        PlaceTL(iconBox.rectTransform, 28, 14, 120, 120);
        var icon = NewImage("Icon", iconBox.transform, null, Color.white, 1f, sliced: false);
        Stretch(icon.rectTransform, 16);
        icon.preserveAspect = true;
        var iconFrame = NewImage("Frame", iconBox.transform, Load(IconFrame), Color.white, 1f, sliced: false);
        Stretch(iconFrame.rectTransform, 0);
        var cardName = NewText("Name", card, false, 24, Ink, "炎の剣");
        PlaceTL(cardName.rectTransform, 10, 142, 156, 40);
        StyleText(cardName, false, 24, 14, Ink);
        var cardInfo = NewText("Info", card, false, 19, MidBrown, "1,240G・在庫4");
        PlaceTL(cardInfo.rectTransform, 10, 184, 156, 32);
        StyleText(cardInfo, false, 19, 12, MidBrown);

        var title = NewText("Title", active, true, 32, Cream, up ? "炎の剣の需要を上げている" : "薬草の需要を下げている");
        PlaceTL(title.rectTransform, 232, 92, 360, 52);
        StyleText(title, true, 32, 18, Cream, TextAlignmentOptions.Left);
        var turns = NewText("Turns", active, true, 30, BrightGold, "あと 3 ターン");
        PlaceTL(turns.rectTransform, 232, 152, 360, 44);
        StyleText(turns, true, 30, 18, BrightGold, TextAlignmentOptions.Left);
        var slotProp = so.FindProperty(up ? "upSlot" : "downSlot");
        var pipImages = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            pipImages[i] = NewImage($"Pip{i}", active, Part("vui_round_white"), BrightGold, 6f);
            PlaceTL(pipImages[i].rectTransform, 234 + i * 74, 206, 62, 20);
        }
        var noteA = NewText("Note", active, true, 21, Hex("E9DCC6"), "切れたら元の需要へ戻ろうと動く");
        PlaceTL(noteA.rectTransform, 232, 240, 360, 34);
        StyleText(noteA, true, 21, 13, Hex("E9DCC6"), TextAlignmentOptions.Left);

        var shade = NewImage("Blocked", s, Part("vui_round_white"), new Color(0f, 0f, 0f, 0.55f), 1.2f);
        Stretch(shade.rectTransform, 0);
        var shadeText = NewText("Text", shade.transform, true, 24, Lavender, "力が 0 のあいだは入れられない\n<size=85%>配信 1 回で 1 つ回復</size>");
        PlaceTL(shadeText.rectTransform, 20, 246, w - 40, 76);
        StyleText(shadeText, true, 24, 14, Lavender);
        shade.gameObject.SetActive(false);

        // 描く順: 地 → 枠線 → 中身 → 幕 → 確かめ → 見出し・鍵
        dashed.transform.SetSiblingIndex(0);
        solid.transform.SetSiblingIndex(1);
        shade.transform.SetAsLastSibling();
        preview.transform.SetAsLastSibling();
        head.transform.SetAsLastSibling();
        lockMark.transform.SetAsLastSibling();

        var sso = new SerializedObject(slot);
        sso.FindProperty("direction").intValue = (int)dir; // enumValueIndex は値の順（Down=-1 が先）なので値で入れる
        sso.FindProperty("button").objectReferenceValue = button;
        sso.FindProperty("background").objectReferenceValue = bgImg;
        sso.FindProperty("dashedFrame").objectReferenceValue = dashed.gameObject;
        sso.FindProperty("solidFrame").objectReferenceValue = solid.gameObject;
        sso.FindProperty("emptyRoot").objectReferenceValue = empty.gameObject;
        sso.FindProperty("emptyText").objectReferenceValue = emptyText;
        sso.FindProperty("emptySubText").objectReferenceValue = emptySub;
        sso.FindProperty("previewRoot").objectReferenceValue = preview.gameObject;
        sso.FindProperty("previewText").objectReferenceValue = previewText;
        sso.FindProperty("activeRoot").objectReferenceValue = active.gameObject;
        sso.FindProperty("activeCard").objectReferenceValue = card;
        sso.FindProperty("activeIcon").objectReferenceValue = icon;
        sso.FindProperty("activeCardName").objectReferenceValue = cardName;
        sso.FindProperty("activeCardInfo").objectReferenceValue = cardInfo;
        sso.FindProperty("activeTitle").objectReferenceValue = title;
        sso.FindProperty("turnsText").objectReferenceValue = turns;
        var pipProp = sso.FindProperty("turnPips");
        pipProp.arraySize = pipImages.Length;
        for (int i = 0; i < pipImages.Length; i++) pipProp.GetArrayElementAtIndex(i).objectReferenceValue = pipImages[i];
        sso.FindProperty("lockMark").objectReferenceValue = lockMark.gameObject;
        sso.FindProperty("blockedShade").objectReferenceValue = shade.gameObject;
        sso.FindProperty("glowTarget").objectReferenceValue = s;
        sso.ApplyModifiedPropertiesWithoutUndo();
        slotProp.objectReferenceValue = slot;
        return s;
    }

    // ───────── 右下: 品のカードの棚 ─────────

    private static void BuildShelf(RectTransform layout, SerializedObject so)
    {
        var shelf = NewImage("ShelfPanel", layout, Part("vui_panel_frame"), Color.white, 3.4f);
        shelf.raycastTarget = true;
        PlaceTL(shelf.rectTransform, 650, 580, 1250, 480);
        var s = shelf.rectTransform;

        var title = NewText("Title", s, true, 32, BrightGold, "品のカード");
        PlaceTL(title.rectTransform, 34, 22, 170, 46);
        StyleText(title, true, 32, 20, BrightGold, TextAlignmentOptions.Left);

        string[] chips = { "すべて", "武器", "防具", "道具", "在庫あり", "新聞で動く" };
        float[] widths = { 100, 80, 80, 80, 112, 132 };
        var filterProp = so.FindProperty("filterButtons");
        filterProp.arraySize = chips.Length;
        float cx = 206;
        for (int i = 0; i < chips.Length; i++)
        {
            var chip = NewImage($"Filter{i}", s, Part("vui_round_white"), new Color(0f, 0f, 0f, 0.3f), 2.4f);
            chip.raycastTarget = true;
            PlaceTL(chip.rectTransform, cx, 26, widths[i], 40);
            cx += widths[i] + 8;
            var b = GetOrAdd<Button>(chip.gameObject);
            b.targetGraphic = chip;
            b.transition = Selectable.Transition.None; // 選んでいるかは View が色で出す
            var t = NewText("Text", chip.transform, false, 21, Cream, chips[i]);
            Stretch(t.rectTransform, 2);
            StyleText(t, false, 21, 12, Cream);
            filterProp.GetArrayElementAtIndex(i).objectReferenceValue = b;
        }

        var sortImg = NewImage("Sort", s, Part("vui_round_white"), new Color(0f, 0f, 0f, 0.001f), 2.4f);
        sortImg.raycastTarget = true;
        PlaceTL(sortImg.rectTransform, 1250 - 30 - 262, 22, 262, 46);
        var sortBtn = GetOrAdd<Button>(sortImg.gameObject);
        sortBtn.targetGraphic = sortImg;
        var sortLabel = NewText("Text", sortImg.transform, true, 21, Hex("F1D9B0"), "並び: 在庫の多い順 ▼");
        Stretch(sortLabel.rectTransform, 2);
        StyleText(sortLabel, true, 21, 12, Hex("F1D9B0"), TextAlignmentOptions.Right);
        so.FindProperty("sortButton").objectReferenceValue = sortBtn;
        so.FindProperty("sortLabel").objectReferenceValue = sortLabel;

        // スクロールする棚（ホイールとスクロールバーで送る。カードのドラッグは上の枠へ運ぶのに使う）
        var scrollImg = NewImage("Scroll", s, Part("vui_round_white"), new Color(0.12f, 0.07f, 0.04f, 0.45f), 1.2f);
        scrollImg.raycastTarget = true;
        PlaceTL(scrollImg.rectTransform, 30, 80, 1190, 372);
        var scroll = GetOrAdd<ScrollRect>(scrollImg.gameObject);
        GetOrAdd<ScrollRectAutoFollow>(scrollImg.gameObject);
        var viewport = NewRect("Viewport", scrollImg.transform);
        Stretch(viewport, 0);
        viewport.offsetMax = new Vector2(-22, 0);
        GetOrAdd<RectMask2D>(viewport.gameObject);
        var content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, 360); content.localScale = Vector3.one;
        var grid = GetOrAdd<GridLayoutGroup>(content.gameObject);
        grid.cellSize = new Vector2(CardW, CardH);
        grid.spacing = new Vector2(12, 12);
        grid.padding = new RectOffset(8, 0, 14, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 6;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        var fitter = GetOrAdd<ContentSizeFitter>(content.gameObject);
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var bar = NewImage("Scrollbar", scrollImg.transform, Part("vui_round_white"), new Color(0.1f, 0.06f, 0.03f, 0.7f), 4f);
        var brt = bar.rectTransform;
        brt.anchorMin = new Vector2(1, 0); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(1, 0.5f);
        brt.anchoredPosition = new Vector2(-4, 0); brt.sizeDelta = new Vector2(14, -16); brt.localScale = Vector3.one;
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

        var emptyText = NewText("EmptyText", scrollImg.transform, true, 26, Lavender, "当てはまる品がない");
        Stretch(emptyText.rectTransform, 20);
        StyleText(emptyText, true, 26, 16, Lavender);
        emptyText.gameObject.SetActive(false);
        so.FindProperty("shelfEmptyText").objectReferenceValue = emptyText;

        so.FindProperty("itemCardTemplate").objectReferenceValue = BuildCardTemplate(s);
    }

    private static ProphetItemCard BuildCardTemplate(RectTransform shelf)
    {
        var root = NewRect("CardTemplate", shelf);
        PlaceTL(root, 40, 96, CardW, CardH);
        var card = GetOrAdd<ProphetItemCard>(root.gameObject);
        var hit = GetOrAdd<Image>(root.gameObject);
        GetOrAdd<CanvasRenderer>(root.gameObject);
        hit.sprite = null;
        hit.color = new Color(0, 0, 0, 0); // 押せる範囲（見た目は Face）
        hit.raycastTarget = true;
        var button = GetOrAdd<Button>(root.gameObject);

        var face = NewRect("Face", root);
        Stretch(face, 0);
        var faceBg = NewImage("Bg", face, Part("vui_article_big"), Color.white, 4f);
        Stretch(faceBg.rectTransform, 0);
        button.targetGraphic = faceBg;
        SetTint(button);

        var iconBox = NewImage("IconBox", face, Load(IconBack), Color.white, 1f, sliced: false);
        PlaceTL(iconBox.rectTransform, 10, 10, 72, 72);
        var icon = NewImage("Icon", iconBox.transform, null, Color.white, 1f, sliced: false);
        Stretch(icon.rectTransform, 10);
        icon.preserveAspect = true;
        var iconFrame = NewImage("Frame", iconBox.transform, Load(IconFrame), Color.white, 1f, sliced: false);
        Stretch(iconFrame.rectTransform, 0);

        var nameText = NewText("Name", face, false, 22, Ink, "ミスリルの大剣");
        PlaceTL(nameText.rectTransform, 88, 8, 86, 52);
        StyleText(nameText, false, 22, 13, Ink, TextAlignmentOptions.TopLeft);
        nameText.textWrappingMode = TextWrappingModes.Normal;
        nameText.lineSpacing = -18;
        var news = NewText("News", face, false, 19, FateUp, "新聞 ▲");
        PlaceTL(news.rectTransform, 88, 58, 86, 26);
        StyleText(news, false, 19, 12, FateUp, TextAlignmentOptions.Left);
        var info = NewText("Info", face, false, 20, MidBrown, "1,240G・在庫4");
        PlaceTL(info.rectTransform, 10, 92, 162, 32);
        StyleText(info, false, 20, 12, MidBrown, TextAlignmentOptions.Left);
        var change = NewText("Change", face, false, 18, MidBrown, "前日 ▲5%");
        PlaceTL(change.rectTransform, 10, 126, 162, 28);
        StyleText(change, false, 18, 12, MidBrown, TextAlignmentOptions.Left);

        var dim = NewImage("Dim", face, Part("vui_article_big"), new Color(0.12f, 0.08f, 0.16f, 0.55f), 4f);
        Stretch(dim.rectTransform, 0);
        dim.gameObject.SetActive(false);
        var inUse = NewImage("InUseTag", face, Part("vui_round_white"), Hex("5A3420"), 2.4f);
        PlaceTL(inUse.rectTransform, CardW - 92, 6, 86, 30);
        var inUseText = NewText("Text", inUse.transform, true, 18, BrightGold, "効果中");
        Stretch(inUseText.rectTransform, 0);
        StyleText(inUseText, true, 18, 12, BrightGold);
        inUse.gameObject.SetActive(false);
        var selected = NewImage("Selected", face, Load(GoldFrame), BrightGold, 1.6f);
        Stretch(selected.rectTransform, -5);
        selected.gameObject.SetActive(false);

        var cso = new SerializedObject(card);
        cso.FindProperty("button").objectReferenceValue = button;
        cso.FindProperty("face").objectReferenceValue = face;
        cso.FindProperty("icon").objectReferenceValue = icon;
        cso.FindProperty("nameText").objectReferenceValue = nameText;
        cso.FindProperty("newsText").objectReferenceValue = news;
        cso.FindProperty("infoText").objectReferenceValue = info;
        cso.FindProperty("changeText").objectReferenceValue = change;
        cso.FindProperty("inUseTag").objectReferenceValue = inUse.gameObject;
        cso.FindProperty("dimOverlay").objectReferenceValue = dim;
        cso.FindProperty("selectedFrame").objectReferenceValue = selected.gameObject;
        cso.ApplyModifiedPropertiesWithoutUndo();

        root.gameObject.SetActive(false);
        return card;
    }

    private static readonly Color FateUp = Hex("4F9E3E");

    // ───────── 小物 ─────────

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
        t.raycastTarget = false;
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
        img.sprite = sprite;
        img.color = color;
        bool canSlice = sliced && sprite != null && sprite.border != Vector4.zero;
        img.type = canSlice ? Image.Type.Sliced : Image.Type.Simple;
        img.pixelsPerUnitMultiplier = ppu;
        img.fillCenter = true;
        img.raycastTarget = false;
        return img;
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

    /// <summary>プレハブの階層を書き出す（-executeMethod ProphetUIBuilder.DumpBatch -dumpOut &lt;file&gt;）。</summary>
    public static void DumpBatch()
    {
        string path = @"D:\WorkTree\TomsLands\_scratch_wt04\prophet\prefab_dump.txt";
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
            sb.Append($" pos{rt.anchoredPosition} size{rt.sizeDelta}");
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) { sb.Append(" |MISSING"); continue; }
            if (c is Transform) continue;
            sb.Append(" |").Append(c.GetType().Name);
        }
        sb.AppendLine();
        foreach (Transform ch in t) Dump(ch, depth + 1, sb);
    }
}
