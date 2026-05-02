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
    [SerializeField] private TextMeshProUGUI detailRewardText;

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
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));
        EnsureSingleColumnLayout();
    }

    public void ShowDialogue(string message)
    {
        if (dialogueText != null) dialogueText.text = message;
    }

    public void ShowDungeonDetail(DungeonLevelUpDetailData detail)
    {
        if (detailDungeonNameText) detailDungeonNameText.text = detail.DungeonName;
        if (detailLevelText)       detailLevelText.text       = detail.LevelText;
        if (detailRewardText)      detailRewardText.text      = detail.RewardText;

        PopulateMonsterContainer(currentMonsterContainer, currentMonsterObjects, detail.CurrentMonsters);
        PopulateMonsterContainer(nextMonsterContainer,    nextMonsterObjects,    detail.NextMonsters);
    }

    public void ClearDungeonDetail()
    {
        if (detailDungeonNameText) detailDungeonNameText.text = string.Empty;
        if (detailLevelText)       detailLevelText.text       = string.Empty;
        if (detailRewardText)      detailRewardText.text      = string.Empty;

        ClearMonsterObjects(currentMonsterObjects);
        ClearMonsterObjects(nextMonsterObjects);
    }

    public List<DungeonLevelUpSlot> PopulateDungeonList(List<DungeonLevelUpSlotData> dungeons)
    {
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
        }
        return slots;
    }

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
    public string          RewardText;
    public List<EnemyData> CurrentMonsters;
    public List<EnemyData> NextMonsters;
}
