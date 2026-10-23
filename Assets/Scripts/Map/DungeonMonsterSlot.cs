using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonMonsterSlot : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI monsterNameText;
    [SerializeField] private TextMeshProUGUI monsterElementText;
    [SerializeField] private Image monsterIconImage;
    [SerializeField] private Image isBossImage;
    private bool isBoss;
    
    public void SetMonsterData(EnemyData enemyData)
    {
        monsterNameText.text = $"{enemyData.enemyName}";
        monsterElementText.text = $"属性:{ElementTypeToJapanese(enemyData.elementType)}";
        monsterIconImage.sprite = enemyData.enemySprite;
        monsterIconImage.preserveAspect = true;
        isBoss = enemyData.isBoss;

        if (isBoss) isBossImage.gameObject.SetActive(true);
    }

    /// <summary>敵の属性の表記。字は ItemAttributeText にそろえ、属性なしは「なし」。</summary>
    private static string ElementTypeToJapanese(ElementType elementType)
    {
        var attr = ElementAttributeMapper.ToItemAttribute(elementType);
        return attr.HasValue ? ItemAttributeText.ToJapanese(attr.Value) : "なし";
    }
}
