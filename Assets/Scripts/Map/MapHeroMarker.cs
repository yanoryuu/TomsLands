using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 地図上の勇者ドットキャラ。次の行き先のアイコンのそばに立ち、SPUM の移動モーションでその場で足踏みし続ける。
/// 見た目は SPUM の勇者（Hero_SPUM.prefab）。地図の Canvas は Screen Space - Overlay で SpriteRenderer を直接置けないため、
/// 画面外の撮影台（専用カメラ＋RenderTexture）で SPUM を写し、その絵を RawImage で地図に出す。
/// </summary>
public class MapHeroMarker : MonoBehaviour
{
    [Header("表示")]
    [Tooltip("撮影した勇者を映す RawImage（足元が下端中央に来る）")]
    [SerializeField] private RawImage view;

    [Header("SPUM")]
    [Tooltip("地図に立たせる SPUM ユニット（Hero_SPUM.prefab）")]
    [SerializeField] private SPUM_Prefabs heroPrefab;
    [SerializeField] private PlayerState pose = PlayerState.MOVE;
    [Tooltip("撮影範囲の高さ（ワールド単位）。小さいほど大きく写る")]
    [SerializeField] private float viewHeight = 1f;
    [Tooltip("足元より下に空ける余白（ワールド単位。SPUM の影が切れない程度）")]
    [SerializeField] private float footMargin = 0.08f;
    [Tooltip("RenderTexture の縦解像度の上限（基本は RawImage の画面上の大きさに合わせる）")]
    [SerializeField] private int maxTextureHeight = 512;
    [Tooltip("撮影台を置くワールド座標（どのカメラにも写らない遠く）")]
    [SerializeField] private Vector3 stageOrigin = new(0f, -5000f, 0f);
    [Tooltip("撮影台に使うレイヤー（専用カメラはこのレイヤーだけを写す）")]
    [SerializeField, Range(0, 31)] private int stageLayer = 31;

    private RectTransform rect;
    private RectTransform Rect => rect != null ? rect : rect = transform as RectTransform;

    private GameObject stage;
    private Camera stageCamera;
    private SPUM_Prefabs hero;
    private Vector3 heroBaseScale = Vector3.one;
    private RenderTexture texture;
    private bool shown;
    private bool faceRight = true;

    /// <summary>指定アイコンのそば（offset はアイコン中心からのずれ）に立たせる。</summary>
    public void Show(RectTransform anchor, Vector2 offset)
    {
        if (anchor == null || Rect == null)
        {
            Hide();
            return;
        }

        // アイコンと親が違っても同じ見た目の位置に来るよう、ワールド座標経由で合わせる
        rect.position = anchor.position;
        rect.anchoredPosition += offset;
        rect.SetAsLastSibling();

        // アイコンの方を向く（左に立てば右向き、右に立てば左向き）
        faceRight = offset.x <= 0f;

        shown = true;
        gameObject.SetActive(true);
        EnsureStage();
        SyncStage();
        UIFx.Pop(transform, 0.6f, 0.25f);
    }

    public void Hide()
    {
        shown = false;
        SyncStage();
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (shown) SyncStage();
    }

    private void OnDisable()
    {
        // 地図ごと閉じられたときも撮影を止める
        if (stage != null) stage.SetActive(false);
    }

    private void OnDestroy()
    {
        if (stage != null) Destroy(stage);
        if (texture != null)
        {
            if (view != null && view.texture == texture) view.texture = null;
            texture.Release();
            Destroy(texture);
        }
    }

    /// <summary>撮影台（SPUM＋専用カメラ）と RenderTexture を初回だけ作る。</summary>
    private void EnsureStage()
    {
        if (stage != null || heroPrefab == null || view == null) return;

        stage = new GameObject("MapHeroStage");
        stage.layer = stageLayer;
        stage.transform.position = stageOrigin;

        hero = Instantiate(heroPrefab, stage.transform);
        hero.transform.localPosition = Vector3.zero;
        hero.transform.localRotation = Quaternion.identity;
        heroBaseScale = hero.transform.localScale;
        SetLayerRecursively(hero.transform, stageLayer);
        if (hero._anim != null) hero.OverrideControllerInit();

        // ドット絵が崩れないよう、RawImage の画面上のピクセル数と同じ解像度で撮る
        var viewRect = view.rectTransform.rect;
        float scale = view.canvas != null ? view.canvas.scaleFactor : 1f;
        int h = Mathf.Clamp(Mathf.RoundToInt(viewRect.height * scale), 32, Mathf.Max(32, maxTextureHeight));
        float aspect = viewRect.height > 0f ? viewRect.width / viewRect.height : 1f;
        int w = Mathf.Max(32, Mathf.RoundToInt(h * aspect));
        texture = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
        {
            name = "MapHeroRT",
            filterMode = FilterMode.Point,
            antiAliasing = 1,
        };
        texture.Create();
        view.texture = texture;

        var camGo = new GameObject("MapHeroCamera");
        camGo.layer = stageLayer;
        camGo.transform.SetParent(stage.transform, false);
        // 足元（SPUM の原点）が画像の下端から footMargin 上に来るように置く
        camGo.transform.localPosition = new Vector3(0f, viewHeight * 0.5f - footMargin, -10f);

        stageCamera = camGo.AddComponent<Camera>();
        stageCamera.orthographic = true;
        stageCamera.orthographicSize = viewHeight * 0.5f;
        stageCamera.clearFlags = CameraClearFlags.SolidColor;
        stageCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        stageCamera.cullingMask = 1 << stageLayer;
        stageCamera.nearClipPlane = 0.1f;
        stageCamera.farClipPlane = 30f;
        stageCamera.allowHDR = false;
        stageCamera.allowMSAA = false;
        stageCamera.depth = -100f;
        stageCamera.targetTexture = texture;

        var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = false;
        urp.renderShadows = false;
        urp.requiresColorOption = CameraOverrideOption.Off;
        urp.requiresDepthOption = CameraOverrideOption.Off;
    }

    /// <summary>表示中かつ地図が開いているときだけ撮影台を動かす。</summary>
    private void SyncStage()
    {
        if (stage == null) return;
        bool active = shown && isActiveAndEnabled;
        stage.SetActive(active);
        if (!active || hero == null) return;

        // SPUM は右向きで X を負にする（PlayerMove / CustomerCharacter と同じ規則）
        var s = heroBaseScale;
        hero.transform.localScale = faceRight
            ? new Vector3(-Mathf.Abs(s.x), s.y, s.z)
            : new Vector3(Mathf.Abs(s.x), s.y, s.z);

        // 非アクティブにすると Animator が初期状態に戻るので、出すたびにモーションを指定し直す
        if (hero._anim != null && hero.allListsHaveItemsExist()) hero.PlayAnimation(pose, 0);
    }

    private static void SetLayerRecursively(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i), layer);
    }
}
