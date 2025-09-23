using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonInfoView : MonoBehaviour
{
    [SerializeField] private List<DungeonInfoScriptableObj> dungeonInfoScriptableObjs;
    
    [SerializeField] private Image dungeonNameImage;
    
    [SerializeField] private GameObject dungeonInfoPanel;
    
    [SerializeField] private TextMeshProUGUI dungeonLevelText;
    [SerializeField] private TextMeshProUGUI dungeonDescriptionText;
    [SerializeField] private TextMeshProUGUI dungeonMonsterText;    
    [SerializeField] private TextMeshProUGUI dungeonBossText;
    [SerializeField] private TextMeshProUGUI dungeonRewardText;
    
    public void ShowDungeonInfo(DungeonName dungeonName)
    {
        SetDungeonInfo(dungeonName);
        dungeonInfoPanel.SetActive(true);
    }

    public void HideDungeonInfo()
    {
        dungeonInfoPanel.SetActive(false);
    }

    public void SetDungeonInfo(DungeonName dungeonName)
    {
        int index = (int)dungeonName;

        if (index < 0 || index >= dungeonInfoScriptableObjs.Count)
        {
            Debug.LogWarning($"DungeonInfo not found for {dungeonName}");
            return;
        }

        DungeonInfoScriptableObj info = dungeonInfoScriptableObjs[index];
        if (info != null && info.dungeonImage != null)
        {
            dungeonNameImage.sprite = info.dungeonImage;
        }
        else
        {
            Debug.LogWarning($"Sprite not set for {dungeonName}");
        }
    }
}