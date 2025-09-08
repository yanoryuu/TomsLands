using System.Collections.Generic;
using R3;

public class WorldStateModel
{
    public ReactiveProperty<int> currentDay{ get; private set; }
    
    //次のダンジョンID
    public ReactiveProperty<int> nextDungeonId{ get; private set; }
    
    //ダンジョンの順番
    public List<int> dungeonOrder {get; private set;}
    
    //現在のダンジョンのインデックス
    public int currentDungeonIndex {get; private set;}
    
    public WorldStateModel()
    {
        currentDay = new ReactiveProperty<int>(1);
        nextDungeonId = new ReactiveProperty<int>(1);
        currentDungeonIndex = 0;
    }
    
    
    //1日進める
    public void AdvanceDay()
    {
        currentDay.Value += 1;
    }
    
    //ダンジョン終了時に呼び出し
    public void SetNextDungeon()
    {
        nextDungeonId.Value = dungeonOrder[currentDungeonIndex];
        currentDungeonIndex++;
    }
    
    //ゲーム開始時にダンジョンの順番をランダムに設定
    public void SetDungeonOrder()
    {
        dungeonOrder = new List<int> {0, 1, 2, 3, 4};
        // ランダムにシャッフル
        for (int i = 0; i < dungeonOrder.Count; i++)
        {
            int temp = dungeonOrder[i];
            int randomIndex = UnityEngine.Random.Range(i, dungeonOrder.Count);
            dungeonOrder[i] = dungeonOrder[randomIndex];
            dungeonOrder[randomIndex] = temp;
        }
    }
}
