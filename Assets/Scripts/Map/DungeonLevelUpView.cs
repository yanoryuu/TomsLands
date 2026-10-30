using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonLevelUpView : MonoBehaviour
{
    [Header("スロットリスト")]
    [SerializeField] private Transform  slotContainer;
    [SerializeField] private GameObject dungeonLevelUpSlotPrefab;

    [Header("詳細パネル")]
    [SerializeField] private TextMeshProUGUI detailDungeonNameText;
    [SerializeField] private TextMeshProUGUI detailLevelText;
    [Tooltip("防衛報酬（勇者敗北時のrewardGold）の変化表示")]
    [SerializeField] private TextMeshProUGUI detailRewardText;
    [Tooltip("勇者のクリア確率の変化表示")]
    [SerializeField] private TextMeshProUGUI detailClearProbabilityText;
    [Tooltip("配当のオッズの見込みの枠（オッズが全部 1 のときは隠す）")]
    [SerializeField] private GameObject supportOddsRoot;
    [Tooltip("配当のオッズの見込み（いまのLv・もう 1 回支援したLv・倍率は配信開始時の見込みで決まる）")]
    [SerializeField] private TextMeshProUGUI supportOddsText;

    [Header("モンスター表示")]
    [SerializeField] private Transform  currentMonsterContainer;
    [SerializeField] private Transform  nextMonsterContainer;
    [SerializeField] private GameObject dungeonMonsterSlotPrefab;

    [Header("ボタン・セリフ")]
    [SerializeField] private Button          closeButton;
    [SerializeField] private Button          characterButton;
    [SerializeField] private TextMeshProUGUI dialogueText;

    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<Unit> OnCharacterClicked { get; } = new();

    private readonly List<DungeonLevelUpSlot> activeSlots           = new();
    private readonly List<GameObject>         currentMonsterObjects = new();
    private readonly List<GameObject>         nextMonsterObjects    = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        if (characterButton != null)
        {
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));
            characterButton.navigation = new Navigation { mode = Navigation.Mode.None }; // 立ち絵はパッドで選ばない（画面より大きい枠が出るため）
        }
        EnsureSingleColumnLayout();
    }

    public void ShowDialogue(string message)
    {
        if (dialogueText != null) dialogueText.text = message;
    }

    public void ShowDungeonDetail(DungeonLevelUpDetailData detail)
    {
        if (detailDungeonNameText)      detailDungeonNameText.text      = detail.DungeonName;
        if (detailLevelText)            detailLevelText.text            = detail.LevelText;
        if (detailRewardText)           detailRewardText.text           = detail.RewardText;
        if (detailClearProbabilityText) detailClearProbabilityText.text = detail.ClearProbabilityText;
        ShowSupportOdds(detail.OddsPreview);

        PopulateMonsterContainer(currentMonsterContainer, currentMonsterObjects, detail.CurrentMonsters);
        PopulateMonsterContainer(nextMonsterContainer,    nextMonsterObjects,    detail.NextMonsters);
    }

    public void ClearDungeonDetail()
    {
        if (detailDungeonNameText)      detailDungeonNameText.text      = string.Empty;
        if (detailLevelText)            detailLevelText.text            = string.Empty;
        if (detailRewardText)           detailRewardText.text           = string.Empty;
        if (detailClearProbabilityText) detailClearProbabilityText.text = string.Empty;
        ShowSupportOdds(null);

        ClearMonsterObjects(currentMonsterObjects);
        ClearMonsterObjects(nextMonsterObjects);
    }

    /// <summary>配当のオッズの見込みを出す。見せない（オッズが全部 1・null）なら枠ごと隠す。</summary>
    public void ShowSupportOdds(SupportOddsPreview.Result odds)
    {
        bool visible = odds != null && odds.Visible;
        if (supportOddsText) supportOddsText.text = visible ? odds.Text : string.Empty;
        if (supportOddsRoot) supportOddsRoot.SetActive(visible);
        else if (supportOddsText) supportOddsText.gameObject.SetActive(visible);
    }

    /// <summary>いま出ているオッズの見込みの文（隠れていれば空。テスト・確認用）。</summary>
    public string SupportOddsShownText =>
        supportOddsText != null && supportOddsText.gameObject.activeInHierarchy ? supportOddsText.text : string.Empty;

    // ---------------- パッド/キー ----------------

    private UIFocusScope focusScope;
    private readonly PendingFocus pendingFocus = new();
    private DungeonName? focusKey;
    private bool focusOnLevelUp;
    private Func<Selectable> slotFocusTarget;

    private void OnEnable()
    {
        pendingFocus.Request(true);
        staggerNextList = true;
    }

    private void LateUpdate()
    {
        if (focusScope == null) focusScope = GetComponentInParent<UIFocusScope>();
        // メソッドグループを毎フレーム渡すとデリゲートが作られるので、1つを使い回す
        pendingFocus.Tick(focusScope, slotFocusTarget ??= SlotFocusTarget);
    }

    /// <summary>作り直し前に選んでいた行（レベルアップ直後など）→ 無ければ先頭行。</summary>
    private Selectable SlotFocusTarget()
    {
        DungeonLevelUpSlot first = null;
        foreach (var slot in activeSlots)
        {
            if (slot == null || slot.SelectButton == null) continue;
            if (!UIFocusManager.IsUsable(slot.SelectButton.gameObject)) continue;
            if (first == null) first = slot;
            if (focusKey.HasValue && slot.DungeonKey == focusKey.Value)
            {
                if (focusOnLevelUp && slot.LevelUpButton != null && UIFocusManager.IsUsable(slot.LevelUpButton.gameObject))
                    return slot.LevelUpButton;
                return slot.SelectButton;
            }
        }
        return first != null ? first.SelectButton : null;
    }

    public List<DungeonLevelUpSlot> PopulateDungeonList(List<DungeonLevelUpSlotData> dungeons)
    {
        // 選択中の行を覚えておき、作り直した後も同じ行へ戻す
        bool focusInList = false;
        focusKey = null;
        focusOnLevelUp = false;
        foreach (var slot in activeSlots)
        {
            if (slot == null || !ControllerFocusUtil.IsSelectionInside(slot.transform)) continue;
            focusInList = true;
            focusKey = slot.DungeonKey;
            focusOnLevelUp = slot.LevelUpButton != null && ControllerFocusUtil.IsSelectionInside(slot.LevelUpButton.transform);
        }
        pendingFocus.Request(focusInList);

        foreach (var slot in activeSlots)
            if (slot != null) Destroy(slot.gameObject);
        activeSlots.Clear();

        var slots = new List<DungeonLevelUpSlot>();
        foreach (var dungeon in dungeons)
        {
            var go   = Instantiate(dungeonLevelUpSlotPrefab, slotContainer);
            var slot = go.GetComponent<DungeonLevelUpSlot>();
            slot.SetSlot(dungeon);
            slots.Add(slot);
            activeSlots.Add(slot);
            // 支援で上がったダンジョンの行（作り直した後の新しい行）のレベルを弾ませる
            if (shownLevels.TryGetValue(dungeon.DungeonKey, out int prev) && dungeon.CurrentLevel > prev) slot.PopLevel();
            shownLevels[dungeon.DungeonKey] = dungeon.CurrentLevel;
        }

        // #3 開いて最初の一覧だけ札を順に出す（支援した直後の作り直しでは動かさない）
        if (staggerNextList && slotContainer != null && slotContainer.gameObject.activeInHierarchy)
        {
            staggerNextList = false;
            var rows = new List<RectTransform>();
            foreach (var s in slots) if (s != null && s.transform is RectTransform rt) rows.Add(rt);
            UIMotion.Stagger(rows, 0.06f);
        }
        return slots;
    }

    private bool staggerNextList = true;
    private readonly Dictionary<DungeonName, int> shownLevels = new();

    private void PopulateMonsterContainer(Transform container, List<GameObject> objectList, List<EnemyData> monsters)
    {
        ClearMonsterObjects(objectList);
        if (container == null || dungeonMonsterSlotPrefab == null || monsters == null) return;

        foreach (var monster in monsters)
        {
            if (monster == null) continue;
            var go = Instantiate(dungeonMonsterSlotPrefab, container);
            go.GetComponent<DungeonMonsterSlot>().SetMonsterData(monster);
            objectList.Add(go);
        }
    }

    private static void ClearMonsterObjects(List<GameObject> objectList)
    {
        foreach (var go in objectList)
            if (go != null) Destroy(go);
        objectList.Clear();
    }

    private void EnsureSingleColumnLayout()
    {
        if (slotContainer == null) return;

        var grid = slotContainer.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 1;
            return;
        }

        if (slotContainer.GetComponent<VerticalLayoutGroup>() == null)
        {
            var vlg = slotContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = false;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 8f;
        }
    }
}

public class DungeonLevelUpDetailData
{
    public string          DungeonName;
    public string          LevelText;
    /// <summary>防衛報酬（勇者敗北時のrewardGold）の変化表示</summary>
    public string          RewardText;
    /// <summary>勇者のクリア確率の変化表示</summary>
    public string          ClearProbabilityText;
    /// <summary>配当のオッズの見込み（オッズが全部 1 なら Visible = false）</summary>
    public SupportOddsPreview.Result OddsPreview;
    public List<EnemyData> CurrentMonsters;
    public List<EnemyData> NextMonsters;
}
