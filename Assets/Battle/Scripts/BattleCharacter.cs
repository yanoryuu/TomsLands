using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public class BattleCharacter : MonoBehaviour
{

    public event Action<int, int> OnHpChanged; // <現在HP, 最大HP>
    public event Action<int, int> OnMpChanged; // <現在MP, 最大MP>
    public string CharacterName { get; private set; }
    public Sprite CharacterSprite { get; private set; } // タイムラインUI用
    public bool IsEnemy { get; private set; }
    public int MaxHp { get; private set; }
    public int MaxMp { get; private set; }
    public int AttackPower { get; private set; }
    public int DefensePower { get; private set; }

    private int _currentHp;
    public int CurrentHp
    {
        get => _currentHp;
        private set
        {
            _currentHp = Mathf.Clamp(value, 0, MaxHp);
            OnHpChanged?.Invoke(_currentHp, MaxHp);
        }
    }

    private int _currentMp;
    public int CurrentMp
    {
        get => _currentMp;
        private set
        {
            _currentMp = Mathf.Clamp(value, 0, MaxMp);
            OnMpChanged?.Invoke(_currentMp, MaxMp);
        }
    }
    
    private List<SkillData> skills;

    public void Setup(EnemyData enemyData)
    {
        CharacterName = enemyData.enemyName;
        CharacterSprite = enemyData.enemySprite;
        IsEnemy = true;

        MaxHp = enemyData.hp;
        MaxMp = 0;
        AttackPower = enemyData.attackPower;
        DefensePower = enemyData.defensePower;
        skills = new List<SkillData>(enemyData.skills);
        
        this.CurrentHp = MaxHp;
        this.CurrentMp = MaxMp;
        
        gameObject.name = $"[Enemy] {CharacterName}";
    }

    public void SetupHero() 
    {
        // TODO: 本来はHeroDataや装備、レベルからステータスを計算する
        CharacterName = "勇者";
        //CharacterSprite = heroData.sprite; // HeroDataからスプライトを設定
        IsEnemy = false;

        MaxHp = 200;
        MaxMp = 40;
        AttackPower = 25;
        DefensePower = 15;
        skills = new List<SkillData>(); // TODO: 勇者のスキルを設定
        
        this.CurrentHp = MaxHp;
        this.CurrentMp = MaxMp;

        gameObject.name = "[Hero]";
    }

    public void TakeDamage(int damage)
    {
        int finalDamage = Mathf.Max(1, damage - DefensePower);
        CurrentHp -= finalDamage;

        // ダメージを受けたという事実自体はManagerにログとして送ってもらう
        Debug.Log($"{CharacterName} は {finalDamage} のダメージを受けた！ (残りHP: {CurrentHp})");
    }

    public void Act(BattleCharacter target)
    {
        // シンプルなAI: とりあえず通常攻撃
        target.TakeDamage(AttackPower);
    }

    public bool IsDead() => CurrentHp <= 0;
}
