#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VContainer;

/// <summary>
/// デバッグメニュー「便利」タブ。仕様: Docs/DebugMenu_Spec.md の「便利」節。
/// 追加依存は ConstructUtility で任意解決（無いシーンではそのセクションを出さない）。
/// </summary>
public partial class DebugMenuView
{
    private TomsEventExecutor _utilEventExecutor;
    private DungeonIntelModel _utilDungeonIntel;
    private NewspaperSubscriptionModel _utilSubscriptions;
    private ScrapbookModel _utilScrapbook;
    private NewsModel _utilNews;
    private SellOrderModel _utilSellOrders;
    private ShopMachineModel _utilMachines;

    private const int UtilMoneyCap = 99_999_999;
    private bool _utilItemsOpen;
    private bool _utilEventsOpen;
    private bool _utilLogsOpen = true;
    private Vector2 _utilItemScroll;
    private Vector2 _utilEventScroll;
    private string _utilEventFilter = "";
    private List<TomsEvent> _utilEvents;
    private float _utilPrevTimeScale = 1f;
    private string _utilMessage = "";

    [Inject]
    public void ConstructUtility(IObjectResolver resolver)
    {
        resolver.TryResolve(out _utilEventExecutor);
        resolver.TryResolve(out _utilDungeonIntel);
        resolver.TryResolve(out _utilSubscriptions);
        resolver.TryResolve(out _utilScrapbook);
        resolver.TryResolve(out _utilNews);
        resolver.TryResolve(out _utilSellOrders);
        resolver.TryResolve(out _utilMachines);
    }

    private void DrawUtilitySection()
    {
        if (!string.IsNullOrEmpty(_utilMessage)) GUILayout.Label(_utilMessage);

        DrawUtilMoney();
        DrawUtilItems();
        DrawUtilEvents();
        DrawUtilInfo();
        DrawUtilSave();
        DrawUtilDisplay();
        DrawUtilHero();
    }

    // ---------------- 1. お金 ----------------
    private void DrawUtilMoney()
    {
        GUILayout.Label("■ お金ショートカット", _headerStyle);
        if (_tomsModel == null) { GUILayout.Label("TomsModel なし"); return; }

        GUILayout.Label($"所持金 {_tomsModel.PlayerMoney.Value:N0} G / 借入元本 {_tomsModel.BorrowedPrincipal:N0} G / 返済回数 {_tomsModel.DebtCycle.Value}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+1,000,000"))
        {
            long v = Math.Min((long)_tomsModel.PlayerMoney.Value + 1_000_000, UtilMoneyCap);
            SetMoney((int)v);
        }
        if (GUILayout.Button($"最大化 ({UtilMoneyCap:N0})")) SetMoney(UtilMoneyCap);
        if (GUILayout.Button("借入元本を0に"))
        {
            _tomsModel.BorrowedPrincipal = 0;
            _tomsModel.SavePlayerMoney();
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
    }

    // ---------------- 2. アイテム ----------------
    private void DrawUtilItems()
    {
        GUILayout.Label("■ アイテム", _headerStyle);
        if (_itemModel == null) { GUILayout.Label("ItemModel なし"); GUILayout.Space(6); return; }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("全在庫を0に"))
        {
            foreach (var item in _itemModel.RuntimeItems) item?.UpdateStock(0);
            _itemModel.SaveData();
        }
        if (_tomsModel != null && GUILayout.Button("全アイテム解放（鍛冶屋Lvを要求Lv最大へ）"))
        {
            int max = _itemModel.RuntimeItems.Where(i => i != null).Select(i => i.RequiredLevel.Value).DefaultIfEmpty(1).Max();
            if (_tomsModel.BlacksmithLevel.Value < max) _tomsModel.BlacksmithLevel.Value = max;
            _tomsModel.SavePlayerMoney();
            _utilMessage = $"鍛冶屋Lv = {_tomsModel.BlacksmithLevel.Value}";
        }
        GUILayout.EndHorizontal();

