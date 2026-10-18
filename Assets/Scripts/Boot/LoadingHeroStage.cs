using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// ローディング画面で SPUM の勇者をその場で歩かせる（PlayerState.MOVE）。
/// ローディング画面は Screen Space - Overlay の Canvas なので SpriteRenderer を直接置けない。
/// MapHeroMarker と同じく、画面外の撮影台（専用カメラ＋RenderTexture）で SPUM を写し、その絵を RawImage に出す。
/// 撮影台はシーンをまたいで使うため DontDestroyOnLoad に置き、表示中以外はカメラごと止めて負荷をかけない。
/// 参照は任意（未設定なら何も出さないだけで、ローディング自体は止めない）。
/// </summary>
public sealed class LoadingHeroStage : MonoBehaviour
{
    [Header("表示")]
    [Tooltip("撮影した勇者を映す RawImage（足元が下端中央に来る）")]
    [SerializeField] private RawImage view;

    [Header("SPUM")]
    [Tooltip("歩かせる SPUM ユニット（Hero_SPUM.prefab）")]
    [SerializeField] private SPUM_Prefabs heroPrefab;
    [SerializeField] private PlayerState pose = PlayerState.MOVE;
    [Tooltip("右向きに歩かせる")]
    [SerializeField] private bool faceRight = true;
    [Tooltip("撮影範囲の高さ（ワールド単位）。小さいほど大きく写る")]
    [SerializeField] private float viewHeight = 1f;
    [Tooltip("足元より下に空ける余白（ワールド単位。SPUM の影が切れない程度）")]
    [SerializeField] private float footMargin = 0.08f;
    [Tooltip("RenderTexture の縦解像度の上限")]
    [SerializeField] private int maxTextureHeight = 512;
    [Tooltip("撮影台を置くワールド座標（どのカメラにも写らない遠く。地図の勇者 MapHeroMarker の撮影台とも離す）")]
    [SerializeField] private Vector3 stageOrigin = new(5000f, -6000f, 0f);
    [Tooltip("撮影台に使うレイヤー（専用カメラはこのレイヤーだけを写す）")]
    [SerializeField, Range(0, 31)] private int stageLayer = 31;

    private GameObject stage;
    private SPUM_Prefabs hero;
    private RenderTexture texture;
    private bool playing;

    /// <summary>撮影を始めて勇者を歩かせる。何度呼んでもよい。</summary>
    public void Play()
    {
        playing = true;
        EnsureStage();
        SyncStage();
    }

    /// <summary>撮影を止める（カメラ・Animator ともに止まる）。</summary>
    public void Stop()
    {
        playing = false;
        SyncStage();
    }

    private void OnDisable()
    {
        if (stage != null) stage.SetActive(false);
    }

    private void OnEnable()
    {
        if (playing) SyncStage();
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

        stage = new GameObject("LoadingHeroStage") { layer = stageLayer };
        stage.transform.position = stageOrigin;
        // ローディング画面と同じくシーン遷移で消えないようにする（撮影台はルートに置く必要がある）
        DontDestroyOnLoad(stage);

        hero = Instantiate(heroPrefab, stage.transform);
        hero.transform.localPosition = Vector3.zero;
        hero.transform.localRotation = Quaternion.identity;
        var s = hero.transform.localScale;
        // SPUM は右向きで X を負にする（PlayerMove / CustomerCharacter と同じ規則）
        hero.transform.localScale = faceRight
            ? new Vector3(-Mathf.Abs(s.x), s.y, s.z)
            : new Vector3(Mathf.Abs(s.x), s.y, s.z);
        SetLayerRecursively(hero.transform, stageLayer);
        if (hero._anim != null)
        {
            // timeScale=0 の間でも歩き続けるようにする
            hero._anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            hero.OverrideControllerInit();
        }

        // ドット絵が崩れないよう、RawImage の画面上のピクセル数に近い解像度で撮る
        var viewRect = view.rectTransform.rect;
        float scale = view.canvas != null ? view.canvas.scaleFactor : 1f;
        int h = Mathf.Clamp(Mathf.RoundToInt(viewRect.height * scale), 32, Mathf.Max(32, maxTextureHeight));
        float aspect = viewRect.height > 0f ? viewRect.width / viewRect.height : 1f;
        int w = Mathf.Max(32, Mathf.RoundToInt(h * aspect));
        texture = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
        {
            name = "LoadingHeroRT",
            filterMode = FilterMode.Point,
            antiAliasing = 1,
        };
        texture.Create();
        view.texture = texture;

        var camGo = new GameObject("LoadingHeroCamera") { layer = stageLayer };
        camGo.transform.SetParent(stage.transform, false);
        // 足元（SPUM の原点）が画像の下端から footMargin 上に来るように置く
        camGo.transform.localPosition = new Vector3(0f, viewHeight * 0.5f - footMargin, -10f);

        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = viewHeight * 0.5f;
        cam.aspect = aspect;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.cullingMask = 1 << stageLayer;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 30f;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.depth = -100f;
        cam.targetTexture = texture;

        var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = false;
        urp.renderShadows = false;
        urp.requiresColorOption = CameraOverrideOption.Off;
        urp.requiresDepthOption = CameraOverrideOption.Off;
    }

    /// <summary>再生中かつ自身が有効なときだけ撮影台を動かす。</summary>
    private void SyncStage()
    {
        if (view != null) view.enabled = playing && stage != null;
        if (stage == null) return;
        bool active = playing && isActiveAndEnabled;
        stage.SetActive(active);
        // 非アクティブにすると Animator が初期状態に戻るので、出すたびにモーションを指定し直す
        if (active && hero != null && hero._anim != null && hero.allListsHaveItemsExist())
            hero.PlayAnimation(pose, 0);
    }

    private static void SetLayerRecursively(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i), layer);
    }
}
