using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonMonsterSlot : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI monsterNameText;
    [SerializeField] private TextMeshProUGUI monsterElementText;
    // [SerializeField] private TextMeshProUGUI monsterDescriptionText;
    [SerializeField] private Image monsterIconImage;
    [SerializeField] private Image isBossImage;
    private bool isBoss;
    
    public void SetMonsterData(EnemyData enemyData)
    {
        monsterNameText.text = $"{enemyData.enemyName}";
        monsterElementText.text = $"属性: {ElementTypeToJapanese(enemyData.elementType)}";
        // monsterDescriptionText.text = enemyData.description;
        monsterIconImage.sprite = enemyData.enemySprite;
        isBoss = enemyData.isBoss;
        
        if(isBoss)isBossImage.gameObject.SetActive(true);
    }

    private static string ElementTypeToJapanese(ElementType elementType) => elementType switch
    {
        ElementType.Water => "水",
        ElementType.Fire => "火",
        ElementType.Wood => "木",
        ElementType.Light => "光",
        ElementType.Dark => "闇",
        ElementType.None => "なし",
        _ => elementType.ToString()
    };
}
