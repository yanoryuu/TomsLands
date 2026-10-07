﻿using R3;
using System;
using UnityEngine;
using VContainer.Unity;

public class CharacterPresenter : IDisposable, IBattleCharacterViewModel,IStartable
{
    private readonly CharacterModel model;
    private readonly CharacterView view;
    private readonly BattleSequencer sequencer;
    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public ReadOnlyReactiveProperty<int> CurrentHp => model.CurrentHp;
    public ReadOnlyReactiveProperty<int> CurrentMp => model.CurrentMp;
    public int MaxHp => model.MaxHp;
    public int MaxMp => model.MaxMp;

    public Subject<CharacterModel> OnTakeDamage { get; } = new Subject<CharacterModel>();
    public CharacterModel GetModel() => model;
    public CharacterView GetView() => view;

    public CharacterPresenter(CharacterModel model, CharacterView view, BattleSequencer sequencer)
    {
        this.model = model;
        this.view = view;
        this.sequencer = sequencer;
    }

    /// <summary>
    /// View の初期化と Model の監視を開始する。
    /// CharacterFactory から Instantiate 後に呼び出すこと。
    /// </summary>
    public void Initialize()
    {
        Bind();
    }

    public void Start()
    {
        // VContainer 経由の場合のフォールバック（通常は手動 new + Initialize）
    }

    private void Bind()
    {
        view.Initialize(this, model.Name, model.CharacterSprite);

        view.OnClicked
            .Subscribe(_ =>
            {
                Debug.Log($"{model.Name} がクリックされました！(Presenterが検知)");
                // 介入（青スパ）の対象指定に使う。購読側が無ければ何も起きない
                if (sequencer != null) sequencer.OnCharacterClicked.OnNext(this);
            })
            .AddTo(disposables);
    }

    /// <summary>
    /// 攻撃する。damageMultiplier は攻撃力に掛ける倍率（介入: スキル・必殺技・呪い・ボス強化）、
    /// bonusDamage は防御無視の追加ダメージ（介入: 罠）。既定値（1, 0）なら従来と同じ計算。
    /// </summary>
    public int PerformAttack(CharacterPresenter targetPresenter, float damageMultiplier = 1f, int bonusDamage = 0)
    {
        var targetModel = targetPresenter.GetModel();
        int attack = damageMultiplier == 1f
            ? this.model.AttackPower
            : Mathf.Max(1, Mathf.RoundToInt(this.model.AttackPower * damageMultiplier));
        int damageDealt = targetModel.ApplyDamage(attack);
        if (bonusDamage > 0) damageDealt += targetModel.ApplyBonusDamage(bonusDamage);
        targetPresenter.OnTakeDamage.OnNext(this.model);
        sequencer.OnCharacterDamaged.OnNext((this.model, targetModel));
        return damageDealt;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}