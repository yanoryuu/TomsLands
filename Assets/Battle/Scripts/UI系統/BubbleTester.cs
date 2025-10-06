using UnityEngine;
using R3;

public class BubbleTester : MonoBehaviour
{
    private readonly string[] testMessages = 
    {
        "1つ目のメッセージ",
        "2つ目のメッセージ！",
        "3つ目のメッセージ？",
        "4つ目のメッセージ！！！",
    };

    private int messageIndex = 0;

    void Start()
    {
        // Managerのイベントを監視して、ログを出力する
        // これでイベントがちゃんと動いているか確認できる
        if (SpeechBubbleManager.Instance != null)
        {
            SpeechBubbleManager.Instance.OnBubbleShown.Subscribe(data =>
            {
                Debug.Log($"<color=cyan>EVENT LOG: 吹き出し『{data.bubble.name}』に「{data.message}」が表示されました。</color>");
            }).AddTo(this);
        }
        else
        {
            Debug.LogError("シーンに SpeechBubbleManager が見つかりません！");
        }
    }

    void Update()
    {
        // スペースキーが押されたら、次のメッセージを3秒間表示する
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (SpeechBubbleManager.Instance != null)
            {
                // 次のメッセージを取得
                string message = testMessages[messageIndex];
                
                Debug.Log($"<color=yellow>ACTION: スペースキーが押されました。メッセージ「{message}」を表示します。</color>");
                
                // Managerに吹き出しの表示をリクエスト
                SpeechBubbleManager.Instance.RequestBubble(message, 3f);
                
                // 次のメッセージの番号を更新（最後まで行ったら最初に戻る）
                messageIndex = (messageIndex + 1) % testMessages.Length;
            }
        }
    }
}