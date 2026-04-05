using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ダンジョンレベルアップ画面のView。
/// スクロールリストにダンジョンスロットを生成し、閉じるボタンを提供する。
/// </summary>
public class DungeonLevelUpView : MonoBehaviour
{
    [Header("Content")]
    [SerializeField] private Transform slotContainer;
    [SerializeField] private GameObject dungeonLevelUpSlotPrefab;

    [Header("Buttons")]
    [SerializeField] private Button closeButton;

    /// <summary>閉じるボタンが押された</summary>
    public Subject<Unit> OnCloseRequested { get; } = new();

    private readonly List<DungeonLevelUpSlot> activeSlots = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        EnsureSingleColumnLayout();
    }

    /// <summary>
    /// slotContainer のレイアウトを1列に強制する。
    /// GridLayoutGroup が付いていれば constraintCount=1 に設定し、
    /// 何も無ければ VerticalLayoutGroup を追加する。
    /// </summary>
    private void EnsureSingleColumnLayout()
    {
        if (slotContainer == null) return;

        var grid = slotContainer.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 1;
            return;
        }

        // VerticalLayoutGroup が無ければ追加
        if (slotContainer.GetComponent<VerticalLayoutGroup>() == null)
        {
            var vlg = slotContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 8f;
        }
    }

    /// <summary>
    /// ダンジョン一覧からスロットを生成して返す。
    /// </summary>
    public List<DungeonLevelUpSlot> PopulateDungeonList(List<DungeonData> dungeons)
    {
        // 既存スロットを破棄
        foreach (var slot in activeSlots)
        {
            if (slot != null) Destroy(slot.gameObject);
        }
        activeSlots.Clear();

        var slots = new List<DungeonLevelUpSlot>();

        foreach (var dungeon in dungeons)
        {
            var go = Instantiate(dungeonLevelUpSlotPrefab, slotContainer);
            var slot = go.GetComponent<DungeonLevelUpSlot>();

            int cost = dungeon.levelUpCost;
            bool isMax = dungeon.currentDungeonLevel >= GameConst.MaxDungeonLevel;

            slot.SetSlot(
                dungeon.key,
                dungeon.dungeonName,
                dungeon.dungeonIcon,
                dungeon.currentDungeonLevel,
                cost,
                isMax
            );

            slots.Add(slot);
            activeSlots.Add(slot);
        }

        return slots;
    }
}

