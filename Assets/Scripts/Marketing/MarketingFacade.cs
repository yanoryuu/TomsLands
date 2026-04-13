using System;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// マーケティングシステムのファサード（統合窓口）。
/// 各サブシステム（ステータス・広告・バズ・フォロワー・売上）を統合し、
/// 既存の GameFlowManager やPresenterから簡潔に呼び出せるインターフェースを提供する。
/// 
/// 【既存システムとの統合方法】
/// 1. GameLifetimeScope で RegisterEntryPoint で登録する。
/// 2. GameFlowManager から InjectMarketingFacade() で注入する（または直接参照）。
/// 3. TurnEndSummaryPresenter で売上計算にマーケティングボーナスを適用する。
/// 
/// 【呼び出し例】
/// - ターン処理時: marketingFacade.ProcessTurn()
/// - 広告実行時: marketingFacade.ExecuteAdvertisement(adData)
/// - 売上計算時: marketingFacade.CalculateFinalRevenue(baseRevenue)
/// </summary>
public class MarketingFacade : IStartable, IDisposable
{
    private readonly ShopStatusModel _statusModel;
    private readonly AdvertisementSystem _adSystem;
    private readonly BuzzSystem _buzzSystem;
    private readonly FollowerSystem _followerSystem;
    private readonly SalesCalculator _salesCalculator;
    private readonly CompositeDisposable _disposables = new();

    public MarketingFacade(
        ShopStatusModel statusModel,
        AdvertisementSystem adSystem,
        BuzzSystem buzzSystem,
        FollowerSystem followerSystem,
        SalesCalculator salesCalculator)
    {
        _statusModel = statusModel;
        _adSystem = adSystem;
        _buzzSystem = buzzSystem;
        _followerSystem = followerSystem;
        _salesCalculator = salesCalculator;
    }

    // =====================================================
    // プロパティ（外部参照用）
    // =====================================================

    /// <summary>ステータスモデルへのアクセス</summary>
    public ShopStatusModel Status => _statusModel;

    /// <summary>広告システムへのアクセス</summary>
    public AdvertisementSystem Ads => _adSystem;

    /// <summary>バズシステムへのアクセス</summary>
    public BuzzSystem Buzz => _buzzSystem;

    /// <summary>フォロワーシステムへのアクセス</summary>
    public FollowerSystem Followers => _followerSystem;

    /// <summary>売上計算へのアクセス</summary>
    public SalesCalculator Sales => _salesCalculator;

    /// <summary>
    /// 最後のターン処理で発生したバズ結果。
    /// TomsShopPresenter が参照してバズ演出を表示し、ConsumeLastBuzzResult() でクリアする。
    /// </summary>
    public BuzzTurnResult LastBuzzResult { get; private set; }

    // =====================================================
    // ライフサイクル
    // =====================================================

    public void Start()
    {
        Debug.Log("[MarketingFacade] マーケティングシステム起動完了");

        // フォロワー変化時にマイルストーン状況をログ出力
        _statusModel.Followers
            .Subscribe(_ => _followerSystem?.LogMilestoneStatus())
            .AddTo(_disposables);
    }

    // =====================================================
    // ターン処理
    // =====================================================

    /// <summary>
    /// ターン開始時の処理を実行する。
    /// バズの持続効果適用・終了判定・新規バズ判定を行う。
    /// 
    /// 【呼び出しタイミング】
    /// GameFlowManager.NextTurn() 内、ターン番号更新後に呼び出す。
    /// または TurnEndSummaryPresenter.Entry() 内で呼び出す。
    /// </summary>
    /// <returns>バズのターン処理結果</returns>
    public BuzzTurnResult ProcessTurn()
    {
        Debug.Log("[MarketingFacade] ===== ターン処理開始 =====");

        // バズシステムのターン処理
        var result = _buzzSystem.ProcessTurnStart();

        // バズ結果を保存（TomsShopPresenter がバズ演出表示に使用）
        LastBuzzResult = result;

        if (result.BuzzEnded)
        {
            Debug.Log($"[MarketingFacade] バズ終了: {result.EndedBuzzType}");
        }

        if (result.NewBuzzOccurred)
        {
            Debug.Log($"[MarketingFacade] 新バズ発生: {result.NewBuzzType}");
        }

        // フォロワーマイルストーン状況をログ
        _followerSystem?.LogMilestoneStatus();

        Debug.Log("[MarketingFacade] ===== ターン処理完了 =====");

        return result;
    }

    // =====================================================
    // 広告
    // =====================================================

    /// <summary>
    /// 広告を実行する。
    /// </summary>
    /// <returns>実行に成功した場合 true</returns>
    public bool ExecuteAdvertisement(AdvertisementData ad)
    {
        return _adSystem.Execute(ad);
    }

    /// <summary>
    /// 広告の割引後コストを取得する。
    /// </summary>
    public int GetAdvertisementCost(AdvertisementData ad)
    {
        return _adSystem.GetDiscountedCost(ad);
    }

    /// <summary>
    /// 広告が購入可能かを判定する。
    /// </summary>
    public bool CanExecuteAdvertisement(AdvertisementData ad)
    {
        return _adSystem.CanExecute(ad);
    }

    // =====================================================
    // 売上計算
    // =====================================================

    /// <summary>
    /// 基本売上にマーケティングボーナスを適用した最終売上を計算する。
    /// TurnEndSummaryPresenter で使用する。
    /// </summary>
    public int CalculateFinalRevenue(int baseRevenue)
    {
        return _salesCalculator.CalculateFinalRevenue(baseRevenue);
    }

    /// <summary>
    /// 売上の内訳情報を取得する。
    /// </summary>
    public SalesBreakdown GetSalesBreakdown(int baseRevenue)
    {
        return _salesCalculator.GetBreakdown(baseRevenue);
    }

    // =====================================================
    // バズ結果の参照・消費
    // =====================================================

    /// <summary>
    /// 最後のバズ結果を取得してクリアする（消費型）。
    /// TomsShopPresenter がバズ演出を表示した後に呼び出す。
    /// </summary>
    /// <returns>最後のバズ結果（なければ null）</returns>
    public BuzzTurnResult ConsumeLastBuzzResult()
    {
        var result = LastBuzzResult;
        LastBuzzResult = null;
        return result;
    }

    // =====================================================
    // リセット
    // =====================================================

    /// <summary>
    /// マーケティングシステム全体をリセットする（ゲーム開始時）。
    /// </summary>
    public void ResetAll()
    {
        _statusModel.Reset();
        _buzzSystem.ForceReset();
        Debug.Log("[MarketingFacade] マーケティングシステムをリセットしました");
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

