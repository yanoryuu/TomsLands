using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

/// <summary>
/// ゲーム全体の状態を管理し、フェーズ遷移に応じて画面やPresenterを切り替えるクラス。
/// Presenterを直接保持せず、「OnEnter登録型」で依存を一方向にすることで循環依存を回避。
/// </summary>
public class StateManager : IDisposable
{
    /// <summary>現在のフェーズ</summary>
    public ReactiveProperty<GamePhase> CurrentPhase { get; private set; }

    /// <summary>トムの店フェーズ内の現在のサブフェーズ</summary>
    public ReactiveProperty<TomsShopGamePhase> CurrentTomsShopPhase { get; private set; }

    /// <summary>フェーズ遷移時に実行する処理を登録するディクショナリ</summary>
    private readonly Dictionary<GamePhase, Action> onEnter = new();

    /// <summary>トムの店サブフェーズ遷移時に実行する処理を登録するディクショナリ</summary>
    private readonly Dictionary<TomsShopGamePhase, Action> onEnterTomsShop = new();


    private readonly CompositeDisposable disposables = new();

    /// <summary>UI全体のパネル制御を担当するマネージャー（任意）</summary>
    private readonly GamePanelManager gamePanelManager;

    /// <summary>シーン間で渡される開始モード</summary>
    private readonly StartModeData _startModeData;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="gamePanelManager">UI表示を統合的に制御するマネージャー</param>
    /// <param name="startModeData">タイトルシーンから渡される開始モード</param>
    public StateManager(GamePanelManager gamePanelManager, StartModeData startModeData)
    {
        this.gamePanelManager = gamePanelManager;
        _startModeData = startModeData;

        // タイトルシーンからどちらのモードで来ても、TomsShopシーン内では常にTomsShopフェーズから開始
        // （Preparationは別シーンに分離済み）
        CurrentPhase = new ReactiveProperty<GamePhase>(GamePhase.TomsShop);
        CurrentTomsShopPhase = new ReactiveProperty<TomsShopGamePhase>(TomsShopGamePhase.Shop);
        Bind();
        // 初期フェーズを確実に発火させる（同値ガードを回避）
        CurrentPhase.ForceNotify();

        Debug.Log($"[StateManager] Initialized with StartMode={_startModeData.Mode}, InitialPhase=TomsShop");
    }

    /// <summary>
    /// フェーズ切り替え時の挙動を購読。
    /// </summary>
    private void Bind()
    {
        CurrentPhase
            .Subscribe(phase =>
            {
                try
                {
                    // まずUI切替（GamePanelManager）
                    gamePanelManager?.ShowPanel(phase);

                    // 登録されたEnterイベント実行
                    if (onEnter.TryGetValue(phase, out var handler))
                    {
                        handler?.Invoke();
                    }

                    // サブフェーズを持つフェーズに入ったら、サブフェーズを強制発火してパネルを表示させる
                    if (phase == GamePhase.TomsShop)
                    {
                        CurrentTomsShopPhase.ForceNotify();
                    }

                    Debug.Log($"[StateManager] Phase changed to {phase}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[StateManager] OnEnter error at {phase}: {e}");
                }
            })
            .AddTo(disposables);

        CurrentTomsShopPhase
            .Subscribe(subPhase =>
            {
                // メインフェーズがTomsShopでなければパネル切替しない
                if (CurrentPhase.Value != GamePhase.TomsShop) return;

                try
                {
                    gamePanelManager?.ShowTomsShopPanel(subPhase);

                    if (onEnterTomsShop.TryGetValue(subPhase, out var handler))
                    {
                        handler?.Invoke();
                    }

                    Debug.Log($"[StateManager] TomsShop sub-phase changed to {subPhase}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[StateManager] OnEnter error at TomsShop.{subPhase}: {e}");
                }
            })
            .AddTo(disposables);
    }

    /// <summary>
    /// 任意のPresenterなどが「このフェーズに入った時に呼ばれる処理」を登録。
    /// </summary>
    public void RegisterOnEnter(GamePhase phase, Action handler)
    {
        if (onEnter.ContainsKey(phase))
        {
            onEnter[phase] += handler;
        }
        else
        {
            onEnter[phase] = handler;
        }
    }

    /// <summary>
    /// トムの店サブフェーズのOnEnter登録。
    /// </summary>
    public void RegisterOnEnter(TomsShopGamePhase subPhase, Action handler)
    {
        if (onEnterTomsShop.ContainsKey(subPhase))
        {
            onEnterTomsShop[subPhase] += handler;
        }
        else
        {
            onEnterTomsShop[subPhase] = handler;
        }
    }


    /// <summary>
    /// 現在のフェーズを変更。購読側で自動的に切替・処理が実行される。
    /// </summary>
    public void ChangePhase(GamePhase nextPhase)
    {
        if (CurrentPhase.Value == nextPhase) return;
        Debug.Log($"[StateManager] Changing phase: {CurrentPhase.Value} → {nextPhase}");
        CurrentPhase.Value = nextPhase;
    }

    /// <summary>
    /// トムの店サブフェーズを変更。メインフェーズがTomsShopでない場合は自動遷移。
    /// </summary>
    public void ChangeTomsShopPhase(TomsShopGamePhase nextSubPhase)
    {
        if (CurrentPhase.Value != GamePhase.TomsShop)
        {
            ChangePhase(GamePhase.TomsShop);
        }

        if (CurrentTomsShopPhase.Value == nextSubPhase)
        {
            // 同じ値でも強制発火させたい場合はForceNotifyを使う
            CurrentTomsShopPhase.ForceNotify();
            return;
        }
        Debug.Log($"[StateManager] Changing TomsShop sub-phase: {CurrentTomsShopPhase.Value} → {nextSubPhase}");
        CurrentTomsShopPhase.Value = nextSubPhase;
    }


    public void Dispose()
    {
        disposables.Dispose();
    }
}