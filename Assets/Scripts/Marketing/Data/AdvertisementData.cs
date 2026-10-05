using UnityEngine;

/// <summary>
/// 広告データ（ScriptableObject）
/// 各広告の名前、コスト、ステータス上昇量、フォロワー獲得量を定義する。
/// Inspector上で自由に調整可能。
/// </summary>
[CreateAssetMenu(fileName = "AdvertisementData", menuName = "ScriptableObjects/Marketing/AdvertisementData")]
public class AdvertisementData : ScriptableObject
{
    [Header("基本情報")]
    [Tooltip("広告の名前")]
    public string advertisementName;

    [Tooltip("広告のアイコン")]
    public Sprite icon;

    [Tooltip("選択時に表示する背景画像")]
    public Sprite selectedBackground;

    [Tooltip("広告の実行コスト（ゴールド）")]
    public int cost;

    [Header("ステータス上昇量")]
    [Tooltip("信頼度の上昇量（0〜100）")]
    public int trustGain;

    [Tooltip("注目度の上昇量（0〜100）")]
    public int attentionGain;

    [Tooltip("拡散力の上昇量（0〜100）")]
    public int spreadGain;

    [Tooltip("顧客維持力の上昇量（0〜100）")]
    public int retentionGain;

    [Tooltip("この広告で上げられる各ステータス（信頼/注目/拡散/維持。フォロワーは対象外）の上限（マックス）。" +
             "現在値がこの値以上のステータスは上昇しない（上限を超えないよう上昇量が削られる）。" +
             "0 = 個別上限なし（GameBalanceData.statMax まで上がる）。")]
    public int statMax;

    [Header("フォロワー")]
    [Tooltip("フォロワー獲得量")]
    public int followerGain;
}

