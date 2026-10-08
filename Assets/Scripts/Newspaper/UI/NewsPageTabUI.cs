using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 紙面下の面タブ1つ（一面 / 二面 / 市況 / うわさ / 自店）。
/// その社が保有する面だけ表示する。「自店」は社に依存せず常設（Docs/News_Spec.md §10.3）。
/// </summary>
public class NewsPageTabUI : MonoBehaviour
{
    public const string ShopPage = "shop";

    [Tooltip("front / second / market / rumor / shop")]
    [SerializeField] private string pageKey = "front";
    [SerializeField] private Button button;
    [SerializeField] private Image background;
    [SerializeField] private Sprite normalSprite;    // 11−0面タブ
    [SerializeField] private Sprite selectedSprite;  // 11−1面タブ
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Color normalTextColor = new Color32(0xF7, 0xEB, 0xD2, 0xFF);
    [SerializeField] private Color selectedTextColor = new Color32(0x4A, 0x2E, 0x1E, 0xFF);

    public string PageKey => pageKey;
    public event Action<string> OnClicked;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(() => OnClicked?.Invoke(pageKey));
    }

    public void SetState(bool visible, bool selected)
    {
        gameObject.SetActive(visible);
        if (background != null)
        {
            var sprite = selected ? selectedSprite : normalSprite;
            if (sprite != null) background.sprite = sprite;
        }
        if (label != null) label.color = selected ? selectedTextColor : normalTextColor;
    }
}
