using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 村シーンの初回チュートリアル会話（占い師に村づくりを教わる）の進行役。
/// 前半（Tutorial_Village）は暗幕で村を隠した「まだ村ではない」状態で再生し、
/// 暗幕をフェードアウトして村を見せ、（あれば）帰還の到着演出（今回の儲け→村資金）を流してから
/// 後半（Tutorial_VillageBuild。看板を調べて投資する説明）を再生する。
/// 会話中はプレイヤー移動（PlayerInput/PlayerMove）と村UIのクリックを止め、終わったら戻す。
/// 既読は TutorialScenarioService（MetaProgressModel）が管理するので、途中で抜けても未読分だけ再生される。
/// ScenarioPlayer やラベルが無い（ブック未インポート等）場合は何もせずに終わる。
/// </summary>
public class VillageTutorialDirector
{
    // シナリオラベル（Assets/TomsScenario/Scenarios/VillageTutorial.csv と一致させること）
    public const string IntroLabel = "Tutorial_Village";
    public const string BuildLabel = "Tutorial_VillageBuild";

    private const float RevealDuration = 0.9f;
    private static readonly Color CurtainColor = new(0.06f, 0.05f, 0.05f, 1f);

    private readonly TutorialScenarioService scenario;

    private readonly List<PlayerInput> lockedInputs = new();
    private readonly List<PlayerMove> lockedMovers = new();
    private GameObject curtain;

    public VillageTutorialDirector(TutorialScenarioService scenario)
    {
        this.scenario = scenario;
    }

    /// <summary>未読の村チュートリアルが残っているか。</summary>
    public bool ShouldPlay =>
        scenario != null && (!scenario.HasSeen(IntroLabel) || !scenario.HasSeen(BuildLabel));

    /// <summary>
    /// 未読の会話を再生する。終了（またはキャンセル）時には必ず入力ロックと暗幕を解除する。
    /// </summary>
    /// <param name="uiCamera">村の Base カメラ（暗幕を宴の立ち絵より下に描くため）。null なら Camera.main</param>
    /// <param name="setUiLocked">村UIのクリック可否を切り替える（true=ロック）。null 可</param>
    /// <param name="betweenParts">前半と後半のあいだに流す演出（帰還の到着演出）。null 可</param>
    public async UniTask PlayAsync(Camera uiCamera, Action<bool> setUiLocked, CancellationToken ct,
        Func<CancellationToken, UniTask> betweenParts = null)
    {
        if (!ShouldPlay) return;

        bool needIntro = !scenario.HasSeen(IntroLabel);
        LockInput();
        setUiLocked?.Invoke(true);
        try
        {
            if (needIntro)
            {
                // 会話前半は「まだ村ではない」演出として村を暗幕で隠す
                ShowCurtain(uiCamera);

                // ブック未インポート等でラベルが無いときは暗幕だけ出して終わらないよう即座に抜ける
                if (!await HasLabelAsync(IntroLabel, ct)) return;

                await scenario.PlayOnceAsync(IntroLabel, ct);

                // ▼背景切り替え（トコの村へ）: 暗幕を開けて村を見せる
                await RevealAsync(ct);
            }

            // 儲けが村資金になる見せ場を、投資の説明より先に出す（「さっきのお金で建てよう」につなぐ）
            if (betweenParts != null) await betweenParts(ct);

            await scenario.PlayOnceAsync(BuildLabel, ct);

            // 会話を閉じた決定キー（Space/Enter）が区画の「調べる」に漏れないよう少し待ってから戻す
            await UniTask.DelayFrame(2, cancellationToken: ct);
        }
        finally
        {
            DestroyCurtain();
            setUiLocked?.Invoke(false);
            UnlockInput();
        }
    }

    private static async UniTask<bool> HasLabelAsync(string label, CancellationToken ct)
    {
        var player = UnityEngine.Object.FindFirstObjectByType<ScenarioPlayer>();
        if (player == null) return false;
        await player.WaitUntilReadyAsync(ct);
        if (player.HasLabel(label)) return true;
        Debug.LogWarning($"[VillageTutorialDirector] ラベル '{label}' が見つかりません。VillageTutorial.csv を宴にインポートしてください。");
        return false;
    }

    // ───────────────────────── 暗幕 ─────────────────────────

    /// <summary>
    /// 村全体を覆う暗幕を出す。宴の立ち絵（宴カメラ＝Base カメラのスタック上の Overlay カメラ）より
    /// 下に描きたいので、Overlay キャンバスではなく村の Base カメラの Screen Space - Camera キャンバスにする。
    /// </summary>
    private void ShowCurtain(Camera uiCamera)
    {
        DestroyCurtain();

        var cam = uiCamera != null ? uiCamera : Camera.main;
        curtain = new GameObject("VillageTutorialCurtain", typeof(RectTransform));
        var canvas = curtain.AddComponent<Canvas>();
        if (cam != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = Mathf.Max(cam.nearClipPlane + 0.01f, 0.5f);
            // 村のどのスプライト/キャンバスよりも手前（最前面のソーティングレイヤー・最大オーダー）
            var layers = SortingLayer.layers;
            if (layers != null && layers.Length > 0) canvas.sortingLayerID = layers[layers.Length - 1].id;
            canvas.sortingOrder = 32000;
        }
        else
        {
            // カメラが見つからない場合の保険（立ち絵は隠れるがセリフ枠は宴の UI キャンバス(2000)が上に出る）
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1990;
        }
        curtain.AddComponent<CanvasScaler>();
        curtain.AddComponent<GraphicRaycaster>(); // 暗幕越しに村UIを押せないように

        var image = curtain.AddComponent<Image>();
        image.color = CurtainColor;
        image.raycastTarget = true;
    }

    private async UniTask RevealAsync(CancellationToken ct)
    {
        if (curtain == null) return;
        var image = curtain.GetComponent<Image>();
        if (image == null) return;

        var tween = image.DOFade(0f, RevealDuration)
            .SetEase(Ease.InOutSine)
            .SetLink(curtain);
        // UniTask の DOTween 連携（UNITASK_DOTWEEN_SUPPORT）は未導入のため、完了をポーリングで待つ
        await UniTask.WaitWhile(() => tween.IsActive() && !tween.IsComplete(), cancellationToken: ct);
        DestroyCurtain();
    }

    private void DestroyCurtain()
    {
        if (curtain == null) return;
        var image = curtain.GetComponent<Image>();
        if (image != null) image.DOKill();
        UnityEngine.Object.Destroy(curtain);
        curtain = null;
    }

    // ───────────────────────── 入力ロック ─────────────────────────

    private void LockInput()
    {
        lockedInputs.Clear();
        foreach (var pi in PlayerInput.all)
        {
            if (pi == null || !pi.inputIsActive) continue;
            pi.DeactivateInput(); // 進行中の移動入力もキャンセルされる
            lockedInputs.Add(pi);
        }

        lockedMovers.Clear();
        foreach (var mover in UnityEngine.Object.FindObjectsByType<PlayerMove>(FindObjectsSortMode.None))
        {
            if (mover == null || !mover.enabled) continue;
            mover.enabled = false;
            lockedMovers.Add(mover);
        }
    }

    private void UnlockInput()
    {
        foreach (var pi in lockedInputs)
            if (pi != null) pi.ActivateInput();
        lockedInputs.Clear();

        foreach (var mover in lockedMovers)
            if (mover != null) mover.enabled = true;
        lockedMovers.Clear();
    }
}
