#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using VContainer;

/// <summary>
/// 開発用デバッグメニュー（OnGUI/IMGUIオーバーレイ）。
/// F12キーで開閉する。GameLifetimeScope から自動生成されるためシーン配線は不要。
/// Editor と Development Build でのみコンパイルされ、リリースビルドには一切含まれない。
///
/// 【機能】
/// - 情報表示: ターン/フェーズ/所持金/バズ状態/バズ発生確率/店ステータス
/// - 経済操作: 所持金の増減
/// - 進行操作: 次ターンへ送る
/// - バズ操作: 通常/超バズ/炎上の強制発生・強制終了
/// - ステータス操作: 信頼/注目/拡散/定着/フォロワーの増減
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

    private bool _visible;
    private Rect _windowRect = new Rect(24, 24, 360, 600);
    private Vector2 _scroll;
    private GUIStyle _headerStyle;

    [Inject]
    public void Construct(
        TomsModel tomsModel,
        GameFlowManager gameFlowManager,
        BuzzSystem buzzSystem,
        ShopStatusModel statusModel,
        TurnPhaseManager turnPhaseManager,
        PortfolioModel portfolioModel,
        ItemModel itemModel)
    {
        _tomsModel = tomsModel;
        _gameFlowManager = gameFlowManager;
        _buzzSystem = buzzSystem;
        _statusModel = statusModel;
        _turnPhaseManager = turnPhaseManager;
        _portfolioModel = portfolioModel;
        _itemModel = itemModel;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F12))
        {
            _visible = !_visible;
        }
    }

    private void OnGUI()
    {
        if (!_visible) return;

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
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(540));

        DrawInfoSection();
        DrawEconomySection();
        DrawFlowSection();
        DrawBuzzSection();
        DrawStatusSection();

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
            GUILayout.Label($"所持金: {_tomsModel.PlayerMoney.Value:N0} G");

        if (_buzzSystem != null)
        {
            string buzzState = _buzzSystem.IsBuzzActive.Value
                ? $"{GetBuzzLabel(_buzzSystem.CurrentBuzzType.Value)}（残り{_buzzSystem.RemainingTurns.Value}ターン）"
                : "なし";
            GUILayout.Label($"バズ状態: {buzzState}");
            GUILayout.Label($"バズ発生確率: {_buzzSystem.CalculateBuzzChance():F1} %");
            GUILayout.Label($"超バズ発生確率: {_buzzSystem.CalculateBigBuzzChance():F1} %");
        }

        if (_statusModel != null)
        {
            GUILayout.Label(
                $"信頼:{_statusModel.Trust.Value} 注目:{_statusModel.Attention.Value} " +
                $"拡散:{_statusModel.Spread.Value} 定着:{_statusModel.Retention.Value}");
            GUILayout.Label($"フォロワー: {_statusModel.Followers.Value:N0}");
        }

        GUILayout.Space(8);
    }

    // =====================================================
    // 経済操作
    // =====================================================
    private void DrawEconomySection()
    {
        GUILayout.Label("■ 経済", _headerStyle);
        if (_tomsModel == null)
        {
            GUILayout.Label("TomsModel なし");
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+1,000G")) _tomsModel.PlayerMoney.Value += 1000;
        if (GUILayout.Button("+10,000G")) _tomsModel.PlayerMoney.Value += 10000;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("半減")) _tomsModel.PlayerMoney.Value /= 2;
        if (GUILayout.Button("0にする")) _tomsModel.PlayerMoney.Value = 0;
        GUILayout.EndHorizontal();

        // 店レベル操作（陳列上限の検証用）
        GUILayout.BeginHorizontal();
        GUILayout.Label($"店Lv: {_tomsModel.ShopLevel.Value}");
        if (GUILayout.Button("店Lv +1")) _tomsModel.ShopLevel.Value++;
        if (GUILayout.Button("店Lv 1に")) _tomsModel.ShopLevel.Value = 1;
        GUILayout.EndHorizontal();

        // 情報屋レベル操作（金融商品の解放検証用）
        GUILayout.BeginHorizontal();
        GUILayout.Label($"情報屋Lv: {_tomsModel.InfoBrokerLevel.Value}");
        if (GUILayout.Button("情報屋Lv +1")) _tomsModel.InfoBrokerLevel.Value++;
        GUILayout.EndHorizontal();

        // 金融資産の状態（検証用）
        if (_portfolioModel != null)
        {
            GUILayout.Label($"金融資産: ポジション {_portfolioModel.Positions.Count}件 / 評価額 {_portfolioModel.TotalAssetsEstimate.Value:N0}G");
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

    // =====================================================
    // 進行操作
    // =====================================================
    private void DrawFlowSection()
    {
        GUILayout.Label("■ 進行", _headerStyle);
        if (_gameFlowManager == null)
        {
            GUILayout.Label("GameFlowManager なし");
            return;
        }

        if (GUILayout.Button("次ターンへ（NextTurn）"))
        {
            _gameFlowManager.NextTurn();
        }
        GUILayout.Label("※フロー上のイベント/戦闘ノードへも通常通り遷移します");

        GUILayout.Space(8);
    }

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
