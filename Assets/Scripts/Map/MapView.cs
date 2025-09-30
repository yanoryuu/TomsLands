using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.UI;

public class MapView : MonoBehaviour
{
    [Header("ダンジョン")]
    [SerializeField] private Button greenRestButton;
    [SerializeField] private Button frostReachButton;
    [SerializeField] private Button duskHeavenButton;
    [SerializeField] private Button centerCity;
    [SerializeField] private Button mausoleumOblivion;
    [SerializeField] private Button scorchingVolcanoPrison;
    [SerializeField] private Button iceMistCave;
    [SerializeField] private Button deepGreenBeastForest;
    [SerializeField] private Button ancientMechanicalCastle;
    [SerializeField] private Button demonKingsCastle;
    
    [Header("UI")]
    [SerializeField] private Button backButton;
    public Subject<DungeonName> OnMapIcon { get; private set;} = new();
    public Subject<Unit> OnBackRequested { get; private set;} = new();

    private void Awake()
    {
        Bind();
    }

    private void Bind()
    {
        greenRestButton.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.GreenRest));
        frostReachButton.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.FrostReach));
        duskHeavenButton.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.DuskHeaven));
        centerCity.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.CenterCity));
        mausoleumOblivion.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.MausoleumOblivion));
        scorchingVolcanoPrison.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.ScorchingVolcanoPrison));
        iceMistCave.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.IceMistCave));
        deepGreenBeastForest.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.DeepGreenBeastForest));
        ancientMechanicalCastle.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.AncientMechanicalCastle));
        demonKingsCastle.onClick.AddListener(() => OnMapIcon.OnNext(DungeonName.DemonKingCastle));
        backButton.onClick.AddListener(() => OnBackRequested.OnNext(Unit.Default));
    }

    public void SetDungeonStatus(List<DungeonData> dungeonDates)
    {
        foreach (var data in dungeonDates)
        {
            switch (data.key) // data.key が DungeonName
            {
                case DungeonName.GreenRest:
                    break;

                case DungeonName.FrostReach:
                    break;

                case DungeonName.DuskHeaven:
                    break;

                case DungeonName.CenterCity:
                    break;

                case DungeonName.MausoleumOblivion:
                    break;

                case DungeonName.ScorchingVolcanoPrison:
                    break;

                case DungeonName.IceMistCave:
                    break;

                case DungeonName.DeepGreenBeastForest:
                    break;

                case DungeonName.AncientMechanicalCastle:
                    break;

                case DungeonName.DemonKingCastle:
                    break;

                default:
                    // フォールバック（想定外の値）
                    // TODO: ログやデフォルト表示など
                    break;
            }

        }
    }
    
    private void DungeonIcon(GameObject dungeonIcon, DungeonStatus status)
    {
        // ここでアイコンの見た目を変更するロジックを実装
        // 例: クリア済みならチェックマークを表示、未クリアならグレーアウトなど
        switch (status)
        {
            case DungeonStatus.Clear:
                // クリア済みの見た目に設定
                break;
            case DungeonStatus.Fail:
                // 失敗の見た目に設定
                break;
            case DungeonStatus.Still:
                // 進行中の見た目に設定
                break;
            default:
                // フォールバック（想定外の値）
                break;
        }
    }
}