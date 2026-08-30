using R3;
using System.Collections.Generic;
using UnityEngine;

public enum CharacterType
{
    Hero,
    Enemy
}

public class CharacterModel
{
    public string Id { get; }
    public string Name { get; }
    public CharacterType Type { get; }
    public ReactiveProperty<int> CurrentHp { get; }
    public int MaxHp { get; }
    public ReactiveProperty<int> CurrentMp { get; }
    public int MaxMp { get; }
    public int AttackPower { get; }
    public int DefensePower { get; }
    public ItemData EquippedWeapon { get; }
    public ItemData EquippedArmor { get; }
    public ElementType Element { get; }
    public bool IsBoss { get; }
    public IReadOnlyList<SkillData> Skills { get; }
    public Observable<Unit> OnDied => CurrentHp.Where(hp => hp <= 0).AsUnitObservable();
    public bool IsDead => CurrentHp.CurrentValue <= 0;
    public Sprite CharacterSprite { get; }

    public CharacterModel(EnemyData data)
    {
        Id = data.enemyId;
        Name = data.enemyName;
        Type = CharacterType.Enemy;
        CharacterSprite = data.enemySprite;
        Element = data.elementType;
        IsBoss = data.isBoss;
        MaxHp = data.hp;
        CurrentHp = new ReactiveProperty<int>(data.hp);
        MaxMp = 0;
        CurrentMp = new ReactiveProperty<int>(0);
        AttackPower = data.attackPower;
        DefensePower = data.defensePower;
        Skills = new List<SkillData>(data.skills);
    }

    /// <summary>
    /// 勇者データからModelを生成するためのコンストラクタ。
    /// powerMul はレリック補正（HeroPowerMul。1未満で勇者が弱くなり防衛=敗北を狙いやすくなる）。
    /// </summary>
    public CharacterModel(HeroData masterData, HeroModel savedHeroModel, float powerMul = 1f)
    {
        Name = masterData.heroName;
        Type = CharacterType.Hero;
        CharacterSprite = masterData.heroSprite;
        Element = ElementType.None;
        IsBoss = false;

        int Scale(int value) => Mathf.Max(1, Mathf.RoundToInt(value * powerMul));

        // RuntimeHeroData (CSV由来) からステータスを取得
        var runtime = savedHeroModel?.heroData;
        if (runtime != null)
        {
            MaxHp = Scale(runtime.hp.Value);
            CurrentHp = new ReactiveProperty<int>(MaxHp);
            MaxMp = runtime.mp.Value;
            CurrentMp = new ReactiveProperty<int>(runtime.mp.Value);
            AttackPower = Scale(runtime.attackPower.Value);
            DefensePower = Scale(runtime.defensePower.Value);
            Debug.Log($"[CharacterModel] Hero created from RuntimeHeroData: HP={MaxHp}, AT={AttackPower}, DF={DefensePower} (powerMul={powerMul:F2})");
        }
        else
        {
            // フォールバック: ScriptableObject のデフォルト値を使用
            MaxHp = Scale(masterData.hp);
            CurrentHp = new ReactiveProperty<int>(MaxHp);
            MaxMp = masterData.mp;
            CurrentMp = new ReactiveProperty<int>(masterData.mp);
            AttackPower = Scale(masterData.attackPower);
            DefensePower = Scale(masterData.defensePower);
            Debug.LogWarning("[CharacterModel] RuntimeHeroData が null のため、HeroData ScriptableObject のデフォルト値を使用します。");
        }

        Skills = new List<SkillData>();
    }
    public int ApplyDamage(int damage)
    {
        if (IsDead) return 0;
        
        int finalDamage = Mathf.Max(1, damage - DefensePower);
        CurrentHp.Value -= finalDamage;
        return finalDamage;
    }
}
