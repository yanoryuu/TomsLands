using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using VContainer.Unity;

public class MapPresenter : IPresenter, IStartable, IDisposable
{
    private readonly CompositeDisposable disposable;
    private readonly MapModel mapModel;
    private readonly MapView mapView;
    private readonly DungeonRepository dungeonRepository;
    private readonly DungeonInfoView dungeonInfoView;
    private readonly StateManager stateManager;
    private readonly HeroModel heroModel;
    private readonly ItemModel itemModel;
    private readonly RelicEffectResolver relicResolver;
    private readonly GameFlowManager gameFlowManager;
    private readonly DungeonIntelModel dungeonIntel;

    public MapPresenter(MapModel mapModel, MapView mapView,DungeonRepository dungeonRepository,DungeonInfoView dungeonInfoView,StateManager stateManager, HeroModel heroModel, ItemModel itemModel, RelicEffectResolver relicResolver,
        GameFlowManager gameFlowManager, DungeonIntelModel dungeonIntel)
    {
        disposable = new CompositeDisposable();

        this.mapModel = mapModel;
        this.mapView = mapView;
        this.dungeonRepository = dungeonRepository;
        this.dungeonInfoView = dungeonInfoView;
        this.stateManager = stateManager;
        this.heroModel = heroModel;
        this.itemModel = itemModel;
        this.relicResolver = relicResolver;
        this.gameFlowManager = gameFlowManager;
        this.dungeonIntel = dungeonIntel;

        stateManager.RegisterOnEnter(TomsShopGamePhase.Map,Entry);
    }

    public void Start() => Bind();

    public void Dispose() => disposable.Dispose();

    public void Entry() => mapView.SetDungeons(BuildDisplays());

    /// <summary>
    /// 名札の表示データを組み立てる（Docs/Map_Overlay_Spec.md R1〜R3）。
    /// クリア確率は装備・レベル・レリックで変わるので、地図に入るたびに計算し直す。
    /// </summary>
    private List<MapDungeonDisplay> BuildDisplays()
    {
        var list = new List<MapDungeonDisplay>();
        var dungeons = dungeonRepository.availableDungeons;
        if (dungeons == null) return list;

        // 次の行き先は鍛冶屋ヘッダーと同じ取り方（配信前の寄り道中は今日配信するダンジョン）
        bool awaitingStream = gameFlowManager != null && gameFlowManager.IsAwaitingStream.Value;
        DungeonName? nextKey = null;
        if (gameFlowManager != null)
            nextKey = awaitingStream ? gameFlowManager.PendingBattleDungeon : gameFlowManager.GetNextBattleDungeon();

        string nextText = null;
        if (nextKey.HasValue)
        {
            int turnsUntil = awaitingStream ? 0 : gameFlowManager.GetTurnsUntilNextBattle();
            nextText = awaitingStream ? "本日配信" : turnsUntil >= 0 ? $"あと{turnsUntil}ターン" : "";
        }

        foreach (var data in dungeons)
        {
            if (data == null) continue;

            // Lv・確率は情報購入で開示（DungeonInfoView と同じ）、
            // 弱点は情報屋・新聞で知ったかで開示（鍛冶屋ヘッダーと同じ判定）
            bool bought = data.isShowedInfo;
            bool weaknessKnown = dungeonIntel != null && dungeonIntel.IsWeaknessKnown(data.key);
            string probability = "??%";
            if (bought)
            {
                float p = ClearProbabilityCalculator.Calculate(
                    heroModel.heroData, itemModel, data, data.currentDungeonLevel, relicResolver);
                probability = $"{p:F0}%";
            }

            bool isNext = nextKey.HasValue && nextKey.Value == data.key;
            list.Add(new MapDungeonDisplay
            {
                Key = data.key,
                Name = data.dungeonName,
                LevelText = bought ? $"Lv.{data.currentDungeonLevel}" : "Lv.???",
                WeaknessText = weaknessKnown ? $"弱点:{ItemAttributeText.ToJapanese(data.requiredAttribute)}" : "弱点:?",
                ProbabilityText = probability,
                Status = data.dungeonStatus,
                IsNext = isNext,
                NextText = isNext ? nextText : null,
            });
        }
        return list;
    }

    private void Bind()
    {
        dungeonInfoView.OnCloseRequested
            .Subscribe(_ => dungeonInfoView.HideDungeonInfo())
            .AddTo(disposable);

        mapView.OnMapIcon.Subscribe(key =>
        {
            var data = dungeonRepository.GetById(key);
            if (data == null) return;

            // 勇者の現在ステータスでのクリア確率（現在のダンジョンレベル基準）
            float clearProbability = ClearProbabilityCalculator.Calculate(
                heroModel.heroData, itemModel, data, data.currentDungeonLevel, relicResolver);

            dungeonInfoView.ShowDungeonInfo(data, clearProbability);
        }).AddTo(disposable);

        mapView.OnBackRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposable);
    }
}
