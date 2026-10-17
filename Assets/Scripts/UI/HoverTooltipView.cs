using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HoverTooltipTrigger から呼ばれる、カーソル位置に出る説明ツールチップ（シーンに1つ）。
/// 最前面の ScreenSpaceOverlay Canvas に置き、レイキャストは受けない。
/// カーソルの右下に出し、画面からはみ出す側はピボットを反転して左／上に出す。
/// 未配線の参照があっても例外を出さない。
/// </summary>
public class HoverTooltipView : MonoBehaviour
{
    public static HoverTooltipView Instance { get; private set; }

    [SerializeField] private Canvas canvas;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text bodyLabel;
    [SerializeField] private Sprite defaultBackground;
    [SerializeField] private Color defaultTitleColor = new(1f, 0.82f, 0.36f, 1f);
    [Tooltip("カーソルからのずらし量（Canvas 単位）")]
    [SerializeField] private Vector2 cursorOffset = new(22f, 26f);
    [Tooltip("画面端との余白（Canvas 単位）")]
    [SerializeField] private float screenMargin = 12f;

    private HoverTooltipTrigger _owner;

    private void Awake()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (Instance == null || Instance == this) Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        _owner = null;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    public void Show(HoverTooltipTrigger owner, string title, string body, Sprite bg, Color? titleColor, Vector2 screenPos)
    {
        if (panel == null) return;
        bool wasShown = panel.gameObject.activeSelf && _owner != null;
        _owner = owner;

        if (background != null)
        {
            var s = bg != null ? bg : defaultBackground;
            if (s != null) background.sprite = s;
        }
        if (titleLabel != null)
        {
            bool has = !string.IsNullOrEmpty(title);
            titleLabel.gameObject.SetActive(has);
            titleLabel.text = has ? title : string.Empty;
            titleLabel.color = titleColor ?? defaultTitleColor;
        }
        if (bodyLabel != null)
        {
            bool has = !string.IsNullOrEmpty(body);
            bodyLabel.gameObject.SetActive(has);
            bodyLabel.text = has ? body : string.Empty;
        }

        panel.gameObject.SetActive(true);
        panel.SetAsLastSibling();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        Place(screenPos);

        if (!wasShown)
        {
            UIFx.PanelOpen(panel.gameObject, 0.12f);
            UIFx.Pop(panel, 0.92f, 0.14f);
        }
    }

    public void Move(HoverTooltipTrigger owner, Vector2 screenPos)
    {
        if (owner != _owner || panel == null || !panel.gameObject.activeSelf) return;
        Place(screenPos);
    }

    public void Hide(HoverTooltipTrigger owner)
    {
        if (owner != _owner) return;
        _owner = null;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    /// <summary>カーソル位置へ置く。右・下にはみ出すならピボットを反転する。</summary>
    private void Place(Vector2 screenPos)
    {
        var root = canvas != null ? canvas.transform as RectTransform : panel.parent as RectTransform;
        if (root == null) return;
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPos, cam, out var local)) return;

        var area = root.rect;
        var size = panel.rect.size;
        bool flipX = local.x + cursorOffset.x + size.x > area.xMax - screenMargin;
        bool flipY = local.y - cursorOffset.y - size.y < area.yMin + screenMargin;

        panel.pivot = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        var pos = new Vector2(
            local.x + (flipX ? -cursorOffset.x : cursorOffset.x),
            local.y + (flipY ? cursorOffset.y : -cursorOffset.y));

        // 反転しても収まらないとき（巨大なツールチップ）は画面内へ押し戻す
        float minX = area.xMin + screenMargin + (flipX ? size.x : 0f);
        float maxX = area.xMax - screenMargin - (flipX ? 0f : size.x);
        float minY = area.yMin + screenMargin + (flipY ? 0f : size.y);
        float maxY = area.yMax - screenMargin - (flipY ? size.y : 0f);
        if (minX <= maxX) pos.x = Mathf.Clamp(pos.x, minX, maxX);
        if (minY <= maxY) pos.y = Mathf.Clamp(pos.y, minY, maxY);

        // panel は root 直下・中央アンカー前提でなくても動くよう、ワールド経由で置く
        panel.position = root.TransformPoint(pos);
    }
}
