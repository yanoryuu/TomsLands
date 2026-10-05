using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// TomsShop シーン復帰時に BattleOutputData を読み取り、
/// 戦闘結果を ItemModel 等に反映するハンドラ。
/// </summary>
public class BattleResultHandler : IStartable, IDisposable
{
    private readonly BattleOutputData _outputData;
    private readonly BattleInputData _inputData;
    private readonly ItemModel _itemModel;
    private readonly StateManager _stateManager;
    private readonly GameFlowManager _gameFlowManager;
    private readonly ShopEconomySettings _economySettings;
    private readonly TomsModel _tomsModel;
    private readonly HeroModel _heroModel;
    private readonly RelicRewardService _relicRewardService;
    private readonly MetaProgressModel _metaProgress;
    private readonly System.Threading.CancellationTokenSource _talkCts = new();

    public BattleResultHandler(
        BattleOutputData outputData,
        BattleInputData inputData,
        ItemModel itemModel,
        StateManager stateManager,
        GameFlowManager gameFlowManager,
        ShopEconomySettings economySettings,
        TomsModel tomsModel,
        HeroModel heroModel,
        RelicRewardService relicRewardService,
        MetaProgressModel metaProgress)
    {
        _metaProgress = metaProgress;
        _outputData = outputData;
        _inputData = inputData;
        _itemModel = itemModel;
        _stateManager = stateManager;
        _gameFlowManager = gameFlowManager;
        _economySettings = economySettings;
        _tomsModel = tomsModel;
        _heroModel = heroModel;
        _relicRewardService = relicRewardService;
    }

