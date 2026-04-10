using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HeroInfoView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI heroLvText;
    // [SerializeField] private TextMeshProUGUI heroExpText;
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
            heroHpText.text = "HP: ???";
            heroMpText.text = "MP: ???";
            weaponText.text = "???";
            heroTacticsText.text = "???";
            return;
        }

        // 購入済み：実際のデータを表示
        heroLvText.text = $"Lv: {heroInfo.level.Value}";
        // heroExpText.text = $"Exp: {heroInfo.Experience}/{heroInfo.ExpToNextLevel}";
        heroHpText.text = $"HP: {heroInfo.hp.Value}";
        // heroAttackText.text = $"Attack: {heroInfo.Attack}";
        heroMpText.text = $"MP: {heroInfo.mp.Value}";
        weaponText.text = $"{heroInfo.weaponName.Value}";
        // heroArmorText.text = $"Armor: {heroInfo.EquippedArmor}";
        heroTacticsText.text = $"{heroInfo.tactics.Value}";
        //flavorText.text = heroInfo.flavorText;
    }
}
