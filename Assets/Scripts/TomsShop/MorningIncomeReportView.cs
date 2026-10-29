using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 朝レポートの中身（今朝の入金の合計・出どころの行・総資産・その他の行）。
/// MorningReportPanel の Window に付ける。開閉（パネルの表示・OK ボタン・パッドのフォーカス）は TomsShopView が持ち、
/// ここは中身の差し替えと行の演出だけを行う。参照は未配線（null）でも動作する。
/// </summary>
public class MorningIncomeReportView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [Tooltip("今朝の入金の合計（金色・大きく）")]
    [SerializeField] private TextMeshProUGUI totalText;
    [Tooltip("総資産と昨日比")]
    [SerializeField] private TextMeshProUGUI netWorthText;
    [SerializeField] private Transform rowParent;
    [SerializeField] private MorningIncomeRowView rowPrefab;
    [Tooltip("入金以外の行（生成アイテム・取り立てなど）")]
    [SerializeField] private TextMeshProUGUI notesText;
    [Tooltip("出どころにアイコンが無いとき（売上・配当・債券）に出す金貨")]
    [SerializeField] private Sprite fallbackIcon;
    [Tooltip("行の上限。溢れた分は最後の1行「ほか n件」にまとめる")]
    [SerializeField] private int maxRows = 5;
    [Tooltip("行を1行ずつ弾ませるときの間隔（秒）")]
    [SerializeField] private float rowStagger = 0.05f;

    private const string IncomeTitle = "今朝の入金";
    private const string ReportTitle = "朝の報告";

    private readonly List<MorningIncomeRowView> rows = new();

    public bool IsWired => rowParent != null && rowPrefab != null;

    /// <summary>
    /// 中身を差し替える。incomes が空なら合計と行を隠して「朝の報告」として出す。
    /// </summary>
    public void Setup(IReadOnlyList<MorningIncomeEntry> incomes, string netWorthLine, IReadOnlyList<string> notes)
    {
        var shown = MorningReportModel.BuildRows(incomes, maxRows);
        bool hasIncome = shown.Count > 0;

        if (titleText != null) titleText.text = hasIncome ? IncomeTitle : ReportTitle;
        if (totalText != null)
        {
            totalText.gameObject.SetActive(hasIncome);
            if (hasIncome) totalText.text = $"+{MorningReportModel.SumAmount(shown):N0}G";
        }
        if (netWorthText != null)
        {
            netWorthText.gameObject.SetActive(!string.IsNullOrEmpty(netWorthLine));
            netWorthText.text = netWorthLine ?? string.Empty;
        }

        if (rowParent != null) rowParent.gameObject.SetActive(hasIncome);
        BuildRows(shown);

        if (notesText != null)
        {
            bool hasNotes = notes != null && notes.Count > 0;
            notesText.gameObject.SetActive(hasNotes);
            notesText.text = hasNotes ? string.Join("\n", notes) : string.Empty;
        }

        // 行数に合わせて枠の高さを詰める（Window の ContentSizeFitter）
        if (transform is RectTransform self) LayoutRebuilder.ForceRebuildLayoutImmediate(self);
    }

    /// <summary>開いた直後の演出: 合計を弾ませ、行を上から順に弾ませる（待ち時間は増やさない）。</summary>
    public void PlayOpen()
    {
        if (totalText != null && totalText.gameObject.activeInHierarchy)
            UIFx.Pop(totalText.transform, 0.8f, 0.25f);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null || !row.gameObject.activeSelf) continue;
            var t = row.transform;
            t.DOKill();
            t.localScale = new Vector3(1f, 0.6f, 1f);
            t.DOScale(1f, 0.18f).SetDelay(i * rowStagger).SetEase(Ease.OutBack).SetLink(row.gameObject);
        }
    }

    private void BuildRows(List<MorningIncomeEntry> shown)
    {
        if (!IsWired) return;
        while (rows.Count < shown.Count)
        {
            var row = Instantiate(rowPrefab, rowParent);
            row.name = $"{rowPrefab.name}_{rows.Count}";
            rows.Add(row);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] == null) continue;
            bool active = i < shown.Count;
            rows[i].gameObject.SetActive(active);
            if (active) rows[i].Setup(shown[i], fallbackIcon);
        }
    }
}
