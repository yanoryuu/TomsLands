using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 広告購入画面のView。
/// 広告一覧の表示・購入操作・現在のステータス数値表示を担当する。
/// 広告スロットを選択すると、そのステータス上昇量のプレビューを表示する。
/// バズ状態表示・ゲージ表示はこのViewでは行わない。
/// バズ演出はターン開始時に BuzzAnnounceView が担当する。
/// パネルの表示/非表示は GamePanelManager が管理する。
/// </summary>
public class AdvertisementView : MonoBehaviour
{
    // =====================================================
    // 広告リスト
    // =====================================================
    [Header("広告リスト")]
    [Tooltip("広告スロットを並べる親Transform（ScrollViewのContent等）")]
    [SerializeField] private Transform adListParent;

    [Tooltip("広告スロットのPrefab（AdvertisementSlotUI をアタッチしたもの）")]
    [SerializeField] private GameObject adSlotPrefab;

    // =====================================================
    // ステータス数値表示
    // =====================================================
    [Header("ステータス表示")]
    [Tooltip("信頼度の数値テキスト")]
    [SerializeField] private TextMeshProUGUI trustText;

    [Tooltip("注目度の数値テキスト")]
    [SerializeField] private TextMeshProUGUI attentionText;

    [Tooltip("拡散力の数値テキスト")]
    [SerializeField] private TextMeshProUGUI spreadText;

    [Tooltip("顧客維持力の数値テキスト")]
    [SerializeField] private TextMeshProUGUI retentionText;

    [Tooltip("フォロワー数の数値テキスト")]
    [SerializeField] private TextMeshProUGUI followerText;

    // =====================================================
    // 効果プレビュー（広告選択時に上昇量を表示）
    // =====================================================
    [Header("効果プレビュー")]

    [Tooltip("信頼度の上昇プレビュー（例: +40）")]
    [SerializeField] private TextMeshProUGUI trustPreviewText;

    [Tooltip("注目度の上昇プレビュー")]
    [SerializeField] private TextMeshProUGUI attentionPreviewText;

    [Tooltip("拡散力の上昇プレビュー")]
    [SerializeField] private TextMeshProUGUI spreadPreviewText;

    [Tooltip("顧客維持力の上昇プレビュー")]
    [SerializeField] private TextMeshProUGUI retentionPreviewText;

    [Tooltip("フォロワーの上昇プレビュー")]
    [SerializeField] private TextMeshProUGUI followerPreviewText;

    // =====================================================
    // ボタン
    // =====================================================
    [Header("ボタン")]
    [SerializeField] private Button closeButton;

    // =====================================================
    // ステータス説明ボタン（タップで説明Popup表示）
    // =====================================================
    [Header("ステータス説明ボタン")]
    [Tooltip("信頼度の領域に付けるButton")]
    [SerializeField] private Button trustInfoButton;

    [Tooltip("注目度の領域に付けるButton")]
    [SerializeField] private Button attentionInfoButton;

    [Tooltip("拡散力の領域に付けるButton")]
    [SerializeField] private Button spreadInfoButton;

    [Tooltip("顧客維持力の領域に付けるButton")]
    [SerializeField] private Button retentionInfoButton;

    [Tooltip("フォロワー数の領域に付けるButton")]
    [SerializeField] private Button followerInfoButton;

    // =====================================================
    // 選択時の背景演出
    // =====================================================
    [Header("選択時背景")]
    [Tooltip("スロット選択時にSpriteが変わる背景Image")]
    [SerializeField] private Image selectionBackgroundImage;

    [Tooltip("未選択時の背景Sprite")]
    [SerializeField] private Sprite defaultBackgroundSprite;

    // =====================================================
    // イベント
    // =====================================================

    /// <summary>閉じるボタンが押された</summary>
    public Subject<Unit> OnCloseClicked { get; } = new();

    /// <summary>ステータス説明ボタンが押された（引数は押されたステータスの種類）</summary>
    public Subject<StatusType> OnStatusInfoClicked { get; } = new();

    /// <summary>生成された広告スロットのリスト</summary>
    private readonly List<AdvertisementSlotUI> activeSlots = new();

