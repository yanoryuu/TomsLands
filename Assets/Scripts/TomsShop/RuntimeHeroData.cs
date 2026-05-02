using R3;

public class RuntimeHeroData
{
    public ReactiveProperty<int> level { get; private set; }
    public ReactiveProperty<int> experience { get; private set; }
    public ReactiveProperty<int> expToNextLevel { get; private set; }
    public ReactiveProperty<int> hp { get; private set; }
    public ReactiveProperty<int> mp { get; private set; }
    public ReactiveProperty<string> weaponId { get; private set; }
    public ReactiveProperty<string> weaponName { get; private set; }
    public ReactiveProperty<string> armorId { get; private set; }
    public ReactiveProperty<string> armorName { get; private set; }
    public ReactiveProperty<int> attackPower { get; private set; }
    public ReactiveProperty<int> defensePower { get; private set; }
    public ReactiveProperty<HeroTactics> tactics { get; private set; }

    private RuntimeHeroData(
        int level,
        int experience,
        int expToNextLevel,
        int hp,
        int mp,
        string weaponId,
        string weaponName,
        string armorId,
        string armorName,
        int attackPower,
        int defensePower,
        HeroTactics tactics)
    {
        this.level = new ReactiveProperty<int>(level);
        this.experience = new ReactiveProperty<int>(experience);
        this.expToNextLevel = new ReactiveProperty<int>(expToNextLevel);
        this.hp = new ReactiveProperty<int>(hp);
        this.mp = new ReactiveProperty<int>(mp);
        this.weaponId = new ReactiveProperty<string>(weaponId);
        this.weaponName = new ReactiveProperty<string>(weaponName);
        this.armorId = new ReactiveProperty<string>(armorId);
        this.armorName = new ReactiveProperty<string>(armorName);
        this.attackPower = new ReactiveProperty<int>(attackPower);
        this.defensePower = new ReactiveProperty<int>(defensePower);
        this.tactics = new ReactiveProperty<HeroTactics>(tactics);
    }

    public static RuntimeHeroData CreateDefault()
    {
        return new RuntimeHeroData(
            level: 1,
            experience: 0,
            expToNextLevel: GameConst.GetHeroExpToNextLevel(1),
            hp: 100,
            mp: 50,
            weaponId: "",
            weaponName: "",
            armorId: "",
            armorName: "",
            attackPower: 10,
            defensePower: 5,
            tactics: HeroTactics.Balanced);
    }

    public static RuntimeHeroData CreateFromLevelData(HeroLevelData levelData, int experience = 0)
    {
        return new RuntimeHeroData(
            level: levelData.Level,
            experience: experience,
            expToNextLevel: GameConst.GetHeroExpToNextLevel(levelData.Level),
            hp: levelData.MaxHp,
            mp: 0,
            weaponId: "",
            weaponName: "",
            armorId: "",
            armorName: "",
            attackPower: levelData.Attack,
            defensePower: levelData.Defense,
            tactics: HeroTactics.Balanced);
    }

    public HeroSaveData ToSaveData()
    {
        return new HeroSaveData
        {
            level = this.level.Value,
            experience = this.experience.Value,
            expToNextLevel = this.expToNextLevel.Value,
            hp = this.hp.Value,
            mp = this.mp.Value,
            weaponId = this.weaponId.Value,
            weaponName = this.weaponName.Value,
            armorId = this.armorId.Value,
            armorName = this.armorName.Value,
            attackPower = this.attackPower.Value,
            defensePower = this.defensePower.Value,
            tactics = (int)this.tactics.Value
        };
    }

    public static RuntimeHeroData CreateFromSaveData(HeroSaveData save)
    {
        if (save == null)
        {
            UnityEngine.Debug.LogWarning("[RuntimeHeroData] HeroSaveData was null. Using default values.");
            return CreateDefault();
        }

        int safeLevel = save.level > 0 ? save.level : 1;
        return new RuntimeHeroData(
            level: safeLevel,
            experience: save.experience,
            expToNextLevel: save.expToNextLevel > 0 ? save.expToNextLevel : GameConst.GetHeroExpToNextLevel(safeLevel),
            hp: save.hp,
            mp: save.mp,
            weaponId: save.weaponId ?? "",
            weaponName: save.weaponName ?? "",
            armorId: save.armorId ?? "",
            armorName: save.armorName ?? "",
            attackPower: save.attackPower,
            defensePower: save.defensePower,
            tactics: (HeroTactics)save.tactics);
    }
}
