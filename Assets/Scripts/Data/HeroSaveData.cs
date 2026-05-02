using System;

/// <summary>
/// JSON保存用のHeroデータ。ReactivePropertyを使わない純粋なPOCO。
/// </summary>
[Serializable]
public class HeroSaveData
{
    public int level;
    public int experience;
    public int expToNextLevel;
    public int hp;
    public int mp;
    public string weaponId;
    public string weaponName;
    public string armorId;
    public string armorName;
    public int attackPower;
    public int defensePower;
    public int tactics; // HeroTactics の int値
}

