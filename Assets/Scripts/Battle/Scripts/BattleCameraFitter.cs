using UnityEngine;

/// <summary>
/// 配信画面のカメラを 16:9 以外の画面（16:10・21:9 など）に合わせる。
/// UI（CanvasScaler: Expand、基準 1920x1080）は 16:9 の枠が必ず画面に収まるように拡縮するので、
/// ワールド（戦闘・街の背景）も同じ「16:9 の枠が収まる」大きさで映す:
///   縦長（16:10 など）… 横幅を 16:9 と同じに保つ（orthographicSize を広げる）→ 戦闘が配信枠からずれない
///   横長（21:9 など）… 縦の高さはそのまま（左右が広く映る）
/// 広く映った分は背景を同じ縦横比のまま拡大して覆い、画面外に置いてある目印（客の出入り口など）は描かない。
/// </summary>
[RequireComponent(typeof(Camera))]
public class BattleCameraFitter : MonoBehaviour
{
    [Tooltip("16:9 のときの orthographicSize（シーンの既定値）")]
    [SerializeField] private float baseOrthographicSize = 5f;
    [Tooltip("UI の基準の縦横比（1920x1080）")]
    [SerializeField] private float referenceAspect = 16f / 9f;
    [Tooltip("画面を覆うまで拡大する背景（同じ縦横比のまま）")]
    [SerializeField] private SpriteRenderer[] coverBackgrounds;
    [Tooltip("描かない目印（客の出入り口の白い四角など。位置だけ使う）")]
    [SerializeField] private SpriteRenderer[] hideRenderers;

    private Camera _camera;
    private Vector3[] _baseScales;
    private Vector2[] _baseSizes;
    private float _lastAspect = -1f;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        if (coverBackgrounds != null)
        {
            _baseScales = new Vector3[coverBackgrounds.Length];
            _baseSizes = new Vector2[coverBackgrounds.Length];
            for (int i = 0; i < coverBackgrounds.Length; i++)
            {
                var sr = coverBackgrounds[i];
                if (sr == null) continue;
                _baseScales[i] = sr.transform.localScale;
                // 初期は非アクティブのことがある（bounds が 0 になる）のでスプライトの大きさから出す
                _baseSizes[i] = sr.sprite != null ? Vector2.Scale(sr.sprite.bounds.size, sr.transform.lossyScale) : Vector2.zero;
            }
        }
        if (hideRenderers != null)
            foreach (var sr in hideRenderers) if (sr != null) sr.enabled = false;
        Apply();
    }

    private void LateUpdate()
    {
        if (_camera != null && !Mathf.Approximately(_camera.aspect, _lastAspect)) Apply();
    }

    private void Apply()
    {
        if (_camera == null || !_camera.orthographic) return;
        float aspect = Mathf.Max(0.1f, _camera.aspect);
        _lastAspect = aspect;
        _camera.orthographicSize = baseOrthographicSize * Mathf.Max(1f, referenceAspect / aspect);

        if (coverBackgrounds == null || _baseSizes == null) return;
        float viewH = _camera.orthographicSize * 2f;
        float viewW = viewH * aspect;
        for (int i = 0; i < coverBackgrounds.Length; i++)
        {
            var sr = coverBackgrounds[i];
            if (sr == null || _baseSizes[i].x <= 0f || _baseSizes[i].y <= 0f) continue;
            float k = Mathf.Max(1f, viewW / _baseSizes[i].x, viewH / _baseSizes[i].y);
            sr.transform.localScale = _baseScales[i] * k;
        }
    }
}
