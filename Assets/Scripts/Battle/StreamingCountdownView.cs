using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// 配信開始前のカウントダウン演出（3 → 2 → 1 → 配信開始！）。
/// 品出し確定後、戦闘・販売ループが動き出す前に BattleSceneStarter から再生される。
/// </summary>
public class StreamingCountdownView : MonoBehaviour
{
    [Tooltip("ディム背景を含むカウントダウン全体のルート")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI countText;
    [Tooltip("「配信開始！」表示の長さ（秒）")]
    [SerializeField] private float startLabelDuration = 0.6f;

    /// <summary>
    /// カウントダウンを再生する。完了（または panel 未配線）で戻る。
    /// </summary>
    public async UniTask PlayAsync(int seconds, CancellationToken token)
    {
        if (panel == null || countText == null) return;

        panel.SetActive(true);
        try
        {
            for (int i = seconds; i >= 1; i--)
            {
                PopText(i.ToString());
                await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
            }

            PopText("配信開始！");
            await UniTask.Delay(TimeSpan.FromSeconds(startLabelDuration), cancellationToken: token);
        }
        finally
        {
            countText.transform.DOKill();
            panel.SetActive(false);
        }
    }

    private void PopText(string text)
    {
        countText.text = text;
        countText.transform.DOKill();
        countText.transform.localScale = Vector3.one * 1.6f;
        countText.transform.DOScale(1f, 0.3f).SetEase(Ease.OutCubic).SetLink(countText.gameObject);
    }
}
