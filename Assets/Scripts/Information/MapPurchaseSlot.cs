using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 情報屋・地図タブの1行（ダンジョン）。
/// 行クリックで選択し、購入は右の詳細パネル（MapInfoView）から行う（鍛冶屋と同じ操作感）。
/// </summary>
public class MapPurchaseSlot : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private Button infoButton;
    [SerializeField] private Button purchaseButton;

    /// <summary>行がクリックされた（選択→右の詳細に表示）。</summary>
    public Subject<DungeonName> OnSelected { get; } = new();

    private DungeonName dungeonKey;
    private Image rowBackground;

    public DungeonName DungeonKey => dungeonKey;

    private void Awake()
    {
        // 行のどこを押しても選択できるようにする（購入は右の詳細パネルから）
        infoButton?.onClick.AddListener(Select);
        purchaseButton?.onClick.AddListener(Select);

        var bg = transform.Find("BackGround");
        if (bg != null)
        {
            rowBackground = bg.GetComponent<Image>();
            var btn = bg.GetComponent<Button>();
            if (btn == null) btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(Select);
        }
    }

    private void Select()
    {
        OnSelected.OnNext(dungeonKey);
    }

    public void SetMapInfo(DungeonName key, string dungeonName, Sprite sprite, int price, bool purchased, bool sellable)
    {
        dungeonKey = key;
        if (icon) icon.sprite = sprite;
        if (nameText) nameText.text = dungeonName;
        if (priceText)
        {
            if (purchased)
            {
                priceText.text = "購入済み";
                priceText.color = new Color(0.55f, 0.9f, 0.55f);
            }
            else if (!sellable)
            {
                // コスト表にないダンジョン（魔王城など）は情報を売っていない
                priceText.text = "取扱なし";
                priceText.color = new Color(0.7f, 0.7f, 0.7f);
            }
            else
            {
                priceText.text = $"{price:N0}G";
                priceText.color = Color.white;
            }
        }
        // 行には購入ボタンを出さない（詳細パネルで買う）
        if (purchaseButton) purchaseButton.gameObject.SetActive(false);
    }

    /// <summary>この行の選択ハイライトを切り替える。</summary>
    public void SetSelected(bool selected)
    {
        if (rowBackground != null)
            rowBackground.color = selected ? new Color(1f, 0.9f, 0.65f) : Color.white;
    }

    private void OnDestroy()
    {
        infoButton?.onClick.RemoveAllListeners();
        purchaseButton?.onClick.RemoveAllListeners();
        OnSelected.Dispose();
    }
}
