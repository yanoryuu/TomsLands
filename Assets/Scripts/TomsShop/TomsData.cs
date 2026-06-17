using System;

[Serializable]
public class TomsData
{
    public int shopMoney;

    public int blacksmithLevel;

    public int infoBrokerLevel;

    public int currentTurn;

    public int gameFlowIndex;

    public int debtCycle;

    // --- 自動生成フローの再現用（旧セーブには無いので欠損時は 0/false=手動扱い） ---
    public int flowSeed;
    public int gameMode;      // GameModeId を int で保存（JsonUtility 安全）
    public bool useAutoFlow;

    public TomsData(int shopMoney, int blacksmithLevel, int currentTurn, int gameFlowIndex = 0, int infoBrokerLevel = 1, int debtCycle = 0,
        int flowSeed = 0, int gameMode = 0, bool useAutoFlow = false)
    {
        this.shopMoney = shopMoney;
        this.blacksmithLevel = blacksmithLevel;
        this.infoBrokerLevel = infoBrokerLevel;
        this.currentTurn = currentTurn;
        this.gameFlowIndex = gameFlowIndex;
        this.debtCycle = debtCycle;
        this.flowSeed = flowSeed;
        this.gameMode = gameMode;
        this.useAutoFlow = useAutoFlow;
    }
}
