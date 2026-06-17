using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ターン進行フェーズ（イベント/仕入れ/商品陳列/営業）のUI出し分け。
/// 同一の Shop ホーム内で、現フェーズに対応するボタングループだけを表示する。
/// </summary>
public class TurnPhaseView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI phaseLabel;
    [Tooltip("次へ/スキップ ボタン（仕入れ・陳列フェーズで表示）")]
    [SerializeField] private Button advanceButton;

    [Header("フェーズ別ボタングループ")]
    [SerializeField] private GameObject procurementGroup; // 仕入れ：情報屋/鍛冶屋/道具屋/ダンジョンLv/広告/ヒーロー/マップ
    [SerializeField] private GameObject displayGroup;     // 陳列：陳列設定/預言者/需要ダッシュボード
    [SerializeField] private GameObject salesGroup;       // 営業：営業開始

    public Subject<Unit> OnAdvanceClicked { get; } = new();

    private void Awake()
    {
        if (advanceButton != null)
            advanceButton.onClick.AddListener(() => OnAdvanceClicked.OnNext(Unit.Default));
    }

    /// <summary>現フェーズに応じてボタングループとラベル・次へボタンを切り替える。</summary>
    public void ShowForPhase(TurnPhase phase)
    {
        SetActiveSafe(procurementGroup, phase == TurnPhase.Procurement);
        SetActiveSafe(displayGroup, phase == TurnPhase.Display);
        SetActiveSafe(salesGroup, phase == TurnPhase.Sales);

        // 「次へ」は仕入れ・陳列のみ。営業は salesGroup の「営業開始」で進み、イベントはポップアップ任せ。
        bool showAdvance = phase == TurnPhase.Procurement || phase == TurnPhase.Display;
        if (advanceButton != null) advanceButton.gameObject.SetActive(showAdvance);

        if (phaseLabel != null) phaseLabel.text = ToLabel(phase);
    }

    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    private static string ToLabel(TurnPhase phase) => phase switch
    {
        TurnPhase.Event => "イベント",
        TurnPhase.Procurement => "仕入れ",
        TurnPhase.Display => "商品陳列",
        TurnPhase.Sales => "営業",
        _ => "",
    };
}
