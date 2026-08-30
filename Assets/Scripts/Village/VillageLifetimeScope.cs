using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// 村シーン（VillageScene.unity）用の LifetimeScope。
/// フロー: タイトル（ニューゲーム）→ 村 → 出撃準備、ラン終了（リザルト/ゲームオーバー）→ 村。
/// 村UIが未配線の間は PreparationScene へ素通りする（既存規約）。
/// </summary>
public class VillageLifetimeScope : LifetimeScope
{
    [Header("Views")]
    [SerializeField] private VillageView villageView;

    protected override void Configure(IContainerBuilder builder)
    {
        // 村施設マスター（balance.json の villageFacilities 区画で上書き可能）
        var facilities = AddressableLoader.LoadAll<VillageFacilityData>("VillageFacilityData");
        facilities = RemoteBalance.ApplyList("villageFacilities", facilities, f => f.facilityId);
        builder.RegisterInstance(facilities);

        // Models（MetaProgressModel はコンストラクタで metaData.json を自動ロード）
        builder.Register<MetaProgressModel>(Lifetime.Singleton);
        builder.Register<VillageModel>(Lifetime.Singleton);

        // View / Presenter（View未配線なら Start() で素通りする）
        if (villageView != null)
        {
            builder.RegisterComponent(villageView);
            builder.RegisterEntryPoint<VillagePresenter>();
        }
        else
        {
            Debug.LogWarning("[VillageLifetimeScope] villageView が未設定のため素通りします（Docs/Village_UnityWiring.md 参照）");
        }

        Debug.Log("[VillageLifetimeScope] Configured.");
    }

    private void Start()
    {
        // View未配線時のフォールバック: 旧フロー同様に準備シーンへ直行
        if (villageView == null)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("PreparationScene");
        }
    }
}
