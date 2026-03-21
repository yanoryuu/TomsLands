using UnityEngine;
using TMPro;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;

/// <summary>
/// 戦闘中のUI要素の表示・更新を全て担当するクラス
/// </summary>
public class BattleUIView : MonoBehaviour
{
    [Header("ログ表示関連")]
    [SerializeField] private TMP_Text logText;
    [SerializeField] private int maxLogLines = 8;
    [SerializeField] private float logDisplayDuration = 0.5f;

    [Header("背景")]
    [SerializeField] private SpriteRenderer backgroundRenderer;

    // 各要素は "1メッセージ（改行を含む）" を単位として保持
    private readonly List<string> logLines = new List<string>();

    /// <summary>
    /// ダンジョンの背景画像を設定する。null の場合は非表示にする。
    /// </summary>
    public void SetBackground(Sprite dungeonImage)
    {
        if (backgroundRenderer == null)
        {
            Debug.LogWarning("[BattleUIView] backgroundRenderer が Inspector で未設定です。FightScene の BattleUIView に SpriteRenderer を設定してください。");
            return;
        }

        if (dungeonImage != null)
        {
            backgroundRenderer.sprite = dungeonImage;
        }
        // dungeonImage が null の場合は元の背景をそのまま維持する
    }


    public async UniTask AddLogAsync(string message, CancellationToken token)
    {
        logLines.Add(message);

        // テキストを更新してレンダリング情報を強制更新
        logText.text = string.Join("\n", logLines);
        // ForceMeshUpdate を呼ばないと textInfo が古いままなので必ず呼ぶ
        logText.ForceMeshUpdate();

        while (logText.textInfo.lineCount > maxLogLines && logLines.Count > 0)
        {
            // 古いメッセージを削除して再描画→再評価
            logLines.RemoveAt(0);
            logText.text = string.Join("\n", logLines);
            logText.ForceMeshUpdate();
        }

        // 4) ログが流れるように少し待機（キャンセル対応）
        await UniTask.Delay(System.TimeSpan.FromSeconds(logDisplayDuration), cancellationToken: token);
    }
}
