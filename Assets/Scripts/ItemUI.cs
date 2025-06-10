using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Button sellButton;
    [SerializeField] private Image popularIcon; 

    public Subject<Unit> OnPurchaseClicked { get; } = new();
    public Subject<Unit> OnSellClicked { get; } = new();

    [SerializeField] private Image itemIconImage;

    public void SetItem(string name, int price, int stock, bool isPopular, Sprite icon)
    {
        nameText.text = name;
        UpdatePrice(price);
        UpdateStock(stock);
        popularIcon.gameObject.SetActive(isPopular);
        itemIconImage.sprite = icon;
    }


    public void BindPrice(ReactiveProperty<int> price)
    {
        price.Subscribe(p => UpdatePrice(p)).AddTo(this);
    }

    public void BindStock(ReactiveProperty<int> stock)
    {
        stock.Subscribe(s => UpdateStock(s)).AddTo(this);
    }

    public void BindPopularity(ReactiveProperty<bool> isPopular)
    {
        isPopular.Subscribe(flag => popularIcon.gameObject.SetActive(flag)).AddTo(this);
    }

    private void Awake()
    {
        purchaseButton.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
        sellButton.onClick.AddListener(() => OnSellClicked.OnNext(Unit.Default));
    }

    public void UpdatePrice(int price)
    {
        priceText.text = $"{price}G";
    }

    public void UpdateStock(int stock)
    {
        stockText.text = $"Stock: {stock}";
    }
}