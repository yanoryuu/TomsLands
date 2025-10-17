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

    /// <summary>フェーズ遷移時に実行する処理を登録するディクショナリ</summary>
    private readonly Dictionary<GamePhase, Action> onEnter = new();

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
    }

    /// <summary>
    /// 任意のPresenterなどが「このフェーズに入った時に呼ばれる処理」を登録。
    /// </summary>
    public void RegisterOnEnter(GamePhase phase, Action handler)
    {
        if (onEnter.ContainsKey(phase))
        {
            onEnter[phase] += handler; // 追加登録（複数OK）
        }
        else
        {
            onEnter[phase] = handler;
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

    public void Dispose()
    {
        disposables.Dispose();
    }
}