        _utilItemsOpen = GUILayout.Toggle(_utilItemsOpen, " 個別在庫 +1 / +10 を表示");
        if (_utilItemsOpen)
        {
            _utilItemScroll = GUILayout.BeginScrollView(_utilItemScroll, GUILayout.Height(220));
            foreach (var item in _itemModel.RuntimeItems)
            {
                if (item == null) continue;
                bool locked = _tomsModel != null && item.RequiredLevel.Value > _tomsModel.BlacksmithLevel.Value;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{(locked ? "[未解放Lv" + item.RequiredLevel.Value + "] " : "")}{item.ItemName}  在庫{item.Stock.Value}/{item.MaxStock.Value}", GUILayout.Width(300));
                if (GUILayout.Button("+1")) { item.UpdateStock(item.Stock.Value + 1); _itemModel.SaveData(); }
                if (GUILayout.Button("+10")) { item.UpdateStock(item.Stock.Value + 10); _itemModel.SaveData(); }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
        GUILayout.Space(6);
    }

    // ---------------- 3. イベント ----------------
    private void DrawUtilEvents()
    {
        GUILayout.Label("■ イベント強制発生", _headerStyle);
        if (_utilEventExecutor == null) { GUILayout.Label("TomsEventExecutor なし（このシーンでは不可）"); GUILayout.Space(6); return; }

        _utilEventsOpen = GUILayout.Toggle(_utilEventsOpen, " イベント一覧を表示");
        if (_utilEventsOpen)
        {
            if (_utilEvents == null) _utilEvents = EventDataLoader.LoadAll();
            GUILayout.BeginHorizontal();
            GUILayout.Label("絞り込み", GUILayout.Width(60));
            _utilEventFilter = GUILayout.TextField(_utilEventFilter);
            GUILayout.EndHorizontal();
            _utilEventScroll = GUILayout.BeginScrollView(_utilEventScroll, GUILayout.Height(220));
            foreach (var e in _utilEvents)
            {
                if (e == null) continue;
                if (!string.IsNullOrEmpty(_utilEventFilter) &&
                    (e.id ?? "").IndexOf(_utilEventFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (e.title ?? "").IndexOf(_utilEventFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button($"{e.id}  {e.title}"))
                {
                    try
                    {
                        _utilEventExecutor.Execute(e);
                        _utilMessage = $"イベント実行: {e.id}";
                    }
                    catch (Exception ex)
                    {
                        _utilMessage = $"イベント失敗: {e.id} ({ex.Message})";
                        Debug.LogException(ex);
                    }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Label("※コマンド効果のみ実行（所持金等は即保存）。イベント演出UIは出ません");
        }
        GUILayout.Space(6);
    }

    // ---------------- 4. 新聞・情報 ----------------
    private void DrawUtilInfo()
    {
        GUILayout.Label("■ 新聞・情報", _headerStyle);
        bool any = false;
        if (_utilDungeonIntel != null)
        {
            any = true;
            bool next = GUILayout.Toggle(_utilDungeonIntel.RevealAll, " 全ダンジョンの弱点を常に開示（この起動中のみ）");
            if (next != _utilDungeonIntel.RevealAll) _utilDungeonIntel.RevealAll = next;
        }
        if (_utilSubscriptions != null)
        {
            any = true;
            int turn = CurrentTurnSafe();
            GUILayout.Label($"購読中の新聞社: {_utilSubscriptions.UsedSlots(turn)}件");
        }
        if (_utilScrapbook != null)
        {
            any = true;
            GUILayout.Label($"スクラップ: ピン {_utilScrapbook.Pinned.Count}/{_utilScrapbook.Capacity} / 過去 {_utilScrapbook.Archive.Count}");
        }
        if (!any) GUILayout.Label("新聞関連Modelなし");
        GUILayout.Label("※全ダンジョン情報の解放は「その他」タブ");
        GUILayout.Space(6);
    }

    // ---------------- 5. セーブ ----------------
    private void DrawUtilSave()
    {
        GUILayout.Label("■ セーブ", _headerStyle);
        string root = SaveSlotManager.CurrentRoot;
        GUILayout.Label($"slot_{SaveSlotManager.CurrentSlot}: {root}");
        GUILayout.BeginHorizontal();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (GUILayout.Button("フォルダをエクスプローラで開く"))
        {
            try
            {
                System.IO.Directory.CreateDirectory(root);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + root.Replace('/', '\\') + "\"");
            }
            catch (Exception ex) { _utilMessage = "開けませんでした: " + ex.Message; }
        }
#endif
        if (GUILayout.Button("今すぐ全部保存")) SaveEverything();
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
    }

    private void SaveEverything()
    {
        var saved = new List<string>();
        void Try(string name, Action a)
        {
            try { a(); saved.Add(name); }
            catch (Exception ex) { Debug.LogWarning($"[DebugMenu] 保存失敗 {name}: {ex.Message}"); }
        }
        if (_tomsModel != null) Try("Toms", _tomsModel.SavePlayerMoney);
        if (_itemModel != null) Try("Item", _itemModel.SaveData);
        if (_heroModel?.heroData != null) Try("Hero", _heroModel.SaveHeroData);
        if (_statusModel != null) Try("Status", _statusModel.SaveData);
        if (_relicInventory != null) Try("Relic", _relicInventory.SaveData);
        if (_portfolioModel != null) Try("Portfolio", _portfolioModel.SaveData);
        if (_dungeonRepository != null) Try("Dungeon", _dungeonRepository.Save);
        if (_utilSellOrders != null) Try("SellOrder", _utilSellOrders.SaveData);
        if (_utilMachines != null) Try("Machine", _utilMachines.SaveData);
        if (_tomsModel != null && _utilSubscriptions != null)
            Try("Newspaper", () => NewspaperSaveStore.Save(_tomsModel.FlowSeed, _utilSubscriptions, _utilScrapbook, _utilNews));
        Try("Meta", Meta.SaveData);
        _utilMessage = "保存: " + string.Join(", ", saved);
        Debug.Log("[DebugMenu] 全保存 " + _utilMessage);
    }

    // ---------------- 6. 表示系 ----------------
    private void DrawUtilDisplay()
    {
        GUILayout.Label("■ 表示・ログ", _headerStyle);
        DebugUtilityOverlay.ShowFps = GUILayout.Toggle(DebugUtilityOverlay.ShowFps, " FPS表示（画面右上。メニューを閉じても表示）");

        bool paused = Time.timeScale == 0f;
        bool nextPaused = GUILayout.Toggle(paused, " ゲーム一時停止（timeScale=0）");
        if (nextPaused != paused)
        {
            if (nextPaused) { _utilPrevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f; Time.timeScale = 0f; }
            else Time.timeScale = _utilPrevTimeScale;
        }

        GUILayout.Label($"警告 {DebugUtilityOverlay.WarningCount} / エラー {DebugUtilityOverlay.ErrorCount}（起動以降）");
        GUILayout.BeginHorizontal();
        _utilLogsOpen = GUILayout.Toggle(_utilLogsOpen, " 直近の警告/エラーを表示");
        if (GUILayout.Button("クリア", GUILayout.Width(70))) DebugUtilityOverlay.ClearLogs();
        GUILayout.EndHorizontal();
        if (_utilLogsOpen)
        {
            var logs = DebugUtilityOverlay.Recent;
            if (logs.Count == 0) GUILayout.Label("（なし）");
            for (int i = logs.Count - 1; i >= 0; i--)
            {
                var prev = GUI.color;
                GUI.color = logs[i].type == LogType.Warning ? Color.yellow : new Color(1f, 0.5f, 0.5f);
                GUILayout.Label(logs[i].text);
                GUI.color = prev;
            }
        }
        GUILayout.Space(6);
    }

    // ---------------- 7. 勇者 ----------------
    private void DrawUtilHero()
    {
        GUILayout.Label("■ 勇者", _headerStyle);
        var hero = _heroModel?.heroData;
        if (hero == null) { GUILayout.Label("HeroModel なし"); return; }

        GUILayout.Label($"Lv{hero.level.Value}  EXP {hero.experience.Value}/{hero.expToNextLevel.Value}");
        GUILayout.Label($"HP {hero.hp.Value}  ATK {hero.attackPower.Value}  DEF {hero.defensePower.Value}  戦術 {hero.tactics.Value}");
        GUILayout.Label($"武器 {hero.weaponName.Value}  防具 {hero.armorName.Value}");
        if (GUILayout.Button("HP全回復（現Lvの最大HPへ）"))
        {
            var loader = new HeroLevelDataLoader();
            loader.LoadFromCSV("HeroStatusData");
            var data = loader.GetLevelData(hero.level.Value);
            if (data != null)
            {
                hero.hp.Value = data.MaxHp;
                _heroModel.SaveHeroData();
            }
            else _utilMessage = "Lvデータが見つかりません";
        }
        GUILayout.Space(8);
    }
}

/// <summary>
/// 便利タブ用の常駐オーバーレイ。FPS表示とログ収集をメニューの開閉と無関係に行う。
/// 初回シーンロード前に自動生成される（Editor / Development Build のみ）。
/// </summary>
public class DebugUtilityOverlay : MonoBehaviour
{
    public struct LogEntry { public LogType type; public string text; }

    public static bool ShowFps;
    public static int WarningCount { get; private set; }
    public static int ErrorCount { get; private set; }
    private const int MaxRecent = 8;
    private static readonly List<LogEntry> _recent = new();
    public static IReadOnlyList<LogEntry> Recent => _recent;

    private static DebugUtilityOverlay _instance;
    private float _smoothDt = 1f / 60f;
    private GUIStyle _style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (!Debug.isDebugBuild || _instance != null) return;
        var go = new GameObject("DebugUtilityOverlay") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<DebugUtilityOverlay>();
    }

    public static void ClearLogs()
    {
        _recent.Clear();
        WarningCount = 0;
        ErrorCount = 0;
    }

    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log) return;
        if (type == LogType.Warning) WarningCount++; else ErrorCount++;
        string text = condition.Length > 200 ? condition.Substring(0, 200) + "..." : condition;
        _recent.Add(new LogEntry { type = type, text = $"[{type}] {text}" });
        if (_recent.Count > MaxRecent) _recent.RemoveAt(0);
    }

    private void Update()
    {
        _smoothDt += (Time.unscaledDeltaTime - _smoothDt) * 0.1f;
    }

    private void OnGUI()
    {
        if (!ShowFps) return;
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            _style.normal.textColor = Color.green;
        }
        float fps = 1f / Mathf.Max(0.0001f, _smoothDt);
        GUI.Label(new Rect(Screen.width - 220, 4, 214, 28), $"{fps:F0} FPS ({_smoothDt * 1000f:F1} ms)", _style);
    }
}
#endif
