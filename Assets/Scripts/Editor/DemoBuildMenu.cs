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
///   productName を一時的に <see cref="DemoBuild.DemoProductName"/> にして D:\UnityGame\TomsShop_Demo\ へ出す。
///   終わったら（失敗しても）productName を元に戻す。Player Settings の定義は触らないので、本編のビルド設定は変わらない。
/// ・「エディタで体験版を試す」は Player Settings（Standalone）の定義に DEMO_BUILD を足す／外す。
///   入れたまま本編をビルドしようとすると、ビルド前に止める（<see cref="OnPreprocessBuild"/>）。
/// ・バッチモード: -executeMethod DemoBuildMenu.BuildBatch（開発ビルドは DemoBuildMenu.BuildBatchDevelopment）。失敗は終了コード 1。
/// </summary>
public sealed class DemoBuildMenu : IPreprocessBuildWithReport
{
    public const string Define = "DEMO_BUILD";
    public const string OutputDir = @"D:\UnityGame\TomsShop_Demo";
    public const string ExeName = "TomSLand_Demo.exe";

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
    public static void BuildBatch() => ExitWith(Build(development: false));

    /// <summary>バッチモード用（開発ビルド。体験版の App ID が決まっていれば steam_appid.txt が入る）。</summary>
    public static void BuildBatchDevelopment() => ExitWith(Build(development: true));

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
        if (!Application.isBatchMode && !EditorUtility.DisplayDialog("体験版をビルド",
                $"体験版（{Define}）を{(development ? "開発ビルドで" : "")}\n{OutputDir}\\{ExeName}\nへビルドします。", "ビルドする", "やめる"))
            return;
        var report = Build(development);
        if (Application.isBatchMode || report == null) return;
        bool ok = report.summary.result == BuildResult.Succeeded;
        EditorUtility.DisplayDialog("体験版をビルド",
            ok ? $"できました（{report.summary.totalSize / (1024 * 1024)} MB）。\n{OutputDir}" : $"失敗しました（{report.summary.result}）。コンソールを見てください。",
            "OK");
        if (ok) EditorUtility.RevealInFinder(Path.Combine(OutputDir, ExeName));
    }

    /// <summary>体験版をビルドする。productName は必ず元に戻す。</summary>
    public static BuildReport Build(bool development)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[DemoBuild] Build Settings に有効なシーンがありません");
            return null;
        }

        Directory.CreateDirectory(OutputDir);
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(OutputDir, ExeName),
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
            if (s.result == BuildResult.Succeeded) Debug.Log(msg); else Debug.LogError(msg);
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
