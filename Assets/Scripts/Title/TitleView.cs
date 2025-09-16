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
    public Subject<Unit> OnNewGameRequested { get; private set;} = new Subject<Unit>();
    public Subject<Unit> OnLoadGameRequested { get; private set;} = new Subject<Unit>();
    
    void Awake()
    {
        newGameButton.onClick.AddListener(() => OnNewGameRequested.OnNext(Unit.Default));
        loadGameButton.onClick.AddListener(() => OnLoadGameRequested.OnNext(Unit.Default));
        Bind();
    }
    
    public void ShowTitleScreen()
    {
        titleScreen.SetActive(true);
    }
    public void HideTitleScreen()
    {
        titleScreen.SetActive(false);
    }

    private void Bind()
    {
        titleIcon.transform.DOScale(new Vector3(7.3f,7.3f,1),1f)
            .SetLoops(-1,LoopType.Yoyo)
            .SetEase(Ease.OutCubic);
    }
    
}
