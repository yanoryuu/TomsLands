using System.Collections.Generic;
using DG.Tweening;
using R3;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InfoBrokerView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button characterButton;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private GameObject mapTab;

    [Header("Tab Buttons")]
    [SerializeField] private Button mapButton;

    [Header("Content Panels")]
    [SerializeField] private GameObject mapPanel;

    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<Unit> OnCharacterClicked { get; } = new();
    public Subject<Unit> OnRefreshRequested { get; } = new();
    public Subject<InfoBrokerTab> OnChangePanel { get; } = new();

    private readonly Dictionary<InfoBrokerTab, Vector3> initTabPos = new();

    private void Awake()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        }

        if (characterButton != null)
        {
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));
        }

        if (mapButton != null)
        {
            mapButton.onClick.AddListener(() => OnChangePanel.OnNext(InfoBrokerTab.Map));
        }

        if (mapTab != null)
        {
            initTabPos[InfoBrokerTab.Map] = mapTab.transform.localPosition;
        }

        ShowPanel(InfoBrokerTab.Map);
        SortItemTab(InfoBrokerTab.Map);
    }

    public void ShowPanel(InfoBrokerTab tab)
    {
        if (mapPanel != null)
        {
            mapPanel.SetActive(tab == InfoBrokerTab.Map);
        }
    }

    public void SortItemTab(InfoBrokerTab type)
    {
        if (type != InfoBrokerTab.Map || mapTab == null || !initTabPos.ContainsKey(InfoBrokerTab.Map)) return;

        mapTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Map].y + 10, 0.2f);
    }

    public void ShowDialogue(string message)
    {
        if (dialogueText != null) dialogueText.text = message ?? string.Empty;
    }
}

public enum InfoBrokerTab
{
    Map,
    Guess
}
