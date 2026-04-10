using R3;

public class RuntimeHeroData
{
    public ReactiveProperty<int> level { get; private set; }
    public ReactiveProperty<int> hp {get; private set;}
    public ReactiveProperty<int> mp {get; private set;}
    public ReactiveProperty<string> weaponId {get; private set;}
    public ReactiveProperty<string> weaponName {get; private set;}
    public ReactiveProperty<string> armorId {get; private set;}
    public ReactiveProperty<string> armorName {get; private set;}
    public ReactiveProperty<int> attackPower {get; private set;}
    public ReactiveProperty<int> defensePower {get; private set;}
    public ReactiveProperty<HeroTactics> tactics {get; private set;}

    private RuntimeHeroData(int level,int hp ,int mp,string weaponId,string weaponName,string armorId,string armorName,int attackPower,int defensePower, HeroTactics tactics)
    {
        this.level = new ReactiveProperty<int>(level);
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
            hp: 100,
            mp: 50,
            weaponId: "weapon_001",
            weaponName: "",
            armorId: "armor_001",
            armorName: "",
            attackPower: 10,
            defensePower: 5,
            tactics: HeroTactics.Balanced
        );
    }

    /// <summary>
    /// HeroLevelData (CSV由来) から RuntimeHeroData を生成する
    /// </summary>
    public static RuntimeHeroData CreateFromLevelData(HeroLevelData levelData)
    {
        return new RuntimeHeroData(
            level: levelData.Level,
            hp: levelData.MaxHp,
            mp: 0,
            weaponId: "",
            weaponName: "",
            armorId: "",
            armorName: "",
            attackPower: levelData.Attack,
            defensePower: levelData.Defense,
            tactics: HeroTactics.Balanced
        );
    }

    /// <summary>
    /// JSON保存用の HeroSaveData に変換する
    /// </summary>
    public HeroSaveData ToSaveData()
    {
        return new HeroSaveData
        {
            level = this.level.Value,
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

    /// <summary>
    /// JSON読み込み後の HeroSaveData から RuntimeHeroData を復元する
    /// </summary>
    public static RuntimeHeroData CreateFromSaveData(HeroSaveData save)
    {
        if (save == null)
        {
            UnityEngine.Debug.LogWarning("[RuntimeHeroData] HeroSaveData が null です。デフォルト値を使用します。");
            return CreateDefault();
        }
        return new RuntimeHeroData(
            level: save.level,
            hp: save.hp,
            mp: save.mp,
            weaponId: save.weaponId ?? "",
            weaponName: save.weaponName ?? "",
            armorId: save.armorId ?? "",
            armorName: save.armorName ?? "",
            attackPower: save.attackPower,
            defensePower: save.defensePower,
            tactics: (HeroTactics)save.tactics
        );
    }
}