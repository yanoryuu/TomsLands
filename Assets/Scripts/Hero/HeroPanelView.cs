using System;
using System.Collections.Generic;
using TMPro;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HeroPanelView : MonoBehaviour
{
    [Header("操作")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button characterButton;
    [SerializeField] private TMP_Dropdown weaponDropdown;
    [SerializeField] private TMP_Dropdown armorDropdown;
    [SerializeField] private TMP_Dropdown tacticsDropdown;

    [Header("表示")]
    [SerializeField] private TextMeshProUGUI heroStatusText;
    [SerializeField] private TextMeshProUGUI heroLevelText;
    [SerializeField] private TextMeshProUGUI heroHpText;
    [SerializeField] private TextMeshProUGUI heroMpText;
    [SerializeField] private TextMeshProUGUI dialogueText;

    [Header("装備アイコン")]
    [SerializeField] private Image weaponIconImage;
    [SerializeField] private Image armorIconImage;

    [Header("クリア確率")]
    [SerializeField] private TextMeshProUGUI clearProbabilityText;

    [Header("説明ボタン")]
    [SerializeField] private Button weaponInfoButton;
    [SerializeField] private Button armorInfoButton;
    [SerializeField] private Button tacticsInfoButton;
    [SerializeField] private Button clearProbabilityInfoButton;

    public Subject<Unit>        OnCloseRequested { get; } = new();
    public Subject<string>      OnWeaponChanged  { get; } = new();
    public Subject<string>      OnArmorChanged   { get; } = new();
    public Subject<HeroTactics> OnTacticsChanged { get; } = new();
    public Subject<Unit>        OnCharacterClicked { get; } = new();
    public Subject<string>      OnHoverEnter     { get; } = new();
    public Subject<Unit>        OnHoverExit      { get; } = new();

    private readonly List<string>              weaponIds          = new();
    private readonly List<string>              armorIds           = new();
    private readonly List<HeroTactics>         tacticsValues      = new();
    private readonly Dictionary<Button, string> buttonDescriptions = new();
    private bool suppressEvents;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));

        if (characterButton != null)
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));

        if (weaponDropdown != null)
        {
            weaponDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressEvents || index < 0 || index >= weaponIds.Count) return;
                OnWeaponChanged.OnNext(weaponIds[index]);
            });
        }

        if (armorDropdown != null)
        {
            armorDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressEvents || index < 0 || index >= armorIds.Count) return;
                OnArmorChanged.OnNext(armorIds[index]);
            });
        }

        if (tacticsDropdown != null)
        {
            tacticsDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressEvents || index < 0 || index >= tacticsValues.Count) return;
                OnTacticsChanged.OnNext(tacticsValues[index]);
            });
        }

        RegisterInfoButton(weaponInfoButton);
        RegisterInfoButton(armorInfoButton);
        RegisterInfoButton(tacticsInfoButton);
        RegisterInfoButton(clearProbabilityInfoButton);
    }

    private void RegisterInfoButton(Button button)
    {
        if (button == null) return;

        var trigger = button.GetComponent<EventTrigger>() ?? button.gameObject.AddComponent<EventTrigger>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ =>
        {
            if (buttonDescriptions.TryGetValue(button, out var desc))
                OnHoverEnter.OnNext(desc);
        });
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => OnHoverExit.OnNext(Unit.Default));
        trigger.triggers.Add(exit);
    }

    // ─────────────────────────────────────────────────────────
    // Public set methods
    // ─────────────────────────────────────────────────────────

    public void SetHeroStatus(RuntimeHeroData hero)
    {
        if (hero == null) return;

        string expText = hero.expToNextLevel.Value > 0
            ? $"{hero.experience.Value}/{hero.expToNextLevel.Value}"
            : "MAX";

        if (heroLevelText != null)
            heroLevelText.text = $"Lv {hero.level.Value}\nEXP {expText}";

        // 「HP」「MP」の静的ラベルがパネル側にあるため、ここでは数値のみを表示する
        if (heroHpText != null)
            heroHpText.text = $"{hero.hp.Value}";

        if (heroMpText != null)
            heroMpText.text = $"{hero.mp.Value}";

        if (heroStatusText != null)
        {
            heroStatusText.text =
                $"Lv {hero.level.Value}\n" +
                $"EXP {expText}\n" +
                $"HP {hero.hp.Value} / MP {hero.mp.Value}\n" +
                $"攻撃 {hero.attackPower.Value} / 防御 {hero.defensePower.Value}";
        }
    }

    public void SetEquipmentIcons(Sprite weaponIcon, Sprite armorIcon)
    {
        SetIcon(weaponIconImage, weaponIcon);
        SetIcon(armorIconImage, armorIcon);
    }

    public void SetClearProbability(float probability, bool infoAvailable)
    {
        if (clearProbabilityText != null)
        {
            clearProbabilityText.text = infoAvailable
                ? $"クリア確率: {Mathf.RoundToInt(probability)}%"
                : "クリア確率: 情報未購入";
        }
    }

    public void SetDialogue(string message)
    {
        if (dialogueText != null)
            dialogueText.text = message ?? string.Empty;
    }

    public void SetWeaponOptions(IReadOnlyList<HeroEquipmentOption> options, string selectedItemId)
    {
        SetEquipmentOptions(weaponDropdown, weaponIds, options, selectedItemId);
    }

    public void SetArmorOptions(IReadOnlyList<HeroEquipmentOption> options, string selectedItemId)
    {
        SetEquipmentOptions(armorDropdown, armorIds, options, selectedItemId);
    }

    public void SetTacticsOptions(HeroTactics selected)
    {
        if (tacticsDropdown == null) return;

        suppressEvents = true;
        tacticsValues.Clear();
        tacticsDropdown.ClearOptions();

        var labels = new List<string>();
        foreach (HeroTactics tactics in Enum.GetValues(typeof(HeroTactics)))
        {
            tacticsValues.Add(tactics);
            labels.Add(GetTacticsName(tactics));
        }

        tacticsDropdown.AddOptions(labels);
        int selectedIndex = Mathf.Max(0, tacticsValues.IndexOf(selected));
        tacticsDropdown.SetValueWithoutNotify(selectedIndex);
        tacticsDropdown.RefreshShownValue();
        suppressEvents = false;
    }

    public void SetTacticsHoverDescription(string description)
    {
        SetButtonDescription(tacticsInfoButton, description);
    }

    public void SetStaticHoverDescriptions(string weapon, string armor, string clearProbability)
    {
        SetButtonDescription(weaponInfoButton, weapon);
        SetButtonDescription(armorInfoButton, armor);
        SetButtonDescription(clearProbabilityInfoButton, clearProbability);
    }

    // ─────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────

    private void SetButtonDescription(Button button, string description)
    {
        if (button == null) return;
        buttonDescriptions[button] = description;
    }

    private void SetEquipmentOptions(
        TMP_Dropdown dropdown,
        List<string> ids,
        IReadOnlyList<HeroEquipmentOption> options,
        string selectedItemId)
    {
        if (dropdown == null) return;

        suppressEvents = true;
        ids.Clear();
        dropdown.ClearOptions();

        var labels = new List<string> { "なし" };
        ids.Add(string.Empty);

        int selectedIndex = 0;
        for (int i = 0; i < options.Count; i++)
        {
            var option = options[i];
            ids.Add(option.ItemId);
            labels.Add(option.Label);
            if (option.ItemId == selectedItemId)
                selectedIndex = i + 1;
        }

        dropdown.AddOptions(labels);
        dropdown.SetValueWithoutNotify(selectedIndex);
        dropdown.RefreshShownValue();
        suppressEvents = false;
    }

    private void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite  = sprite;
        image.enabled = sprite != null;
    }

    private string GetTacticsName(HeroTactics tactics)
    {
        return tactics switch
        {
            HeroTactics.Aggressive   => "攻撃重視",
            HeroTactics.Defensive    => "防御重視",
            HeroTactics.Stealth      => "慎重",
            HeroTactics.MagicFocused => "属性重視",
            _ => "バランス"
        };
    }
}

public readonly struct HeroEquipmentOption
{
    public string ItemId { get; }
    public string Label  { get; }

    public HeroEquipmentOption(string itemId, string label)
    {
        ItemId = itemId;
        Label  = label;
    }
}
