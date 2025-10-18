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
    [SerializeField] private int maxLogLines = 16;

    // 元のLogManagerが持っていたメッセージキューを、このクラスが管理
    private readonly Queue<string> logMessages = new Queue<string>();

    /// <summary>
    /// バトルログに新しいメッセージを追加します
    /// </summary>
    public async UniTask AddLogAsync(string message, CancellationToken token)
    {
        logMessages.Enqueue(message);
        if (logMessages.Count > maxLogLines)
        {
            logMessages.Dequeue();
        }

        // UIを更新します
        logText.text = string.Join("\n", logMessages);

        // ログが流れるように少し待機します
        await UniTask.Delay(500, cancellationToken: token);
    }

}