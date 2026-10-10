using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 選択できる枠の「内側」に別のボタンがある部品（記事カード内のスクラップボタンなど）用。
/// Automatic ナビは内側のボタンを方向の候補にしないため、枠が選ばれた瞬間に
/// 「指定方向 = 内側のボタン、その他の方向 = 通常の Automatic と同じ移動先」を Explicit で組み直す。
/// 内側のボタンが非表示・操作不可のときは通常どおり。
/// </summary>
[DisallowMultipleComponent]
public sealed class InnerSelectableNavigation : MonoBehaviour, ISelectHandler
{
    public enum Direction { Right, Left, Up, Down }

    [SerializeField] private Selectable inner;
    [SerializeField] private Direction direction = Direction.Right;

    /// <summary>コードから付与・設定する。</summary>
    public static InnerSelectableNavigation Attach(Selectable outer, Selectable inner, Direction direction = Direction.Right)
    {
        if (outer == null) return null;
        var nav = outer.GetComponent<InnerSelectableNavigation>();
        if (nav == null) nav = outer.gameObject.AddComponent<InnerSelectableNavigation>();
        nav.inner = inner;
        nav.direction = direction;
        return nav;
    }

    public void OnSelect(BaseEventData eventData)
    {
        var self = GetComponent<Selectable>();
        if (self == null) return;
        var rot = transform.rotation;
        bool innerUsable = inner != null && inner.isActiveAndEnabled && inner.IsInteractable()
                           && inner.navigation.mode != Navigation.Mode.None;

        Selectable Pick(Direction d, Vector3 axis)
            => innerUsable && d == direction ? inner : self.FindSelectable(rot * axis);

        self.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = Pick(Direction.Up, Vector3.up),
            selectOnDown = Pick(Direction.Down, Vector3.down),
            selectOnLeft = Pick(Direction.Left, Vector3.left),
            selectOnRight = Pick(Direction.Right, Vector3.right),
        };
    }
}
