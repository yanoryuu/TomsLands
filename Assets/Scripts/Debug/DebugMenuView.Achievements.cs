#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// デバッグメニュー「実績」タブ。仕様: Docs/DebugMenu_Spec.md の「実績」節、Docs/SteamAchievements.md。
/// 22個の解除状態・進捗（Stats）・このランの途中経過を見せ、全部リセットできる（エディタ／開発ビルドのみ）。
/// AchievementService はシーンを跨いで1つなので、どのシーンでも同じ内容が出る。
/// </summary>
public partial class DebugMenuView
{
    private bool _achResetArmed;
    private string _achMessage = "";

    private void DrawAchievementSection()
    {
        var service = AchievementService.Shared;

        GUILayout.Label("■ Steam", _headerStyle);
        GUILayout.Label($"Steam: {SteamManager.StatusMessage}{(AchievementStore.IsSandboxed ? "（自動テストの隔離中）" : "")}");
        GUILayout.Label($"記録: {AchievementStore.GlobalPath}");

        int unlockedCount = 0;
        foreach (var def in AchievementDefinitions.All)
            if (service.IsUnlocked(def.Id)) unlockedCount++;

        GUILayout.Space(6);
        GUILayout.Label($"■ 実績 {unlockedCount}/{AchievementDefinitions.All.Count}", _headerStyle);
        foreach (var def in AchievementDefinitions.All)
        {
            bool done = service.IsUnlocked(def.Id);
            string progress = "";
            if (def.ProgressStat.HasValue)
            {
                int v = def.Id == AchievementId.AllPapers ? service.SubscribedCompanyCount : service.GetStat(def.ProgressStat.Value);
                progress = $"  [{v:N0}/{def.ProgressTarget:N0}]";
            }
            GUILayout.Label($"{(done ? "■" : "□")} {def.DisplayName}（{def.ApiName}）{progress}");
        }

        GUILayout.Space(6);
        GUILayout.Label("■ Stats", _headerStyle);
        foreach (AchievementStat stat in System.Enum.GetValues(typeof(AchievementStat)))
            GUILayout.Label($"{AchievementDefinitions.StatApiName(stat)}: {service.GetStat(stat):N0}");

        GUILayout.Space(6);
        GUILayout.Label("■ このランの途中経過", _headerStyle);
        foreach (var line in AchievementTracker.DescribeRun()) GUILayout.Label(line);

        GUILayout.Space(8);
        GUILayout.Label("■ リセット（手元の記録と Steam の両方）", _headerStyle);
        GUILayout.BeginHorizontal();
        if (!_achResetArmed)
        {
            if (GUILayout.Button("全部リセット…")) _achResetArmed = true;
        }
        else
        {
            if (GUILayout.Button("本当にリセットする"))
            {
                service.ResetAllForDevelopment();
                _achResetArmed = false;
                _achMessage = "リセットしました";
            }
            if (GUILayout.Button("やめる")) _achResetArmed = false;
        }
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(_achMessage)) GUILayout.Label(_achMessage);
    }
}
#endif
