using UnityEngine;

/// <summary>
/// 情報屋でのHero画面の情報をまとめたクラスその為、次の戦闘日なども含む
/// </summary>
public class HeroInfoData
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
    public HeroTactics Tactics { get; private set; }
    public string FlavorText { get; private set; }
    public int AttackPower { get; private set; }
    public int DefencePower { get; private set; }
    public Sprite HeroSprite { get; private set; }

    // 引数付きコンストラクタ
    public HeroInfoData(int level, int experience, int expToNextLevel, int currentHp, int maxHp,
        int attack, int mp, string equippedWeapon,
        string equippedArmor, HeroTactics tactics, string flavorText, int attackPower,
        int defencePower, Sprite heroSprite)
    {
        Level = level;
        Experience = experience;
        ExpToNextLevel = expToNextLevel;
        CurrentHp = currentHp;
        MaxHp = maxHp;
        Attack = attack;
        Mp = mp;
        EquippedWeapon = equippedWeapon;
        EquippedArmor = equippedArmor;
        Tactics = tactics;
        FlavorText = flavorText;
        AttackPower = attackPower;
        DefencePower = defencePower;
        HeroSprite = heroSprite;
    }

    //TODO: RuntimeHeroDataから情報をセットするメソッドを追加
    public void SetHeroInfo(RuntimeHeroData heroData)
    {
        Level = heroData.level.Value;
        CurrentHp = heroData.hp.Value;
        Mp = heroData.mp.Value;
        EquippedWeapon = heroData.weaponId.Value;
        EquippedArmor = heroData.armorId.Value;
        AttackPower = heroData.attackPower.Value;
        DefencePower = heroData.defensePower.Value;
        Tactics = heroData.tactics.Value;
    }
    
    // 個別セッターメソッド
    public HeroInfoData SetLevel(int value) { Level = value; return this; }
    public HeroInfoData SetExperience(int value) { Experience = value; return this; }
    public HeroInfoData SetExpToNextLevel(int value) { ExpToNextLevel = value; return this; }
    public HeroInfoData SetCurrentHp(int value) { CurrentHp = value; return this; }
    public HeroInfoData SetMaxHp(int value) { MaxHp = value; return this; }
    public HeroInfoData SetAttack(int value) { Attack = value; return this; }
    public HeroInfoData SetMp(int value) { Mp = value; return this; }
    public HeroInfoData SetEquippedWeapon(string value) { EquippedWeapon = value; return this; }
    public HeroInfoData SetEquippedArmor(string value) { EquippedArmor = value; return this; }
    public HeroInfoData SetTactics(HeroTactics value) { Tactics = value; return this; }
    public HeroInfoData SetFlavorText(string value) { FlavorText = value; return this; }
    public HeroInfoData SetAttackPower(int value) { AttackPower = value; return this; }
    public HeroInfoData SetDefencePower(int value) { DefencePower = value; return this; }
    public HeroInfoData SetHeroSprite(Sprite value) { HeroSprite = value; return this; }
}
