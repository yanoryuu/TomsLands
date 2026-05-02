using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ターン終了時のサマリーパネルView。
/// 売れた数・売上・アイテムトレンド・次の配信までの日数を表示する。
/// パネルの表示/非表示は GamePanelManager が管理する。
/// </summary>
public class TurnEndSummaryView : MonoBehaviour
{
    [Header("Content")]
    [SerializeField] private Transform contentParent;
    [SerializeField] private GameObject rowPrefab;

    [Header("Summary Texts")]
    [SerializeField] private TextMeshProUGUI totalRevenueText;
    [SerializeField] private TextMeshProUGUI totalSoldCountText;
    [SerializeField] private TextMeshProUGUI daysUntilBattleText;

    [Header("ターン評価")]
    [SerializeField] private UnityEngine.UI.Image gradeImage;
    [SerializeField] private TextMeshProUGUI gradeCommentText;
    [SerializeField] private Sprite gradeS;
    [SerializeField] private Sprite gradeA;
    [SerializeField] private Sprite gradeB;
    [SerializeField] private Sprite gradeC;
    [SerializeField] private Sprite gradeD;
    [SerializeField] private Sprite gradeE;

    [Header("Confirm Button")]
    [SerializeField] private Button confirmButton;

    /// <summary>
    /// 確認ボタンが押された
    /// </summary>
    public Subject<Unit> OnConfirmClicked { get; } = new();

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(() => OnConfirmClicked.OnNext(Unit.Default));
        }
    }

    /// <summary>
    /// サマリーの内容を更新する
    /// </summary>
    public void ShowGrade(TurnGrade grade, string comment)
    {
        if (gradeImage != null)
        {
            gradeImage.sprite = grade switch
            {
                TurnGrade.S => gradeS,
                TurnGrade.A => gradeA,
                TurnGrade.B => gradeB,
                TurnGrade.C => gradeC,
                TurnGrade.D => gradeD,
                _           => gradeE,
            };
        }

        if (gradeCommentText != null)
            gradeCommentText.text = comment;
    }

    public void UpdateContent(List<TurnEndSummaryItemData> items, int totalRevenue, int totalSoldCount, int daysUntilBattle)
    {
        // 既存の行をクリア
        ClearRows();

        // 各アイテム行を生成
        foreach (var item in items)
        {
            var row = Instantiate(rowPrefab, contentParent);
            row.SetActive(true);

            var rowUI = row.GetComponent<TurnEndSummaryRowUI>();
            if (rowUI != null)
            {
                rowUI.Setup(item);
            }
        }

        // サマリーテキスト更新
        if (totalRevenueText != null)
            totalRevenueText.text = $"{totalRevenue}G";

        if (totalSoldCountText != null)
            totalSoldCountText.text = $"{totalSoldCount}個";

        if (daysUntilBattleText != null)
        {
            if (daysUntilBattle >= 0)
                daysUntilBattleText.text = $"あと{daysUntilBattle}ターン";
            else
                daysUntilBattleText.text = "予定なし";
        }
    }

    private void ClearRows()
    {
        if (contentParent == null) return;

        for (int i = contentParent.childCount - 1; i >= 0; i--)
        {
            Destroy(contentParent.GetChild(i).gameObject);
        }
    }
}

