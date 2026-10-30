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
    [Tooltip("熱量バー本体。Sliced なら下端 pivot で高さを伸縮（端の角丸が潰れない）、Filled なら fillAmount で塗る")]
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
    private float _fillMaxHeight;
    private Tween _fillTween;

    // #10 ゲージの残像。Filled なら共通部品 TrailGauge、Sliced（下端 pivot で高さを伸縮）は TrailGauge が
    // 横方向（anchorMax.x）しか扱えないので、同じ見た目（減る: 白い残像が遅れて追う / 増える: 先触れが先に伸びる）をここで作る
    private TrailGauge _filledTrail;
    private Image _heightTrail;
    private Image _heightLead;
    private Sequence _heightTrailSeq;
    private bool _trailReady;
    private static readonly Color HeightTrailColor = new(1f, 1f, 1f, 0.9f);

    private void Awake()
    {
        if (heatBarFill != null) _fillMaxHeight = heatBarFill.rectTransform.sizeDelta.y;

        // 初回 UpdateHeat が来るまでプレースホルダー（New Text）が見えないように空にしておく
        if (tierLabelText != null) tierLabelText.text = string.Empty;
        if (heatValueText != null) heatValueText.text = string.Empty;
        if (priceDirectionText != null) priceDirectionText.text = string.Empty;
    }

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
            if (heatBarFill.type == Image.Type.Filled)
            {
                // 値は TrailGauge.SetValue で（初回は即座に）
                if (_filledTrail == null) _filledTrail = TrailGauge.Attach(heatBarFill);
                if (_filledTrail != null) _filledTrail.SetValue(normalized, animate: _trailReady);
                else heatBarFill.DOFillAmount(normalized, fillDuration).SetEase(Ease.OutQuad).SetLink(heatBarFill.gameObject);
            }
            else
            {
                var rt = heatBarFill.rectTransform;
                PlayHeightTrail(rt.sizeDelta.y, _fillMaxHeight * Mathf.Clamp01(normalized), tierColor);
                _fillTween?.Kill();
                _fillTween = rt.DOSizeDelta(new Vector2(rt.sizeDelta.x, _fillMaxHeight * Mathf.Clamp01(normalized)), fillDuration)
                    .SetEase(Ease.OutQuad)
                    .OnUpdate(() => heatBarFill.enabled = rt.sizeDelta.y > 1f)
                    .SetLink(heatBarFill.gameObject);
            }
            heatBarFill.DOColor(tierColor, fillDuration).SetLink(heatBarFill.gameObject);
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
                // 熱の段の倍率は値段に 1 回だけ乗る（段が変わったら差だけ掛け直す。毎ターン積み上がらない）ので「/ターン」は付けない
                priceDirectionText.text  = $"▲ 値段 +{Mathf.RoundToInt((priceMultiplier - 1f) * 100f)}%";
                priceDirectionText.color = new Color(0.25f, 0.9f, 0.35f);
            }
            else if (priceMultiplier < 1f)
            {
                priceDirectionText.text  = $"▼ 値段 {Mathf.RoundToInt((priceMultiplier - 1f) * 100f)}%";
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
        _trailReady = true;
    }

    /// <summary>Sliced の熱バーの残像・先触れ（高さ from → to）。初回・省略モード・非表示中は即座に揃える。</summary>
    private void PlayHeightTrail(float from, float to, Color tierColor)
    {
        if (!EnsureHeightTrail()) return;
        _heightTrailSeq?.Kill();
        _heightTrailSeq = null;
        var trailRt = _heightTrail.rectTransform;
        var leadRt = _heightLead.rectTransform;
        _heightLead.color = Color.Lerp(tierColor, Color.white, 0.45f);

        if (!_trailReady || UIMotion.IsSkip || !isActiveAndEnabled || Mathf.Approximately(from, to))
        {
            SetHeight(trailRt, _heightTrail, to);
            SetHeight(leadRt, _heightLead, to);
            return;
        }

        var seq = DOTween.Sequence().SetTarget(this);
        if (to < from)
        {
            // 下がった: 本体が先に縮み、白い残像が 0.4 秒待ってから 0.5 秒で追う
            SetHeight(leadRt, _heightLead, to);
            SetHeight(trailRt, _heightTrail, Mathf.Max(trailRt.sizeDelta.y, from));
            float start = trailRt.sizeDelta.y;
            seq.Insert(0.4f, DOVirtual.Float(start, to, 0.5f, h => SetHeight(trailRt, _heightTrail, h)).SetEase(Ease.InOutQuad));
        }
        else
        {
            // 上がった: 先触れが 0.1 秒で先に伸び、本体（fillDuration）が追いつく。残像は本体の後ろに隠す
            SetHeight(trailRt, _heightTrail, from);
            SetHeight(leadRt, _heightLead, from);
            seq.Insert(0f, DOVirtual.Float(from, to, 0.1f, h => SetHeight(leadRt, _heightLead, h)).SetEase(Ease.OutQuad));
            seq.InsertCallback(Mathf.Max(0.1f, fillDuration), () => SetHeight(trailRt, _heightTrail, to));
        }
        seq.OnKill(() =>
        {
            if (_heightTrail == null) return;
            SetHeight(trailRt, _heightTrail, to);
            SetHeight(leadRt, _heightLead, to);
        });
        _heightTrailSeq = UIMotion.Run(seq, gameObject);
    }

    private bool EnsureHeightTrail()
    {
        if (_heightTrail != null && _heightLead != null) return true;
        if (heatBarFill == null || heatBarFill.transform.parent == null) return false;
        _heightTrail = CreateHeightCopy("HeatTrail", HeightTrailColor);
        _heightLead = CreateHeightCopy("HeatLead", Color.white);
        return true;
    }

    /// <summary>熱バーと同じ形の Image を熱バーの直前の兄弟（後ろ）に作る。入力は受けない・レイアウトに入れない。</summary>
    private Image CreateHeightCopy(string name, Color color)
    {
        var src = heatBarFill.rectTransform;
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.layer = heatBarFill.gameObject.layer;
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)go.transform;
        rt.SetParent(src.parent, false);
        rt.SetSiblingIndex(src.GetSiblingIndex());
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.anchoredPosition = src.anchoredPosition;
        rt.sizeDelta = src.sizeDelta;
        rt.localScale = src.localScale;
        rt.localRotation = src.localRotation;
        var img = go.GetComponent<Image>();
        img.sprite = heatBarFill.sprite;
        img.type = heatBarFill.type;
        img.pixelsPerUnitMultiplier = heatBarFill.pixelsPerUnitMultiplier;
        img.color = color;
        img.raycastTarget = false;
        SetHeight(rt, img, src.sizeDelta.y);
        return img;
    }

    private static void SetHeight(RectTransform rt, Image img, float h)
    {
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
        img.enabled = h > 1f; // 熱バー本体と同じ（角丸が潰れる短さは隠す）
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
                    .DOScale(1f, 0.22f).SetEase(Ease.OutBounce).SetLink(heatBarFill.gameObject))
            .SetLink(heatBarFill.gameObject);

        // ラベルもフラッシュ
        if (tierLabelText != null)
        {
            tierLabelText.DOKill();
            tierLabelText.DOColor(Color.white, 0.1f)
                .OnComplete(() => tierLabelText.DOColor(color, 0.3f).SetLink(tierLabelText.gameObject))
                .SetLink(tierLabelText.gameObject);
        }
    }
}
