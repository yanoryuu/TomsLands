using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows/Linux/Mac のビルド後に、開発ビルド（Development Build）のときだけ steam_appid.txt を exe の隣に置く。
/// これがあると Steam クライアントを起動したまま exe を直接起動しても Steam につながる（実績のテスト用）。
/// 製品ビルド（Development Build オフ）には入れない。入れると Steam を通さない起動でも
/// App ID が決まってしまうため。製品は Steam から起動させる（Docs/SteamAchievements.md「起動と steam_appid.txt」）。
/// </summary>
public sealed class SteamAppIdPostBuild : IPostprocessBuildWithReport
{
    private const string FileName = "steam_appid.txt";

    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        var target = report.summary.platform;
        if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows
            && target != BuildTarget.StandaloneLinux64 && target != BuildTarget.StandaloneOSX)
            return;

        string outDir = Path.GetDirectoryName(report.summary.outputPath);
        if (string.IsNullOrEmpty(outDir)) return;
        string dest = Path.Combine(outDir, FileName);

        bool development = (report.summary.options & BuildOptions.Development) != 0;
        if (!development)
        {
            // 前に開発ビルドを同じ場所に出していたら残っているので消す（製品ビルドに紛れ込ませない）
            if (File.Exists(dest))
            {
                File.Delete(dest);
                Debug.Log($"[SteamAppId] 製品ビルドなので {dest} を消しました");
            }
            return;
        }

        string src = Path.Combine(Directory.GetCurrentDirectory(), FileName);
        if (!File.Exists(src))
        {
            Debug.LogWarning($"[SteamAppId] プロジェクト直下に {FileName} がありません（中身は {SteamManager.AppId}）");
            return;
        }
        File.Copy(src, dest, true);
        Debug.Log($"[SteamAppId] 開発ビルドに {FileName} を同梱しました: {dest}");
    }
}
