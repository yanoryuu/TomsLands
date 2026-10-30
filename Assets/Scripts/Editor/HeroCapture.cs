using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 勇者の画面（Hero.prefab）を 1920x1080 の PNG に書き出す（HeroUIBuilder で組んだ見た目の確認用）。
/// 保存しない空のシーンに Hero.prefab と CommonCanvas.prefab（日付・所持金・メニュー。勇者の画面でも上に出る）を置き、
/// カメラ描画の Canvas にして RenderTexture に描く。Play しない・DI もセーブも使わない（中身は見本の品で埋める）。
/// 演出は省略モード（UIMotion.ForcedMode = Skip）で最終状態を撮る。
/// バッチ（描画が要るので -nographics は付けない）:
///   Unity.exe -batchmode -projectPath &lt;wt&gt; -executeMethod HeroCapture.RunBatch -captureOut &lt;dir&gt; [-captureShots weapon,armor,drag,tactics] -logFile &lt;log&gt;
/// 種類: weapon（棚＝武器）/ armor（棚＝防具・職人の仕込みあり）/ drag（武器を持ち上げ中：枠が光り、能力値の板に差）/ tactics（作戦の一覧を開く）。
/// </summary>
public static class HeroCapture
{
    private const string Tag = "[HeroCapture]";
    private const int Width = 1920;
    private const int Height = 1080;
    private static readonly string[] AllShots = { "weapon", "armor", "drag", "tactics" };

    public static void RunBatch()
    {
        int code = 0;
        try
        {
            string dir = Arg("-captureOut") ?? @"D:\WorkTree\TomsLands\_scratch_wt04\hero\shots";
            var shots = (Arg("-captureShots") ?? string.Join(",", AllShots))
                .Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToArray();
            Directory.CreateDirectory(dir);
            foreach (var shot in shots) Capture(shot, dir);
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 失敗: {e}");
            code = 1;
        }
        finally
        {
            UIMotion.ForcedMode = null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        EditorApplication.Exit(code);
    }

    [MenuItem("Tools/TomsLands/勇者の画面をキャプチャ")]
    private static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string dir = @"D:\WorkTree\TomsLands\_scratch_wt04\hero\shots";
        Directory.CreateDirectory(dir);
        try { foreach (var s in AllShots) Capture(s, dir); }
        finally { UIMotion.ForcedMode = null; }
    }

