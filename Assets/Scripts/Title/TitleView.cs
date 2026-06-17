using UnityEngine;
using UnityEngine.UI;
using R3;
using DG.Tweening;
public class TitleView : MonoBehaviour
{
    // ボタンの参照
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button loadGameButton;

    [SerializeField] private GameObject titleIcon;

    [SerializeField] private GameObject titleScreen;

    [Header("ゲームフロー選択（任意・未設定でも動作）")]
    [Tooltip("ON=自動生成 / OFF=手動(SO)フロー")]
    [SerializeField] private Toggle autoGenerationToggle;
    [SerializeField] private Button shortModeButton;
    [SerializeField] private Button mediumModeButton;
    [SerializeField] private Button longModeButton;

    public Subject<Unit> OnNewGameRequested { get; private set;} = new Subject<Unit>();
    public Subject<Unit> OnLoadGameRequested { get; private set;} = new Subject<Unit>();

    private GameModeId _selectedMode = GameModeId.Short;

    /// <summary>現在選択中のゲームモード。</summary>
    public GameModeId SelectedMode => _selectedMode;

    /// <summary>自動生成を使うか。トグル未設定時は true（自動）。</summary>
    public bool UseAutoGeneration => autoGenerationToggle != null ? autoGenerationToggle.isOn : true;

    void Awake()
    {
        newGameButton.onClick.AddListener(() => OnNewGameRequested.OnNext(Unit.Default));
        loadGameButton.onClick.AddListener(() => OnLoadGameRequested.OnNext(Unit.Default));

        // モード選択ボタン（未設定でも安全）
        if (shortModeButton != null)  shortModeButton.onClick.AddListener(() => _selectedMode = GameModeId.Short);
        if (mediumModeButton != null) mediumModeButton.onClick.AddListener(() => _selectedMode = GameModeId.Medium);
        if (longModeButton != null)   longModeButton.onClick.AddListener(() => _selectedMode = GameModeId.Long);

        Bind();
    }

    public void SetContinueButtonVisible(bool visible)
    {
        if (loadGameButton != null)
            loadGameButton.gameObject.SetActive(visible);
    }

    /// <summary>フロー選択UIの初期値を設定する（GameConstの既定値など）。</summary>
    public void InitFlowSelection(GameModeId mode, bool useAuto)
    {
        _selectedMode = mode;
        if (autoGenerationToggle != null)
            autoGenerationToggle.isOn = useAuto;
    }

    private void Bind()
    {
        titleIcon.transform.DOScale(new Vector3(7.3f,7.3f,1),1f)
            .SetLoops(-1,LoopType.Yoyo)
            .SetEase(Ease.OutCubic);
    }
    
}
