#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

/// <summary>
/// デバッグメニュー「画面移動」タブ。
/// 店内フェーズ（StateManager）/ ターン内フェーズ（TurnPhaseManager）/ シーン間移動を行う。
/// 依存は本体の Construct を触らず、ここで追加の [Inject] メソッドとして任意解決する。
/// </summary>
public partial class DebugMenuView
{
    private StateManager _navStateManager;
    private SceneTransitionService _navSceneTransition;
    private StartModeData _navStartModeData;
    private BattleInputData _navBattleInputData;
    private SellOrderModel _navSellOrderModel;
    private bool _navInjected;

    private static readonly TomsShopGamePhase[] NavShopPhases =
        (TomsShopGamePhase[])Enum.GetValues(typeof(TomsShopGamePhase));

    [Inject]
    public void ConstructNavigation(IObjectResolver resolver)
    {
        _navInjected = true;
        resolver.TryResolve(out _navStateManager);
        resolver.TryResolve(out _navSceneTransition);
        resolver.TryResolve(out _navStartModeData);
        resolver.TryResolve(out _navBattleInputData);
        resolver.TryResolve(out _navSellOrderModel);
    }

    private void DrawNavigationSection()
    {
        // ---- 現在地 ----
        GUILayout.Label("■ 現在地", _headerStyle);
        GUILayout.Label($"シーン: {SceneManager.GetActiveScene().name}");
        if (_navStateManager != null)
            GUILayout.Label($"フェーズ: {_navStateManager.CurrentPhase.Value} / 店内: {_navStateManager.CurrentTomsShopPhase.Value}");
        if (_turnPhaseManager != null)
            GUILayout.Label($"ターン内フェーズ: {_turnPhaseManager.CurrentTurnPhase.Value}");
        GUILayout.Label($"セーブスロット: {SaveSlotManager.CurrentSlot + 1}（進行中ラン: {(SaveSlotManager.Exists(SaveSlotManager.CurrentSlot) ? "あり" : "なし")}）");
        if (!_navInjected)
            GUILayout.Label("※ ConstructNavigation が呼ばれていません（追加依存なしで動作）");

        GUILayout.Space(8);
        DrawNavShopPhaseSection();
        GUILayout.Space(8);
        DrawNavTurnPhaseSection();
        GUILayout.Space(8);
        DrawNavSceneSection();
    }

