using UnityEngine;

/// <summary>
/// 2D追従カメラ（村シーン用）。ターゲットを滑らかに追い、マップ境界内にクランプする。
/// target 未配線なら何もしない（既存規約のnull-safe）。
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow2D : MonoBehaviour
{
    [SerializeField] private Transform target;
    [Tooltip("追従の滑らかさ（小さいほどキビキビ）")]
    [SerializeField] private float smoothTime = 0.12f;
    [Tooltip("カメラが出られないワールド境界（タイルマップの範囲に合わせる）")]
    [SerializeField] private Rect worldBounds = new Rect(-24f, -18f, 48f, 32f);

    private Camera cam;
    private Vector3 velocity;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        // シーン開始時にターゲット位置へスナップ（初回のスライド演出を防ぐ）
        if (target != null) transform.position = Clamp(TargetPosition());
    }

    private void LateUpdate()
    {
        if (target == null) return;
        transform.position = Vector3.SmoothDamp(transform.position, Clamp(TargetPosition()), ref velocity, smoothTime);
    }

    private Vector3 TargetPosition() =>
        new Vector3(target.position.x, target.position.y, transform.position.z);

    private Vector3 Clamp(Vector3 desired)
    {
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;

        // 境界が画面より狭い軸は中央固定
        desired.x = worldBounds.width <= halfW * 2f
            ? worldBounds.center.x
            : Mathf.Clamp(desired.x, worldBounds.xMin + halfW, worldBounds.xMax - halfW);
        desired.y = worldBounds.height <= halfH * 2f
            ? worldBounds.center.y
            : Mathf.Clamp(desired.y, worldBounds.yMin + halfH, worldBounds.yMax - halfH);
        return desired;
    }
}
