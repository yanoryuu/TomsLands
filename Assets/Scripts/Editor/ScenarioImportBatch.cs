using UnityEditor;
using UnityEngine;
using Utage;

/// <summary>
/// 宴のシナリオ（Assets/TomsScenario/Scenarios/*.csv）を .book.asset へ取り込み直す（エディタの Import と同じ）。
/// バッチ: Unity -batchmode -nographics -projectPath . -executeMethod ScenarioImportBatch.ImportBatch -quit
/// </summary>
public static class ScenarioImportBatch
{
    private const string ProjectPath = "Assets/TomsScenario/TomsScenario.project.asset";

    [MenuItem("Tools/TomsLands/宴のシナリオを取り込み直す")]
    public static void Import()
    {
        var project = AssetDatabase.LoadAssetAtPath<AdvScenarioDataProject>(ProjectPath);
        if (project == null)
        {
            Debug.LogError($"[ScenarioImportBatch] 宴のプロジェクトが無い: {ProjectPath}");
            return;
        }
        project.ImportAll();
        AssetDatabase.SaveAssets();
        Debug.Log("[ScenarioImportBatch] 取り込んだ");
    }

    public static void ImportBatch()
    {
        try
        {
            Import();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ScenarioImportBatch] 失敗: {e}");
            EditorApplication.Exit(1);
        }
    }
}
