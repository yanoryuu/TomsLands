using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 村シーン（VillageScene）の見た目を 1920x1080 の PNG に書き出す（UI・マップのデザイン確認用）。
/// 編集モードのまま（Play しない）シーンを開き、Main Camera を RenderTexture に描く。
/// VillageModel・DI は起動せず、セーブ（slot_N / metaData.json）も読み書きしない。シーンは保存せず、撮るたびに開き直す。
///
/// バッチモード（描画が要るので -nographics は付けない）:
///   Unity.exe -batchmode -projectPath &lt;wt&gt; -executeMethod VillageCapture.RunBatch -logFile &lt;log&gt;
///     -captureOut &lt;dir&gt; -captureShots hud,invest,conversion,overview -captureLevels 0,3,5 [-captureRanks 0,1,2,3] [-captureFunds 99999]
/// 種類: hud / invest（投資パネル）/ conversion（帰還収支ポップ）/ rankup（格上げのポップ）/ record（自己ベストの掲示板）/
///       tabs（左端の施設ショートカットの帯を開いた状態）/ overview（全景）。
/// -captureFunds: HUD の村資金（投資パネルの費用と比べる額）。多めにすると「足りる＝金・ボタン有効」が撮れる。
/// -captureRanks を付けると、村の格ごとの地面と装飾（Assets/Prefabs/Village/VillageRankDecor.prefab）を一時的に置いて
/// Apply(格) し、施設レベルも格に合わせて（その格になるいちばん低い全施設共通のLv。王都は全Lv5）撮る（village_rank{格}_{種類}.png）。
/// -captureRanks だけなら格の分だけ、-captureLevels も付ければ両方撮る。
/// -captureLevels だけのときも、そのレベルの格（VillageRank.Compute）の地面と装飾を置いて撮る（本編の Presenter と同じ）。
/// 投資パネルの押せる/押せない・MAX の札は本編と同じ判定（VillageModel.CanInvest と同じ。村資金は -captureFunds）。
/// 実行は tools/village_capture.ps1。
/// </summary>
public static class VillageCapture
{
    private const string Tag = "[VillageCapture]";
    private const string ScenePath = "Assets/Scene/VillageScene.unity";
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>撮れる種類。</summary>
    private static readonly string[] AllShots = { "hud", "invest", "conversion", "rankup", "record", "tabs", "overview" };

    /// <summary>HUD の村資金（-captureFunds で上書き。-1 なら 12345×(Lv+1)）。</summary>
    private static int fundsOverride = -1;

    private static readonly string[] FacilityOrder =
    {
        VillageEffects.HallId, VillageEffects.GuildId, VillageEffects.TavernId,
        VillageEffects.BankId, VillageEffects.ArtisanId, VillageEffects.PlazaId,
    };

    private struct Job
    {
        public int Level;
        public string Shot;
        public int Rank; // 0 以上なら格の装飾を置いて撮る（-1 = 置かない）
    }

    private static readonly Queue<Job> jobs = new();
    private static string outDir;
    private static int waitFrames;
    private static Job? current;
    private static int written;

