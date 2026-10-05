using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// オプション画面（加算シーン「Setting」）。
/// タイトル画面・プレイ中メニューのどちらからも additive ロードで開かれる。
/// タブ構成: サウンド / 画面 / 演出 / 情報。設定値は GameSettings（PlayerPrefs）に永続化される。
/// </summary>
public class Setting : MonoBehaviour
{
    [Header("サウンド")]
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider seSlider;

    [Header("画面設定")]
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    [Header("演出・快適性")]
    [SerializeField] private Toggle fastEffectsToggle;
    [SerializeField] private Toggle reduceShakeToggle;

    [Header("情報表示")]
    [SerializeField] private TextMeshProUGUI versionText;

    [Header("タブ")]
    [SerializeField] private Button soundTabButton;
    [SerializeField] private Button displayTabButton;
    [SerializeField] private Button effectTabButton;
    [SerializeField] private Button infoTabButton;
    [SerializeField] private GameObject soundGroup;
    [SerializeField] private GameObject displayGroup;
    [SerializeField] private GameObject effectGroup;
    [SerializeField] private GameObject infoGroup;

    [Header("共通")]
    [SerializeField] private Button closeBtn;
    [Tooltip("タイトルへ戻るボタン。タイトル画面から開いた場合は自動で非表示になる")]
    [SerializeField] private Button toTitleBtn;

    public Subject<Unit> OnCloseButtonClicked { get; } = new();

    // 閉じる処理の二重実行ガード
    private bool isClosing;

    // 解像度ドロップダウンの選択肢（重複除去済み）
    private readonly List<Vector2Int> resolutionOptions = new();

    private void Start()
    {
        InitSound();
        InitDisplay();
        InitEffects();
        InitInfo();
        InitTabs();
        Bind();

        ShowTab(0); // 初期表示はサウンド
    }

    // ---- 初期化 ----

    private void InitSound()
    {
        if (bgmSlider) bgmSlider.value = SoundManager.Instance.GetBGMVolume();
        if (seSlider) seSlider.value = SoundManager.Instance.GetSEVolume();
    }

    private void InitDisplay()
    {
        if (fullscreenToggle) fullscreenToggle.isOn = Screen.fullScreen;

        if (resolutionDropdown)
        {
            // 端末が対応する解像度から重複（リフレッシュレート違い）を除いて列挙
            resolutionOptions.Clear();
            var labels = new List<string>();
            foreach (var res in Screen.resolutions)
            {
                var size = new Vector2Int(res.width, res.height);
                if (resolutionOptions.Contains(size)) continue;
                resolutionOptions.Add(size);
                labels.Add($"{size.x} × {size.y}");
            }

            resolutionDropdown.ClearOptions();
            resolutionDropdown.AddOptions(labels);

            // 現在の解像度を選択状態にする。
            // 手動リサイズ等でリストに無いサイズの場合は「（現在）」項目を追加して正しく表示する
            var current = new Vector2Int(Screen.width, Screen.height);
            int index = resolutionOptions.IndexOf(current);
            if (index < 0)
            {
                resolutionOptions.Add(current);
                resolutionDropdown.AddOptions(new List<string> { $"{current.x} × {current.y}（現在）" });
                index = resolutionOptions.Count - 1;
            }
            resolutionDropdown.SetValueWithoutNotify(index);
        }
    }

    private void InitEffects()
    {
        if (fastEffectsToggle) fastEffectsToggle.isOn = GameSettings.FastEffects;
        if (reduceShakeToggle) reduceShakeToggle.isOn = GameSettings.ReduceShake;
    }

    private void InitInfo()
    {
        if (versionText) versionText.text = $"トムトムランド  Ver {Application.version}";
    }

    private void InitTabs()
    {
        if (soundTabButton) soundTabButton.onClick.AddListener(() => ShowTab(0));
        if (displayTabButton) displayTabButton.onClick.AddListener(() => ShowTab(1));
        if (effectTabButton) effectTabButton.onClick.AddListener(() => ShowTab(2));
        if (infoTabButton) infoTabButton.onClick.AddListener(() => ShowTab(3));
    }

    private void ShowTab(int index)
    {
        if (soundGroup) soundGroup.SetActive(index == 0);
        if (displayGroup) displayGroup.SetActive(index == 1);
        if (effectGroup) effectGroup.SetActive(index == 2);
        if (infoGroup) infoGroup.SetActive(index == 3);

        // 選択中タブを少し強調（interactable を落として押下不能＝選択中表示を兼ねる）
        if (soundTabButton) soundTabButton.interactable = index != 0;
        if (displayTabButton) displayTabButton.interactable = index != 1;
        if (effectTabButton) effectTabButton.interactable = index != 2;
        if (infoTabButton) infoTabButton.interactable = index != 3;
    }

    // ---- 変更の反映 ----

    private void Bind()
    {
        // サウンド: 即時反映 + 永続化
        if (bgmSlider)
            bgmSlider.onValueChanged.AddListener(value =>
            {
                SoundManager.Instance.SetBGMVolume(value);
                GameSettings.BgmVolume = value;
            });

        if (seSlider)
            seSlider.onValueChanged.AddListener(value =>
            {
                SoundManager.Instance.SetSEVolume(value);
                GameSettings.SeVolume = value;
            });

        // 画面: 即時反映 + 永続化
        if (fullscreenToggle)
            fullscreenToggle.onValueChanged.AddListener(isOn =>
            {
                GameSettings.Fullscreen = isOn;
                Screen.fullScreen = isOn;
            });

        if (resolutionDropdown)
            resolutionDropdown.onValueChanged.AddListener(index =>
            {
                if (index < 0 || index >= resolutionOptions.Count) return;
                var size = resolutionOptions[index];
                GameSettings.Resolution = size;
                Screen.SetResolution(size.x, size.y, GameSettings.Fullscreen);
            });

        // 演出・快適性: 永続化のみ（参照側が都度 GameSettings を見る）
        if (fastEffectsToggle)
            fastEffectsToggle.onValueChanged.AddListener(isOn => GameSettings.FastEffects = isOn);

        if (reduceShakeToggle)
            reduceShakeToggle.onValueChanged.AddListener(isOn => GameSettings.ReduceShake = isOn);

        // 閉じる
        closeBtn.onClick.AddListener(() =>
        {
            if (isClosing) return;
            isClosing = true;
            OnCloseButtonClicked.OnNext(Unit.Default);
            SceneManager.UnloadSceneAsync("Setting");
        });

        // タイトルへ戻る（タイトル画面から開いた場合は非表示）
        if (toTitleBtn != null)
        {
            bool onTitle = SceneManager.GetActiveScene().name == "TitleScene";
            toTitleBtn.gameObject.SetActive(!onTitle);
            toTitleBtn.onClick.AddListener(() =>
            {
                if (isClosing) return;
                isClosing = true;
                // Single ロードで Setting（加算シーン）ごと破棄される。
                // ゲーム側の保存は GameLifecycleHandler.Dispose（スコープ破棄時）が行う。
                SceneManager.LoadScene("TitleScene");
            });
        }
    }
}
