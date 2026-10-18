using System;
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

    [Header("キャラ表情（バズ連動・任意）")]
    [SerializeField] private CharacterExpression characterExpression;

    [Header("操作")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button characterButton;

    public Subject<Unit> OnCloseClicked { get; } = new();
    public Subject<Unit> OnCharacterClicked { get; } = new();
    public Subject<string> OnRowHoverEnter { get; } = new();
    public Subject<Unit> OnRowHoverExit { get; } = new();

    // パッド/キー: 開いたときはトレンドの先頭行へ（行は PointerEventBridge で選択＝ホバー扱い）
    private UIFocusScope focusScope;
    private readonly PendingFocus pendingFocus = new();
    private Func<Selectable> firstRow;

    [VContainer.Inject]
    public void Construct(BuzzSystem buzzSystem)
    {
        if (characterExpression != null) characterExpression.Bind(buzzSystem);
    }

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseClicked.OnNext(Unit.Default));
        if (characterButton != null)
        {
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));
            characterButton.navigation = new Navigation { mode = Navigation.Mode.None }; // 立ち絵はパッドで選ばない（画面より大きい枠が出るため）
        }
        focusScope = GetComponentInParent<UIFocusScope>();
    }

    private void OnEnable() => pendingFocus.Request(true);

    // メソッドグループを毎フレーム渡すとデリゲートが作られるので、1つを使い回す
    private void LateUpdate() => pendingFocus.Tick(focusScope, firstRow ??= FirstRow);

    private Selectable FirstRow()
    {
        var row = ControllerFocusUtil.FirstUsable(trendRowParent);
        if (row == null) row = ControllerFocusUtil.FirstUsable(rankingRowParent);
        if (row == null) row = ControllerFocusUtil.FirstUsable(recommendedRowParent);
        return row;
    }

    private void RequestRowFocus(Transform parent) =>
        pendingFocus.Request(ControllerFocusUtil.IsSelectionInside(parent));

    public void ShowTrendRows(List<(Sprite icon, string name, float trend, float demand)> items)
    {
        RequestRowFocus(trendRowParent);
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
        RequestRowFocus(rankingRowParent);
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
        RequestRowFocus(recommendedRowParent);
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
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }
}
