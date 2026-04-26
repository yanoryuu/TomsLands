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

    [Header("セリフ")]
    [SerializeField] private TMP_Text dialogueText;

    [Header("操作")]
    [SerializeField] private Button closeButton;

    public Subject<Unit> OnCloseClicked { get; } = new();
    public Subject<string> OnRowHoverEnter { get; } = new();
    public Subject<Unit> OnRowHoverExit { get; } = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseClicked.OnNext(Unit.Default));
    }

    public void ShowTrendRows(List<(Sprite icon, string name, float trend, float demand)> items)
    {
        ClearChildren(trendRowParent);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var row = Instantiate(trendRowPrefab, trendRowParent);
            row.SetData(item.icon, item.name, item.trend, item.demand, $"trend_{i + 1}");
            row.OnHoverEnter += id => OnRowHoverEnter.OnNext(id);
            row.OnHoverExit += () => OnRowHoverExit.OnNext(Unit.Default);
        }
    }

    public void ShowRankingRows(List<(int rank, Sprite icon, string name, int price)> items)
    {
        ClearChildren(rankingRowParent);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var row = Instantiate(rankingRowPrefab, rankingRowParent);
            row.SetData(item.rank, item.icon, item.name, item.price, $"ranking_{i + 1}");
            row.OnHoverEnter += id => OnRowHoverEnter.OnNext(id);
            row.OnHoverExit += () => OnRowHoverExit.OnNext(Unit.Default);
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
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var row = Instantiate(recommendedRowPrefab, recommendedRowParent);
            row.SetData(item.icon, item.name, item.trend, item.demand, $"recommended_{i + 1}");
            row.OnHoverEnter += id => OnRowHoverEnter.OnNext(id);
            row.OnHoverExit += () => OnRowHoverExit.OnNext(Unit.Default);
        }
    }

    public void ShowDialogue(string message)
    {
        if (dialogueText != null) dialogueText.text = message;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }
}