    /// <summary>現在選択中のスロット（枠表示用）</summary>
    private AdvertisementSlotUI _selectedSlot;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => OnCloseClicked.OnNext(Unit.Default));

        if (trustInfoButton != null)
            trustInfoButton.onClick.AddListener(() => OnStatusInfoClicked.OnNext(StatusType.Trust));
        if (attentionInfoButton != null)
            attentionInfoButton.onClick.AddListener(() => OnStatusInfoClicked.OnNext(StatusType.Attention));
        if (spreadInfoButton != null)
            spreadInfoButton.onClick.AddListener(() => OnStatusInfoClicked.OnNext(StatusType.Spread));
        if (retentionInfoButton != null)
            retentionInfoButton.onClick.AddListener(() => OnStatusInfoClicked.OnNext(StatusType.Retention));
        if (followerInfoButton != null)
            followerInfoButton.onClick.AddListener(() => OnStatusInfoClicked.OnNext(StatusType.Followers));
    }

    // =====================================================
    // ステータス表示更新
    // =====================================================

    /// <summary>信頼度の表示を更新する</summary>
    public void UpdateTrust(int value, int max)
    {
        if (trustText != null) trustText.text = $"{value}/{max}";
    }

    /// <summary>注目度の表示を更新する</summary>
    public void UpdateAttention(int value, int max)
    {
        if (attentionText != null) attentionText.text = $"{value}/{max}";
    }

    /// <summary>拡散力の表示を更新する</summary>
    public void UpdateSpread(int value, int max)
    {
        if (spreadText != null) spreadText.text = $"{value}/{max}";
    }

    /// <summary>顧客維持力の表示を更新する</summary>
    public void UpdateRetention(int value, int max)
    {
        if (retentionText != null) retentionText.text = $"{value}/{max}";
    }

    /// <summary>フォロワー数の表示を更新する</summary>
    public void UpdateFollowers(int value)
    {
        if (followerText != null) followerText.text = $"{value:#,0}";
    }

    // =====================================================
    // 効果プレビュー
    // =====================================================

    /// <summary>
    /// 選択した広告の効果プレビューを表示し、スロットに選択枠を表示する。
    /// 各ステータスの上昇量を「+X」形式で表示し、0の項目は非表示にする。
    /// </summary>
    /// <param name="slot">選択されたスロット</param>
    /// <param name="ad">選択された広告データ</param>
    /// <param name="currentTrust">現在の信頼度</param>
    /// <param name="currentAttention">現在の注目度</param>
    /// <param name="currentSpread">現在の拡散力</param>
    /// <param name="currentRetention">現在の顧客維持力</param>
    /// <param name="currentFollowers">現在のフォロワー数</param>
    public void ShowEffectPreview(AdvertisementSlotUI slot, AdvertisementData ad,
        int currentTrust, int currentAttention, int currentSpread,
        int currentRetention, int currentFollowers)
    {
        if (ad == null) return;

        // 前の選択枠を非表示にし、新しいスロットの枠を表示
        SelectSlot(slot);

        // 各ステータスの上昇プレビュー（0なら非表示）
        SetPreviewText(trustPreviewText, ad.trustGain, currentTrust);
        SetPreviewText(attentionPreviewText, ad.attentionGain, currentAttention);
        SetPreviewText(spreadPreviewText, ad.spreadGain, currentSpread);
        SetPreviewText(retentionPreviewText, ad.retentionGain, currentRetention);
        SetPreviewText(followerPreviewText, ad.followerGain, currentFollowers);
    }

    /// <summary>
    /// 指定スロットを選択状態にし、他のスロットの選択枠を非表示にする。
    /// 背景Imageを選択した広告のSpriteに切り替える。
    /// </summary>
    private void SelectSlot(AdvertisementSlotUI slot)
    {

        // 新しいスロットを選択
        _selectedSlot = slot;


        // 背景Spriteを切り替え（広告データのselectedBackgroundを使用）
        if (selectionBackgroundImage != null)
        {
            if (slot != null && slot.AdData != null && slot.AdData.selectedBackground != null)
                selectionBackgroundImage.sprite = slot.AdData.selectedBackground;
            else
                selectionBackgroundImage.sprite = defaultBackgroundSprite;
        }
    }

    /// <summary>
    /// 効果プレビューをクリアする（何も選択していない状態）
    /// </summary>
    public void ClearEffectPreview()
    {
        // 選択枠を非表示
        SelectSlot(null);

        HidePreviewText(trustPreviewText);
        HidePreviewText(attentionPreviewText);
        HidePreviewText(spreadPreviewText);
        HidePreviewText(retentionPreviewText);
        HidePreviewText(followerPreviewText);
    }

    /// <summary>
    /// 効果プレビューテキストを設定する。
    /// 値が0なら非表示、それ以外なら「現在値 → 変化後（+変化量）」形式で表示。
    /// </summary>
    private void SetPreviewText(TextMeshProUGUI text, int gain, int currentValue)
    {
        if (text == null) return;

        if (gain == 0)
        {
            text.gameObject.SetActive(false);
        }
        else
        {
            text.gameObject.SetActive(true);
            string sign = gain > 0 ? "+" : "";
            text.text = $"{sign}{gain}";
            text.color = gain > 0 ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.8f, 0.2f, 0.2f);
        }
    }

    /// <summary>
    /// プレビューテキストを非表示にする
    /// </summary>
    private void HidePreviewText(TextMeshProUGUI text)
    {
        if (text != null)
            text.gameObject.SetActive(false);
    }

    // =====================================================
    // 広告リスト操作
    // =====================================================

    /// <summary>
    /// 広告リストを生成して表示する。
    /// 各スロットの OnPurchaseClicked を購読して処理できるようにリストを返す。
    /// </summary>
    /// <param name="advertisements">広告データと割引後コスト・購入可否のタプルリスト</param>
    /// <returns>生成された AdvertisementSlotUI のリスト</returns>
    public List<AdvertisementSlotUI> PopulateAdList(
        List<(AdvertisementData ad, int discountedCost, bool canPurchase)> advertisements)
    {
        ClearAdList();

        foreach (var (ad, cost, canPurchase) in advertisements)
        {
            if (adSlotPrefab == null || adListParent == null) continue;

            var slotObj = Instantiate(adSlotPrefab, adListParent);
            slotObj.SetActive(true);

            var slotUI = slotObj.GetComponent<AdvertisementSlotUI>();
            if (slotUI != null)
            {
                slotUI.Setup(ad, cost, canPurchase);
                activeSlots.Add(slotUI);
            }
        }

        return activeSlots;
    }

    /// <summary>
    /// 既存の広告スロットを全て破棄する
    /// </summary>
    public void ClearAdList()
    {
        foreach (var slot in activeSlots)
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
        activeSlots.Clear();
    }

    /// <summary>
    /// 全スロットのコスト表示を更新する（広告実行後のリフレッシュ用）
    /// </summary>
    public void RefreshSlots(System.Func<AdvertisementData, int> getCost)
    {
        foreach (var slot in activeSlots)
        {
            if (slot == null || slot.AdData == null) continue;
            int cost = getCost(slot.AdData);
            slot.UpdateCost(cost);
        }
    }


    private void OnDestroy()
    {
        ClearAdList();
    }
}

