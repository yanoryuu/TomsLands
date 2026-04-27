using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 配信熱バーの UI 表示を担当する MonoBehaviour。
/// Heat 値に応じてバー長・カラー・ラベルをアニメーション付きで更新する。
/// </summary>
public class StreamingHeatView : MonoBehaviour
{
    [Header("UI 参照")]
    [Tooltip("熱量バー本体（Image.Type = Filled / Fill Method = Horizontal）")]
    [SerializeField] private Image heatBarFill;
    [Tooltip("ティア名テキスト（例: 盛り上がり中！）")]
    [SerializeField] private TMP_Text tierLabelText;
    [Tooltip("Heat 数値テキスト（省略可）")]
    [SerializeField] private TMP_Text heatValueText;
    [Tooltip("価格変動の方向を示すテキスト（↑↓−）省略可")]
    [SerializeField] private TMP_Text priceDirectionText;

    [Header("アニメーション")]
    [Tooltip("バーが伸縮するアニメーション時間（秒）")]
    [SerializeField] private float fillDuration = 0.35f;
    [Tooltip("熱が大きく上昇した時にパルス演出を出す閾値")]
    [SerializeField] private float pulseThreshold = 15f;

    private float _prevHeat = 30f;

    // ─────────────────────────────────────────
    //  公開 API
    // ─────────────────────────────────────────

    /// <summary>
    /// 毎ターンまたは Heat 変化時に呼ぶ。
    /// StreamingSalesController の Heat 購読から呼び出される。
    /// </summary>
    public void UpdateHeat(float heat, string tierLabel, Color tierColor, float priceMultiplier)
    {
        float normalized = heat / StreamingHeatModel.MaxHeat;

        // バー量（アニメーション）
        if (heatBarFill != null)
        {
            heatBarFill.DOKill();
            heatBarFill.DOFillAmount(normalized, fillDuration).SetEase(Ease.OutQuad);
            heatBarFill.DOColor(tierColor, fillDuration);
        }

        // ティアラベル
        if (tierLabelText != null)
            tierLabelText.text = tierLabel;

        // 数値
        if (heatValueText != null)
            heatValueText.text = Mathf.RoundToInt(heat).ToString();

        // 価格方向テキスト
        if (priceDirectionText != null)
        {
            if (priceMultiplier > 1f)
            {
                priceDirectionText.text  = $"▲ +{Mathf.RoundToInt((priceMultiplier - 1f) * 100f)}%/ターン";
                priceDirectionText.color = new Color(0.25f, 0.9f, 0.35f);
            }
            else if (priceMultiplier < 1f)
            {
                priceDirectionText.text  = $"▼ {Mathf.RoundToInt((priceMultiplier - 1f) * 100f)}%/ターン";
                priceDirectionText.color = new Color(0.9f, 0.25f, 0.25f);
            }
            else
            {
                priceDirectionText.text  = "− 変化なし";
                priceDirectionText.color = Color.gray;
            }
        }

        // 熱が大きく上昇した時にパルス
        if (heat - _prevHeat >= pulseThreshold)
            PlayPulse(tierColor);

        _prevHeat = heat;
    }

    // ─────────────────────────────────────────
    //  内部演出
    // ─────────────────────────────────────────

    private void PlayPulse(Color color)
    {
        if (heatBarFill == null) return;

        heatBarFill.transform.DOKill();
        heatBarFill.transform.localScale = Vector3.one;

        // 一瞬膨らんでバウンス
        heatBarFill.transform
            .DOScale(1.06f, 0.10f).SetEase(Ease.OutQuad)
            .OnComplete(() =>
                heatBarFill.transform
                    .DOScale(1f, 0.22f).SetEase(Ease.OutBounce));

        // ラベルもフラッシュ
        if (tierLabelText != null)
        {
            tierLabelText.DOKill();
            tierLabelText.DOColor(Color.white, 0.1f)
                .OnComplete(() => tierLabelText.DOColor(color, 0.3f));
        }
    }
}
