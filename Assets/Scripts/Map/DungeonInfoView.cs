using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonInfoView : MonoBehaviour
{
    [SerializeField] private Image dungeonNameImage;
    [SerializeField] private Image dungeonIconImage;
    
    [SerializeField] private GameObject dungeonInfoPanel;
    
    [SerializeField] private GameObject dungeonMonsterPanelSlot;
    
    [SerializeField] private TextMeshProUGUI dungeonLevelText;
    [SerializeField] private TextMeshProUGUI dungeonDescriptionText;
    [SerializeField] private TextMeshProUGUI dungeonRewardText;
    
    [SerializeField] private Button closeButton;
    
    //ダンジョンに出現するモンスター表示用パネルの親
    [SerializeField] private Transform dungeonMonsterBarTransform;
    
    //ダンジョンに出現するモンスター表示用パネルをまとめる
    private readonly List<GameObject> dungeonMonsterPanels = new();

    public Subject<Unit> OnCloseRequested { get; } = new();
    
    private void Awake()
    {
        closeButton.onClick.AddListener(() => { if (!UIMotion.IsClosing(dungeonInfoPanel)) OnCloseRequested.OnNext(Unit.Default); });
    }
    
    /// <param name="clearProbability">勇者のクリア確率（%）。5段階の見込みにして出す。情報未購入時は使用されない</param>
    /// <param name="weaknessKnown">弱点を知っているか（地図の名札と同じ判定。情報の購入とは別）</param>
    public void ShowDungeonInfo(DungeonData dungeonData, float clearProbability, bool weaknessKnown)
    {
        bool shown = dungeonInfoPanel.activeSelf && !UIMotion.IsClosing(dungeonInfoPanel);
        SetDungeonInfo(dungeonData, clearProbability, weaknessKnown);
        // #1 閉じていればぽよんと開く。開いたまま別のダンジョンを選んだら短いポップ
        if (shown) UIFx.Pop(dungeonInfoPanel.transform, 0.97f, 0.15f);
        else UIMotion.Open(dungeonInfoPanel, MotionWindow.Of(dungeonInfoPanel));
        // 出現モンスターの札を順に出す
        var rows = new List<RectTransform>();
        foreach (var p in dungeonMonsterPanels) if (p != null && p.transform is RectTransform rt) rows.Add(rt);
        if (rows.Count > 0 && rows[0].gameObject.activeInHierarchy) UIMotion.Stagger(rows, 0.06f, 20f, 0.25f, 0.1f);
    }

    /// <summary>#1 縮んで閉じる（省略モード・非表示中は即座に非表示）。</summary>
    public void HideDungeonInfo() => UIMotion.Close(dungeonInfoPanel, null, MotionWindow.Of(dungeonInfoPanel));

    private void SetDungeonInfo(DungeonData d, float clearProbability, bool weaknessKnown)
    {
        if (d == null) return;

        dungeonNameImage.sprite = d.dungeonNameImage;
        if (dungeonIconImage) dungeonIconImage.sprite = d.dungeonIcon;
        if (dungeonIconImage) dungeonIconImage.preserveAspect = true;

        if (d.isShowedInfo)
        {
            dungeonDescriptionText.text = d.dungeonDescription;
            dungeonLevelText.text = DungeonLevels.Label(d.currentDungeonLevel);
        }
        else
        {
            // 情報未購入：???で表示
            dungeonDescriptionText.text = "？？？";
            dungeonLevelText.text = "Lv.？";
        }
        dungeonRewardText.text = BuildStatsText(d, clearProbability, weaknessKnown);

        //すでにある前のモンスターデータを削除
        foreach (var panel in dungeonMonsterPanels) Destroy(panel);
        dungeonMonsterPanels.Clear();
        
        // 情報購入済みの場合のみモンスター情報を表示
        if (d.isShowedInfo)
        {
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

    /// <summary>
    /// 数字欄の3行: 弱点／勇者のクリア見込み／防衛報酬（勇者が負けたら入る額）。
    /// 勇者側・魔王側のどちらに張るかを、この3行で見比べられるようにする。
    /// </summary>
    private static string BuildStatsText(DungeonData d, float clearProbability, bool weaknessKnown)
    {
        string weakness = ItemAttributeText.Weakness(d.requiredAttribute, weaknessKnown);
        if (!d.isShowedInfo) return $"{weakness}\nクリア見込み:？\n防衛報酬:？";

        int reward = d.GetDefenseReward(d.currentDungeonLevel);
        string rewardText = d.IsFunded ? $"{reward:N0}G" : "なし（未出資）";
        return $"{weakness}\nクリア見込み:{ClearProbabilityCalculator.FormatOutlook(clearProbability)}\n防衛報酬:{rewardText}";
    }
}