using R3;
using System;
using UnityEngine;

public class CharacterPresenter : IDisposable
{
    private readonly CharacterModel model;
    private readonly CharacterView view;
    private readonly BattleSequencer sequencer;
    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public Subject<CharacterModel> OnTakeDamage { get; } = new Subject<CharacterModel>();
    public CharacterModel GetModel() => model;
    public CharacterView GetView() => view;

    public CharacterPresenter(CharacterModel model, CharacterView view, BattleSequencer sequencer)
    {
        this.model = model;
        this.view = view;
        this.sequencer = sequencer;
        Bind();
    }

    private void Bind()
    {
        view.Initialize(this, model.Name, model.CharacterSprite);

        view.OnClicked
            .Subscribe(_ => Debug.Log($"{model.Name} がクリックされました！(Presenterが検知)"))
            .AddTo(disposables);
    }

    public int PerformAttack(CharacterPresenter targetPresenter)
    {
        var targetModel = targetPresenter.GetModel();
        int damageDealt = targetModel.ApplyDamage(this.model.AttackPower);
        targetPresenter.OnTakeDamage.OnNext(this.model);
        sequencer.OnCharacterDamaged.OnNext((this.model, targetModel));
        return damageDealt;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}