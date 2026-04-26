using System;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// 広告購入画面のPresenter。
/// TomsShop内のサブフェーズ「Advertisement」に遷移した際に、
/// 広告一覧の表示・購入処理・ステータス数値の更新を行う。
/// 
/// 【操作フロー】
/// 1タップ目: スロット選択 → 選択枠表示 + 効果プレビュー表示
/// 2タップ目: 同じスロットを再タップ → 購入確定
/// 別スロットタップ: 選択切り替え
/// 
/// 【既存システムとの連携】
/// - StateManager の RegisterOnEnter(TomsShopGamePhase.Advertisement) で Entry() を登録
/// - MarketingFacade を通じて広告実行・ステータス参照を行う
/// - TomsModel.PlayerMoney を監視してコスト表示をリアルタイム更新
/// </summary>
public class AdvertisementPresenter : IStartable, IDisposable
{
    private readonly AdvertisementView view;
    private readonly MarketingFacade marketingFacade;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;
    private readonly PopUpManager popUpManager;
    private readonly CompositeDisposable disposables = new();

    /// <summary>現在選択中のスロット（2タップ目判定用）</summary>
    private AdvertisementSlotUI _selectedSlot;

    public AdvertisementPresenter(
        AdvertisementView view,
        MarketingFacade marketingFacade,
        TomsModel tomsModel,
        StateManager stateManager,
        PopUpManager popUpManager)
    {
        this.view = view;
        this.marketingFacade = marketingFacade;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;
        this.popUpManager = popUpManager;

        // サブフェーズ遷移時にEntry()を呼ぶ
        stateManager.RegisterOnEnter(TomsShopGamePhase.Advertisement, Entry);
    }

    public void Start()
    {
        Bind();
    }

    /// <summary>
    /// イベント購読をバインドする
    /// </summary>
    private void Bind()
    {
        // 閉じるボタン → Shop画面に戻る
        view.OnCloseClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);

        // 所持金の変化を監視 → コスト表示をリアルタイム更新
        tomsModel.PlayerMoney
            .Subscribe(_ => RefreshSlotStates())
            .AddTo(disposables);

        // マーケティングステータスの変化を監視 → ステータス数値をリアルタイム更新
        var status = marketingFacade.Status;

        status.Trust
            .Subscribe(v => view.UpdateTrust(v))
            .AddTo(disposables);

        status.Attention
            .Subscribe(v => view.UpdateAttention(v))
            .AddTo(disposables);

        status.Spread
            .Subscribe(v => view.UpdateSpread(v))
            .AddTo(disposables);

        status.Retention
            .Subscribe(v => view.UpdateRetention(v))
            .AddTo(disposables);

        status.Followers
            .Subscribe(v => view.UpdateFollowers(v))
            .AddTo(disposables);

        // ステータス説明ボタン → 説明Popup表示
        view.OnStatusInfoClicked
            .Subscribe(ShowStatusDescription)
            .AddTo(disposables);
    }

    /// <summary>
    /// ステータスの説明Popupを表示する
    /// </summary>
    private void ShowStatusDescription(StatusType type)
    {
        var (title, message) = StatusDescriptionLoader.Get(type);

        var data = new PopUpData
        {
            Title = title,
            Message = message,
            IsCloseOnly = true,
            Size = PopupSizeEnum.Medium
        };

        popUpManager.Show(data);
    }

    /// <summary>
    /// 広告購入画面に遷移した時に呼ばれる
    /// </summary>
    public void Entry()
    {
        Debug.Log("[AdvertisementPresenter] Entry - 広告購入画面を表示");

        // 選択状態をリセット
        _selectedSlot = null;

        // ステータス表示を更新
        UpdateStatusDisplay();

        // 効果プレビューをクリア
        view.ClearEffectPreview();

        // 広告一覧を生成
        PopulateAdvertisements();
    }

    /// <summary>
    /// 広告一覧を生成する
    /// </summary>
    private void PopulateAdvertisements()
    {
        var allAds = marketingFacade.Ads.GetAllAdvertisements();
        var adInfoList = new System.Collections.Generic.List<(AdvertisementData ad, int discountedCost, bool canPurchase)>();

        foreach (var ad in allAds)
        {
            if (ad == null) continue;
            int cost = marketingFacade.GetAdvertisementCost(ad);
            bool canBuy = marketingFacade.CanExecuteAdvertisement(ad);
            adInfoList.Add((ad, cost, canBuy));
        }

        var slots = view.PopulateAdList(adInfoList);

        // 各スロットのタップイベントを購読
        foreach (var slot in slots)
        {
            var capturedSlot = slot;

            slot.OnSlotSelected
                .Subscribe(adData => OnSlotTapped(capturedSlot, adData))
                .AddTo(disposables);
        }
    }

    /// <summary>
    /// スロットがタップされた時の処理。
    /// 1タップ目: 選択（プレビュー表示 + 選択枠）
    /// 2タップ目（同じスロット）: 購入確定
    /// 別スロット: 選択切り替え
    /// </summary>
    private void OnSlotTapped(AdvertisementSlotUI slot, AdvertisementData adData)
    {
        if (adData == null) return;

        // 同じスロットの2タップ目 → 購入確定
        if (_selectedSlot == slot)
        {
            ExecutePurchase(adData);
            return;
        }

        // 1タップ目 or 別スロット → 選択してプレビュー表示
        _selectedSlot = slot;

        var status = marketingFacade.Status;
        view.ShowEffectPreview(
            slot,
            adData,
            status.Trust.Value,
            status.Attention.Value,
            status.Spread.Value,
            status.Retention.Value,
            status.Followers.Value
        );

        Debug.Log($"[AdvertisementPresenter] 広告選択: {adData.advertisementName}（もう一度タップで購入）");
    }

    /// <summary>
    /// 広告の購入を実行する（2タップ目で呼ばれる）
    /// </summary>
    private void ExecutePurchase(AdvertisementData adData)
    {
        if (adData == null) return;

        bool success = marketingFacade.ExecuteAdvertisement(adData);

        if (success)
        {
            Debug.Log($"[AdvertisementPresenter] 広告実行成功: {adData.advertisementName}");

            // 選択状態をリセット
            _selectedSlot = null;

            // コスト表示をリフレッシュ
            RefreshSlotStates();

            // 効果プレビューをクリア
            view.ClearEffectPreview();
        }
        else
        {
            Debug.Log($"[AdvertisementPresenter] 広告実行失敗（資金不足）: {adData.advertisementName}");
        }
    }

    /// <summary>
    /// 全スロットのコスト表示を更新する
    /// </summary>
    private void RefreshSlotStates()
    {
        view.RefreshSlots(ad => marketingFacade.GetAdvertisementCost(ad));
    }

    /// <summary>
    /// ステータス表示を一括更新する
    /// </summary>
    private void UpdateStatusDisplay()
    {
        var status = marketingFacade.Status;
        view.UpdateTrust(status.Trust.Value);
        view.UpdateAttention(status.Attention.Value);
        view.UpdateSpread(status.Spread.Value);
        view.UpdateRetention(status.Retention.Value);
        view.UpdateFollowers(status.Followers.Value);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}

