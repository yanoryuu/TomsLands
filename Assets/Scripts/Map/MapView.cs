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
}