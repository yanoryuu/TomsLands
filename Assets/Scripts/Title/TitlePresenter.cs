using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// タイトル内の画面遷移とゲーム開始処理を制御する。
/// </summary>
public sealed class TitlePresenter : IStartable, IDisposable
{
    private const string GameSceneName = "TomsShop";

    private readonly TitleView _view;
    private readonly TitleModel _model;
    private readonly StartModeData _startModeData;
    private readonly PopUpManager _popUpManager;
    private readonly CompositeDisposable _disposables = new();

    public TitlePresenter(
        TitleView view,
        TitleModel model,
        StartModeData startModeData,
        PopUpManager popUpManager)
    {
        _view = view;
        _model = model;
        _startModeData = startModeData;
        _popUpManager = popUpManager;
    }

    public void Start()
    {
        var soundManager = SoundManager.Instance;
        if (soundManager != null)
            soundManager.PlayBGM("OP");
        _view.SetSaveDataAvailable(SaveSlotManager.AnyProfileExists());
        TransitionTo(TitleType.Start);
        Bind();
    }

    public void Dispose() => _disposables.Dispose();

    private void Bind()
    {
        _view.OnStartRequested
            .Subscribe(_ => TransitionTo(TitleType.ContinueOrNewGame))
            .AddTo(_disposables);

        // 難易度はタイトルでは選ばない（村からの出店準備シーンで選ぶ）→ 保存先スロット選択へ直行
        _view.OnNewGameSelected
            .Subscribe(_ => OpenSaveDataPanel(SaveDataPanelMode.NewGameSlot))
            .AddTo(_disposables);

        _view.OnContinueSelected
            .Subscribe(_ => OpenSaveDataPanel(SaveDataPanelMode.Load))
            .AddTo(_disposables);

        // オプション画面（加算シーン。プレイ中メニューと同じ Setting シーンを共用）
        _view.OnOptionSelected
            .Subscribe(_ => OpenOptionScene())
            .AddTo(_disposables);

        _view.OnSaveSlotSelected
            .Subscribe(OnSlotSelected)
            .AddTo(_disposables);

        _view.OnSaveSlotDeleteRequested
            .Subscribe(OnSlotDeleteRequested)
            .AddTo(_disposables);

        _view.OnBackRequested
            .Subscribe(_ => GoBack())
            .AddTo(_disposables);
    }

    // Settingシーンの多重ロード防止（非同期ロード中は isLoaded が false のままのため必要）
    private bool _isOptionTransitioning;

    private void OpenOptionScene()
    {
        const string settingSceneName = "Setting";
        if (_isOptionTransitioning) return;
        if (SceneManager.GetSceneByName(settingSceneName).isLoaded) return;

        _isOptionTransitioning = true;
        var op = SceneManager.LoadSceneAsync(settingSceneName, LoadSceneMode.Additive);
        op.completed += _ =>
        {
            _isOptionTransitioning = false;
            // パッド/キー: 設定画面をモーダルにし、B/Esc で閉じる（閉じるボタンと同じ＝アンロード）
            bool closing = false;
            UIFocusManager.AttachSceneScope(settingSceneName, () =>
            {
                if (closing) return;
                closing = true;
                SceneManager.UnloadSceneAsync(settingSceneName);
            });
        };
    }

    private void GoBack()
    {
        switch (_model.CurrentScreen)
        {
            case TitleType.ContinueOrNewGame:
                TransitionTo(TitleType.Start);
                break;
            case TitleType.SaveData:
                TransitionTo(TitleType.ContinueOrNewGame);
                break;
        }
    }

    /// <summary>セーブデータ画面を指定モードで開き、スロット一覧を構築する。</summary>
    private void OpenSaveDataPanel(SaveDataPanelMode mode)
    {
        _model.SetPanelMode(mode);
        RefreshSaveSlots();
        TransitionTo(TitleType.SaveData);
    }

    /// <summary>現在のスロット状態を読み直してビューに反映する。</summary>
    private void RefreshSaveSlots()
    {
        var infos = new List<SaveSlotInfo>(SaveSlotManager.MaxSlots);
        for (int i = 0; i < SaveSlotManager.MaxSlots; i++)
            infos.Add(SaveSlotManager.GetSlotInfo(i));

        _view.BuildSaveSlots(infos, _model.PanelMode == SaveDataPanelMode.Load);
    }

    /// <summary>スロットが選択されたときの処理。モードに応じてロード/新規開始に分岐する。</summary>
    private void OnSlotSelected(int slot)
    {
        if (_model.PanelMode == SaveDataPanelMode.Load)
        {
            ContinueGame(slot);
            return;
        }

        // ニューゲームの保存先選択
        if (SaveSlotManager.Exists(slot))
            ShowOverwriteConfirmation(slot);
        else
            StartNewGame(slot);
    }

