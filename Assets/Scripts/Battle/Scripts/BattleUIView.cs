using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

/// <summary>
/// 戦闘中のUI要素の表示・更新を全て担当するクラス
/// </summary>
public class BattleUIView : MonoBehaviour
{
    [Header("テンポ設定（通常は GameConst.battleTempo を使う）")]
    [Tooltip("ON のときだけ下の2値で GameConst.battleTempo の speedMultiplier / logSeconds を上書きする（デバッグ用）")]
    [SerializeField] private bool overrideTempoInInspector = false;
    [Tooltip("バトル全体のスピード倍率。大きいほど速い。（0.1〜5.0）")]
    [SerializeField, Range(0.1f, 5f)] private float battleSpeedMultiplier = 1f;
    [Tooltip("1メッセージあたりの表示待機秒数（battleSpeedMultiplierで割られる）")]
    [SerializeField] private float logDisplayDuration = 0.5f;

    /// <summary>現在のテンポ設定（GameConst.battleTempo。取得できなければ既定値）。</summary>
    public BattleTempoData Tempo => GameConst.Data?.battleTempo ?? FallbackTempo;
    private static readonly BattleTempoData FallbackTempo = new BattleTempoData();

    private float SpeedMultiplier
        => Mathf.Max(0.1f, overrideTempoInInspector ? battleSpeedMultiplier : Tempo.speedMultiplier);

    private float LogSeconds
        => Mathf.Max(0f, overrideTempoInInspector ? logDisplayDuration : Tempo.logSeconds);

    // ========== バトルの履歴表示機能（コメントアウト） ==========
    // [Header("ログ表示関連")]
    // [SerializeField] private TMP_Text logText;
    // [SerializeField] private int maxLogLines = 8;
    // private readonly List<string> logLines = new List<string>();
    // ===========================================================

    [Header("背景")]
    [SerializeField] private SpriteRenderer backgroundRenderer;
    private Sprite defaultBackgroundSprite;

    private void Awake()
    {
        if (backgroundRenderer != null)
        {
            defaultBackgroundSprite = backgroundRenderer.sprite;
        }
    }

    /// <summary>
    /// ダンジョンの背景画像を設定する。null の場合は元の背景をそのまま維持する。
    /// </summary>
    public void SetBackground(Sprite dungeonImage)
    {
        if (backgroundRenderer == null)
        {
            Debug.LogWarning("[BattleUIView] backgroundRenderer が Inspector で未設定です。FightScene の BattleUIView に SpriteRenderer を設定してください。");
            return;
        }
        var sprite = dungeonImage != null ? dungeonImage : defaultBackgroundSprite;
        if (sprite != null)
        {
            backgroundRenderer.sprite = sprite;
        }

        backgroundRenderer.enabled = true;
        var color = backgroundRenderer.color;
        color.a = 1f;
        backgroundRenderer.color = color;
    }

    /// <summary>
    /// 指定したミリ秒を battleSpeedMultiplier でスケーリングした遅延ミリ秒を返す。
    /// </summary>
    public int GetSpeedScaledDelay(int baseDelayMs)
        => Mathf.Max(0, Mathf.RoundToInt(baseDelayMs / SpeedMultiplier));

    /// <summary>秒数をスピード倍率でスケーリングして返す。</summary>
    public float ScaleSeconds(float seconds) => Mathf.Max(0f, seconds) / SpeedMultiplier;

    /// <summary>
    /// スピード倍率でスケーリングした秒数だけ待つ。pauseController を渡すと、待機後にポーズ解除まで待つ。
    /// </summary>
    public async UniTask WaitScaledAsync(float seconds, CancellationToken token, BattlePauseController pauseController = null)
    {
        float scaled = ScaleSeconds(seconds);
        if (scaled > 0f)
            await UniTask.Delay(System.TimeSpan.FromSeconds(scaled), cancellationToken: token);
        if (pauseController != null)
            await pauseController.WaitIfPausedAsync(token);
    }

    public async UniTask AddLogAsync(string message, CancellationToken token)
    {
        // ========== バトルの履歴表示機能（コメントアウト） ==========
        // logLines.Add(message);
        // logText.text = string.Join("\n", logLines);
        // logText.ForceMeshUpdate();
        // while (logText.textInfo.lineCount > maxLogLines && logLines.Count > 0)
        // {
        //     logLines.RemoveAt(0);
        //     logText.text = string.Join("\n", logLines);
        //     logText.ForceMeshUpdate();
        // }
        // ===========================================================

        await UniTask.Delay(System.TimeSpan.FromSeconds(LogSeconds / SpeedMultiplier), cancellationToken: token);
    }
}
