using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 村マップ上の施設区画（歩ける村の1建物）。
/// 見た目: 空き地（立て札）→ 建設済み（レベルに応じた段階スプライト）+ 施設アイコンの看板。
/// プレイヤー（PlayerMove）が Trigger に入ると吹き出しを出し、
/// 決定キー（E/Enter/Space）またはクリックで OnInteract を発火する。
/// 参照は未配線（null）でも動作する。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class FacilityPlot : MonoBehaviour
{
    [Tooltip("対応する施設ID（VillageFacilityData.facilityId）")]
    [SerializeField] private string facilityId;

    [Header("見た目")]
    [Tooltip("建物本体。stageSprites[0]=空き地(立て札) / [1]=Lv1 / [2]=Lv2 / [3]=Lv3（足りない分は最後を使う）")]
    [SerializeField] private SpriteRenderer buildingRenderer;
    [SerializeField] private Sprite[] stageSprites;
    [Tooltip("施設アイコンの看板（icon未設定の施設では非表示）")]
    [SerializeField] private SpriteRenderer signIconRenderer;
    [Tooltip("未解禁（領主館ゲート）のときに出す表示")]
    [SerializeField] private GameObject lockedSign;

    [Header("インタラクション")]
    [Tooltip("接近時に出す吹き出し（「調べる」）")]
    [SerializeField] private GameObject bubble;
    [Tooltip("吹き出しの施設名（ワールド空間TMPでもUGUIでも可）")]
    [SerializeField] private TMP_Text bubbleText;

    /// <summary>この区画が調べられた（facilityIdを通知）。</summary>
    public Subject<string> OnInteract { get; } = new();

    public string FacilityId => facilityId;

    private bool playerInside;

    private void Awake()
    {
        if (bubble != null) bubble.SetActive(false);
    }

    /// <summary>Presenter からの表示更新。level=0 は空き地。</summary>
    public void SetState(int level, bool lockedByGate, Sprite icon, string facilityName)
    {
        if (buildingRenderer != null && stageSprites != null && stageSprites.Length > 0)
        {
            int index = Mathf.Clamp(level, 0, stageSprites.Length - 1);
            buildingRenderer.sprite = stageSprites[index];
        }
        if (signIconRenderer != null)
        {
            signIconRenderer.sprite = icon;
            signIconRenderer.enabled = icon != null;
        }
        if (lockedSign != null) lockedSign.SetActive(lockedByGate && level == 0);
        if (bubbleText != null) bubbleText.text = facilityName;
    }

    /// <summary>投資直後の建設演出（ポップ）。</summary>
    public void PlayBuildEffect()
    {
        if (buildingRenderer == null) return;
        var t = buildingRenderer.transform;
        t.DOKill();
        t.localScale = Vector3.one * 0.6f;
        t.DOScale(1f, 0.45f).SetEase(Ease.OutBack).SetLink(buildingRenderer.gameObject);
    }

    private void Update()
    {
        if (!playerInside) return;

        var kb = Keyboard.current;
        bool pressed = kb != null &&
            (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
        if (pressed && !string.IsNullOrEmpty(facilityId))
            OnInteract.OnNext(facilityId);
    }

    private void OnMouseUpAsButton()
    {
        // クリック/タップでも調べられる（近接していなくても可）
        if (!string.IsNullOrEmpty(facilityId))
            OnInteract.OnNext(facilityId);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMove>() == null) return;
        playerInside = true;
        if (bubble != null) bubble.SetActive(true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMove>() == null) return;
        playerInside = false;
        if (bubble != null) bubble.SetActive(false);
    }

    private void OnDestroy()
    {
        OnInteract.Dispose();
    }
}