    /// <summary>スロットの削除要求。確認ポップアップを挟んでから削除する。</summary>
    private void OnSlotDeleteRequested(int slot)
    {
        // プロフィール（メタ進行のみのスロット含む）を対象にする
        if (!SaveSlotManager.HasProfile(slot)) return;

        _popUpManager.Show(new PopUpData
        {
            Title = "削除の確認",
            Message = $"スロット {slot + 1} のデータ（メタ進行を含む）を削除しますか？\nこの操作は取り消せません。",
            ConfirmButtonText = "削除する",
            CancelButtonText = "戻る",
            Size = PopupSizeEnum.Medium,
            OnConfirm = () =>
            {
                SaveSlotManager.DeleteSlot(slot);
                Debug.Log($"[TitlePresenter] Deleted save slot {slot + 1}");
                RefreshSaveSlots();
                _view.SetSaveDataAvailable(SaveSlotManager.AnyProfileExists());
            }
        });
    }

    private void ShowOverwriteConfirmation(int slot)
    {
        // 進行中のランは村に精算されずに消える（持ち込んだお金も戻らない）ことを先に伝える。村の進行は残る
        var info = SaveSlotManager.GetSlotInfo(slot);
        string runLine = info.Exists
            ? $"進行中のラン（{SaveSlotView.GetModeLabel(info.Mode)} Day {info.Day}・所持金 {info.Gold:N0} G）は村に戻らずに消えます。\n村から持ち込んだお金も戻りません。"
            : "進行中のランのデータは消えます。";
        _popUpManager.Show(new PopUpData
        {
            Title = "上書きの確認",
            Message = $"スロット {slot + 1} には進行中のランがあります。\n{runLine}\n上書きして新しく始めますか？",
            ConfirmButtonText = "上書きする",
            CancelButtonText = "戻る",
            Size = PopupSizeEnum.Medium,
            OnConfirm = () => StartNewGame(slot)
        });
    }

    private void StartNewGame(int slot)
    {
        SaveSlotManager.CurrentSlot = slot;
        _startModeData.SetNewGame();
        // 難易度は出店準備シーンで選ぶ（ここでは仮の既定値だけ入れておく）
        _startModeData.SetFlowSelection(GameModeId.Medium, _view.UseAutoGeneration);

        // 初回プレイ（チュートリアル未完了のスロット）は村・準備シーンを経由せず、そのまま店で開店する。
        // 難易度は上の既定値（ふつう）、スターターレリック等の準備設定は無し（RunSetupData 未設定）で
        // GameLifecycleHandler.InitializeNewGame が初期化する。
        // メタ進行はスロットごとのファイルなので、CurrentSlot を決めた後に読み込んで判定する
        if (TutorialSteps.ShouldStartDirectlyInShop(new MetaProgressModel()))
        {
            // 準備シーンを通らないので、前回の準備設定が残っていても使わないよう消しておく
            var runSetup = AddressableLoader.Load<RunSetupData>("SceneData/RunSetupData");
            if (runSetup == null)
                runSetup = RunSetupData.GetOrCreateFallback();
            runSetup.Clear();
            Debug.Log($"[TitlePresenter] NewGame (slot={slot + 1}) → チュートリアル未完了のため店（{GameSceneName}）で直接開店");
            SceneLoader.Load(GameSceneName);
            return;
        }

        Debug.Log($"[TitlePresenter] NewGame (slot={slot + 1}) → 村シーンへ（難易度は準備シーンで選択）");
        // 新規ランは 村（メタ層・投資）→ 準備シーン（借入・難易度・スターターレリック）を経由する
        SceneLoader.Load("VillageScene");
    }

    private void ContinueGame(int slot)
    {
        // 判定は読み込めた中身で行う（壊れて読めないラン内セーブで店に入らないように）
        var info = SaveSlotManager.GetSlotInfo(slot);

        if (info.Exists)
        {
            SaveSlotManager.CurrentSlot = slot;
            _startModeData.SetContinue();
            Debug.Log($"[TitlePresenter] Continue save slot {slot + 1}");
            SceneLoader.Load(GameSceneName);
            return;
        }

        if (info.HasProfile)
        {
            // ラン終了後で村の進行だけが残っているスロット → 村へ（ラン終了時と同じ行き先）。
            // 新しいランは村 → 準備シーンの出店で始まる（StartModeData.SetNewGame は準備シーンが立てる）
            SaveSlotManager.CurrentSlot = slot;
            Debug.Log($"[TitlePresenter] Continue save slot {slot + 1} → 進行中のランが無いので村へ");
            SceneLoader.Load("VillageScene");
            return;
        }

        // 読み直して空きスロット表示を更新（連打などで状態がずれた場合の保険）
        RefreshSaveSlots();
        _view.SetSaveDataAvailable(SaveSlotManager.AnyProfileExists());
    }

    private void TransitionTo(TitleType screen)
    {
        _model.ChangeScreen(screen);
        _view.DisplayScreen(screen);
    }
}
