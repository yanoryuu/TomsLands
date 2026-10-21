using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

/// <summary>
/// 配信リザルト画面（精算書。SUMMARY E-2〜E-4）。
/// 見出し（勝利！／配当確定／敗北...／配信終了）→ 内訳（売上・補充・返金・介入・防衛報酬）を1行ずつ →
/// 純利益をカウントアップ → 勇者の装備の「相場への影響」。確認ボタンで次へ進む（演出中に押すと最後まで飛ばす）。
/// パネルの表示/非表示は BattlePanelManager が管理する。新しい欄は未配線（null）でも動く。
/// </summary>
public class BattleResultView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI resultTitleText;
    [Tooltip("精算の内訳（1行ずつ出す）")]
    [SerializeField] private TextMeshProUGUI resultDetailText;
    [SerializeField] private Button confirmButton;

    [Header("精算書（未設定なら内訳の最後の行に純利益を出す）")]
    [SerializeField] private TextMeshProUGUI netProfitText;
    [Tooltip("「相場への影響」の欄（勇者の装備の値動き）。途中終了・装備なしなら隠す")]
    [SerializeField] private GameObject marketImpactRoot;
    [SerializeField] private TextMeshProUGUI marketImpactText;

    [Header("売れたアイテム一覧")]
    [SerializeField] private BattleResultSoldItemSlot soldItemSlotPrefab;
    [SerializeField] private Transform soldItemContainer;

    [Header("色（木と金縁のトーン。紙面は薄いベージュなので濃い色で書く）")]
    [SerializeField] private Color demonTitleColor = new(0.85f, 0.72f, 1f, 1f);
    [SerializeField] private string inkColor = "#5A3A24";
    [SerializeField] private string plusColor = "#2F6E2A";
    [SerializeField] private string minusColor = "#A8332B";
    [SerializeField] private string demonColor = "#6A2FA0";
    [SerializeField] private string upColor = "#C0661A";
    [SerializeField] private string downColor = "#4A6A8A";
    [SerializeField] private string zeroColor = "#A08670";

    private const float RowInterval = 0.16f;
    private const float CountUpDuration = 0.8f;

    private UniTaskCompletionSource _confirmTcs;
    private CancellationTokenSource _skipCts;
    private ItemModel _itemModel;
    private Color _titleBaseColor = Color.white;
    private Tween _countTween;

    [Inject]
    public void Construct(ItemModel itemModel) => _itemModel = itemModel;

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }
        if (resultTitleText != null) _titleBaseColor = resultTitleText.color;

        // パッド/キー: 確認ボタンへフォーカス（A で次へ）
        var scope = UIFocusScope.GetOrAdd(gameObject);
        if (confirmButton != null) scope.FirstSelected = confirmButton;
        foreach (var scroll in GetComponentsInChildren<ScrollRect>(true))
            if (!scroll.TryGetComponent<ScrollRectAutoFollow>(out _)) scroll.gameObject.AddComponent<ScrollRectAutoFollow>();
        // パネルの表示/非表示は BattlePanelManager が管理するため、ここでは制御しない
    }

    private void OnDestroy()
    {
        _countTween?.Kill();
        _skipCts?.Dispose();
        _skipCts = null;
    }

    /// <summary>
    /// 精算書を表示し、確認ボタンが押されるまで待機する。
    /// パネルの表示/非表示は BattlePanelManager が事前に行う。
    /// </summary>
    public async UniTask ShowResultAsync(BattleSettlement settlement)
    {
        if (settlement == null) return;
        _confirmTcs = new UniTaskCompletionSource();

        ApplyTitle(settlement);
        PopulateSoldItemSlots(settlement.SoldItems);

        var rows = BuildLedgerRows(settlement);
        string net = FormatNetLine(settlement.NetProfit, settlement.NetProfit);
        string market = BuildMarketImpactText(settlement);

        SetLedger(rows, 0, null);
        SetNet(null);
        SetMarket(null);

        _skipCts?.Dispose();
        _skipCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        try
        {
            await PlayIntroAsync(settlement, rows, market, _skipCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (destroyCancellationToken.IsCancellationRequested) throw;
        }
        finally
        {
            _skipCts?.Dispose();
            _skipCts = null;
        }

        // 最終状態（スキップ時もここ）
        _countTween?.Kill();
        SetLedger(rows, rows.Count, net);
        SetNet(net);
        SetMarket(market);

        await _confirmTcs.Task;
        _confirmTcs = null;
    }

    // =====================================================
    // 内容
    // =====================================================

    private void ApplyTitle(BattleSettlement s)
    {
        if (resultTitleText == null) return;
        resultTitleText.text = s.Result switch
        {
            BattleResult.Victory => "勝利！",
            BattleResult.Abort => "配信終了",
            // 魔王側に張って当たった。「防衛成功」は世界観に合わないので使わない（決定 Q30）
            _ when s.IsDemonPayout => "配当確定",
            _ => "敗北……",
        };
        resultTitleText.color = s.IsDemonPayout ? demonTitleColor : _titleBaseColor;
    }

    private List<string> BuildLedgerRows(BattleSettlement s)
    {
        // 行は勝ち負けに関係なく同じ5行で揃える（0 の行も薄く出す。毎回同じ位置で読めるように）
        var rows = new List<string>
        {
            Row("売上", s.Sales > 0 ? Plus(s.Sales) : Zero()),
            Row("補充", s.RestockCost > 0 ? Minus(s.RestockCost) : Zero()),
            Row("売れ残り返金", s.RestockRefund > 0 ? Plus(s.RestockRefund) : Zero()),
            Row("介入", s.InterventionNet > 0 ? Minus(s.InterventionNet) : Zero()),
            s.DefenseReward > 0
                ? Row($"<color={demonColor}>防衛報酬</color>", $"<color={demonColor}>+{s.DefenseReward:N0} G</color>")
                : Row("防衛報酬", Zero()),
        };
        // 村の広場 Lv5「勝負配信の宣言」をした配信だけ6行目を出す
        string declaration = DeclarationRow(s);
        if (declaration != null) rows.Add(declaration);
        return rows;
    }

    private string DeclarationRow(BattleSettlement s)
    {
        switch (s.DeclarationOutcome)
        {
            case StreamDeclarationOutcome.Hit:
                string col = s.DeclarationSide == StreamDeclarationSide.Demon ? demonColor : upColor;
                return Row($"<color={col}>宣言的中 ×{VillageEffects.DeclarationMultiplier:0.##}</color>",
                    $"<color={col}>+{s.DeclarationBonus:N0} G</color>");
            case StreamDeclarationOutcome.Miss:
                return Row("宣言 外れ", $"<color={minusColor}>フォロワー -{StreamDeclaration.MissFollowers:N0}</color>");
            case StreamDeclarationOutcome.Void:
            case StreamDeclarationOutcome.Returned:
                return Row("宣言", $"<color={zeroColor}>無効</color>");
            default:
                return null;
        }
    }

    private string Zero() => $"<color={zeroColor}>0 G</color>";

    /// <summary>左に項目、右に金額の1行（TMP の行の高さ0のテクニックで左右に揃える）。</summary>
    private string Row(string label, string value)
        => $"<align=left><color={inkColor}>{label}</color><line-height=0>\n<align=right>{value}<line-height=1em>\n";

    private string Plus(int v) => $"<color={plusColor}>+{v:N0} G</color>";
    private string Minus(int v) => $"<color={minusColor}>-{v:N0} G</color>";

    private static string FormatNetLine(int shown, int final)
    {
        string sign = shown >= 0 ? "+" : "-";
        string body = $"{sign}{Mathf.Abs(shown):N0}<size=60%> G</size>";
        return final >= 0 ? body : $"<color=#FF8A80>{body}</color>";
    }

    private string BuildMarketImpactText(BattleSettlement s)
    {
        if (s.MarketImpacts == null || s.MarketImpacts.Count == 0) return null;
        var sb = new StringBuilder();
        sb.Append($"<color={inkColor}><size=85%>相場への影響</size></color>\n");
        foreach (var m in s.MarketImpacts)
        {
            bool up = m.PriceAfter > m.PriceBefore;
            bool down = m.PriceAfter < m.PriceBefore;
            string col = up ? upColor : down ? downColor : inkColor;
            string arrow = up ? "↑" : down ? "↓" : "→";
            sb.Append($"<color={inkColor}>{m.ItemName}</color>  ");
            sb.Append($"<color={inkColor}>{m.PriceBefore:N0}G</color> → <color={col}>{m.PriceAfter:N0}G {arrow}</color>");
            if (m.Stock > 0)
            {
                sb.Append($"  <color={inkColor}><size=85%>在庫{m.Stock}</size></color>");
                // 含み損益は取得単価が分かるときだけ（取得単価から見た損益。値段の差ではない）
                if (m.StockGain.HasValue)
                {
                    int gain = m.StockGain.Value;
                    string gainCol = gain >= 0 ? upColor : downColor;
                    string gainText = gain >= 0 ? $"+{gain:N0}G" : $"-{-gain:N0}G";
                    sb.Append($" <color={gainCol}><size=85%>{gainText}</size></color>");
                }
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    private void SetLedger(List<string> rows, int visibleRows, string netWhenNoField)
    {
        if (resultDetailText == null) return;
        var sb = new StringBuilder();
        for (int i = 0; i < visibleRows && i < rows.Count; i++) sb.Append(rows[i]);
        // 純利益の欄が無い（未配線）ときは内訳の最後に純利益を足す
        if (netProfitText == null && !string.IsNullOrEmpty(netWhenNoField))
            sb.Append(Row("純利益", netWhenNoField));
        resultDetailText.text = sb.ToString();
    }

    private void SetNet(string text)
    {
        if (netProfitText == null) return;
        netProfitText.gameObject.SetActive(text != null);
        if (text != null) netProfitText.text = $"<size=50%>純利益  </size>{text}";
    }

    private void SetMarket(string text)
    {
        if (marketImpactRoot != null) marketImpactRoot.SetActive(text != null);
        if (marketImpactText != null) marketImpactText.text = text ?? string.Empty;
    }

    // =====================================================
    // 演出（DemonKingPayoutView と同じやり方: 行が順に出る → カウントアップ → ポン）
    // =====================================================

    private async UniTask PlayIntroAsync(BattleSettlement s, List<string> rows, string market, CancellationToken ct)
    {
        if (resultTitleText != null) UIFx.Pop(resultTitleText.transform, 0.7f, 0.25f);
        await DelaySeconds(0.25f, ct);

        for (int i = 1; i <= rows.Count; i++)
        {
            SetLedger(rows, i, null);
            await DelaySeconds(RowInterval, ct);
        }

        // 純利益のカウントアップ（大儲けの数字を溜めてから見せる）
        int shown = 0;
        SetNet(FormatNetLine(0, s.NetProfit));
        if (netProfitText != null) UIFx.Pop(netProfitText.transform, 0.85f, 0.18f);
        float duration = CountUpDuration * GameSettings.EffectDurationScale;
        _countTween?.Kill();
        _countTween = DOTween.To(() => shown, v => { shown = v; SetNet(FormatNetLine(v, s.NetProfit)); }, s.NetProfit, duration)
            .SetEase(Ease.OutCubic)
            .SetLink(gameObject);
        await DelaySeconds(CountUpDuration + 0.05f, ct);
        if (netProfitText != null)
            netProfitText.transform.DOPunchScale(Vector3.one * 0.12f, 0.3f, 6, 0.6f).SetLink(netProfitText.gameObject);

        if (market != null)
        {
            await DelaySeconds(0.15f, ct);
            SetMarket(market);
            if (marketImpactRoot != null) UIFx.Pop(marketImpactRoot.transform, 0.92f, 0.2f);
        }
    }

    /// <summary>演出の待ち。設定「演出を速くする」で半分になる。</summary>
    private static UniTask DelaySeconds(float seconds, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(seconds * GameSettings.EffectDurationScale), ignoreTimeScale: true, cancellationToken: ct);

    private void PopulateSoldItemSlots(IReadOnlyList<BattleOutputSoldItem> soldItems)
    {
        if (soldItemSlotPrefab == null || soldItemContainer == null) return;

        foreach (Transform child in soldItemContainer)
            Destroy(child.gameObject);

        if (soldItems == null) return;

        foreach (var soldItem in soldItems)
        {
            if (soldItem.SoldQuantity <= 0) continue;

            var runtimeItem = _itemModel?.GetRuntimeItem(soldItem.ItemId);
            if (runtimeItem == null) continue;

            var slot = Instantiate(soldItemSlotPrefab, soldItemContainer);
            slot.Setup(runtimeItem.ItemIcon, soldItem.SoldQuantity, soldItem.Revenue);
        }
    }

    /// <summary>演出中なら最後まで飛ばす。演出が終わっていれば次へ進む。</summary>
    private void OnConfirmClicked()
    {
        if (_skipCts != null && !_skipCts.IsCancellationRequested)
        {
            _skipCts.Cancel();
            return;
        }
        _confirmTcs?.TrySetResult();
    }
}
