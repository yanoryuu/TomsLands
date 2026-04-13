using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 広告購入画面の1行表示用UIコンポーネント。
/// アイコン・広告名・コストを表示する。
/// 1タップ目で選択（プレビュー表示）、2タップ目で購入確定。
/// 購入判定は Presenter が管理する。
/// Prefab化してAdvertisementViewから生成される。
/// </summary>
public class AdvertisementSlotUI : MonoBehaviour
{
    [Header("基本情報")]
    [SerializeField] private Image adIcon;
    [SerializeField] private TextMeshProUGUI adNameText;
    [SerializeField] private TextMeshProUGUI costText;

    [Header("スロット選択")]
    [Tooltip("スロット本体を押すボタン")]
    [SerializeField] private Button slotSelectButton;

    [Tooltip("選択中に表示する枠のGameObject")]
    [SerializeField] private GameObject selectionFrame;

    [Tooltip("スロットの背景Image（選択時に色が変わる）")]
    [SerializeField] private Image backgroundImage;

    [Tooltip("通常時の背景色")]
    [SerializeField] private Color normalColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);

    [Tooltip("選択時の背景色")]
    [SerializeField] private Color selectedColor = new Color(0.3f, 0.5f, 0.8f, 0.9f);

    /// <summary>スロットがタップされた時に広告データを通知する</summary>
    public Subject<AdvertisementData> OnSlotSelected { get; } = new();

    /// <summary>このスロットに紐づく広告データ</summary>
    public AdvertisementData AdData { get; private set; }

    /// <summary>
    /// スロットを広告データで初期化する
    /// </summary>
    public void Setup(AdvertisementData adData, int discountedCost, bool canPurchase)
    {
        AdData = adData;

        // アイコン
        if (adIcon != null && adData.icon != null)
            adIcon.sprite = adData.icon;

        // 広告名
        if (adNameText != null)
            adNameText.text = adData.advertisementName;

        // コスト表示（割引がある場合は取り消し線風に元値も表示）
        UpdateCost(discountedCost);

        // スロットタップ（押すとSubjectからイベント発火）
        if (slotSelectButton != null)
            slotSelectButton.onClick.AddListener(() => OnSlotSelected.OnNext(adData));

        // 選択枠を初期非表示
        if (selectionFrame != null)
            selectionFrame.SetActive(false);
    }

    /// <summary>
    /// 選択状態を設定する。選択中は枠を表示し背景色を変更する。
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (selectionFrame != null)
            selectionFrame.SetActive(selected);

        if (backgroundImage != null)
            backgroundImage.color = selected ? selectedColor : normalColor;
    }


    /// <summary>
    /// コスト表示を更新する（割引率変更時）
    /// </summary>
    public void UpdateCost(int discountedCost)
    {
        if (costText == null || AdData == null) return;

        if (discountedCost < AdData.cost)
            costText.text = $"<s>{AdData.cost}G</s> → {discountedCost}G";
        else
            costText.text = $"{discountedCost}G";
    }
}

