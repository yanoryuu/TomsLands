using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// ホバーして一定時間（既定 0.5 秒）たつと、カーソル位置に HoverTooltipView で説明を出すトリガー。
/// 待っている間にカーソルが外れたら出さない。外れたら・押したら・非表示になったら即消す。
/// 押下・ドラッグは処理しない（Button やスワイプの操作を邪魔しない）。
/// 表示先の HoverTooltipView が無くてもエラーにならない。
/// </summary>
public class HoverTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerDownHandler
{
    [SerializeField] private string title;
    [SerializeField, TextArea(2, 4)] private string body;
    [Tooltip("ツールチップの背景（空なら HoverTooltipView の既定）")]
    [SerializeField] private Sprite background;
    [Tooltip("見出しの色（a=0 なら既定色）")]
    [SerializeField] private Color titleColor = new(0f, 0f, 0f, 0f);
    [Min(0f)] [SerializeField] private float delay = 0.5f;

    private bool _hovering;
    private bool _suppressed;
    private bool _shown;
    private float _timer;
    private Vector2 _pointer;

    public void SetContent(string newTitle, string newBody, Sprite newBackground = null, Color? newTitleColor = null)
    {
        title = newTitle;
        body = newBody;
        if (newBackground != null) background = newBackground;
        if (newTitleColor.HasValue) titleColor = newTitleColor.Value;
        if (_shown) Show(); // 表示中なら内容を差し替える
    }

    public void OnPointerEnter(PointerEventData e)
    {
        _hovering = true;
        _suppressed = false;
        _timer = 0f;
        _pointer = e.position;
    }

    public void OnPointerMove(PointerEventData e)
    {
        _pointer = e.position;
        if (_shown) HoverTooltipView.Instance?.Move(this, _pointer);
    }

    public void OnPointerExit(PointerEventData e)
    {
        _hovering = false;
        Hide();
    }

    public void OnPointerDown(PointerEventData e)
    {
        // 押したら消して、いったん外れるまで出さない
        _suppressed = true;
        Hide();
    }

    private void Update()
    {
        if (!_hovering || _suppressed || _shown) return;
        _timer += Time.unscaledDeltaTime;
        if (_timer >= delay) Show();
    }

    private void Show()
    {
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body)) return;
        var view = HoverTooltipView.Instance;
        if (view == null) return;
        _shown = true;
        view.Show(this, title, body, background, titleColor.a > 0f ? titleColor : (Color?)null, _pointer);
    }

    private void Hide()
    {
        _timer = 0f;
        if (!_shown) return;
        _shown = false;
        HoverTooltipView.Instance?.Hide(this);
    }

    private void OnDisable()
    {
        _hovering = false;
        Hide();
    }
}
