using R3;

public class RuntimeHeroData
{
    public ReactiveProperty<int> level { get; private set; }
    public ReactiveProperty<int> hp {get; private set;}
    public ReactiveProperty<int> mp {get; private set;}
    public ReactiveProperty<string> weaponId {get; private set;}
    public ReactiveProperty<string> armorId {get; private set;}

    public RuntimeHeroData(int level,int hp ,int mp,string weaponId,string armorId)
    {
        this.level = new ReactiveProperty<int>(level);
        this.hp = new ReactiveProperty<int>(hp);
        this.mp = new ReactiveProperty<int>(mp);
        this.weaponId = new ReactiveProperty<string>(weaponId);
        this.armorId = new ReactiveProperty<string>(armorId);
    }
}
