using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonInfoView : MonoBehaviour
{
    [SerializeField] private Image dungeonNameImage;
    
    [SerializeField] private GameObject dungeonInfoPanel;
    
    [SerializeField] private GameObject dungeonMonsterPanelSlot;
    
    [SerializeField] private TextMeshProUGUI dungeonLevelText;
    [SerializeField] private TextMeshProUGUI dungeonDescriptionText;
    [SerializeField] private TextMeshProUGUI dungeonRewardText;
    
    [SerializeField] private Button closeButton;
    
    //ダンジョンに出現するモンスター表示用パネルの親
    [SerializeField] private Transform dungeonMonsterBarTransform;
    
    //ダンジョンに出現するモンスター表示用パネルをまとめる
    private List<GameObject> dungeonMonsterPanels = new List<GameObject>();
    
    
    public Subject<Unit> OnCloseRequested { get; private set; } = new();
    
    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
    }
    
    public void ShowDungeonInfo(DungeonData dungeonData)
    {
        SetDungeonInfo(dungeonData);
        dungeonInfoPanel.SetActive(true);
    }

    public void HideDungeonInfo()
    {
        dungeonInfoPanel.SetActive(false);
    }

    private void SetDungeonInfo(DungeonData　d)
    {
        if(d==null)return;
        
        dungeonNameImage.sprite = d.dungeonImage;
        dungeonDescriptionText.text = d.dungeonDescription;
        dungeonLevelText.text = d.currentDungeonLevel.ToString();
        dungeonRewardText.text = d.rewardGold.ToString();

        //すでにある前のモンスターデータを削除
        if (dungeonMonsterPanels.Count > 0)
        {
            foreach (var panel in dungeonMonsterPanels)
            {
                Destroy(panel);
            }
            dungeonMonsterPanels = new();
        }
        
        foreach (var monster in d.dungeonMonsters)
        {
            var panel = Instantiate(dungeonMonsterPanelSlot, Vector3.zero, Quaternion.identity,
                dungeonMonsterBarTransform);
            
            panel.transform.localPosition = Vector3.zero;
            
            panel.GetComponent<DungeonMonsterSlot>().SetMonsterData(monster);
            
            dungeonMonsterPanels.Add(panel);
        }   
    }
}