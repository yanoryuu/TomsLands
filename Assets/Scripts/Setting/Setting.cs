using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
public class Setting : MonoBehaviour
{
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider seSlider;
    [SerializeField] private Button closeBtn;

    public Subject<Unit> OnCloseButtonClicked { get; } = new();

    // 閉じる処理の二重実行ガード
    private bool isClosing;

    private void Start()
    {
        SetBGMVolume(SoundManager.Instance.GetBGMVolume());
        SetSEVolume(SoundManager.Instance.GetSEVolume());
        Bind();
    }
    
    private void SetBGMVolume(float vol) => bgmSlider.value = vol;
    private void SetSEVolume(float vol) => seSlider.value = vol;

    private void Bind()
    {
        bgmSlider.onValueChanged.AddListener(value => SoundManager.Instance.SetBGMVolume(value));
        seSlider.onValueChanged.AddListener(value => SoundManager.Instance.SetSEVolume(value));
        
        closeBtn.onClick.AddListener(() =>
        {
            if (isClosing) return;
            isClosing = true;
            SceneManager.UnloadSceneAsync("Setting");
        });
    }
}
