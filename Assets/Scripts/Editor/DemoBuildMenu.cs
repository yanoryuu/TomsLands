using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 体験版（スクリプト定義 DEMO_BUILD）のビルドと、エディタで体験版を試すための定義の切り替え（Docs/Demo_Build.md）。
///
/// ・「体験版をビルド」は DEMO_BUILD をこのビルドにだけ足し（BuildPlayerOptions.extraScriptingDefines）、
///   productName を一時的に <see cref="DemoBuild.DemoProductName"/> にしてビルドする。
///   出力先と exe の名前は、メニューからなら保存の画面で選ぶ（前回の場所を覚える。初めは D:\UnityGame\TomsShop_Demo\TomSLand_Demo.exe）。
///   終わったら（失敗しても）productName を元に戻す。Player Settings の定義は触らないので、本編のビルド設定は変わらない。
/// ・「エディタで体験版を試す」は Player Settings（Standalone）の定義に DEMO_BUILD を足す／外す。
///   入れたまま本編をビルドしようとすると、ビルド前に止める（<see cref="OnPreprocessBuild"/>）。
/// ・バッチモード: -executeMethod DemoBuildMenu.BuildBatch（開発ビルドは DemoBuildMenu.BuildBatchDevelopment）。失敗は終了コード 1。
///   出力先は -demoOutput &lt;exe のフルパス&gt; で変えられる（無ければ前回の場所）。
/// </summary>
public sealed class DemoBuildMenu : IPreprocessBuildWithReport
{
    public const string Define = "DEMO_BUILD";
    public const string DefaultOutputDir = @"D:\UnityGame\TomsShop_Demo";
    public const string DefaultExeName = "TomSLand_Demo.exe";
    private const string OutputPathPrefKey = "TomsLands.DemoBuild.OutputPath";
    private const string OutputArg = "-demoOutput";

    /// <summary>前回ビルドした exe のパス（初めは既定の場所）。</summary>
    public static string LastOutputPath
    {
        get => EditorPrefs.GetString(OutputPathPrefKey, Path.Combine(DefaultOutputDir, DefaultExeName));
        private set => EditorPrefs.SetString(OutputPathPrefKey, value);
    }

    private const string MenuRoot = "Tools/TomsLands/体験版/";
    private const string MenuToggle = MenuRoot + "エディタで体験版を試す（DEMO_BUILD を入れる）";

    private static readonly NamedBuildTarget Standalone = NamedBuildTarget.Standalone;

    /// <summary>体験版のビルド中か（ビルドの前後処理が体験版かを知るため）。</summary>
    public static bool IsBuildingDemo { get; private set; }

    /// <summary>今のビルドが体験版か（メニューからのビルド中か、Player Settings に DEMO_BUILD が入っている）。</summary>
    public static bool IsDemoBuild(BuildReport report) => IsBuildingDemo || HasDefineInPlayerSettings();

    public int callbackOrder => -10;

    [MenuItem(MenuRoot + "体験版をビルド", priority = 70)]
    public static void BuildFromMenu() => BuildInteractive(development: false);

    [MenuItem(MenuRoot + "体験版をビルド（開発ビルド）", priority = 71)]
    public static void BuildDevelopmentFromMenu() => BuildInteractive(development: true);

    [MenuItem(MenuToggle, priority = 80)]
    public static void ToggleDefine()
    {
        SetDefineInPlayerSettings(!HasDefineInPlayerSettings());
        Debug.Log($"[DemoBuild] Player Settings の {Define}: {(HasDefineInPlayerSettings() ? "入れました（エディタの再生が体験版になります。本編をビルドする前に外してください）" : "外しました")}");
    }

