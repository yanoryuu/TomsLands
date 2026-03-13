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

    /// <summary>配信フェーズ内の現在のサブフェーズ</summary>
    public ReactiveProperty<StreamingGamePhase> CurrentStreamingPhase { get; private set; }

    /// <summary>フェーズ遷移時に実行する処理を登録するディクショナリ</summary>
    private readonly Dictionary<GamePhase, Action> onEnter = new();

    /// <summary>トムの店サブフェーズ遷移時に実行する処理を登録するディクショナリ</summary>
    private readonly Dictionary<TomsShopGamePhase, Action> onEnterTomsShop = new();

    /// <summary>配信サブフェーズ遷移時に実行する処理を登録するディクショナリ</summary>
    private readonly Dictionary<StreamingGamePhase, Action> onEnterStreaming = new();

    private readonly CompositeDisposable disposables = new();

    /// <summary>UI全体のパネル制御を担当するマネージャー（任意）</summary>
    private readonly GamePanelManager gamePanelManager;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="gamePanelManager">UI表示を統合的に制御するマネージャー</param>
    public StateManager(GamePanelManager gamePanelManager)
    {
        this.gamePanelManager = gamePanelManager;
        CurrentPhase = new ReactiveProperty<GamePhase>(GamePhase.Title);
        CurrentTomsShopPhase = new ReactiveProperty<TomsShopGamePhase>(TomsShopGamePhase.Shop);
        CurrentStreamingPhase = new ReactiveProperty<StreamingGamePhase>(StreamingGamePhase.StreamingSetting);
        Bind();
        ChangePhase(GamePhase.Title);
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

        CurrentStreamingPhase
            .Subscribe(subPhase =>
            {
                try
                {
                    gamePanelManager?.ShowStreamingPanel(subPhase);

                    if (onEnterStreaming.TryGetValue(subPhase, out var handler))
                    {
                        handler?.Invoke();
                    }

                    Debug.Log($"[StateManager] Streaming sub-phase changed to {subPhase}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[StateManager] OnEnter error at Streaming.{subPhase}: {e}");
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
    /// 配信サブフェーズのOnEnter登録。
    /// </summary>
    public void RegisterOnEnter(StreamingGamePhase subPhase, Action handler)
    {
        if (onEnterStreaming.ContainsKey(subPhase))
        {
            onEnterStreaming[subPhase] += handler;
        }
        else
        {
            onEnterStreaming[subPhase] = handler;
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

    /// <summary>
    /// 配信サブフェーズを変更。メインフェーズがStreamingでない場合は自動遷移。
    /// </summary>
    public void ChangeStreamingPhase(StreamingGamePhase nextSubPhase)
    {
        if (CurrentPhase.Value != GamePhase.Streaming)
        {
            ChangePhase(GamePhase.Streaming);
        }

        if (CurrentStreamingPhase.Value == nextSubPhase)
        {
            CurrentStreamingPhase.ForceNotify();
            return;
        }
        Debug.Log($"[StateManager] Changing Streaming sub-phase: {CurrentStreamingPhase.Value} → {nextSubPhase}");
        CurrentStreamingPhase.Value = nextSubPhase;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}