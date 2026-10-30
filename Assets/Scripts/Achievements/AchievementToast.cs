using System;
using R3;
using UnityEngine;

/// <summary>
/// #9 実績を解除したら、画面上部にお知らせ（トースト）を出す。どのシーンでも出るよう、起動時に 1 度だけ
/// AchievementService.OnUnlocked を購読する（シーンには何も置かない。購読はアプリの終わりまで）。
/// ・名前・説明は AchievementDefinitions（ゲーム内の写し）から引く。
/// ・体験版（DemoBuild）は実績の仕組みが無いので購読しない（Service も作らない）。
/// ・省略モード（AutoPlay・人格マラソン）では Toast が何も出さないので、自動テストで解除されても出ない。
/// ・Steam につながっていれば Steam のオーバーレイも別に出る（こちらはゲーム内の見た目に合わせた知らせ）。
/// </summary>
public static class AchievementToast
{
    private const float Seconds = 3.5f;

    private static IDisposable subscription;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        subscription?.Dispose();
        subscription = null;
    }

    // Boot の LifetimeScope（AchievementService.Shared を先に作る）より後に購読する
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (DemoBuild.IsDemo || subscription != null) return;
        try
        {
            subscription = AchievementService.Shared.OnUnlocked.Subscribe(Show);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private static void Show(AchievementId id)
    {
        try
        {
            var def = AchievementDefinitions.Get(id);
            if (def == null) return;
            string text = string.IsNullOrEmpty(def.Description)
                ? $"実績解除「{def.DisplayName}」"
                : $"実績解除「{def.DisplayName}」\n<size=70%>{def.Description}</size>";
            Toast.Show(text, UIMotionFx.SealSprite, ToastKind.Good, Seconds); // 紋章（祝いと同じ絵）を添える
        }
        catch (Exception e)
        {
            // 知らせが出せなくても解除そのものは済んでいる
            Debug.LogException(e);
        }
    }
}
