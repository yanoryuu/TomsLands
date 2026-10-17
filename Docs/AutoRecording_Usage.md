# 自動録画（プレイ動画の自動撮影）使い方

エディタ専用の開発ツール。Unity Recorder（`com.unity.recorder` 5.1.2）で Game View を MP4 に録画し、停止条件を満たしたら自動で止めて保存する。

- メニュー: `Tools/TomsLands/自動録画`
- コード:
  - `Assets/Scripts/Editor/Recording/GameplayAutoRecorder.cs` … 録画制御（`[InitializeOnLoad]`。ウィンドウを閉じていても動く）
  - `Assets/Scripts/Editor/Recording/GameplayAutoRecorderWindow.cs` … ウィンドウ
  - `Assets/Scripts/Editor/Recording/GameplayAutoRecorderSettings.cs` … 設定（`UserSettings/GameplayAutoRecorderSettings.asset` に保存。git 管理外）
  - `Assets/Scripts/Debug/GameplayRecordingSignals.cs` / `GameplayRecordingSignalsDriver.cs` … ゲーム側の出来事を受ける口（ファイル全体が `#if UNITY_EDITOR`）

## 設定項目

| 項目 | 内容 |
|---|---|
| 出力フォルダ | 既定 `Recordings`（プロジェクト直下。相対パスはプロジェクト直下基準、絶対パスも可。`.gitignore` 済み） |
| ファイル名の接頭辞 | `<接頭辞>_yyyyMMdd_HHmmss.mp4` |
| FPS | 固定フレームレート録画（Constant + Cap）。動画の長さ＝録画フレーム数 / FPS |
| 解像度 (GameView) | 既定 1920x1080。Recorder が Game View をこのサイズにする |
| 音声も録る | ON で音声込み |
| Play 開始と同時に録画 | ON にすると Play に入った瞬間に録画開始 |
| 自動停止したら Play も停止 | 停止条件で止まったときだけ Play も止める（手動停止では止めない） |
| 停止の余韻（秒） | 条件を満たしてから実際に止めるまで録り続ける秒数（既定 2 秒。GameOver 画面などを映すため） |
| 録画開始 / 停止して保存 | 手動操作（Play 中のみ） |

停止条件は複数 ON にでき、**どれか1つでも満たしたら停止（OR）**。Play を手動で止めた場合も `ExitingPlayMode` で録画を止めて保存する。
停止理由と保存パスは Console（`[AutoRecorder]`）とウィンドウの「直近の結果」に出る。

## 停止条件と検知方法

| 条件 | 検知方法 |
|---|---|
| 経過時間（動画秒） | `(Time.frameCount - 開始フレーム) / FPS` を `EditorApplication.update` で判定 |
| フェーズに到達 | `StateManager.CurrentTomsShopPhase`（R3 ReactiveProperty）を購読。メインフェーズが TomsShop のときに指定 `TomsShopGamePhase` へ遷移したら停止 |
| N ターン経過 | `GameFlowManager.CurrentTurn` を購読し、**前回値 +1 になったときだけ** 1 ターンと数える。それ以外の変化（TomsShop⇄FightScene の往復で作り直された GameFlowManager の初期値 1 → 復元値への飛び、新しいランで 1 に戻る、デバッグのターン飛ばし）は基準の付け直しで数えない |
| シーンがロード | `SceneManager.sceneLoaded`。シーン名をカンマ区切りで指定（例: `GameOver,ResultScene,VillageScene`）。大文字小文字無視 |
| ラン終了 | `GameOver` / `ResultScene` シーンのロード、または `StateManager.CurrentPhase` が `GamePhase.Result` になったとき |
| 入力なし（秒） | 旧 Input（`Input.anyKey` / マウス移動 / ホイール / タッチ）を毎フレーム監視し、最後の入力からの実時間で判定。エディタ一時停止中は判定しない |

`StateManager` / `GameFlowManager` は、シーンロードのたびに `LifetimeScope.Container.TryResolve` で取り出して購読する（既存クラスは無改修）。
録画開始時点で既にいるフェーズは「到達」扱いにしない（開始時のバインドは初回通知をスキップ）。

## 制約・注意

- エディタ専用。プレイヤービルドには含まれない（Recorder API はエディタ専用、シグナル側も `#if UNITY_EDITOR`）。
- 録画は Play 中のみ。録画中にスクリプトを再コンパイルすると録画は失われる。
- 固定 FPS 録画のため、重い場面ではゲームの進行が実時間より遅くなる（動画は滑らか）。経過時間条件は「動画の長さ」で判定する。
- 放置検知は実入力のみを見る。デバッグメニューのオートプレイ中は入力が無い扱いになるので、オートプレイと併用するときは放置条件を OFF にする。
- Unity Recorder ウィンドウ側でも録画していると二重録画になる。

## オートプレイ連動（Assets/Scripts/Debug/AutoPlay）

人が操作しなくても、録画しながらゲームを自動で進められる。オートプレイ自体の説明は `Docs/AutoPlay_Usage.md`。

| 項目 | 内容 |
|---|---|
| 録画開始でオートプレイ開始 | 録画開始（Play と同時に録画・手動開始のどちらでも）と同時に `AutoPlay.Start` を呼ぶ |
| 速度倍率 / 会話の送り方 / タイトルでの行動 | `AutoPlay.Settings` の `TimeScale` / `ScenarioMode` / `TitleAction` を上書きする。それ以外の設定は現在の `AutoPlay.Settings` を引き継ぐ |
| 録画停止でオートプレイも停止 | 録画が止まったら `AutoPlay.Stop("録画停止（理由）")` |
| 隔離セーブで遊ぶ | 「Play 開始と同時に録画」のときだけ有効。Play 開始前に `Temp/AutoRecordSaves` を空にし、`SaveSlotManager.DevRootOverride` をそこへ向けて Play する（シーンロード前に `GameplayRecordingSignals` が SessionState から設定する）。**ユーザーのセーブスロットは読まない・書かない**。毎回まっさら＝初回ラン（プロローグ・チュートリアル込み）から始まる。Play を抜けると解除。※ Utage 自身のセーブ（`persistentDataPath/Utage/SaveTomsScenario` の既読・オートセーブ等）とリモート設定キャッシュはスロット外なので隔離されない |
| 停止条件「オートプレイ停止」 | `AutoPlay.Stopped` を購読。ターン上限・ラン終了・詰まりなどでオートプレイが止まったら録画も止める。停止理由に `AutoPlay.LastStopReason` が入る |

- オートプレイ実行中（`AutoPlay.IsRunning`）は停止条件「入力なし」を判定しない。
- ラン終了の演出まで撮りたいときは、停止条件「ラン終了」を OFF にして「オートプレイ停止」に任せる（オートプレイ側の `RunEndAction=Stop` はリザルト/ゲームオーバーの演出・会話を流し終えてから止まる）。

### 固定 FPS 録画と Time.timeScale

- Recorder は固定 FPS（`Time.captureFramerate`）で録るので、1 フレームのゲーム時間は `timeScale / FPS`。**timeScale=2 なら動画の中のゲームは 2 倍速**になる（動画の再生時間＝フレーム数/FPS）。等速の動画が欲しいときは速度倍率 1。
- 停止条件「経過時間」は動画の長さ（フレーム数/FPS）で判定するので timeScale の影響を受けない。
- オートプレイの待ち時間のうち実時間で測るもの（ボタンの押し込み表示・シーン到着後の待ち・詰まり判定）は、描画が FPS に追いつかない重い場面では動画上で短く見える。
