using UnityEngine;

/// <summary>
/// 金融商品のマスターデータ（取引所に並ぶ1商品）。
/// 解放は情報屋レベル（TomsModel.InfoBrokerLevel）でゲートする。
/// </summary>
[CreateAssetMenu(fileName = "FinancialProductData", menuName = "ScriptableObjects/Finance/FinancialProductData")]
public class FinancialProductData : ScriptableObject
{
    [Header("共通")]
    public string productId;
    public string productName;
    [TextArea] public string description;
    public Sprite icon;
    public FinancialProductKind kind = FinancialProductKind.Bond;
    [Tooltip("この商品が取引所に並ぶために必要な情報屋レベル")]
    public int unlockInfoBrokerLevel = 1;

    [Header("債券（Bond）専用")]
    [Tooltip("1口の額面（購入価格）")]
    public int bondUnitPrice = 2000;
    [Tooltip("満期時の利率（0.15 = 元本の15%が利息）")]
    public float bondInterestRate = 0.15f;
    [Tooltip("満期までのターン数（購入ターン + この値 の朝に償還）")]
    public int bondMaturityTurns = 8;

    [Header("ファンド（IndexFund）専用")]
    [Tooltip("市場平均が基準値のときの1口価格")]
    public int fundBaseUnitPrice = 1000;
    [Tooltip("true なら特定属性の銘柄だけで構成（属性ファンド）。false なら全銘柄（市場指数）")]
    public bool useAttributeFilter = false;
    public ItemTypeData.ItemAttribute attribute = ItemTypeData.ItemAttribute.Fire;
}
