using UnityEngine;

/// <summary>
/// 敵タップ（CharacterView.OnMouseDown）用の当たり判定を、表示中のスプライトの大きさに合わせ続ける。
/// 魔物ごとにスプライトが差し替わるので、固定サイズの BoxCollider2D だと当たりがずれるため。
/// 青スパの対象指定で使う（InterventionPresenter が OnCharacterClicked を購読）。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class SpriteClickCollider : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("見た目より少し大きめにして押しやすくする")]
    [SerializeField] private float padding = 1.15f;

    private BoxCollider2D _box;
    private Sprite _last;

    private void Awake()
    {
        _box = GetComponent<BoxCollider2D>();
        _box.isTrigger = true;
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        Fit();
    }

    private void LateUpdate()
    {
        if (spriteRenderer != null && spriteRenderer.sprite != _last) Fit();
    }

    private void Fit()
    {
        if (_box == null || spriteRenderer == null) return;
        _last = spriteRenderer.sprite;
        _box.enabled = _last != null;
        if (_last == null) return;

        // SpriteRenderer がこのオブジェクトの子でもローカル座標で合うよう、ローカル空間の bounds に変換する
        var b = _last.bounds;
        var srT = spriteRenderer.transform;
        Vector3 min = transform.InverseTransformPoint(srT.TransformPoint(b.min));
        Vector3 max = transform.InverseTransformPoint(srT.TransformPoint(b.max));
        var size = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
        _box.size = size * padding;
        _box.offset = (Vector2)((min + max) * 0.5f);
    }
}
