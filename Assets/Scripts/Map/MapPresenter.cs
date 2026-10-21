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

    public void Entry()
    {
        mapView.SetDungeons(BuildDisplays());
        mapView.SetStreamSchedule(BuildSchedule());
    }

    /// <summary>
    /// 領主館 Lv5「配信の予定表」: このランの配信（何日目・どのダンジョン）を全部並べる。Lv5 でなければ null（帯を隠す）。
    /// 出すのは日とダンジョン名だけ。弱点・敵・Lv は名札・情報屋の開示ルールのまま（Docs/Map_Overlay_Spec.md R6）。
    /// </summary>
    private List<MapScheduleRow> BuildSchedule()
    {
        if (gameFlowManager == null || !VillageEffects.Current.StreamScheduleVisible) return null;

        int today = gameFlowManager.CurrentTurn.Value;
        var rows = new List<MapScheduleRow>();
        foreach (var e in gameFlowManager.GetStreamSchedule())
        {
            var data = dungeonRepository.GetById(e.Dungeon);
            string status = e.IsPast ? "済" : DayText.UntilStream(Math.Max(0, e.Day - today), "");
            if (e.IsUpcoming) status = $"<color=#E8A33D>次</color>  {status}";
            rows.Add(new MapScheduleRow
            {
                Name = data != null ? data.dungeonName : e.Dungeon.ToString(),
                DayText = DayText.DayOf(e.Day),
                StatusText = status,
                IsPast = e.IsPast,
            });
        }
        return rows;
    }

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
        DungeonName? nextKey = gameFlowManager != null ? gameFlowManager.GetUpcomingStreamDungeon() : null;
        // その次の行き先も1つだけ出す（2手先の仕込みのため。それより先は出さない）
        DungeonName? afterNextKey = gameFlowManager != null ? gameFlowManager.GetStreamDungeonAfterUpcoming() : null;

        string nextText = null;
        if (nextKey.HasValue)
        {
            // 表示用の日数（今日 0・明日 1）。GetTurnsUntilNextBattle は前日に 0 になるので使わない
            int daysUntil = gameFlowManager.GetDaysUntilNextStream();
            nextText = DayText.UntilStream(daysUntil, "");
        }

        foreach (var data in dungeons)
        {
            if (data == null) continue;

            // Lv・確率は情報購入で開示（DungeonInfoView と同じ）、
            // 弱点は情報屋・新聞で知ったかで開示（鍛冶屋ヘッダーと同じ判定）
            bool bought = data.isShowedInfo;
            bool weaknessKnown = dungeonIntel != null && dungeonIntel.IsWeaknessKnown(data.key);
            string probability = "？";
            if (bought)
            {
                float p = ClearProbabilityCalculator.Calculate(
                    heroModel.heroData, itemModel, data, data.currentDungeonLevel, relicResolver);
                probability = ClearProbabilityCalculator.FormatOutlook(p);
            }

            bool isNext = nextKey.HasValue && nextKey.Value == data.key;
            list.Add(new MapDungeonDisplay
            {
                Key = data.key,
                Name = data.dungeonName,
                LevelText = bought ? DungeonLevels.Label(data.currentDungeonLevel) : "Lv.？",
                WeaknessText = ItemAttributeText.Weakness(data.requiredAttribute, weaknessKnown),
                ProbabilityText = probability,
                Status = data.dungeonStatus,
                IsNext = isNext,
                NextText = isNext ? nextText : null,
                IsAfterNext = afterNextKey.HasValue && afterNextKey.Value == data.key,
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

            // 勇者の現在ステータスでのクリア見込み（現在のダンジョンレベル基準）
            float clearProbability = ClearProbabilityCalculator.Calculate(
                heroModel.heroData, itemModel, data, data.currentDungeonLevel, relicResolver);
            bool weaknessKnown = dungeonIntel != null && dungeonIntel.IsWeaknessKnown(data.key);

            dungeonInfoView.ShowDungeonInfo(data, clearProbability, weaknessKnown);
        }).AddTo(disposable);

        mapView.OnBackRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposable);
    }
}
