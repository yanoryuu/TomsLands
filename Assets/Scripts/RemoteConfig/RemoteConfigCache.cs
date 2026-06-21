using System.IO;
using UnityEngine;

/// <summary>前回成功した version と JSON を persistentDataPath に永続化する。</summary>
public sealed class RemoteConfigCache
{
    private readonly string _jsonPath;
    private readonly string _versionPath;

    public RemoteConfigCache(string fileBaseName = "gameconst")
    {
        var dir = Path.Combine(Application.persistentDataPath, "remoteconfig");
        Directory.CreateDirectory(dir);
        _jsonPath = Path.Combine(dir, $"{fileBaseName}.json");
        _versionPath = Path.Combine(dir, $"{fileBaseName}.version");
    }

    public int LoadVersion()
    {
        try
        {
            if (!File.Exists(_versionPath)) return -1;
            return int.TryParse(File.ReadAllText(_versionPath), out var v) ? v : -1;
        }
        catch { return -1; }
    }

    public string LoadJson()
    {
        try { return File.Exists(_jsonPath) ? File.ReadAllText(_jsonPath) : null; }
        catch { return null; }
    }

    public void Save(int version, string json)
    {
        try
        {
            File.WriteAllText(_jsonPath, json);
            File.WriteAllText(_versionPath, version.ToString());
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RemoteConfig] キャッシュ保存失敗: {e.Message}");
        }
    }
}
