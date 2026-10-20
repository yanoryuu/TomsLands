using TMPro;
using UnityEngine;

/// <summary>
/// 地図のダンジョン名札（アイコン直下のプレート）。
/// 1行目: 名前と Lv、2行目: 弱点とクリア確率。NEXT リボン・CLEAR スタンプ・失敗バッジもここで出し分ける。
/// 参照は未配線でも落ちないようにすべて null チェックする。
/// </summary>
public class MapDungeonLabel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI weaknessText;
    [SerializeField] private TextMeshProUGUI probabilityText;

    [Header("状態")]
    [SerializeField] private CanvasGroup plateGroup;   // Clear 時に減光する範囲
    [SerializeField] private GameObject clearStamp;
    [SerializeField] private GameObject failBadge;

    [Header("次の行き先")]
    [SerializeField] private GameObject nextRibbon;
    [SerializeField] private TextMeshProUGUI nextText;

    [SerializeField, Range(0f, 1f)] private float clearedAlpha = 0.55f;
    [Tooltip("「その次」の行き先に出すリボンの濃さ（NEXT より薄くして、次の行き先と見分ける）")]
    [SerializeField, Range(0f, 1f)] private float afterNextAlpha = 0.55f;

    private CanvasGroup ribbonGroup;

    public void Set(MapDungeonDisplay d)
    {
        if (d == null) return;

        if (nameText) nameText.text = d.Name;
        if (levelText) levelText.text = d.LevelText;
        if (weaknessText) weaknessText.text = d.WeaknessText;
        if (probabilityText) probabilityText.text = d.ProbabilityText;

        bool cleared = d.Status == DungeonStatus.Clear;
        if (plateGroup) plateGroup.alpha = cleared ? clearedAlpha : 1f;
        if (clearStamp) clearStamp.SetActive(cleared);
        if (failBadge) failBadge.SetActive(d.Status == DungeonStatus.Fail);

        // 次の行き先には NEXT リボン、その次の行き先には同じリボンを薄くして「その次」と出す
        bool afterNext = d.IsAfterNext && !d.IsNext;
        if (nextRibbon)
        {
            bool wasShown = nextRibbon.activeSelf;
            nextRibbon.SetActive(d.IsNext || afterNext);
            if (ribbonGroup == null && !nextRibbon.TryGetComponent(out ribbonGroup))
                ribbonGroup = nextRibbon.AddComponent<CanvasGroup>();
            ribbonGroup.alpha = afterNext ? afterNextAlpha : 1f;
            if (d.IsNext && !wasShown) UIFx.Pop(nextRibbon.transform, 0.85f, 0.22f);
        }
        if (nextText) nextText.text = afterNext ? "その次" : $"<color=#E8A33D>NEXT</color>  {d.NextText}";
    }
}
