using R3;

public interface IBattleCharacterViewModel
{
    ReadOnlyReactiveProperty<int> CurrentHp { get; }
    ReadOnlyReactiveProperty<int> CurrentMp { get; }
    int MaxHp { get; }
}