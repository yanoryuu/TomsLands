using UnityEngine;

[CreateAssetMenu(fileName = "New HeroData", menuName = "Battle/HeroData")]
public class HeroData : ScriptableObject
{
    [Header("基本ステータス")]
    public string heroName = "勇者";
    public int hp = 300;
    public int mp = 40;
    public int attackPower = 60;
    public int defensePower = 15;
    public Sprite heroSprite;
}