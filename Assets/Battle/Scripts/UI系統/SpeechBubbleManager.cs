using UnityEngine;
using System.Collections.Generic;
using R3;
using System;

public class SpeechBubbleManager : MonoBehaviour
{
    public static SpeechBubbleManager Instance { get; private set; }

    [SerializeField]
    [Tooltip("シーンに配置した2つの吹き出しオブジェクトを設定してください")]
    private List<SpeechBubble> speechBubbles;

    public Subject<(SpeechBubble bubble, string message)> OnBubbleShown { get; private set; } = new();

    private int nextBubbleIndex = 0; // 次に使う吹き出しの番号

    private void Awake()
    {
        if (Instance == null) { Instance = this; } else { Destroy(gameObject); }

        if (speechBubbles.Count == 0)
        {
            Debug.LogError("吹き出しが設定されていません！");
        }
    }

    /// <summary>
    /// 吹き出しの表示をリクエストします。
    /// </summary>
    public void RequestBubble(string message, float duration)
    {
        if (speechBubbles.Count == 0) return;

        // 次に使う吹き出しを取得
        SpeechBubble bubbleToUse = speechBubbles[nextBubbleIndex];

        // 実際に表示する直前に、イベントを発行して外部に通知する
        OnBubbleShown.OnNext((bubbleToUse, message));

        // 吹き出しに表示を命令
        bubbleToUse.Show(message, duration);

        // 次に使う番号を更新（0 → 1 → 0 → 1...と循環させる）
        nextBubbleIndex = (nextBubbleIndex + 1) % speechBubbles.Count;
    }
}