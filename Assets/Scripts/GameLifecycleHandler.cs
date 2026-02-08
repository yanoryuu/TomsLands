using System;
using System.IO;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// GameManagerがやっていた「初期化」と「保存・後始末」だけを担当するクラス
public class GameLifecycleHandler : IStartable, IDisposable
{
    private readonly ItemModel _itemModel;
    private readonly TomsModel _tomsModel;
    private readonly HeroModel _heroModel;
    private readonly DungeonRepository _dungeonRepository;

    // コンストラクタ（依存関係はVContainerが注入）
    public GameLifecycleHandler(
        ItemModel itemModel,
        TomsModel tomsModel,
        HeroModel heroModel,
        DungeonRepository dungeonRepository)
    {
        _itemModel = itemModel;
        _tomsModel = tomsModel;
        _heroModel = heroModel;
        _dungeonRepository = dungeonRepository;
    }

    public void Start()
    {
        // 1. セーブデータの削除
        DeleteAllSaveFiles();

        // 2. ダンジョンカタログの初期化
        var dungeonCatalog = _dungeonRepository.CreateCatalog();
        _dungeonRepository.SetCatalog(dungeonCatalog);

        Debug.Log("Game Initialized via VContainer");
    }

    public void Dispose()
    {
        // アプリ終了時・シーン破棄時に呼ばれる保存処理
        _itemModel.SaveData();
        _tomsModel.SavePlayerMoney();
        _heroModel.SaveHeroData();
        

        Debug.Log("Game Data Saved & Disposed");
    }

    // ファイル削除ロジック（そのまま移植）
    private void DeleteAllSaveFiles()
    {
        string dir = Application.persistentDataPath;
        string[] files = {
            "itemData.json",
            "tomsData.json",
            "displayItemData.json",
            "heroData.json",
        };

        foreach (var filename in files)
        {
            string path = Path.Combine(dir, filename);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"Deleted save file: {filename}");
            }
        }
    }
}