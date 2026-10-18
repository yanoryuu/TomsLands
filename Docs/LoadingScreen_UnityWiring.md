# ローディング画面（勇者が歩く）— 設計と配線

## 概要
- すべてのシーン遷移（Boot→Title、Title/村/準備/店/配信/イベント/リザルト/ゲームオーバー、設定画面の「タイトルへ」）で
  同じローディング画面を出す。黒い全面＋右下で SPUM の勇者（Hero_SPUM）がその場で歩き（PlayerState.MOVE）、左に「Now Loading...」。
- 流れ: フェードイン（0.25秒）→ `SceneManager.LoadSceneAsync(Single)` → 読み込み完了＋最小表示 0.5 秒 → フェードアウト（0.3秒）。
- 演出はすべて unscaled time（`SetUpdate(true)` / `Time.unscaledTime` / Animator は `UnscaledTime`）。timeScale=0 でも動く。

## 構成
| ファイル | 役割 |
|---|---|
| `Assets/Scripts/SceneData/SceneLoader.cs` | 静的ファサード。`SceneLoader.Load(name)` / `LoadAsync(name)` / `ShowImmediate(status)` / `IsLoading` |
| `Assets/Scripts/Boot/LoadingView.cs` | ローディング画面の View（CanvasGroup フェード・ステータス文言・勇者の上下動） |
| `Assets/Scripts/Boot/LoadingHeroStage.cs` | SPUM を画面外の撮影台（専用カメラ＋RenderTexture）で写し RawImage に出す。DontDestroyOnLoad。表示中以外は撮影台ごと無効 |
| `Assets/Resources/LoadingOverlay.prefab` | 実体。Screen Space Overlay Canvas、**sortingOrder 32700**（宴 2000・チュートリアル/コントローラ枠 32000 より上） |
| `Assets/Scripts/Editor/LoadingOverlayPrefabBuilder.cs` | メニュー `Tools/Loading/Build Loading Overlay Prefab` で上のプレハブを組み立てる（再実行で上書き） |

- プレハブは **Resources** に置いている（Addressables の Default Local Group.asset は他ブランチと衝突しやすいため）。
  中身は UI と Hero_SPUM・フォント参照のみで、共有 SO（BattleInputData 等）は参照しないので複製問題は起きない。
- SceneLoader は初回呼び出し時に `Resources.Load` → `Instantiate` → `DontDestroyOnLoad`。どのシーンから Play しても動く。
  プレハブが無い場合は警告を出して演出なしで遷移する（null-safe）。
- 撮影台はワールド座標 (5000, -6000)・レイヤー 31（地図の MapHeroMarker は (0,-5000)）。

## 遷移の規則
- 遷移中（新シーン有効化前）の再呼び出しは無視（連打・二重遷移防止。警告ログ）。
- 新シーン有効化後（`sceneLoaded` 以降、フェードアウト完了まで）の呼び出しは「続けて遷移」として受け付ける。
  村/準備の未配線フォールバック（Start で素通り遷移）を取りこぼさないため。ローディング画面は出したまま次を読む。
- ローディング中は全面の黒画像がクリックを止める（GraphicRaycaster＋CanvasGroup.blocksRaycasts）。
  キーボード/ゲームパッドの決定は止めていない（二重遷移はガードで無視される）。
- `SceneTransitionService.DevSceneLoadInterceptor`（エディタ専用・AutoPlay ヘッドレス用）はそのまま。横取りされた場合はローディングも出ない。
- AutoPlay（AutoPlayController / DebugAutoPlayer）は `SceneLoader.IsLoading` の間は操作しない。
- デバッグメニューのシーン移動（DebugMenuView.Navigation の直接ロード・DebugResetManager）は従来どおり `SceneManager.LoadScene` のまま
  （SceneTransitionService 経由のボタンはローディングが出る）。
- Additive の Setting / PopUp シーンは対象外（従来どおり）。

## 非同期化で変わったこと
- 旧シーンは遷移要求後もフェードイン〜読み込み完了まで（約 0.3〜1 秒）動き続ける。
  遷移前の Save は各呼び出し元で済んでおり、GameLifecycleHandler.Dispose（スコープ破棄時の保存）の時点も「旧シーン破棄時」で変わらない。
- 新シーンの Awake/Start は従来どおりシーン有効化時に走る（ローディング画面の裏）。

## 配線手順（手動）
1. Unity で `Tools/Loading/Build Loading Overlay Prefab` を実行 → `Assets/Resources/LoadingOverlay.prefab` ができる（コミット済みなら不要）。
2. BootScene の旧ローディング UI（StartCanvas）は削除済み。BootLifetimeScope にローディング用の配線は無い。
3. 見た目の調整はプレハブを直接編集（勇者の位置・大きさは `Hero` の RectTransform、撮影範囲は LoadingHeroStage の viewHeight）。
   ビルダーを再実行すると手調整は上書きされる。