    [MenuItem("Tools/TomsLands/村の見た目をキャプチャ（hud・Lv0）")]
    private static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Start(DefaultOutDir(), new[] { "hud" }, new[] { 0 }, Array.Empty<int>(), exitWhenDone: false);
    }

    /// <summary>バッチモード用（-executeMethod VillageCapture.RunBatch）。終わったら Exit(0)、失敗は Exit(1)。</summary>
    public static void RunBatch()
    {
        try
        {
            string dir = Arg("-captureOut") ?? DefaultOutDir();
            var shots = (Arg("-captureShots") ?? "hud,invest,conversion,overview")
                .Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToArray();
            string ranksArg = Arg("-captureRanks");
            var ranks = (ranksArg ?? "")
                .Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Select(int.Parse).ToArray();
            // 格だけ指定されたときは、レベル別の撮影はしない
            string levelsArg = Arg("-captureLevels") ?? (ranks.Length > 0 ? "" : "0");
            var levels = levelsArg
                .Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Select(int.Parse).ToArray();
            fundsOverride = int.TryParse(Arg("-captureFunds"), out int funds) ? funds : -1;
            Start(dir, shots, levels, ranks, exitWhenDone: true);
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 開始に失敗: {e}");
            EditorApplication.Exit(1);
        }
    }

    private static bool exitOnDone;

    private static void Start(string dir, string[] shots, int[] levels, int[] ranks, bool exitWhenDone)
    {
        foreach (var s in shots)
        {
            if (Array.IndexOf(AllShots, s) < 0)
                throw new ArgumentException($"未知の種類: {s}（{string.Join(",", AllShots)}）");
        }
        outDir = dir;
        Directory.CreateDirectory(outDir);
        exitOnDone = exitWhenDone;
        written = 0;
        jobs.Clear();
        foreach (int lv in levels)
            foreach (var s in shots)
                jobs.Enqueue(new Job { Level = Mathf.Max(0, lv), Shot = s, Rank = -1 });
        if (ranks.Length > 0)
        {
            var facilities = LoadFacilities();
            foreach (int r in ranks)
                foreach (var s in shots)
                    jobs.Enqueue(new Job { Level = LevelForRank(facilities, r), Shot = s, Rank = Mathf.Max(0, r) });
        }

        Debug.Log($"{Tag} 開始: {jobs.Count} 枚 → {outDir}");
        current = null;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    /// <summary>
    /// 1枚ずつ「シーンを開き直す→状態を作る→数フレーム待つ→描く」を回す
    /// （Canvas の大きさや TMP のメッシュが編集モードの更新で整うのを待つため）。
    /// </summary>
    private static void Tick()
    {
        try
        {
            if (current == null)
            {
                if (jobs.Count == 0) { Finish(0); return; }
                current = jobs.Dequeue();
                Prepare(current.Value);
                waitFrames = 3;
                return;
            }
            if (waitFrames-- > 0)
            {
                Canvas.ForceUpdateCanvases();
                return;
            }
            Capture(current.Value);
            current = null;
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} 失敗: {e}");
            Finish(1);
        }
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        // 撮影でいじったシーンは保存せずに捨てる（空のシーンへ切り替える）
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Debug.Log($"{Tag} 終了: {written} 枚（コード {code}）");
        if (exitOnDone) EditorApplication.Exit(code);
    }

    // ───────── 状態づくり ─────────

    private static void Prepare(Job job)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid()) throw new InvalidOperationException($"シーンを開けない: {ScenePath}");

        var view = FindInScene<VillageView>(scene);
        var cam = FindMainCamera(scene);
        if (cam == null) throw new InvalidOperationException("Main Camera が見つからない");

        var facilities = LoadFacilities();
        ApplyLevels(view, facilities, job.Level);
        // 格の指定が無ければレベルから格を決めて、その格の地面と装飾にする（本編は Presenter が Apply している）
        int rank = job.Rank >= 0 ? job.Rank : RankForLevel(facilities, job.Level);
        PlaceRankDecor(scene, facilities, rank, job.Level);

        // 実行時は Awake で隠すパネルを、まず全部閉じる
        var so = view != null ? new SerializedObject(view) : null;
        SetActive(so, "investPanel", false);
        SetActive(so, "conversionPopup", false);
        SetActive(so, "recordBoardPopup", false);

        PlaceCamera(cam, job.Shot == "overview");
        SetUiVisible(view, job.Shot != "overview");
        ShowShortcuts(view, facilities, job.Level, rank, job.Shot == "tabs");

        switch (job.Shot)
        {
            case "invest":
                ShowInvest(view, facilities, job.Level);
                break;
            case "conversion":
                ShowConversion(view, so, job.Level);
                break;
            case "rankup":
                ShowRankUp(view, so, rank);
                break;
            case "record":
                ShowRecord(view, so);
                break;
        }
        // 小窓の後ろの幕（本編は開閉のたびに View が合わせる。編集モードは動きなしで呼ぶ）
        Invoke(view, "RefreshModalDim", false);
        RefreshUi(scene);
    }

    private static Dictionary<string, VillageFacilityData> LoadFacilities()
    {
        var map = new Dictionary<string, VillageFacilityData>();
        foreach (var guid in AssetDatabase.FindAssets("t:VillageFacilityData"))
        {
            var f = AssetDatabase.LoadAssetAtPath<VillageFacilityData>(AssetDatabase.GUIDToAssetPath(guid));
            if (f != null && !string.IsNullOrEmpty(f.facilityId)) map[f.facilityId] = f;
        }
        return map;
    }

    /// <summary>全施設を同じレベルにした見た目（区画・HUD・町人）にする。VillagePresenter.RefreshAll と同じ決め方。</summary>
    private static void ApplyLevels(VillageView view, Dictionary<string, VillageFacilityData> facilities, int level)
    {
        int levelOf(VillageFacilityData f) => Mathf.Min(level, f.MaxLevel);
        int hall = facilities.TryGetValue(VillageEffects.HallId, out var h) ? levelOf(h) : level;
        int total = FacilityOrder.Sum(id => facilities.TryGetValue(id, out var f) ? levelOf(f) : 0);
        int maxTotal = FacilityOrder.Sum(id => facilities.TryGetValue(id, out var f) ? f.MaxLevel : 0);
        int rank = VillageRank.Compute(total, hall);

        if (view != null)
        {
            foreach (var plot in view.Plots)
            {
                if (plot == null) continue;
                // 吹き出しは FacilityPlot.Awake で隠れ、近づいたときだけ出る
                SetActive(new SerializedObject(plot), "bubble", false);
                if (plot.FacilityId == VillagePresenter.RecordBoardId)
                {
                    plot.gameObject.SetActive(VillageRank.IsTop(rank));
                    plot.SetState(0, false, null, "自己ベストの掲示板");
                    continue;
                }
                if (!facilities.TryGetValue(plot.FacilityId, out var f)) continue;
                int lv = levelOf(f);
                bool locked = lv == 0 && f.facilityId != VillageEffects.HallId && hall < f.requiredHallLevel;
                plot.SetState(lv, locked, f.icon, f.facilityName);
            }

            string next = VillageRank.IsTop(rank)
                ? $"称号「{VillageRank.TopTitle}」"
                : $"次は『{VillageRank.Name(rank + 1)}』: {VillageRank.ConditionText(rank + 1)}";
            view.UpdateHud(FundsFor(level), total, VillageRank.Name(rank), next);
            view.ShowMessage("稼いだお金で村に投資しよう。施設に近づいて調べると詳細が見られる。");
        }

        // 町人（VillagePopulationView.ApplyScaled と同じ人数。DOTween を編集モードで動かさないよう表示だけ切り替える）
        var population = UnityEngine.Object.FindFirstObjectByType<VillagePopulationView>(FindObjectsInactive.Include);
        if (population != null)
        {
            var pso = new SerializedObject(population);
            var arr = pso.FindProperty("townsfolk");
            int slots = arr != null ? arr.arraySize : 0;
            int count = maxTotal > 0 ? Mathf.CeilToInt(Mathf.Clamp01(total / (float)maxTotal) * slots) : 0;
            for (int i = 0; i < slots; i++)
            {
                var v = arr.GetArrayElementAtIndex(i).objectReferenceValue as Component;
                if (v != null) v.gameObject.SetActive(i < count);
            }
        }
        Debug.Log($"{Tag} Lv{level}: 総合Lv{total}/{maxTotal}・領主館Lv{hall}・格 {VillageRank.Name(rank)}");
    }

    /// <summary>全施設を同じレベルにしたときの格（ApplyLevels と同じ決め方）。</summary>
    private static int RankForLevel(Dictionary<string, VillageFacilityData> facilities, int level)
    {
        int hall = facilities.TryGetValue(VillageEffects.HallId, out var h) ? Mathf.Min(level, h.MaxLevel) : level;
        int total = FacilityOrder.Sum(id => facilities.TryGetValue(id, out var f) ? Mathf.Min(level, f.MaxLevel) : 0);
        return VillageRank.Compute(total, hall);
    }

    /// <summary>投資できるか（VillageModel.CanInvest と同じ。全施設が同じレベル・領主館も同じレベルとして判定）。</summary>
    private static (bool ok, string reason) CanInvest(Dictionary<string, VillageFacilityData> facilities, VillageFacilityData f, int level, int funds)
    {
        int lv = Mathf.Min(level, f.MaxLevel);
        int next = lv + 1;
        if (next > f.MaxLevel) return (false, "最大レベル");
        int hall = facilities.TryGetValue(VillageEffects.HallId, out var h) ? Mathf.Min(level, h.MaxLevel) : level;
        if (f.facilityId != VillageEffects.HallId)
        {
            if (next == 1 && hall < f.requiredHallLevel) return (false, $"領主館Lv{f.requiredHallLevel}で建設可能");
            if (next >= 2 && hall < next - 1) return (false, $"領主館Lv{next - 1}でLv{next}に拡張可能");
        }
        if (funds < f.GetLevel(next).cost) return (false, "村資金が足りない");
        return (true, "");
    }

    private static int FundsFor(int level) => fundsOverride >= 0 ? fundsOverride : 12345 * (level + 1);

    /// <summary>左端の施設ショートカット（Presenter.BuildShortcuts と同じ並び・表示）。open なら開いた位置で撮る。</summary>
    private static void ShowShortcuts(VillageView view, Dictionary<string, VillageFacilityData> facilities, int level, int rank, bool open)
    {
        if (view == null) return;
        var entries = new List<VillageView.ShortcutEntry>();
        var ordered = facilities.Values
            .OrderBy(f => f.facilityId == VillageEffects.HallId ? 0 : 1)
            .ThenBy(f => f.requiredHallLevel)
            .ThenBy(f => Array.IndexOf(FacilityOrder, f.facilityId));
        foreach (var f in ordered)
        {
            int lv = Mathf.Min(level, f.MaxLevel);
            entries.Add(new VillageView.ShortcutEntry
            {
                Id = f.facilityId,
                Name = f.facilityName,
                Facility = f,
                LevelLabel = lv >= f.MaxLevel ? "MAX" : lv <= 0 ? "未建設" : $"Lv{lv}",
                CanUpgrade = CanInvest(facilities, f, level, FundsFor(level)).ok,
            });
        }
        if (VillageRank.IsTop(rank) && view.Plots.Any(p => p != null && p.FacilityId == VillagePresenter.RecordBoardId))
            entries.Add(new VillageView.ShortcutEntry { Id = VillagePresenter.RecordBoardId, Name = "自己ベストの掲示板", LevelLabel = "" });
        view.SetShortcuts(entries);
        Canvas.ForceUpdateCanvases();
        view.SetShortcutOpen(open, false);
    }

    /// <summary>その格になる、いちばん低い全施設共通のレベル（王都は全Lv5 = 最大の見た目）。</summary>
    private static int LevelForRank(Dictionary<string, VillageFacilityData> facilities, int rank)
    {
        int max = facilities.Count > 0 ? facilities.Values.Max(f => f.MaxLevel) : 5;
        if (VillageRank.IsTop(rank)) return max;
        for (int lv = 0; lv <= max; lv++)
        {
            int hall = facilities.TryGetValue(VillageEffects.HallId, out var h) ? Mathf.Min(lv, h.MaxLevel) : lv;
            int total = FacilityOrder.Sum(id => facilities.TryGetValue(id, out var f) ? Mathf.Min(lv, f.MaxLevel) : 0);
            if (VillageRank.Compute(total, hall) == rank) return lv;
        }
        Debug.LogWarning($"{Tag} 格{rank}になる共通のレベルが無いので Lv{max}");
        return max;
    }

    /// <summary>格ごとの地面と装飾のプレハブを一時的に置いて Apply する（シーンは保存しないので撮るたびに消える）。</summary>
    private static void PlaceRankDecor(Scene scene, Dictionary<string, VillageFacilityData> facilities, int rank, int level)
    {
        var decor = UnityEngine.Object.FindFirstObjectByType<VillageRankDecor>(FindObjectsInactive.Include);
        if (decor == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VillageRankDecorBuilder.PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"{Tag} {VillageRankDecorBuilder.PrefabPath} が無いので装飾なしで撮る");
                return;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            decor = go.GetComponent<VillageRankDecor>();
        }
        decor.Apply(rank);
        foreach (var f in facilities.Values) decor.ApplyFacilityLevel(f.facilityId, Mathf.Min(level, f.MaxLevel), f.MaxLevel);
        Debug.Log($"{Tag} 格の装飾: 格{rank}（{VillageRank.Name(rank)}）・全施設Lv{level}");
    }

    private static void ShowInvest(VillageView view, Dictionary<string, VillageFacilityData> facilities, int level)
    {
        if (view == null) return;
        if (!facilities.TryGetValue(VillageEffects.GuildId, out var f) && !facilities.TryGetValue(VillageEffects.HallId, out f))
        {
            Debug.LogWarning($"{Tag} 施設マスターが無いので投資パネルは配置のまま");
            return;
        }
        InitNextCaptionDefaults(view);

        // VillagePresenter.RefreshInvestPanel と同じ決め方
        int lv = Mathf.Min(level, f.MaxLevel);
        int special = VillageEffects.SpecialEffectLevel;
        string cur, nxt;
        bool nextIsSpecial;
        if (lv >= f.MaxLevel && lv >= special && lv >= 2)
        {
            cur = f.GetLevel(lv - 1)?.effectText;
            nxt = f.GetLevel(lv)?.effectText;
            nextIsSpecial = true;
        }
        else
        {
            cur = lv > 0 ? f.GetLevel(lv)?.effectText : null;
            nxt = lv < f.MaxLevel ? f.GetLevel(lv + 1)?.effectText : null;
            nextIsSpecial = lv < f.MaxLevel && lv + 1 >= special;
        }
        int cost = lv < f.MaxLevel ? f.GetLevel(lv + 1)?.cost ?? -1 : -1;
        // 押せる/押せない・MAX の札は本編と同じ判定にする
        var (ok, reason) = CanInvest(facilities, f, level, FundsFor(level));
        view.ShowInvestPanel(f, lv, cur, nxt, cost, ok, reason, nextIsSpecial);
    }

    /// <summary>
    /// 投資パネルの「次Lv」見出しの既定値は VillageView.Awake で覚えるが、編集モードでは Awake が走らないので同じ値を入れておく。
    /// </summary>
    private static void InitNextCaptionDefaults(VillageView view)
    {
        var caption = GetField<TextMeshProUGUI>(view, "nextCaptionText");
        if (caption == null) return;
        SetField(view, "defaultNextCaption", caption.text);
        SetField(view, "defaultNextCaptionColor", caption.color);
        SetField(view, "defaultNextCaptionMaxSize", caption.fontSizeMax);
        SetField(view, "defaultNextCaptionAlign", caption.horizontalAlignment);
    }

    /// <summary>帰還収支ポップ（ShowConversionPopup はカウントアップに DOTween を使うので、文字を直接入れる）。</summary>
    private static void ShowConversion(VillageView view, SerializedObject so, int level)
    {
        if (view == null || so == null) return;
        var popup = so.FindProperty("conversionPopup")?.objectReferenceValue as GameObject;
        if (popup == null) { Debug.LogWarning($"{Tag} conversionPopup が未配線"); return; }
        popup.SetActive(true);

        const int profit = 48200;
        var effects = VillageEffects.ForLevels(level, level, level, level, level, level);
        var breakdown = MetaProgressModel.CalculateVillageFundsBreakdown(true, profit, 5000, GameModeId.Medium, 3, effects);
        SetText(so, "conversionTitleText", "ランクリア！　村への還元");
        SetText(so, "conversionEarnedText", $"今回の儲け +{profit:N0}G");
        SetText(so, "conversionConvertedText", $"村へ +{breakdown.Total:N0}G");

        var method = typeof(VillageView).GetMethod("SetConversionBreakdown", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method != null) method.Invoke(view, new object[] { breakdown });
        else Debug.LogWarning($"{Tag} VillageView.SetConversionBreakdown が見つからない（内訳は出さない）");
    }

    /// <summary>格上げのポップ（ShowRankUpPopup は DOTween を使うので、同じ並びを直接作る）。</summary>
    private static void ShowRankUp(VillageView view, SerializedObject so, int rank)
    {
        if (view == null || so == null) return;
        var popup = so.FindProperty("conversionPopup")?.objectReferenceValue as GameObject;
        if (popup == null) { Debug.LogWarning($"{Tag} conversionPopup が未配線"); return; }
        popup.SetActive(true);
        // 今の格に上がった瞬間として撮る（開拓地なら「村」へ上がったところ）
        int to = Mathf.Clamp(Mathf.Max(1, rank), 0, VillageRank.TopRank);
        int from = to - 1;
        string unlock = VillageRank.IsTop(to)
            ? $"称号「{VillageRank.TopTitle}」と、広場に自己ベストの掲示板"
            : VillageRank.TryGetUnlockedMode(to, out var mode)
                ? $"「{SaveSlotView.GetModeLabel(mode)}」のランが選べるようになった"
                : "";
        SetText(so, "conversionTitleText", "村の格が上がった！");
        SetText(so, "conversionConvertedText", $"トコの村は『{VillageRank.Name(to)}』に");
        SetText(so, "conversionEarnedText", unlock);
        Invoke(view, "SwapConversionRows", true);
        Invoke(view, "SetConversionBreakdown", new object[] { null });
        Invoke(view, "ApplyRankUpDecor", true, from, to);
    }

    /// <summary>自己ベストの掲示板（ShowRecordBoard は UIFx を使うので、3列の文字を直接入れる）。</summary>
    private static void ShowRecord(VillageView view, SerializedObject so)
    {
        if (so == null) return;
        var popup = so.FindProperty("recordBoardPopup")?.objectReferenceValue as GameObject;
        if (popup == null) { Debug.LogWarning($"{Tag} recordBoardPopup が未配線"); return; }
        popup.SetActive(true);
        SetText(so, "recordBoardTitleText", "自己ベスト");
        IReadOnlyList<VillageView.RecordRow> rows = new List<VillageView.RecordRow>
        {
            new("かんたん", "+123,456G", "S"),
            new("ふつう", "+1,234,567G", "A"),
            new("むずかしい", "まだ記録なし", ""),
        };
        Invoke(view, "SetRecordRows", rows);
    }

    /// <summary>View の private メソッドを呼ぶ（編集モードで動きなしの見た目だけ作る用）。</summary>
    private static void Invoke(object target, string method, params object[] args)
    {
        var m = target?.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (m == null) { Debug.LogWarning($"{Tag} {target?.GetType().Name}.{method} が見つからない"); return; }
        m.Invoke(target, args);
    }

    // ───────── カメラ・UI ─────────

    private static void PlaceCamera(Camera cam, bool overview)
    {
        cam.aspect = Width / (float)Height;
        // CameraFollow2D.Awake と同じ前後関係（Y軸ソート）
        cam.transparencySortMode = TransparencySortMode.CustomAxis;
        cam.transparencySortAxis = Vector3.up;

        Rect bounds = new Rect(-24f, -18f, 48f, 32f);
        Transform target = null;
        var follow = cam.GetComponent<CameraFollow2D>();
        if (follow != null)
        {
            var fso = new SerializedObject(follow);
            bounds = fso.FindProperty("worldBounds").rectValue;
            target = fso.FindProperty("target").objectReferenceValue as Transform;
        }
        if (target == null)
        {
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerMove>();
            if (player != null) target = player.transform;
        }

        Vector3 pos = cam.transform.position;
        if (overview)
        {
            cam.orthographicSize = Mathf.Max(bounds.height * 0.5f, bounds.width * 0.5f / cam.aspect);
            pos.x = bounds.center.x;
            pos.y = bounds.center.y;
        }
        else
        {
            if (target != null) { pos.x = target.position.x; pos.y = target.position.y; }
            // CameraFollow2D.Clamp と同じ
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            pos.x = bounds.width <= halfW * 2f ? bounds.center.x : Mathf.Clamp(pos.x, bounds.xMin + halfW, bounds.xMax - halfW);
            pos.y = bounds.height <= halfH * 2f ? bounds.center.y : Mathf.Clamp(pos.y, bounds.yMin + halfH, bounds.yMax - halfH);
        }
        cam.transform.position = pos;
    }

    private static void SetUiVisible(VillageView view, bool visible)
    {
        if (view == null) return;
        var canvas = view.GetComponentInParent<Canvas>(true);
        if (canvas == null) canvas = view.GetComponentInChildren<Canvas>(true);
        if (canvas != null) canvas.rootCanvas.enabled = visible;
    }

    private static void RefreshUi(Scene scene)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var scaler in root.GetComponentsInChildren<CanvasScaler>(true))
            {
                // 解像度に合わせ直す（OnEnable で Handle が走る）
                if (!scaler.isActiveAndEnabled) continue;
                scaler.enabled = false;
                scaler.enabled = true;
            }
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp.isActiveAndEnabled) tmp.ForceMeshUpdate(true);
            }
        }
        Canvas.ForceUpdateCanvases();
    }

    // ───────── 描画 ─────────

    private static void Capture(Job job)
    {
        var scene = SceneManager.GetActiveScene();
        var cam = FindMainCamera(scene);
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            RefreshUi(scene);
            // 1回目で Canvas が RenderTexture の大きさに合うので、2回描いて2回目を使う
            cam.Render();
            RefreshUi(scene);
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            string file = job.Rank >= 0 ? $"village_rank{job.Rank}_{job.Shot}.png" : $"village_lv{job.Level}_{job.Shot}.png";
            string path = Path.Combine(outDir, file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            written++;
            Debug.Log($"{Tag} 書き出し: {path}");
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }

    // ───────── 小物 ─────────

    private static string DefaultOutDir() =>
        Path.Combine(@"D:\WorkTree\TomsLands\_scratch_wt04\village_capture", DateTime.Now.ToString("yyyyMMdd_HHmmss"));

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }

    private static Camera FindMainCamera(Scene scene)
    {
        Camera first = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var c in root.GetComponentsInChildren<Camera>(true))
            {
                if (c.CompareTag("MainCamera")) return c;
                first ??= c;
            }
        }
        return first;
    }

    private static void SetActive(SerializedObject so, string field, bool active)
    {
        var go = so?.FindProperty(field)?.objectReferenceValue as GameObject;
        if (go != null) go.SetActive(active);
    }

    private static void SetText(SerializedObject so, string field, string text)
    {
        var t = so.FindProperty(field)?.objectReferenceValue as TMP_Text;
        if (t != null) t.text = text;
    }

    private static T GetField<T>(object target, string name) where T : class =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T;

    private static void SetField(object target, string name, object value)
    {
        var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f != null) f.SetValue(target, value);
        else Debug.LogWarning($"{Tag} {target.GetType().Name}.{name} が見つからない");
    }
}
