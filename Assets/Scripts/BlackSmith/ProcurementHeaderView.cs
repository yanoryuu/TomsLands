using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 仕入れ画面の上部に常設する「次ダンジョン情報」バナー。
/// 次の戦闘ダンジョン・弱点属性・残りターン数・勇者の現装備を一目で確認できる。
/// 「何のために何を仕入れるか」を判断する文脈を購入画面に持ち込む。
/// </summary>
public class ProcurementHeaderView : MonoBehaviour
{
    [Header("次ダンジョン")]
    [SerializeField] private Image dungeonIcon;
    [SerializeField] private TextMeshProUGUI dungeonNameText;
    [SerializeField] private TextMeshProUGUI weaknessText;   // 弱点:火 など
    [SerializeField] private TextMeshProUGUI turnsUntilText; // あと2ターン

    [Header("勇者の現装備")]
    [SerializeField] private TextMeshProUGUI heroEquipText;  // 勇者Lv.5 ⚔鉄剣 / 🛡革鎧

    /// <summary>次の戦闘が存在する場合のバナー表示。</summary>
    public void Show(Sprite icon, string dungeonName, string weakness, int turnsUntil,
        int heroLevel, string weaponName, string armorName)
    {
        if (dungeonIcon)
        {
            dungeonIcon.sprite = icon;
            dungeonIcon.enabled = icon != null;
        }
        if (dungeonNameText) dungeonNameText.text = dungeonName;
        if (weaknessText) weaknessText.text = weakness;
        if (turnsUntilText)
            turnsUntilText.text = turnsUntil >= 0 ? $"あと{turnsUntil}ターン" : "—";

        if (heroEquipText)
        {
            string w = string.IsNullOrEmpty(weaponName) ? "なし" : weaponName;
            string a = string.IsNullOrEmpty(armorName) ? "なし" : armorName;
            heroEquipText.text = $"勇者Lv.{heroLevel}　武器:{w} / 防具:{a}";
        }
    }

    /// <summary>次の戦闘が存在しない場合の表示。</summary>
    public void ShowNoBattle(int heroLevel, string weaponName, string armorName)
    {
        if (dungeonIcon) dungeonIcon.enabled = false;
        if (dungeonNameText) dungeonNameText.text = "次の戦闘なし";
        if (weaknessText) weaknessText.text = "—";
        if (turnsUntilText) turnsUntilText.text = "—";
        if (heroEquipText)
        {
            string w = string.IsNullOrEmpty(weaponName) ? "なし" : weaponName;
            string a = string.IsNullOrEmpty(armorName) ? "なし" : armorName;
            heroEquipText.text = $"勇者Lv.{heroLevel}　武器:{w} / 防具:{a}";
        }
    }
}
