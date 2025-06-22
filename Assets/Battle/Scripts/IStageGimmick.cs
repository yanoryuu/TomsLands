public interface IStageGimmick
{
    // ターン終了時に呼ばれる処理
    //void OnTurnEnd(BattleManager manager);

    // UIに表示するための説明文
    string GetDescription();
}