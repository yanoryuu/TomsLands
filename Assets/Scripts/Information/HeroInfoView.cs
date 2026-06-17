using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HeroInfoView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI heroLvText;
    [SerializeField] private TextMeshProUGUI heroExpText;
    [SerializeField] private TextMeshProUGUI heroHpText;
    // [SerializeField] private TextMeshProUGUI heroAttackText;
    [SerializeField] private TextMeshProUGUI heroMpText;
    [SerializeField] private TextMeshProUGUI clearProbabilityText;
    [SerializeField] private TextMeshProUGUI weaponText;
    // [SerializeField] private TextMeshProUGUI heroArmorText;
    [SerializeField] private TextMeshProUGUI nextBattleDayText;
    [SerializeField] private TextMeshProUGUI heroTacticsText;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private TextMeshProUGUI flavorText;
    
    public readonly Subject<Unit> OnPurchaseButtonClicked = new Subject<Unit>();
    private void Awake()
    {
        purchaseButton.onClick.AddListener(() => OnPurchaseButtonClicked.OnNext(Unit.Default));
    }
    
    public void UpdateHeroInfo(RuntimeHeroData heroInfo, bool isPurchased)
    {
        if (!isPurchased)
        {
            // 未購入時は「???」を表示
            heroLvText.text = "Lv: ???";
            if (heroExpText != null) heroExpText.text = "Exp: ???";
            heroHpText.text = "HP: ???";
            heroMpText.text = "MP: ???";
            weaponText.text = "???";
            heroTacticsText.text = "???";
            if (flavorText != null) flavorText.text = "";
            return;
        }

        // 購入済み：実際のデータを表示
        heroLvText.text = $"Lv: {heroInfo.level.Value}";
        if (heroExpText != null)
        {
            heroExpText.text = heroInfo.expToNextLevel.Value > 0
                ? $"Exp: {heroInfo.experience.Value}/{heroInfo.expToNextLevel.Value}"
                : "Exp: MAX";
        }
        heroHpText.text = $"HP: {heroInfo.hp.Value}";
        // heroAttackText.text = $"Attack: {heroInfo.Attack}";
        heroMpText.text = $"MP: {heroInfo.mp.Value}";
        string weaponName = string.IsNullOrEmpty(heroInfo.weaponName.Value) ? "None" : heroInfo.weaponName.Value;
        string armorName = string.IsNullOrEmpty(heroInfo.armorName.Value) ? "None" : heroInfo.armorName.Value;
        weaponText.text = $"Weapon: {weaponName}\nArmor: {armorName}";
        // heroArmorText.text = $"Armor: {heroInfo.EquippedArmor}";
        heroTacticsText.text = HeroBattleInfluence.GetDisplayName(heroInfo.tactics.Value);
        if (flavorText != null)
        {
            var settings = AddressableLoader.Load<BattlePriceSettings>("BattlePriceSettings");
            flavorText.text = HeroBattleInfluence.BuildSummary(heroInfo, weaponName, armorName, settings);
        }
    }
}
