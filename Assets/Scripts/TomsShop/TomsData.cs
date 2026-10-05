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

    // --- 旧セーブには無いフィールド。欠損時は 0 になるためロード側で正規化する ---
    public int toolShopLevel;  // 欠損時 0 → 1 に正規化
    public float trust;        // 欠損時 0 → 1 に正規化
    public int shopLevel;      // 店レベル。欠損時 0 → 1 に正規化

    // --- 準備シーン（借入・スタートダッシュ）。旧セーブは欠損時 0 = 効果なし ---
    public int borrowedPrincipal;
    public float firstDebtDiscountRate;

    public TomsData(int shopMoney, int blacksmithLevel, int currentTurn, int gameFlowIndex = 0, int infoBrokerLevel = 1, int debtCycle = 0,
        int flowSeed = 0, int gameMode = 0, bool useAutoFlow = false, int toolShopLevel = 1, float trust = 1f, int shopLevel = 1,
        int borrowedPrincipal = 0, float firstDebtDiscountRate = 0f)
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
        this.toolShopLevel = toolShopLevel;
        this.trust = trust;
        this.shopLevel = shopLevel;
        this.borrowedPrincipal = borrowedPrincipal;
        this.firstDebtDiscountRate = firstDebtDiscountRate;
    }
}
