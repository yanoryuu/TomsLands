﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// FightScene 用の LifetimeScope。
/// BattleInputData から情報を受け取り、戦闘を開始する。
/// </summary>
public class BattleLifetimeScope : LifetimeScope
{
    [Header("Scene References")]
    [SerializeField] private BattleSequencer battleSequencer;
    [SerializeField] private StreamingSalesController streamingSalesController;

    [Header("ダンジョンデータ（Inspector で設定。不足分は自動補完）")]
    [SerializeField] private List<DungeonInfoScriptableObj> dungeonInfos;

    [Header("Shared ScriptableObjects")]
    [SerializeField] private BattleInputData battleInputData;
    [SerializeField] private BattleOutputData battleOutputData;

    protected override void Configure(IContainerBuilder builder)
    {
        // SO を共有データとして登録
        if (battleInputData == null)
        {
            battleInputData = Resources.Load<BattleInputData>("SceneData/BattleInputData") ?? ScriptableObject.CreateInstance<BattleInputData>();
            Debug.LogWarning("[BattleLifetimeScope] BattleInputData が未設定だったため、Resourcesまたは実行時インスタンスを使用します。");
        }

        if (battleOutputData == null)
        {
            battleOutputData = Resources.Load<BattleOutputData>("SceneData/BattleOutputData") ?? ScriptableObject.CreateInstance<BattleOutputData>();
            Debug.LogWarning("[BattleLifetimeScope] BattleOutputData が未設定だったため、Resourcesまたは実行時インスタンスを使用します。");
        }

        builder.RegisterInstance(battleInputData);
        builder.RegisterInstance(battleOutputData);

        // ダンジョンカタログを構築して登録
        // Inspector リストが空/不足なら、プロジェクト内の全 DungeonInfoScriptableObj を自動収集
        var allDungeons = CollectAllDungeonInfos();
        var catalog = new DungeonCatalog(allDungeons);
        builder.RegisterInstance<IDungeonCatalog>(catalog);

        // BattleSequencer をコンポーネントとして登録
        builder.RegisterComponent(battleSequencer);

        // StreamingSalesController をコンポーネントとして登録
        if (streamingSalesController != null)
        {
            builder.RegisterComponent(streamingSalesController);
        }
        else
        {
            Debug.LogWarning("[BattleLifetimeScope] StreamingSalesController が未設定です。");
        }

        // ItemModel（マスターデータ）を構築して登録
        var masterItems = Resources.LoadAll<ItemData>("ItemData").ToList();
        builder.RegisterInstance(masterItems);
        builder.Register<ItemModel>(Lifetime.Singleton);

        // シーン遷移サービス
        builder.Register<SceneTransitionService>(Lifetime.Singleton);

        // 戦闘開始の EntryPoint
        builder.RegisterEntryPoint<BattleSceneStarter>();

        Debug.Log($"[BattleLifetimeScope] Configured. Dungeon catalog size: {allDungeons.Count}");
    }

    /// <summary>
    /// Inspector に設定された dungeonInfos をベースに、
    /// 不足している DungeonInfoScriptableObj を Resources.FindObjectsOfTypeAll で補完する。
    /// </summary>
    private List<DungeonInfoScriptableObj> CollectAllDungeonInfos()
    {
        var result = new List<DungeonInfoScriptableObj>();
        var seenKeys = new HashSet<DungeonName>();

        // 1) Inspector で設定済みのものを優先
        if (dungeonInfos != null)
        {
            foreach (var so in dungeonInfos)
            {
                if (so == null) continue;
                if (seenKeys.Add(so.key))
                    result.Add(so);
            }
        }

        // 2) 不足分を FindObjectsOfTypeAll で補完（エディタ + ビルド両対応）
#if UNITY_EDITOR
        var allInProject = UnityEditor.AssetDatabase.FindAssets("t:DungeonInfoScriptableObj")
            .Select(guid => UnityEditor.AssetDatabase.GUIDToAssetPath(guid))
            .Select(path => UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonInfoScriptableObj>(path))
            .Where(so => so != null);

        foreach (var so in allInProject)
        {
            if (seenKeys.Add(so.key))
            {
                result.Add(so);
                Debug.Log($"[BattleLifetimeScope] Auto-added dungeon from project: {so.key} ({so.dungeonName})");
            }
        }
#endif

        if (result.Count == 0)
        {
            Debug.LogWarning("[BattleLifetimeScope] ダンジョンデータが1つも見つかりません。Inspector の dungeonInfos を設定するか、DungeonInfoScriptableObj アセットを作成してください。");
        }

        return result;
    }
}
