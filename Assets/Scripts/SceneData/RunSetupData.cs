using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 準備シーン → TomsShop へラン設定を受け渡す ScriptableObject（StartModeData と同じ規約）。
/// GameLifecycleHandler.InitializeNewGame が一度だけ消費して Clear() する。
/// アセット未作成時は static フォールバックインスタンスをシーン間で共有する
/// （ドメインリロードまで生存するため受け渡しは成立する）。
/// </summary>
[CreateAssetMenu(fileName = "RunSetupData", menuName = "ScriptableObjects/SceneData/RunSetupData")]
public class RunSetupData : ScriptableObject
{
    /// <summary>準備シーンを経由した設定があるか。false なら従来通りの初期化。</summary>
    public bool HasSetup;

    [Header("借入（初期資金レバレッジ）")]
    [Tooltip("借入額。初期資金に加算され、初回返済に利息付きで上乗せされる")]
    public int BorrowedAmount;

    [Header("持ち込みアイテム（初期在庫に加算）")]
    public List<string> CarryItemIds = new();
    public List<int> CarryItemCounts = new();

    [Header("スターターレリック")]
    public string StarterRelicId = "";

    [Header("スタートダッシュ（消費済みの適用フラグ）")]
    [Tooltip("宣伝ビラ: 開始時に注目度とフォロワーを加算")]
    public bool UseFlyer;
    [Tooltip("目利きの手引き: 開始時の全アイテム需要を上振れさせる")]
    public bool UseAppraisal;
    [Tooltip("返済猶予証: 初回返済額を割引する")]
    public bool UseGrace;

    private static RunSetupData runtimeFallback;

    /// <summary>アセット未登録時のシーン間共有フォールバック。</summary>
    public static RunSetupData GetOrCreateFallback()
    {
        if (runtimeFallback == null)
        {
            runtimeFallback = CreateInstance<RunSetupData>();
            Debug.LogWarning("[RunSetupData] アセットが見つからないため実行時フォールバックを使用します。" +
                             "Create > ScriptableObjects > SceneData > RunSetupData を作成し Addressables（SceneData/RunSetupData）に登録してください。");
        }
        return runtimeFallback;
    }

    public void Clear()
    {
        HasSetup = false;
        BorrowedAmount = 0;
        CarryItemIds.Clear();
        CarryItemCounts.Clear();
        StarterRelicId = "";
        UseFlyer = false;
        UseAppraisal = false;
        UseGrace = false;
    }
}
