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
/// 占い師の画面（FortuneTeller.prefab）を 1920x1080 の PNG に書き出す（ProphetUIBuilder で組んだ見た目の確認用）。
/// 保存しない空のシーンに FortuneTeller.prefab と CommonCanvas.prefab（日付・所持金。占い師の画面でも上に出る）を置き、
/// カメラ描画の Canvas にして RenderTexture に描く。Play しない・DI もセーブも使わない。
/// 状態は仮の実装 ProphetInterventionStub で作り、Presenter と同じ写し方で View に渡す（品は ItemData の見本）。
/// 演出は省略モード（UIMotion.ForcedMode = Skip）で最終状態を撮る。
/// バッチ（描画が要るので -nographics は付けない）:
///   Unity.exe -batchmode -projectPath &lt;wt&gt; -executeMethod ProphetCapture.RunBatch -captureOut &lt;dir&gt; [-captureShots empty,up,both,power0,preview] -logFile &lt;log&gt;
/// 種類: empty（何も入っていない・力 2）/ up（上げる枠だけ・力 1）/ both（両方の枠・力 0）/ power0（枠は空・力 0）/ preview（カードを選んで上げる枠に重ねた確かめ）。
/// </summary>
public static class ProphetCapture
{
    private const string Tag = "[ProphetCapture]";
    private const int Width = 1920;
    private const int Height = 1080;
    private static readonly string[] AllShots = { "empty", "up", "both", "power0", "preview" };

    public static void RunBatch()
    {
        int code = 0;
        try
        {
            string dir = Arg("-captureOut") ?? @"D:\WorkTree\TomsLands\_scratch_wt04\prophet\shots";
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

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProphetUIBuilder.PrefabPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.SetActive(true);
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
            var view = go.GetComponentInChildren<ProphetView>(true);
            Fill(view, shot);
            Refresh(scene, cam);
            cam.Render();
            Refresh(scene, cam);
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            string path = Path.Combine(dir, $"prophet_{shot}.png");
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

    // ───────── 見本の中身（仮の実装で状態を作る） ─────────

    private static void Fill(ProphetView view, string shot)
    {
        var dialogues = LoadDialogues();
        typeof(ProphetView).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(view, null);

        var items = AssetDatabase.FindAssets("t:ItemData")
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(i => i != null && i.requiredLevel <= 3)
            .GroupBy(i => i.itemId).Select(g => g.First())
            .OrderBy(i => i.itemId, StringComparer.Ordinal)
            .ToList();
        var byId = items.ToDictionary(i => i.itemId);

        var stub = new ProphetInterventionStub();
        string upId = items.FirstOrDefault(i => i.itemType == ItemTypeData.ItemType.Weapon)?.itemId;
        string downId = items.FirstOrDefault(i => i.itemType == ItemTypeData.ItemType.Tool)?.itemId
                        ?? items.LastOrDefault()?.itemId;
        switch (shot)
        {
            case "up":
                stub.TryPlace(upId, ProphetDirection.Up);
                break;
            case "both":
                stub.TryPlace(upId, ProphetDirection.Up);
                stub.TryPlace(downId, ProphetDirection.Down);
                stub.AdvanceDay(); // 上げる枠は残り 2、下げる枠も 2
                stub.ForceSlot(ProphetDirection.Down, downId, 3);
                break;
            case "power0":
                stub.SetPower(0);
                break;
        }

        // 見本の値段・在庫（ItemData の基準値から決まった形で）
        int n = 0;
        var infos = items.Select(d =>
        {
            n++;
            int stock = (n * 7) % 13 == 0 ? 0 : (n * 7) % 13;
            int sign = n % 3 == 0 ? -1 : 1;
            return new ProphetItemInfo
            {
                ItemId = d.itemId, Name = d.itemName, Icon = d.itemIcon, Type = d.itemType,
                Price = Mathf.Max(1, d.basePrice + sign * (n * 37 % 11) * d.basePrice / 100),
                Change1d = sign * (n * 37 % 11) / 100f,
                Stock = stock,
                NewsDirection = n % 4 == 1 ? 1 : 0,
                InSlot = Slot(stub, d.itemId),
            };
        }).ToList();

        ProphetSlotInfo? Info(ProphetDirection d)
        {
            var s = stub.GetSlot(d);
            if (!s.HasValue) return null;
            var data = byId.TryGetValue(s.Value.ItemId, out var x) ? x : null;
            var info = infos.FirstOrDefault(i => i.ItemId == s.Value.ItemId);
            return new ProphetSlotInfo
            {
                ItemId = s.Value.ItemId, Name = data?.itemName ?? s.Value.ItemId, Icon = data?.itemIcon,
                Price = info.Price, Stock = info.Stock, TurnsLeft = s.Value.TurnsLeft, TotalTurns = s.Value.TotalTurns,
            };
        }

        view.SetPower(stub.Power, stub.MaxPower);
        view.SetSlots(Info(ProphetDirection.Up), Info(ProphetDirection.Down), stub.EffectTurns, stub.Power <= 0);
        view.SetItems(infos, animate: false);

        string line = stub.Power >= stub.MaxPower ? "power_full" : stub.Power <= 0 ? "power_empty" : "default";
        if (shot == "up") line = "placed_up";
        string text = dialogues.TryGetValue(line, out var l) ? l : "";
        string upName = upId != null && byId.TryGetValue(upId, out var u) ? u.itemName : "";
        view.ShowDialogue(text.Replace("{item}", upName).Replace("{turns}", stub.EffectTurns.ToString()).Replace("{cost}", stub.Cost.ToString("N0")));

        if (shot == "preview")
        {
            // 品のカードを押して選び、上げる枠にカーソルを重ねた（ホバーの確かめ）
            var pick = infos.FirstOrDefault(i => i.Stock > 0 && i.InSlot == 0);
            view.PreviewProvider = (id, d) => ($"需要 {(d == ProphetDirection.Up ? "▲" : "▼")} {stub.EffectTurns} ターン／力を 1 つ使う\n<size=80%>{stub.Cost:N0}G・離すと入る</size>", true);
            view.ItemCardButton(pick.ItemId)?.onClick.Invoke();
            var relay = view.UpSlotButton != null ? view.UpSlotButton.GetComponent<ProphetHoverRelay>() : null;
            relay?.Entered?.Invoke();
            view.ShowDialogue(dialogues.TryGetValue("default", out var d0) ? d0 : "");
        }
    }

    private static int Slot(ProphetInterventionStub stub, string id)
    {
        var up = stub.GetSlot(ProphetDirection.Up);
        if (up.HasValue && up.Value.ItemId == id) return 1;
        var down = stub.GetSlot(ProphetDirection.Down);
        if (down.HasValue && down.Value.ItemId == id) return -1;
        return 0;
    }

    /// <summary>台詞 CSV を直接読む（ProphetDialogueLoader は Addressables で読むので編集モードでは使わない）。</summary>
    private static Dictionary<string, string> LoadDialogues()
    {
        var dict = new Dictionary<string, string>();
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources_moved/ProphetDialogues.csv");
        if (asset == null) return dict;
        foreach (var raw in asset.text.Split('\n').Skip(1))
        {
            var line = raw.Trim();
            int comma = line.IndexOf(',');
            if (comma <= 0) continue;
            dict[line.Substring(0, comma).Trim()] = line.Substring(comma + 1).Trim().Trim('"');
        }
        return dict;
    }

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