    [MenuItem(MenuToggle, validate = true)]
    private static bool ToggleDefineValidate()
    {
        Menu.SetChecked(MenuToggle, HasDefineInPlayerSettings());
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    /// <summary>バッチモード用（製品ビルド＝Development Build なし）。</summary>
    public static void BuildBatch() => ExitWith(Build(development: false, BatchOutputPath()));

    /// <summary>バッチモード用（開発ビルド。体験版の App ID が決まっていれば steam_appid.txt が入る）。</summary>
    public static void BuildBatchDevelopment() => ExitWith(Build(development: true, BatchOutputPath()));

    /// <summary>コマンドラインの -demoOutput の次の引数。無ければ前回の場所。</summary>
    private static string BatchOutputPath()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, OutputArg);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : LastOutputPath;
    }

    /// <summary>バッチモード用: Player Settings に DEMO_BUILD を入れる／外す（コンパイルの確認用）。</summary>
    public static void AddDefineBatch() { SetDefineInPlayerSettings(true); AssetDatabase.SaveAssets(); EditorApplication.Exit(0); }
    public static void RemoveDefineBatch() { SetDefineInPlayerSettings(false); AssetDatabase.SaveAssets(); EditorApplication.Exit(0); }

    private static void ExitWith(BuildReport report)
    {
        bool ok = report != null && report.summary.result == BuildResult.Succeeded;
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static void BuildInteractive(bool development)
    {
        string exePath = LastOutputPath;
        if (!Application.isBatchMode)
        {
            exePath = EditorUtility.SaveFilePanel($"体験版{(development ? "（開発ビルド）" : "")}の保存先と名前",
                Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath), "exe");
            if (string.IsNullOrEmpty(exePath)) return; // やめた
            exePath = Path.GetFullPath(exePath);
            if (!string.Equals(Path.GetFileName(exePath), DefaultExeName, StringComparison.OrdinalIgnoreCase)
                && !EditorUtility.DisplayDialog("体験版をビルド",
                    $"exe の名前が {DefaultExeName} ではありません。\nSteam に上げるなら、Steamworks の起動の設定と SteamPipe の設定（app_build_demo.vdf の ContentRoot）もこの名前・場所に合わせてください。",
                    "このままビルドする", "やめる"))
                return;
        }
        var report = Build(development, exePath);
        if (Application.isBatchMode || report == null) return;
        bool ok = report.summary.result == BuildResult.Succeeded;
        EditorUtility.DisplayDialog("体験版をビルド",
            ok ? $"できました（{report.summary.totalSize / (1024 * 1024)} MB）。\n{exePath}" : $"失敗しました（{report.summary.result}）。コンソールを見てください。",
            "OK");
        if (ok) EditorUtility.RevealInFinder(exePath);
    }

    /// <summary>体験版を exePath へビルドする。productName は必ず元に戻す。</summary>
    public static BuildReport Build(bool development, string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) exePath = LastOutputPath;
        if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exePath += ".exe";
        exePath = Path.GetFullPath(exePath);
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[DemoBuild] Build Settings に有効なシーンがありません");
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(exePath));
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = development ? BuildOptions.Development : BuildOptions.None,
            extraScriptingDefines = new[] { Define },
        };

        string previousProductName = PlayerSettings.productName;
        BuildReport report = null;
        IsBuildingDemo = true;
        try
        {
            PlayerSettings.productName = DemoBuild.DemoProductName;
            Debug.Log($"[DemoBuild] 体験版をビルドします → {options.locationPathName}（{(development ? "開発ビルド" : "製品ビルド")}、productName={PlayerSettings.productName}）");
            report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string msg = $"[DemoBuild] 結果: {s.result} / {s.totalSize / (1024 * 1024)} MB / {s.totalTime} / エラー {s.totalErrors} / 注意 {s.totalWarnings}";
            if (s.result == BuildResult.Succeeded)
            {
                Debug.Log(msg);
                LastOutputPath = exePath;
            }
            else Debug.LogError(msg);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DemoBuild] ビルド中に例外: {e}");
        }
        finally
        {
            IsBuildingDemo = false;
            PlayerSettings.productName = previousProductName;
            AssetDatabase.SaveAssets();
            Debug.Log($"[DemoBuild] productName を {previousProductName} に戻しました（Player Settings の定義は触っていません）");
        }
        return report;
    }

    /// <summary>本編のビルドに DEMO_BUILD が混ざらないように止める（エディタで体験版を試したまま本編をビルドした場合）。</summary>
    public void OnPreprocessBuild(BuildReport report)
    {
        if (IsBuildingDemo) return;
        if (HasDefineInPlayerSettings())
            throw new BuildFailedException(
                $"Player Settings に {Define} が入ったままです。本編をビルドするなら「{MenuToggle}」で外してください。体験版は「{MenuRoot}体験版をビルド」から。");
    }

    public static bool HasDefineInPlayerSettings()
    {
        PlayerSettings.GetScriptingDefineSymbols(Standalone, out var defines);
        return defines.Contains(Define);
    }

    private static void SetDefineInPlayerSettings(bool on)
    {
        PlayerSettings.GetScriptingDefineSymbols(Standalone, out var defines);
        var list = defines.ToList();
        if (on && !list.Contains(Define)) list.Add(Define);
        if (!on) list.RemoveAll(d => d == Define);
        PlayerSettings.SetScriptingDefineSymbols(Standalone, list.ToArray());
    }
}
