#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// デバッグメニュー「ログ」タブ。仕様: Docs/DebugMenu_Spec.md の「ログ」節。
/// ビルド上でも Unity コンソール相当のログ（Log/Warning/Error）を種類・文字列で絞り込んで見られる。
/// 収集は DebugConsoleLog が起動時から常駐で行うため、メニューを開く前のログも残っている。
/// </summary>
public partial class DebugMenuView
{
    private const int MaxConsoleRows = 300;
    private bool _consoleShowLog = true;
    private bool _consoleShowWarning = true;
    private bool _consoleShowError = true;
    private bool _consoleAutoScroll = true;
    private string _consoleFilter = "";
    private Vector2 _consoleScroll;
    private Vector2 _consoleStackScroll;
    private int _consoleSelectedSeq = -1;
    private int _consoleLastSeenSeq = -1;
    private readonly List<DebugConsoleLog.Entry> _consoleView = new();

    private void DrawConsoleSection()
    {
        GUILayout.Label("■ コンソールログ", _headerStyle);

        DebugConsoleLog.CopyTo(_consoleView, out int logCount, out int warnCount, out int errCount);

        GUILayout.BeginHorizontal();
        _consoleShowLog = GUILayout.Toggle(_consoleShowLog, $" Log {logCount}");
        var prev = GUI.color;
        GUI.color = Color.yellow;
        _consoleShowWarning = GUILayout.Toggle(_consoleShowWarning, $" Warning {warnCount}");
        GUI.color = new Color(1f, 0.5f, 0.5f);
        _consoleShowError = GUILayout.Toggle(_consoleShowError, $" Error {errCount}");
        GUI.color = prev;
        GUILayout.FlexibleSpace();
        _consoleAutoScroll = GUILayout.Toggle(_consoleAutoScroll, " 最新へ自動スクロール");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("絞り込み", GUILayout.Width(60));
        _consoleFilter = GUILayout.TextField(_consoleFilter);
        if (GUILayout.Button("クリア", GUILayout.Width(60)))
        {
            DebugConsoleLog.Clear();
            _consoleSelectedSeq = -1;
        }
        if (GUILayout.Button("コピー", GUILayout.Width(60))) CopyConsoleLogs();
        GUILayout.EndHorizontal();

        // 絞り込み後の一覧
        var filtered = new List<DebugConsoleLog.Entry>();
        foreach (var e in _consoleView)
            if (PassesConsoleFilter(e)) filtered.Add(e);

        int newestSeq = _consoleView.Count > 0 ? _consoleView[_consoleView.Count - 1].seq : -1;
        if (_consoleAutoScroll && newestSeq != _consoleLastSeenSeq) _consoleScroll.y = float.MaxValue;
        _consoleLastSeenSeq = newestSeq;

        _consoleScroll = GUILayout.BeginScrollView(_consoleScroll, GUILayout.Height(300));
        if (filtered.Count == 0) GUILayout.Label("（なし）");
        // IMGUI は行数に比例して重くなるため、表示は新しい MaxConsoleRows 件までにする
        int start = Math.Max(0, filtered.Count - MaxConsoleRows);
        if (start > 0) GUILayout.Label($"（古い {start} 件は省略。絞り込みかコピーで確認）");
        DebugConsoleLog.Entry? selected = null;
        for (int i = start; i < filtered.Count; i++)
        {
            var e = filtered[i];
            GUI.color = ConsoleColor(e.type, e.seq == _consoleSelectedSeq);
            string firstLine = FirstLine(e.condition);
            if (GUILayout.Button($"{e.time:HH:mm:ss} {firstLine}", GUI.skin.label))
                _consoleSelectedSeq = e.seq == _consoleSelectedSeq ? -1 : e.seq;
            if (e.seq == _consoleSelectedSeq) selected = e;
        }
        GUI.color = prev;
        GUILayout.EndScrollView();

        // 選択中のログの全文とスタックトレース
        GUILayout.Label("■ 詳細（行をクリックで選択）", _headerStyle);
        _consoleStackScroll = GUILayout.BeginScrollView(_consoleStackScroll, GUILayout.Height(150));
        if (selected.HasValue)
        {
            var s = selected.Value;
            GUILayout.TextArea($"[{s.type}] {s.time:HH:mm:ss}\n{s.condition}\n\n{s.stackTrace}");
        }
        else GUILayout.Label("（未選択）");
        GUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(Application.consoleLogPath))
            GUILayout.Label($"ログファイル: {Application.consoleLogPath}");
    }

    private bool PassesConsoleFilter(DebugConsoleLog.Entry e)
    {
        bool typeOk = e.type switch
        {
            LogType.Log => _consoleShowLog,
            LogType.Warning => _consoleShowWarning,
            _ => _consoleShowError,
        };
        if (!typeOk) return false;
        return string.IsNullOrEmpty(_consoleFilter)
               || e.condition.IndexOf(_consoleFilter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void CopyConsoleLogs()
    {
        var sb = new StringBuilder();
        foreach (var e in _consoleView)
        {
            if (!PassesConsoleFilter(e)) continue;
            sb.Append($"{e.time:HH:mm:ss} [{e.type}] {e.condition}\n");
            if (e.type != LogType.Log && !string.IsNullOrEmpty(e.stackTrace)) sb.Append(e.stackTrace).Append('\n');
        }
        GUIUtility.systemCopyBuffer = sb.ToString();
        _utilMessage = "ログをクリップボードにコピーしました";
    }

    private static Color ConsoleColor(LogType type, bool selected)
    {
        Color c = type switch
        {
            LogType.Log => Color.white,
            LogType.Warning => Color.yellow,
            _ => new Color(1f, 0.5f, 0.5f),
        };
        return selected ? Color.Lerp(c, Color.cyan, 0.6f) : c;
    }

    private static string FirstLine(string text)
    {
        int nl = text.IndexOf('\n');
        string line = nl >= 0 ? text.Substring(0, nl) : text;
        return line.Length > 160 ? line.Substring(0, 160) + "..." : line;
    }
}

/// <summary>
/// ログタブ用のログ収集。起動時（初回シーンロード前）に購読を始め、直近 MaxEntries 件を保持する。
/// 別スレッドからのログも拾うため logMessageReceivedThreaded を使い、lock で保護する。
/// </summary>
public static class DebugConsoleLog
{
    public struct Entry
    {
        public int seq;
        public DateTime time;
        public LogType type;
        public string condition;
        public string stackTrace;
    }

    private const int MaxEntries = 1000;
    private static readonly Queue<Entry> _entries = new();
    private static readonly object _lock = new();
    private static int _seq;
    private static int _logCount, _warnCount, _errCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Boot()
    {
        Application.logMessageReceivedThreaded -= OnLog;
        if (!Debug.isDebugBuild) return;
        Clear();
        Application.logMessageReceivedThreaded += OnLog;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        lock (_lock)
        {
            if (type == LogType.Log) _logCount++;
            else if (type == LogType.Warning) _warnCount++;
            else _errCount++;

            _entries.Enqueue(new Entry
            {
                seq = _seq++,
                time = DateTime.Now,
                type = type,
                condition = condition ?? "",
                stackTrace = stackTrace ?? "",
            });
            if (_entries.Count > MaxEntries) _entries.Dequeue();
        }
    }

    /// <summary>保持中のログを古い順に list へコピーする。件数は起動（またはクリア）以降の累計。</summary>
    public static void CopyTo(List<Entry> list, out int logCount, out int warnCount, out int errCount)
    {
        lock (_lock)
        {
            list.Clear();
            list.AddRange(_entries);
            logCount = _logCount;
            warnCount = _warnCount;
            errCount = _errCount;
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _logCount = _warnCount = _errCount = 0;
        }
    }
}
#endif