    public void Start()
    {
        // 戦闘結果がない場合（初回起動時など）はスキップ
        if (!_outputData.HasResult) return;

        Debug.Log($"[BattleResultHandler] Processing battle result: {_outputData.Result}");

        // GameFlowManager のインデックスを復元（シーン再生成で失われるため）
        _gameFlowManager.RestoreIndex(_inputData.GameFlowIndex);

        // 初めて配信から帰ってきたターンを記録する（イベント／バズのチュートリアルの起点）
        _metaProgress?.RecordFirstBattleTurn(_gameFlowManager.CurrentTurn.Value);

        // --- 売上処理（在庫を減らしてお金を加算）---
        // 在庫減算は SoldFromStock（持ち込み分のみ。バトル中の補充分は店在庫に無い）
        foreach (var soldItem in _outputData.SoldItems)
        {
            if (soldItem.SoldFromStock > 0)
            {
                _itemModel.ConsumeStock(soldItem.ItemId, soldItem.SoldFromStock);
            }
        }

        if (_outputData.TotalEarnings != 0)
        {
            // 配信の売上は「現場での現金取引」として即金のまま（売り注文の遅延対象外）
            _tomsModel.AddRevenue(_outputData.TotalEarnings);
            _tomsModel.SavePlayerMoney();
            Debug.Log($"[BattleResultHandler] Battle net earnings applied to PlayerMoney: {_outputData.TotalEarnings}G");
        }

        // --- 勝敗ボーナス/ペナルティ ---
        if (_outputData.Result == BattleResult.Victory)
        {
            if (!string.IsNullOrEmpty(_outputData.WeaponId))
                _itemModel.BattleWinBonus(_outputData.WeaponId, 5);
            if (!string.IsNullOrEmpty(_outputData.ArmorId))
                _itemModel.BattleWinBonus(_outputData.ArmorId, 5);

            // 配信勝利報酬: レリック3択を保留に積む（ホーム復帰時に選択UIが出る）
            _relicRewardService?.QueueBattleReward();

            Debug.Log("[BattleResultHandler] Victory bonuses applied.");
        }
        else
        {
            if (!string.IsNullOrEmpty(_outputData.WeaponId))
                _itemModel.BattleDefeatPenalty(_outputData.WeaponId, 2);
            if (!string.IsNullOrEmpty(_outputData.ArmorId))
                _itemModel.BattleDefeatPenalty(_outputData.ArmorId, 2);

            Debug.Log("[BattleResultHandler] Defeat penalties applied.");
        }

        int gainedExp = _outputData.DefeatedMobCount * GameConst.HeroExpPerMob
                      + _outputData.DefeatedBossCount * GameConst.HeroExpPerBoss;
        if (_outputData.Result != BattleResult.Victory)
        {
            // 敗北時の経験値上乗せ: 雑魚撃破分に倍率＋ボス経験値の一部（ボスまで届かなくても成長させる）
            int mobExp = _outputData.DefeatedMobCount * GameConst.HeroExpPerMob;
            int mobBonus = Mathf.RoundToInt(mobExp * Mathf.Max(0f, GameConst.HeroDefeatMobExpMultiplier - 1f));
            int bossShare = Mathf.RoundToInt(GameConst.HeroExpPerBoss * Mathf.Max(0f, GameConst.HeroDefeatBossExpShare));
            gainedExp += mobBonus + bossShare;
        }

        // 計算順: 経験値（敗北上乗せ込み）でのレベルアップ → 不足分だけ最低保証で補う（二重には上がらない）
        int levelUps = 0;
        if (gainedExp > 0)
        {
            levelUps = _heroModel.AddExperience(gainedExp);
            Debug.Log($"[BattleResultHandler] Hero gained EXP: {gainedExp} (mobs={_outputData.DefeatedMobCount}, bosses={_outputData.DefeatedBossCount}, levelUps={levelUps})");
        }

        // 勝敗に関係なく、配信（ダンジョン挑戦）後は最低保証ぶん必ずレベルアップする
        // （負けてレベルが上がらないまま難しいダンジョンへ進み、勝てなくなる詰みを防ぐ）
        int guaranteedLevelUps = _outputData.Result == BattleResult.Victory
            ? GameConst.HeroGuaranteedLevelUpsOnVictory
            : GameConst.HeroGuaranteedLevelUpsOnDefeat;
        if (levelUps < guaranteedLevelUps)
        {
            int forced = _heroModel.ForceLevelUp(guaranteedLevelUps - levelUps);
            Debug.Log($"[BattleResultHandler] Guaranteed hero level up: +{forced} (expLevelUps={levelUps}, guaranteed={guaranteedLevelUps})");
        }

        // --- 案D1: 戦闘結果の属性波及 ---
        // 使用装備と同属性の全アイテムに需要を緩やかに波及させる
        var usedItemIds = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(_outputData.WeaponId)) usedItemIds.Add(_outputData.WeaponId);
        if (!string.IsNullOrEmpty(_outputData.ArmorId)) usedItemIds.Add(_outputData.ArmorId);
        if (usedItemIds.Count > 0)
        {
            _itemModel.ApplyBattleAttributeSpread(
                _outputData.Result,
                usedItemIds,
                _economySettings,
                _tomsModel.BlacksmithLevel.Value);
            Debug.Log("[BattleResultHandler] D1: Attribute spread applied.");
        }

        // トコの一言用に、クリア前の勝敗・稼ぎを控えておく
        var talkResult = _outputData.Result;
        int talkEarnings = _outputData.TotalEarnings;
        RunHistory.RecordStreaming(talkResult == BattleResult.Victory, _outputData.SoldItems); // リザルトの振り返り用

        // 結果をクリア（次回の戦闘まで誤発火しないように）
        _outputData.Clear();

        // 戦闘結果（在庫減少・需要/価格変動）を永続化
        _itemModel.SaveData();

        // 戦闘結果処理後、次のターンへ進む
        _gameFlowManager.NextTurn();

        // 帰還したらトコの一言を流す。上の処理（精算→NextTurn）の順序には一切関与せず、
        // 少し待ってから店に留まっている場合だけ再生する（NextTurn で次の配信・リザルトへ直行する場合は流さない）
        TokoTalk.PlayStreamingReturnAsync(
            talkResult, talkEarnings, _tomsModel.PlayerMoney.Value, _metaProgress, _talkCts.Token).Forget();
    }

    public void Dispose()
    {
        _talkCts.Cancel();
        _talkCts.Dispose();
    }
}
