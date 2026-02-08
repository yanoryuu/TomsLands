using UnityEngine;
        
        public class HeroRuntimeData
        {
            public int Level { get; private set; }
            public int Experience { get; private set; }
            public int ExpToNextLevel { get; private set; }
            public int CurrentHp { get; private set; }
            public int MaxHp { get; private set; }
            public int Attack { get; private set; }
            public int Mp { get; private set; }
            public string EquippedWeapon { get; private set; }
            public string EquippedArmor { get; private set; }
            public string Tactics { get; private set; }
            public int AttackPower { get; private set; }
            public int DefencePower { get; private set; }
            public Sprite HeroSprite { get; private set; }
        
            public void UpdateFromLevelData(HeroLevelData levelData)
            {
                Level = levelData.Level;
                ExpToNextLevel = levelData.ExpToNextLevel;
                MaxHp = levelData.MaxHp;
                Attack = levelData.Attack;
                Mp = levelData.MaxMp;
                CurrentHp = MaxHp;
            }
        
            public void SetEquipment(string weapon, string armor)
            {
                EquippedWeapon = weapon;
                EquippedArmor = armor;
            }
        
            public void SetTactics(string tactics) => Tactics = tactics;
            public void SetSprite(Sprite sprite) => HeroSprite = sprite;
            public void AddExperience(int exp) => Experience += exp;
            public void SetAttackPower(int power) => AttackPower = power;
            public void SetDefencePower(int power) => DefencePower = power;
            public void SetCurrentHp(int hp) => CurrentHp = Mathf.Clamp(hp, 0, MaxHp);
        }