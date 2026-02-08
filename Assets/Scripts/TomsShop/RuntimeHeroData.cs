using R3;

public class RuntimeHeroData
{
    public ReactiveProperty<int> level { get; private set; }
    public ReactiveProperty<int> hp {get; private set;}
    public ReactiveProperty<int> mp {get; private set;}
    public ReactiveProperty<string> weaponId {get; private set;}
    public ReactiveProperty<string> armorId {get; private set;}
    public ReactiveProperty<int> attackPower {get; private set;}
    public ReactiveProperty<int> defensePower {get; private set;}
    public ReactiveProperty<HeroTactics> tactics {get; private set;}

    private RuntimeHeroData(int level,int hp ,int mp,string weaponId,string armorId,int attackPower,int defensePower, HeroTactics tactics)
    {
        this.level = new ReactiveProperty<int>(level);
        this.hp = new ReactiveProperty<int>(hp);
        this.mp = new ReactiveProperty<int>(mp);
        this.weaponId = new ReactiveProperty<string>(weaponId);
        this.armorId = new ReactiveProperty<string>(armorId);
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
            armorId: "armor_001",
            attackPower: 10,
            defensePower: 5,
            tactics: HeroTactics.Balanced
        );
    }
}