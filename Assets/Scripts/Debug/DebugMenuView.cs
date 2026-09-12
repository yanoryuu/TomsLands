#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using VContainer;

/// <summary>
/// 開発用デバッグメニュー（OnGUI/IMGUIオーバーレイ）。仕様: Docs/DebugMenu_Spec.md
/// F12キーで開閉する。各シーンの LifetimeScope から自動生成されるためシーン配線は不要。
/// Editor と Development Build でのみコンパイルされ、リリースビルドには一切含まれない
/// （保険としてランタイムでも Debug.isDebugBuild を確認する）。
///
/// 依存Modelは IObjectResolver.TryResolve で任意解決し、
/// そのシーンに存在しないModelのセクションは表示しない。
/// 例外: MetaProgressModel（銀行預金/村資金/村施設Lv）はスコープ未登録のシーンでは
/// ローカル生成して metaData.json を直接読み書きする。
/// </summary>
public class DebugMenuView : MonoBehaviour
{
    private TomsModel _tomsModel;
    private GameFlowManager _gameFlowManager;
    private BuzzSystem _buzzSystem;
    private ShopStatusModel _statusModel;
    private TurnPhaseManager _turnPhaseManager;
    private PortfolioModel _portfolioModel;
    private ItemModel _itemModel;
    private RelicInventoryModel _relicInventory;
    private RelicRewardService _relicRewardService;
    private HeroModel _heroModel;
    private DungeonRepository _dungeonRepository;
    private MetaProgressModel _metaProgress;
    private List<RelicDefinition> _relicDefinitions;

    // MetaProgressModel がスコープに無いシーン用のローカルインスタンス（metaData.json直読み書き）
    private MetaProgressModel _localMeta;
    private MetaProgressModel Meta => _metaProgress ?? (_localMeta ??= new MetaProgressModel());

    private bool _visible;
    private Rect _windowRect = new Rect(24, 24, 400, 640);
    private Vector2 _scroll;
    private GUIStyle _headerStyle;
    private int _tab;
    private static readonly string[] TabNames = { "情報", "お金", "レベル", "レリック", "マーケ", "その他" };

    private string _moneyInput = "10000";
    private string _bankInput = "10000";

    /// <summary>村施設ID（VillageFacilityData.facilityId と一致させること）。</summary>
    private static readonly (string id, string label)[] Facilities =
    {
        ("hall", "領主館"), ("bank", "銀行"), ("guild", "冒険者ギルド"), ("antique", "骨董品店"),
        ("shrine", "祠"), ("tavern", "酒場"), ("warehouse", "倉庫"), ("workshop", "工房"),
        ("artisan", "職人組合"), ("farm", "農場"), ("press", "印刷所"), ("road", "街道整備"),
        ("training", "訓練所"),
    };

    [Inject]
    public void Construct(IObjectResolver resolver)
    {
        resolver.TryResolve(out _tomsModel);
        resolver.TryResolve(out _gameFlowManager);
        resolver.TryResolve(out _buzzSystem);
        resolver.TryResolve(out _statusModel);
        resolver.TryResolve(out _turnPhaseManager);
        resolver.TryResolve(out _portfolioModel);
        resolver.TryResolve(out _itemModel);
        resolver.TryResolve(out _relicInventory);
        resolver.TryResolve(out _relicRewardService);
        resolver.TryResolve(out _heroModel);
        resolver.TryResolve(out _dungeonRepository);
        resolver.TryResolve(out _metaProgress);
        resolver.TryResolve(out _relicDefinitions);
    }

    private void Update()
    {
        if (!Debug.isDebugBuild) return;
        if (Input.GetKeyDown(KeyCode.F12))
        {
            _visible = !_visible;
        }
    }

    private void OnGUI()
    {
        if (!Debug.isDebugBuild || !_visible) return;

        // 高解像度でも操作できるよう画面高さに応じてスケーリングする
        float scale = Mathf.Max(1f, Screen.height / 900f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        if (_headerStyle == null)
        {
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13
            };
            _headerStyle.normal.textColor = Color.yellow;
        }

        _windowRect = GUILayout.Window(GetInstanceID(), _windowRect, DrawWindow, "デバッグメニュー [F12で閉じる]");
    }

