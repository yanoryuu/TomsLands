using System;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>
/// セーブファイルの読み書きの共通処理（全セーブファイルがここを通る）。
///
/// 書き込み: 一時ファイル（.tmp）に書いてから本体と置き換え、直前の本体は .bak に残す。
///   途中で電源が落ちても「古い本体」か「新しい本体」のどちらかが必ず残る。
///   失敗しても例外を外へ出さない（1回の失敗で以後の保存やゲーム進行が止まらないように）。
/// 読み込み: 本体が壊れていたら .bak から復旧する。どちらも駄目なら false を返し、
///   呼び出し側は既定値で起動する（壊れた本体は .corrupt に写して残す）。
///
/// 本体が無いときは .bak があっても読まない（ラン削除で本体だけが消えた状態を「データなし」として扱うため）。
/// </summary>
public static class SaveFile
{
    public const string TempSuffix = ".tmp";
    public const string BackupSuffix = ".bak";
    public const string CorruptSuffix = ".corrupt";

    /// <summary>書き込みの再試行回数（ウイルス対策・同期ソフトが一瞬ファイルを掴んでいる場合など）。</summary>
    private const int WriteRetryCount = 2;
    private const int WriteRetryWaitMs = 30;

    /// <summary>
    /// 原子的に書き込む。成功で true。失敗してもログを出して false を返すだけで、例外は投げない。
    /// </summary>
    public static bool WriteAtomic(string path, string content)
    {
        if (string.IsNullOrEmpty(path)) return false;

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                WriteAtomicOnce(path, content ?? string.Empty);
                return true;
            }
            catch (Exception e) when (attempt < WriteRetryCount && (e is IOException || e is UnauthorizedAccessException))
            {
                Debug.LogWarning($"[SaveFile] 書き込みに失敗したので再試行します（{attempt + 1}/{WriteRetryCount}）: {path}\n{e.Message}");
                Thread.Sleep(WriteRetryWaitMs);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveFile] 書き込みに失敗しました（ゲームは続行します）: {path}");
                Debug.LogException(e);
                return false;
            }
        }
    }

    private static void WriteAtomicOnce(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tempPath = path + TempSuffix;
        string backupPath = path + BackupSuffix;

        File.WriteAllText(tempPath, content);

        if (!File.Exists(path))
        {
            File.Move(tempPath, path);
            return;
        }

        try
        {
            // 置き換えと .bak の作成を1回で行う（同じボリューム内なら途中で本体が消える瞬間が無い）
            File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceByCopy(tempPath, path, backupPath);
        }
        catch (IOException e) when (File.Exists(tempPath) && File.Exists(path))
        {
            // File.Replace が使えない環境（一部のファイルシステム）向け。本体は残っているので手作業で置き換える
            Debug.LogWarning($"[SaveFile] File.Replace に失敗したため、コピーで置き換えます: {e.Message}");
            ReplaceByCopy(tempPath, path, backupPath);
        }
    }

    private static void ReplaceByCopy(string tempPath, string path, string backupPath)
    {
        File.Copy(path, backupPath, overwrite: true);
        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }

    /// <summary>
    /// JSON を読み込む。本体が無ければ false（データなし）。
    /// 本体が壊れていれば .bak から復旧し、復旧できたら本体も書き戻す。どちらも駄目なら false。
    /// </summary>
    /// <param name="isValid">読めた中身が使えるかの追加判定（null なら null でなければ可）。</param>
    public static bool TryReadJson<T>(string path, out T data, Func<T, bool> isValid = null) where T : class
    {
        data = null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

        if (TryParse(path, isValid, out data, out var error))
            return true;

        Debug.LogWarning($"[SaveFile] セーブを読めませんでした。バックアップから復旧を試みます: {path}\n{error}");
        KeepCorruptCopy(path);

        string backupPath = path + BackupSuffix;
        if (File.Exists(backupPath) && TryParse(backupPath, isValid, out data, out var backupError))
        {
            try
            {
                File.Copy(backupPath, path, overwrite: true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveFile] バックアップの書き戻しに失敗（読み込みは続行）: {e.Message}");
            }
            Debug.LogWarning($"[SaveFile] バックアップから復旧しました: {path}");
            return true;
        }

        Debug.LogError($"[SaveFile] セーブもバックアップも読めませんでした。既定値で続行します: {path}");
        data = null;
        return false;
    }

    /// <summary>本体が存在するが読めない（壊れている）か。タイトルのスロット表示用。</summary>
    public static bool IsUnreadable<T>(string path, Func<T, bool> isValid = null) where T : class
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        if (TryParse<T>(path, isValid, out _, out _)) return false;
        string backupPath = path + BackupSuffix;
        return !(File.Exists(backupPath) && TryParse<T>(backupPath, isValid, out _, out _));
    }

    /// <summary>本体と .tmp / .bak / .corrupt をまとめて消す（ラン内セーブの削除用）。</summary>
    public static void DeleteWithBackups(string path)
    {
        foreach (var p in new[] { path, path + TempSuffix, path + BackupSuffix, path + CorruptSuffix })
        {
            try
            {
                if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveFile] 削除に失敗: {p}\n{e.Message}");
            }
        }
    }

    private static bool TryParse<T>(string path, Func<T, bool> isValid, out T data, out string error) where T : class
    {
        data = null;
        error = null;
        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "空のファイル";
                return false;
            }
            var parsed = JsonUtility.FromJson<T>(json);
            if (parsed == null || (isValid != null && !isValid(parsed)))
            {
                error = "中身が不正";
                return false;
            }
            data = parsed;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private static void KeepCorruptCopy(string path)
    {
        try
        {
            File.Copy(path, path + CorruptSuffix, overwrite: true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveFile] 壊れたセーブの退避に失敗: {e.Message}");
        }
    }
}