    private static void Capture(string shot, string dir)
    {
        if (Array.IndexOf(AllShots, shot) < 0) throw new ArgumentException($"未知の種類: {shot}");
        UIMotion.ForcedMode = UIMotionMode.Skip;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("CaptureCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.orthographic = true;
        camGo.transform.position = new Vector3(0, 0, -10);
        new GameObject("EventSystem", typeof(EventSystem));

        var heroPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeroUIBuilder.PrefabPath);
        var hero = (GameObject)PrefabUtility.InstantiatePrefab(heroPrefab, scene);
        hero.SetActive(true);
        var commonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Screens/CommonCanvas.prefab");
        GameObject common = commonPrefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(commonPrefab, scene) : null;
        if (common != null) common.SetActive(true);

        foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Where(c => c.isRootCanvas))
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = common != null && canvas.transform.IsChildOf(common.transform) ? 5 : 10;
        }

        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        cam.targetTexture = rt;
        try
        {
            Refresh(scene, cam);
            var view = hero.GetComponentInChildren<HeroPanelView>(true);
            Fill(view, shot);
            Refresh(scene, cam);
            Act(view, shot);
            Refresh(scene, cam);
            cam.Render();
            Refresh(scene, cam);
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            string path = Path.Combine(dir, $"hero_{shot}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log($"{Tag} 書き出し: {path}");
        }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }

    private static void Refresh(UnityEngine.SceneManagement.Scene scene, Camera cam)
    {
        cam.Render(); // Canvas を RenderTexture の大きさに合わせる
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

    // ───────── 見本の中身 ─────────

    private static void Fill(HeroPanelView view, string shot)
    {
        PreloadDialogues();
        // 実行時と同じく Awake（ボタンの配線）を通す
        typeof(HeroPanelView).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(view, null);

        var items = AssetDatabase.FindAssets("t:ItemData")
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(i => i != null).ToDictionary(i => i.itemId);
        ItemData I(string id) => items.TryGetValue(id, out var d) ? d : null;

        const int atk = 86, def = 54, hp = 840;
        view.SetCombatStats(atk, def, hp);
        view.SetUnequipPreview(new ClearProbabilityCalculator.HeroStats(hp, 80, def), new ClearProbabilityCalculator.HeroStats(800, atk, 47));

        HeroEquipmentOption W(string id, int stock, int price, int bonusPct, int after, bool weak = false)
        {
            var d = I(id);
            return new HeroEquipmentOption(id, d?.itemName ?? id)
            {
                Name = d?.itemName ?? id, Icon = d?.itemIcon,
                Attribute = d != null ? d.itemAttribute : default,
                AttributeLabel = d != null ? ItemAttributeText.ToJapanese(d.itemAttribute) : "火",
                Stock = stock, Price = price, BonusText = $"攻撃+{bonusPct}%", IsWeakness = weak,
                After = new ClearProbabilityCalculator.HeroStats(hp, after, def),
            };
        }
        HeroEquipmentOption A(string id, int stock, int price, int defPct, int hpPct, int afterDef, int afterHp, bool weak = false)
        {
            var d = I(id);
            return new HeroEquipmentOption(id, d?.itemName ?? id)
            {
                Name = d?.itemName ?? id, Icon = d?.itemIcon,
                Attribute = d != null ? d.itemAttribute : default,
                AttributeLabel = d != null ? ItemAttributeText.ToJapanese(d.itemAttribute) : "水",
                Stock = stock, Price = price, BonusText = $"防御+{defPct}% HP+{hpPct}%", IsWeakness = weak,
                After = new ClearProbabilityCalculator.HeroStats(afterHp, atk, afterDef),
            };
        }

        var weapons = new List<HeroEquipmentOption>
        {
            W("SW3", 12, 1240, 10, 92, weak: true),
            W("SW4", 8, 1980, 25, 105),
            W("SW6", 6, 1720, 25, 105),
            W("SW2", 5, 980, 15, 96),
            W("SW1", 20, 600, 10, 86),
            W("BO1", 3, 1500, 10, 92),
            W("SW5", 0, 2400, 30, 110),
            W("HA1", 0, 640, 10, 92),
        };
        var armors = new List<HeroEquipmentOption>
        {
            A("AR2", 9, 1100, 20, 8, 62, 880),
            A("AR7", 8, 800, 15, 5, 58, 862),
            A("HE6", 4, 1650, 25, 10, 66, 900, weak: false),
            A("AR1", 3, 760, 15, 5, 58, 862),
            A("HE7", 0, 520, 15, 5, 58, 862),
        };
        view.SetWeaponOptions(weapons, "SW1");
        view.SetArmorOptions(armors, "AR7");
        view.SetEquipmentIcons(I("SW1")?.itemIcon, I("AR7")?.itemIcon);
        view.SetClearProbability(68f, true);
        view.SetTacticsOptions(HeroTactics.Aggressive);
        view.SetDialogue("「炎の剣か。今回のダンジョンにはこいつが効くはずだ」");
        view.SetStaticHoverDescriptions("武器の説明", "防具の説明", "見込みの説明");
        bool craft = shot == "armor";
        view.SetCraft(craft, craft, craft ? ArtisanCraftKind.Fine : ArtisanCraftKind.None, ItemTypeData.ItemAttribute.Fire, "業物", "なまくら");
    }

    private static void Act(HeroPanelView view, string shot)
    {
        switch (shot)
        {
            case "armor":
                Field<Button>(view, "armorTabButton")?.onClick.Invoke();
                break;
            case "drag":
                // 弱点に合う炎の剣を持ち上げる（パッド・クリックと同じ：置き先の枠の上に浮かぶ）
                view.ShelfCardButton("SW3", true)?.onClick.Invoke();
                break;
            case "tactics":
            {
                var dd = Field<TMP_Dropdown>(view, "tacticsDropdown");
                if (dd != null)
                {
                    dd.alphaFadeSpeed = 0f;
                    // 編集モードでは Start が走らず、開くときのフェードの部品が無い（実行時と同じく Start を通す）
                    typeof(TMP_Dropdown).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(dd, null);
                    dd.Show();
                    var list = dd.transform.Find("Dropdown List");
                    if (list != null && list.TryGetComponent<CanvasGroup>(out var g)) g.alpha = 1f;
                }
                break;
            }
        }
    }

    private static T Field<T>(object o, string name) where T : class
        => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o) as T;

    /// <summary>HeroDialogueLoader は Addressables で読むので、編集モードでは CSV を直接読んで詰めておく。</summary>
    private static void PreloadDialogues()
    {
        var f = typeof(HeroDialogueLoader).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static);
        if (f == null) return;
        var path = AssetDatabase.FindAssets("HeroDialogues t:TextAsset").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
        var asset = path != null ? AssetDatabase.LoadAssetAtPath<TextAsset>(path) : null;
        if (asset == null) return;
        var dict = new Dictionary<string, string>();
        foreach (var raw in asset.text.Split('\n').Skip(1))
        {
            var line = raw.Trim();
            int comma = line.IndexOf(',');
            if (comma <= 0) continue;
            dict[line.Substring(0, comma).Trim()] = line.Substring(comma + 1).Trim().Trim('"');
        }
        f.SetValue(null, dict);
    }

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