    private void DrawWindow(int windowId)
    {
        _tab = GUILayout.Toolbar(_tab, TabNames);
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(560));

        switch (_tab)
        {
            case 0: DrawInfoSection(); break;
            case 1: DrawMoneySection(); break;
            case 2: DrawLevelSection(); break;
            case 3: DrawRelicSection(); break;
            case 4: DrawBuzzSection(); DrawStatusSection(); break;
            case 5: DrawMiscSection(); break;
        }

        GUILayout.EndScrollView();

        // タイトルバーでドラッグ移動できるようにする
        GUI.DragWindow(new Rect(0, 0, 10000, 24));
    }

    // =====================================================
    // 情報表示
    // =====================================================
    private void DrawInfoSection()
    {
        GUILayout.Label("■ 情報", _headerStyle);

        if (_gameFlowManager != null)
            GUILayout.Label($"ターン: {_gameFlowManager.CurrentTurn.Value}");
        if (_turnPhaseManager != null)
            GUILayout.Label($"フェーズ: {_turnPhaseManager.CurrentTurnPhase.Value}");
        if (_tomsModel != null)
            GUILayout.Label($"所持金: {_tomsModel.PlayerMoney.Value:N0} G / 当日仕入れ支出: {_tomsModel.TurnProcurementSpend:N0} G");
        GUILayout.Label($"銀行預金: {Meta.BankedGold.Value:N0} G / 村資金: {Meta.VillageFunds:N0} G");
        GUILayout.Label($"スロット: slot_{SaveSlotManager.CurrentSlot}");

        if (_buzzSystem != null)
        {
            string buzzState = _buzzSystem.IsBuzzActive.Value
                ? $"{GetBuzzLabel(_buzzSystem.CurrentBuzzType.Value)}（残り{_buzzSystem.RemainingTurns.Value}ターン）"
                : "なし";
            GUILayout.Label($"バズ状態: {buzzState}");
            GUILayout.Label($"バズ発生確率: {_buzzSystem.CalculateBuzzChance():F1} % / 超バズ: {_buzzSystem.CalculateBigBuzzChance():F1} %");
        }

        if (_statusModel != null)
        {
            GUILayout.Label(
                $"信頼:{_statusModel.Trust.Value} 注目:{_statusModel.Attention.Value} " +
                $"拡散:{_statusModel.Spread.Value} 定着:{_statusModel.Retention.Value}");
            GUILayout.Label($"フォロワー: {_statusModel.Followers.Value:N0}");
        }

        if (_heroModel?.heroData != null)
            GUILayout.Label($"勇者: Lv{_heroModel.heroData.level.Value} EXP {_heroModel.heroData.experience.Value}/{_heroModel.heroData.expToNextLevel.Value}");

        GUILayout.Space(8);
    }

    // =====================================================
    // お金
    // =====================================================
    private void DrawMoneySection()
    {
        GUILayout.Label("■ 所持金", _headerStyle);
        if (_tomsModel != null)
        {
            GUILayout.Label($"所持金: {_tomsModel.PlayerMoney.Value:N0} G");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1,000")) AddMoney(1000);
            if (GUILayout.Button("+10,000")) AddMoney(10000);
            if (GUILayout.Button("+100,000")) AddMoney(100000);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("半減")) SetMoney(_tomsModel.PlayerMoney.Value / 2);
            if (GUILayout.Button("0にする")) SetMoney(0);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _moneyInput = GUILayout.TextField(_moneyInput, GUILayout.Width(120));
            if (GUILayout.Button("この値に設定") && int.TryParse(_moneyInput, out int v)) SetMoney(v);
            GUILayout.EndHorizontal();
        }
        else GUILayout.Label("TomsModel なし");

        GUILayout.Space(6);
        GUILayout.Label("■ 銀行預金（ラン間持ち越し）", _headerStyle);
        GUILayout.Label($"預金: {Meta.BankedGold.Value:N0} G");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+10,000")) { Meta.DepositRunGold(10000); }
        if (GUILayout.Button("-10,000")) { Meta.TrySpendBankedGold(10000); Meta.SaveData(); }
        if (GUILayout.Button("0にする")) { Meta.TrySpendBankedGold(Meta.BankedGold.Value); Meta.SaveData(); }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        _bankInput = GUILayout.TextField(_bankInput, GUILayout.Width(120));
        if (GUILayout.Button("この値に設定") && int.TryParse(_bankInput, out int bank))
        {
            Meta.TrySpendBankedGold(Meta.BankedGold.Value);
            Meta.DepositRunGold(Mathf.Max(0, bank));
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("■ 村資金", _headerStyle);
        GUILayout.Label($"村資金: {Meta.VillageFunds:N0} G");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+10,000")) { Meta.AddVillageFunds(10000); Meta.SaveData(); }
        if (GUILayout.Button("0にする")) { Meta.TrySpendVillageFunds(Meta.VillageFunds); Meta.SaveData(); }
        GUILayout.EndHorizontal();

        // 金融資産の状態（検証用）
        if (_portfolioModel != null && _tomsModel != null && _gameFlowManager != null)
        {
            GUILayout.Space(6);
            GUILayout.Label("■ 金融", _headerStyle);
            GUILayout.Label($"ポジション {_portfolioModel.Positions.Count}件 / 評価額 {_portfolioModel.TotalAssetsEstimate.Value:N0}G");
            if (GUILayout.Button("最初の商品を1口買う（検証）"))
            {
                var product = _portfolioModel.AllProducts.Count > 0 ? _portfolioModel.AllProducts[0] : null;
                if (product != null)
                {
                    bool ok = product.kind == FinancialProductKind.Bond
                        ? _portfolioModel.BuyBond(product, 1, _tomsModel, _gameFlowManager.CurrentTurn.Value)
                        : _portfolioModel.BuyFund(product, 1, _tomsModel, _itemModel, _tomsModel.BlacksmithLevel.Value, _gameFlowManager.CurrentTurn.Value);
                    Debug.Log($"[DebugMenu] 金融購入テスト: {product.productName} → {(ok ? "成功" : "失敗")}");
                }
                else Debug.Log("[DebugMenu] FinancialProductData が1件もありません");
            }
        }

        GUILayout.Space(8);
    }

    private void AddMoney(int amount)
    {
        _tomsModel.PlayerMoney.Value += amount;
        _tomsModel.SavePlayerMoney();
    }

    private void SetMoney(int amount)
    {
        _tomsModel.PlayerMoney.Value = Mathf.Max(0, amount);
        _tomsModel.SavePlayerMoney();
    }

    // =====================================================
    // レベル
    // =====================================================
    private void DrawLevelSection()
    {
        GUILayout.Label("■ 店・施設レベル", _headerStyle);
        if (_tomsModel != null)
        {
            DrawLevelRow("鍛冶屋Lv", _tomsModel.BlacksmithLevel);
            DrawLevelRow("情報屋Lv", _tomsModel.InfoBrokerLevel);
            DrawLevelRow("店Lv", _tomsModel.ShopLevel);
        }
        else GUILayout.Label("TomsModel なし");

        // 勇者（AddExperienceで正しくステータスも更新する）
        if (_heroModel?.heroData != null)
        {
            GUILayout.Space(6);
            GUILayout.Label("■ 勇者", _headerStyle);
            var hero = _heroModel.heroData;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Lv{hero.level.Value} (EXP {hero.experience.Value}/{hero.expToNextLevel.Value})", GUILayout.Width(180));
            if (GUILayout.Button("Lv +1"))
            {
                int need = Mathf.Max(1, hero.expToNextLevel.Value - hero.experience.Value);
                _heroModel.AddExperience(need);
                _heroModel.SaveHeroData();
            }
            GUILayout.EndHorizontal();
        }

        // ダンジョンレベル
        if (_dungeonRepository != null && _dungeonRepository.availableDungeons != null)
        {
            GUILayout.Space(6);
            GUILayout.Label("■ ダンジョンLv（1〜5）", _headerStyle);
            foreach (var d in _dungeonRepository.availableDungeons)
            {
                if (d == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{d.dungeonName}: Lv{d.currentDungeonLevel}", GUILayout.Width(200));
                if (GUILayout.Button("-1")) { d.currentDungeonLevel = Mathf.Max(1, d.currentDungeonLevel - 1); _dungeonRepository.Save(); }
                if (GUILayout.Button("+1")) { d.currentDungeonLevel = Mathf.Min(5, d.currentDungeonLevel + 1); _dungeonRepository.Save(); }
                GUILayout.EndHorizontal();
            }
        }

        // 村施設レベル（メタ進行）
        GUILayout.Space(6);
        GUILayout.Label("■ 村施設Lv（0〜3・メタ進行）", _headerStyle);
        foreach (var (id, label) in Facilities)
        {
            int lv = Meta.GetFacilityLevel(id);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: Lv{lv}", GUILayout.Width(160));
            if (GUILayout.Button("-1")) { Meta.SetFacilityLevel(id, Mathf.Max(0, lv - 1)); Meta.SaveData(); }
            if (GUILayout.Button("+1")) { Meta.SetFacilityLevel(id, Mathf.Min(3, lv + 1)); Meta.SaveData(); }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("※村シーンの見た目はシーン再入で反映");

        GUILayout.Space(8);
    }

    private void DrawLevelRow(string label, R3.ReactiveProperty<int> level)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: {level.Value}", GUILayout.Width(120));
        if (GUILayout.Button("+1")) { level.Value++; _tomsModel.SavePlayerMoney(); }
        if (GUILayout.Button("=1")) { level.Value = 1; _tomsModel.SavePlayerMoney(); }
        GUILayout.EndHorizontal();
    }

    // =====================================================
    // レリック
    // =====================================================
    private void DrawRelicSection()
    {
        GUILayout.Label("■ レリック", _headerStyle);
        if (_relicInventory == null)
        {
            GUILayout.Label("RelicInventoryModel なし（このシーンでは操作不可）");
            return;
        }

        GUILayout.Label($"所持: {_relicInventory.Owned.Count}個");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("ランダム獲得"))
        {
            var picks = _relicRewardService?.PickChoices(1);
            if (picks != null && picks.Count > 0)
                _relicInventory.Add(picks[0].relicId, CurrentTurnSafe(), GameConst.RelicMaxEquipSlots);
            else Debug.Log("[DebugMenu] 獲得できるレリックがありません");
        }
        if (GUILayout.Button("3択テスト"))
        {
            _relicRewardService?.QueueBattleReward();
        }
        GUILayout.EndHorizontal();

        // 全定義から付与/剥奪（呪い含む）
        if (_relicDefinitions != null && _relicDefinitions.Count > 0)
        {
            GUILayout.Space(4);
            GUILayout.Label("― 全レリック（クリックで付与⇔剥奪） ―");
            foreach (var relic in _relicDefinitions)
            {
                if (relic == null) continue;
                bool owned = _relicInventory.Has(relic.relicId);
                string curse = relic.isCurse ? "呪 " : "";
                string mark = owned ? "★" : "　";
                if (GUILayout.Button($"{mark} [{relic.rarity}] {curse}{relic.relicName}"))
                {
                    if (owned) _relicInventory.Remove(relic.relicId);
                    else _relicInventory.Add(relic.relicId, CurrentTurnSafe(), GameConst.RelicMaxEquipSlots);
                    _relicInventory.SaveData();
                }
            }
        }
        else
        {
            GUILayout.Label("RelicDefinition 一覧なし（このシーンでは個別付与不可）");
        }

        GUILayout.Space(8);
    }

    private int CurrentTurnSafe() => _gameFlowManager != null ? _gameFlowManager.CurrentTurn.Value : 1;

    // =====================================================
    // バズ操作
    // =====================================================
    private void DrawBuzzSection()
    {
        GUILayout.Label("■ バズ", _headerStyle);
        if (_buzzSystem == null)
        {
            GUILayout.Label("BuzzSystem なし");
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("バズ発生")) _buzzSystem.DebugStartBuzz(BuzzType.Normal);
        if (GUILayout.Button("超バズ発生")) _buzzSystem.DebugStartBuzz(BuzzType.Big);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("炎上発生")) _buzzSystem.DebugStartBuzz(BuzzType.Flame);
        if (GUILayout.Button("強制終了")) _buzzSystem.DebugEndBuzz();
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
    }

    // =====================================================
    // ステータス操作
    // =====================================================
    private void DrawStatusSection()
    {
        GUILayout.Label("■ 店ステータス", _headerStyle);
        if (_statusModel == null)
        {
            GUILayout.Label("ShopStatusModel なし");
            return;
        }

        DrawStatRow("信頼", () => _statusModel.ChangeTrust(10), () => _statusModel.ChangeTrust(-10));
        DrawStatRow("注目", () => _statusModel.ChangeAttention(10), () => _statusModel.ChangeAttention(-10));
        DrawStatRow("拡散", () => _statusModel.ChangeSpread(10), () => _statusModel.ChangeSpread(-10));
        DrawStatRow("定着", () => _statusModel.ChangeRetention(10), () => _statusModel.ChangeRetention(-10));

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("全ステ +10")) _statusModel.ChangeAllStats(10);
        if (GUILayout.Button("全ステ -10")) _statusModel.ChangeAllStats(-10);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("フォロワー +100")) _statusModel.ChangeFollowers(100);
        if (GUILayout.Button("フォロワー +1,000")) _statusModel.ChangeFollowers(1000);
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
    }

    private void DrawStatRow(string label, System.Action onPlus, System.Action onMinus)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(60));
        if (GUILayout.Button("+10")) onPlus();
        if (GUILayout.Button("-10")) onMinus();
        GUILayout.EndHorizontal();
    }

    // =====================================================
    // その他
    // =====================================================
    private void DrawMiscSection()
    {
        GUILayout.Label("■ 進行", _headerStyle);
        if (_gameFlowManager != null)
        {
            if (GUILayout.Button("次ターンへ（NextTurn）"))
            {
                _gameFlowManager.NextTurn();
            }
            GUILayout.Label("※フロー上のイベント/戦闘ノードへも通常通り遷移します");
        }
        else GUILayout.Label("GameFlowManager なし");

        GUILayout.Space(6);
        GUILayout.Label("■ ダンジョン情報", _headerStyle);
        if (_dungeonRepository != null)
        {
            if (GUILayout.Button("全ダンジョン情報を解放"))
            {
                foreach (var d in _dungeonRepository.availableDungeons)
                    if (d != null) d.isShowedInfo = true;
                _dungeonRepository.Save();
            }
        }
        else GUILayout.Label("DungeonRepository なし");

        GUILayout.Space(6);
        GUILayout.Label("■ 在庫", _headerStyle);
        if (_itemModel != null)
        {
            if (GUILayout.Button("全アイテム在庫 +10"))
            {
                foreach (var item in _itemModel.RuntimeItems)
                    item?.UpdateStock(item.Stock.Value + 10);
                _itemModel.SaveData();
            }
        }
        else GUILayout.Label("ItemModel なし");

        GUILayout.Space(6);
        GUILayout.Label("■ タイムスケール", _headerStyle);
        GUILayout.BeginHorizontal();
        foreach (float t in new[] { 0.5f, 1f, 2f, 4f })
        {
            if (GUILayout.Button($"x{t}")) Time.timeScale = t;
        }
        GUILayout.EndHorizontal();
        GUILayout.Label($"現在: x{Time.timeScale:0.0}");

        GUILayout.Space(8);
    }

    private static string GetBuzzLabel(BuzzType type)
    {
        switch (type)
        {
            case BuzzType.Flame: return "炎上";
            case BuzzType.Big: return "超バズ";
            default: return "バズ";
        }
    }
}
#endif
