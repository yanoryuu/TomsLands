using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using DG.Tweening;
using R3;

public class InfoBrokerView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject heroTab;
    [SerializeField] private GameObject mapTab;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Image characterImage;
    [SerializeField] private CanvasGroup panelsGroup;

    [Header("Tab Buttons (左から 地図, 勇者, 予測)")]
    [SerializeField] private Button mapButton;
    [SerializeField] private Button heroButton;

    [Header("Content Panels")]
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private GameObject heroPanel;
    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<Unit> OnRefreshRequested { get; } = new();
    public Subject<InfoBrokerTab> OnChangePanel { get; } = new();

    private readonly Dictionary<InfoBrokerTab, Vector3> initTabPos = new();
    private InfoBrokerTab _currentTab = InfoBrokerTab.Map;

    private System.Random random = new System.Random();

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));

        mapButton.onClick.AddListener(() => { _currentTab = InfoBrokerTab.Map; OnChangePanel.OnNext(InfoBrokerTab.Map); });
        heroButton.onClick.AddListener(() => { _currentTab = InfoBrokerTab.Hero; OnChangePanel.OnNext(InfoBrokerTab.Hero); });
        
        initTabPos[InfoBrokerTab.Hero] = heroTab.transform.localPosition;
        initTabPos[InfoBrokerTab.Map] = mapTab.transform.localPosition;
        
        ShowPanel(InfoBrokerTab.Map);
        SortItemTab(InfoBrokerTab.Map);
    }
    
    /// <summary>
    /// タブに対応するコンテンツパネルの表示を切り替え
    /// </summary>
    public void ShowPanel(InfoBrokerTab tab)
    {
        if (mapPanel) mapPanel.SetActive(tab == InfoBrokerTab.Map);
        if (heroPanel) heroPanel.SetActive(tab == InfoBrokerTab.Hero);
    }
    
    public void SortItemTab(InfoBrokerTab type)
    {
        var heroSeq =  heroTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Hero].y, 0.1f);
        var mapSeq = mapTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Map].y, 0.1f); ;
        switch (type)
        {
            case InfoBrokerTab.Hero:
                heroSeq.Kill();
                heroTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Hero].y+10, 0.2f);
                break;
            case InfoBrokerTab.Map:
                mapSeq.Kill();
                mapTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Map].y + 10, 0.2f);
                break;
        }
    }
}

public enum InfoBrokerTab
{
    Hero,
    Map,
    Guess
}