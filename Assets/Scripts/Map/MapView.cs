using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class MapView : MonoBehaviour
{
    [Header("ダンジョン")]
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

                case DungeonName.MausoleumOblivion:
                    SetDungeonIcon(mausoleumOblivion.gameObject, data.dungeonStatus);
                    break;

                case DungeonName.ScorchingVolcanoPrison:
                    SetDungeonIcon(scorchingVolcanoPrison.gameObject, data.dungeonStatus);
                    break;

                case DungeonName.IceMistCave:
                    SetDungeonIcon(iceMistCave.gameObject, data.dungeonStatus);
                    break;

                case DungeonName.DeepGreenBeastForest:
                    SetDungeonIcon(deepGreenBeastForest.gameObject, data.dungeonStatus);   ;
                    break;

                case DungeonName.AncientMechanicalCastle:
                    SetDungeonIcon(ancientMechanicalCastle.gameObject, data.dungeonStatus); 
                    break;

                case DungeonName.DemonKingCastle:
                    SetDungeonIcon(demonKingsCastle.gameObject, data.dungeonStatus);
                    break;

                default:
                    // フォールバック（想定外の値）
                    // TODO: ログやデフォルト表示など
                    break;
            }

        }
    }
    
    private void SetDungeonIcon(GameObject dungeonIcon, DungeonStatus status)
    {
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
                dungeonIcon.transform.DOLocalMoveY(dungeonIcon.transform.localPosition.y + 40, 1f)
                    .SetEase(Ease.OutBack)
                    .SetLoops(-1, LoopType.Yoyo);
                break;
            default:
                // フォールバック（想定外の値）
                break;
        }
    }
}