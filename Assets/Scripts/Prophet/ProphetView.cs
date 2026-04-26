using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProphetView : MonoBehaviour
{
    [Header("トレンドセクション")]
    [SerializeField] private Transform trendRowParent;
    [SerializeField] private ProphetTrendRowUI trendRowPrefab;

    [Header("価格ランキングセクション")]
    [SerializeField] private Transform rankingRowParent;
    [SerializeField] private ProphetRankingRowUI rankingRowPrefab;

    [Header("次のダンジョンセクション")]
    [SerializeField] private TMP_Text dungeonNameText;
    [SerializeField] private TMP_Text dungeonAttributeText;
    [SerializeField] private TMP_Text dungeonDifficultyText;
    [SerializeField] private TMP_Text turnsUntilBattleText;
    [SerializeField] private Image dungeonIconImage;

    [Header("おすすめ武器セクション")]
    [SerializeField] private Transform recommendedRowParent;
    [SerializeField] private ProphetTrendRowUI recommendedRowPrefab;

    [Header("操作")]
    [SerializeField] private Button closeButton;

    public Subject<Unit> OnCloseClicked { get; } = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseClicked.OnNext(Unit.Default));
    }

    public void ShowTrendRows(List<(Sprite icon, string name, float trend, float demand)> items)
    {
        ClearChildren(trendRowParent);
        foreach (var item in items)
        {
            var row = Instantiate(trendRowPrefab, trendRowParent);
            row.SetData(item.icon, item.name, item.trend, item.demand);
        }
    }

    public void ShowRankingRows(List<(int rank, Sprite icon, string name, int price)> items)
    {
        ClearChildren(rankingRowParent);
        foreach (var item in items)
        {
            var row = Instantiate(rankingRowPrefab, rankingRowParent);
            row.SetData(item.rank, item.icon, item.name, item.price);
        }
    }

    public void ShowDungeonInfo(string dungeonName, string attribute, int difficulty, int turnsUntil, Sprite icon)
    {
        dungeonNameText.text = turnsUntil >= 0 ? dungeonName : "次のダンジョンなし";
        dungeonAttributeText.text = $"属性: {attribute}";
        dungeonDifficultyText.text = $"難易度: {difficulty}";
        turnsUntilBattleText.text = turnsUntil >= 0 ? $"あと {turnsUntil} ターン" : "—";
        if (dungeonIconImage != null) dungeonIconImage.sprite = icon;
    }

    public void ShowRecommendedRows(List<(Sprite icon, string name, float trend, float demand)> items)
    {
        ClearChildren(recommendedRowParent);
        foreach (var item in items)
        {
            var row = Instantiate(recommendedRowPrefab, recommendedRowParent);
            row.SetData(item.icon, item.name, item.trend, item.demand);
        }
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }
}
