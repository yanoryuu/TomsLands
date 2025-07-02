using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public class BattleCharacter : MonoBehaviour
{
    [Header("キャラクター種別")]
    [SerializeField] private bool isHero = false;

    // --- イベント定義 ---
    public event Action<int, int> OnHpChanged;
    public event Action<int, int> OnMpChanged;

    // --- プロパティ ---
    public string CharacterName { get; private set; }
    public Sprite CharacterSprite { get; private set; }
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

    void Awake()
    {
        if (isHero)
            SetupHero();
    }

    // 敵としてセットアップするメソッド
    public void Setup(EnemyData enemyData)
    {
        this.isHero = false;

        CharacterName = enemyData.enemyName;
        CharacterSprite = enemyData.enemySprite;
        MaxHp = enemyData.hp;
        MaxMp = 0;
        AttackPower = enemyData.attackPower;
        DefensePower = enemyData.defensePower;
        skills = new List<SkillData>(enemyData.skills);
        
        this.CurrentHp = MaxHp;
        this.CurrentMp = MaxMp;
        
        gameObject.name = $"[Enemy] {CharacterName}";
    }

    // 勇者としてセットアップするメソッド
    public void SetupHero()
    {
        this.isHero = true;

        CharacterName = "勇者";
        MaxHp = 200;
        MaxMp = 40;
        AttackPower = 25;
        DefensePower = 15;
        skills = new List<SkillData>();
        
        this.CurrentHp = MaxHp;
        this.CurrentMp = MaxMp;

        gameObject.name = "[Hero]";
    }

    public void TakeDamage(int damage)
    {
        int finalDamage = Mathf.Max(1, damage - DefensePower);
        CurrentHp -= finalDamage;
        Debug.Log($"{CharacterName} は {finalDamage} のダメージを受けた！ (残りHP: {CurrentHp})");
    }

    public void Act(BattleCharacter target)
    {
        target.TakeDamage(AttackPower);
    }

    public bool IsDead() => CurrentHp <= 0;
    public bool IsEnemy() => !isHero;
}