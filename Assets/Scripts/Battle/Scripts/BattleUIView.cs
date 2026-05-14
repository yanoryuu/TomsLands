using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

/// <summary>
/// 戦闘中のUI要素の表示・更新を全て担当するクラス
/// </summary>
public class BattleUIView : MonoBehaviour
{
    [Header("テンポ設定")]
    [Tooltip("バトル全体のスピード倍率。大きいほど速い。（0.1〜5.0）")]
    [SerializeField, Range(0.1f, 5f)] private float battleSpeedMultiplier = 1f;
    [Tooltip("1メッセージあたりの表示待機秒数（battleSpeedMultiplierで割られる）")]
    [SerializeField] private float logDisplayDuration = 0.5f;

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
        => Mathf.Max(0, Mathf.RoundToInt(baseDelayMs / battleSpeedMultiplier));

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

        await UniTask.Delay(System.TimeSpan.FromSeconds(logDisplayDuration / battleSpeedMultiplier), cancellationToken: token);
    }
}