    // ------------------------------------------------------------
    // 店内フェーズ（TomsShopGamePhase）
    // ------------------------------------------------------------
    private void DrawNavShopPhaseSection()
    {
        GUILayout.Label("■ 店内画面（TomsShopGamePhase）", _headerStyle);
        if (_navStateManager == null)
        {
            GUILayout.Label("（店シーン以外では使用不可）");
            return;
        }

        const int perRow = 3;
        for (int i = 0; i < NavShopPhases.Length; i += perRow)
        {
            GUILayout.BeginHorizontal();
            for (int j = i; j < i + perRow && j < NavShopPhases.Length; j++)
            {
                var phase = NavShopPhases[j];
                bool available = _navStateManager.HasHandler(phase);
                bool current = _navStateManager.CurrentPhase.Value == GamePhase.TomsShop
                               && _navStateManager.CurrentTomsShopPhase.Value == phase;
                GUI.enabled = available;
                string label = available ? phase.ToString() : $"{phase}(未配線)";
                if (current) label = "▶" + label;
                if (GUILayout.Button(label))
                {
                    _navStateManager.ChangeTomsShopPhase(phase);
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("※ 未配線（HasHandler=false）の画面は空画面で操作不能になるため無効化");
    }

    // ------------------------------------------------------------
    // ターン内フェーズ（TurnPhaseManager）
    // ------------------------------------------------------------
    private void DrawNavTurnPhaseSection()
    {
        GUILayout.Label("■ ターン内フェーズ（イベント→仕入れ→陳列→営業）", _headerStyle);
        if (_turnPhaseManager == null)
        {
            GUILayout.Label("（店シーン以外では使用不可）");
            return;
        }

        var cur = _turnPhaseManager.CurrentTurnPhase.Value;

        GUILayout.BeginHorizontal();
        GUI.enabled = _turnPhaseManager.CanGoBack();
        if (GUILayout.Button("◀ 前へ")) _turnPhaseManager.GoBackTurnPhase();
        GUI.enabled = cur != TurnPhase.Sales;
        if (GUILayout.Button("次へ ▶")) _turnPhaseManager.AdvanceTurnPhase();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        // 直接ジャンプ: 正規の Advance / GoBack を繰り返して到達させる（不正な値の直接代入はしない）
        GUILayout.BeginHorizontal();
        GUI.enabled = false;
        GUILayout.Button("イベント(不可)");
        GUI.enabled = true;
        DrawNavTurnJumpButton("仕入れ", TurnPhase.Procurement, cur);
        DrawNavTurnJumpButton("陳列", TurnPhase.Display, cur);
        DrawNavTurnJumpButton("営業", TurnPhase.Sales, cur);
        GUILayout.EndHorizontal();
        GUILayout.Label("※ イベントは消化済みのため戻れない（正規APIの GoBack も仕入れが下限）");
    }

    private void DrawNavTurnJumpButton(string label, TurnPhase target, TurnPhase cur)
    {
        bool isCurrent = cur == target;
        GUI.enabled = !isCurrent;
        if (GUILayout.Button(isCurrent ? "▶" + label : label))
        {
            NavJumpTurnPhase(target);
        }
        GUI.enabled = true;
    }

    private void NavJumpTurnPhase(TurnPhase target)
    {
        // TurnPhase は Event < Procurement < Display < Sales の順
        for (int guard = 0; guard < 8 && _turnPhaseManager.CurrentTurnPhase.Value != target; guard++)
        {
            var before = _turnPhaseManager.CurrentTurnPhase.Value;
            if (before < target) _turnPhaseManager.AdvanceTurnPhase();
            else _turnPhaseManager.GoBackTurnPhase();
            if (_turnPhaseManager.CurrentTurnPhase.Value == before) break; // 進めない（下限/終端）
        }
    }

    // ------------------------------------------------------------
    // シーン移動
    // ------------------------------------------------------------
    private void DrawNavSceneSection()
    {
        GUILayout.Label("■ シーン移動", _headerStyle);
        bool inShop = _gameFlowManager != null && _tomsModel != null;
        bool hasRun = SaveSlotManager.Exists(SaveSlotManager.CurrentSlot);

        if (inShop)
            GUILayout.Label("※ 店シーンからの移動は、遷移前に進行中ランをセーブします");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("タイトル"))
        {
            NavSaveRunIfInShop();
            NavLoad("TitleScene", s => s.GoToTitle());
        }
        if (GUILayout.Button("村（新規ラン扱い）"))
        {
            NavSaveRunIfInShop();
            // タイトルの「はじめから」と同じく NewGame を立ててから村へ（村→準備→店で新規ランになる）
            NavStartMode()?.SetNewGame();
            NavLoad("VillageScene", s => s.GoToVillage());
        }
        if (GUILayout.Button("準備（新規ラン扱い）"))
        {
            NavSaveRunIfInShop();
            NavStartMode()?.SetNewGame();
            NavLoad("PreparationScene", null);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUI.enabled = hasRun || inShop;
        if (GUILayout.Button(hasRun || inShop ? "店（続きから）" : "店（続きから：ランなし）"))
        {
            NavSaveRunIfInShop();
            NavStartMode()?.SetContinue();
            NavLoad("TomsShop", s => s.ReturnToTomsShop());
        }
        GUI.enabled = true;
        if (GUILayout.Button("店（新規ラン・現スロット上書き）"))
        {
            NavStartMode()?.SetNewGame();
            NavLoad("TomsShop", s => s.ReturnToTomsShop());
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUI.enabled = inShop && _navBattleInputData != null && _dungeonRepository != null && _heroModel != null;
        if (GUILayout.Button(GUI.enabled ? "配信（次のダンジョン）" : "配信（店シーンのみ）"))
        {
            NavGoToBattle();
        }
        GUI.enabled = hasRun || inShop;
        if (GUILayout.Button(GUI.enabled ? "リザルト" : "リザルト（ランなし）"))
        {
            NavSaveRunIfInShop();
            NavLoad("ResultScene", s => s.GoToResult());
        }
        if (GUILayout.Button(GUI.enabled ? "ゲームオーバー" : "ゲームオーバー（ランなし）"))
        {
            NavSaveRunIfInShop();
            NavLoad("GameOver", s => s.GoToGameOver());
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUI.enabled = false;
        GUILayout.Button("イベント（BuildSettings未登録・EventInputData要）");
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Label("※ 配信: 現在のターン位置のまま次の配信ダンジョンへ。帰還後もターンは進まない（テスト用）");
        GUILayout.Label("※ リザルト/ゲームオーバーで「村へ」等を押すと進行中ランは削除される（通常仕様）");
    }

    private StartModeData NavStartMode()
    {
        if (_navStartModeData == null)
            _navStartModeData = AddressableLoader.Load<StartModeData>("SceneData/StartModeData");
        return _navStartModeData;
    }

    /// <summary>遷移サービスがあれば経由し、無いシーンでは既存Presenterと同じく SceneManager で直接ロードする。</summary>
    private void NavLoad(string sceneName, Action<SceneTransitionService> viaService)
    {
        Debug.Log($"[DebugMenu] 画面移動 → {sceneName}");
        if (_navSceneTransition != null && viaService != null)
            viaService(_navSceneTransition);
        else
            SceneManager.LoadScene(sceneName);
    }

    /// <summary>店シーンにいるときは GameFlowManager の遷移前処理と同じ内容でランをセーブする。</summary>
    private void NavSaveRunIfInShop()
    {
        if (_gameFlowManager == null || _tomsModel == null) return;
        _itemModel?.SaveData();
        _navSellOrderModel?.SaveData();
        _portfolioModel?.SaveData();
        _tomsModel.CurrentTurn.Value = _gameFlowManager.CurrentTurn.Value;
        _tomsModel.GameFlowIndex = _gameFlowManager.CurrentIndex;
        _tomsModel.SavePlayerMoney();
    }

    /// <summary>
    /// GameFlowManager.NextTurn の配信分岐と同じ手順で BattleInputData を作って FightScene へ。
    /// GameFlowIndex は現在位置のままなので、帰還後は同じターンに戻る。
    /// </summary>
    private void NavGoToBattle()
    {
        DungeonName? key = _gameFlowManager.GetNextBattleDungeon();
        if (key == null)
        {
            var all = _dungeonRepository.GetAll();
            if (all == null || all.Count == 0)
            {
                Debug.LogWarning("[DebugMenu] 配信先ダンジョンが見つかりません");
                return;
            }
            key = all[0].key;
        }

        NavSaveRunIfInShop();

        var dungeon = _dungeonRepository.GetById(key.Value);
        int level = dungeon?.currentDungeonLevel ?? 1;
        _navBattleInputData.Setup(
            key.Value,
            level,
            new List<string>(_heroModel.EquippedItemIds),
            new List<BattleInputItem>(),
            _gameFlowManager.CurrentIndex);

        Debug.Log($"[DebugMenu] 画面移動 → FightScene（{key.Value} Lv{level}）");
        if (_navSceneTransition != null)
            _navSceneTransition.GoToBattle();
        else
            SceneManager.LoadScene("FightScene");
    }
}
#endif
