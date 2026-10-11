# デバッグメニュー仕様（F11）

## 目的
開発・テストプレイ中に、経済・レリック・各種レベルを即座に確認/変更できるチート画面。

## リリースビルドからの除外（必須要件）
- コード全体を `#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲み、リリースビルドにはコンパイル自体されない
  - 対象: `Assets/Scripts/Debug/DebugMenuView.cs` 本体と、各 LifetimeScope の登録ブロック
- さらにランタイムガードとして `Debug.isDebugBuild`（エディタでは常にtrue）を確認し、
  万一 development フラグなしで混入しても表示・入力処理を一切行わない

## 起動・表示
- F11 キーで開閉（旧F12も互換で有効。IMGUI/OnGUI オーバーレイ。シーン配線不要）
- 各シーンの LifetimeScope から `RegisterComponentOnNewGameObject` で自動生成
  - GameLifetimeScope（TomsShop）/ VillageLifetimeScope / BattleLifetimeScope /
    TitleLifetimeScope / EventLifetimeScope / PreparationLifetimeScope / ResultLifetimeScope / GameOverLifetimeScope
  - Boot / Popup 系には登録しない
- 依存は `IObjectResolver.TryResolve` で任意解決。**そのシーンに無いModelのセクションは非表示**
  - 例外: MetaProgressModel（銀行預金/村資金/施設Lv）はスコープ未登録のシーンでは
    ローカル生成（metaData.json を直接読み書き）で編集可能にする
- タブ構成: 情報 / お金 / レベル / レリック / マーケ / その他

## タブ別機能

### 情報（表示のみ）
- ターン / フェーズ / 所持金 / バズ状態・発生確率 / 店ステータス4種+フォロワー（既存を継承）

### お金
- 所持金: +1,000 / +10,000 / +100,000 / 半減 / 0 / 任意値入力→設定（SavePlayerMoneyで即保存）
- 銀行預金 (metaData.bankedGold): +10,000 / -10,000 / 0 / 任意値入力→設定（SaveDataで即保存）
- 村資金: +10,000 / 0
- 当日仕入れ支出（表示のみ）
- 金融: ポジション件数/評価額表示、最初の商品を1口購入（既存検証機能を継承）

### レベル
- 鍛冶屋Lv / 情報屋Lv / 店Lv: 表示・+1・=1（TomsModel直接変更、SavePlayerMoney）
- 勇者Lv: 表示（Lv/EXP）・+1（AddExperienceで正しくステータス更新）・SaveHeroData
- ダンジョンLv: 全ダンジョンの currentDungeonLevel を +1/-1（1〜5クランプ、Save）
- 村施設Lv: 13施設（hall/guild/antique/shrine/bank/warehouse/workshop/artisan/farm/press/road/tavern/training）
  の +1/-1（0〜3クランプ、MetaProgress.SetFacilityLevel + SaveData）

### レリック
- 所持一覧（名前表示）と個別「外す」
- 全RelicDefinition（呪い含む）一覧から「付与」（重複は無効）
- ランダム獲得 / 3択テスト（既存を継承）
- ※ RelicInventoryModel が無いシーン（Village/Fight）では非表示

### マーケ
- バズ強制: 通常 / 超バズ / 炎上 / 強制終了（既存）
- ステータス: 信頼/注目/拡散/定着 ±10、全ステ±10、フォロワー +100/+1,000（既存）

### その他
- 次ターンへ（NextTurn。既存）
- 全ダンジョン情報を解放（isShowedInfo=true + Save）
- 全アイテム在庫 +10（ItemModel.SaveData）
- Time.timeScale: 0.5 / 1 / 2 / 4
- セーブスロット: 現在スロット表示

### 画面移動（DebugMenuView.Navigation.cs）
- 依存は `ConstructNavigation`（追加の [Inject] メソッド）で任意解決: StateManager / SceneTransitionService / StartModeData / BattleInputData / SellOrderModel
- 現在地表示: シーン名 / GamePhase・店内フェーズ / ターン内フェーズ / セーブスロットと進行中ランの有無
- 店内画面: TomsShopGamePhase 全値をボタン化 → `StateManager.ChangeTomsShopPhase`
  - `HasHandler` が false（View未配線）の画面は「(未配線)」表示で無効化（空画面で操作不能になるため）
  - 店シーン以外では非表示
- ターン内フェーズ: 前へ（GoBackTurnPhase、CanGoBack 時のみ）/ 次へ（AdvanceTurnPhase、営業では無効）
  - 仕入れ / 陳列 / 営業 へ直接ジャンプ（正規の Advance/GoBack を繰り返して到達。値の直接代入はしない）
  - イベントへのジャンプは不可（消化済みイベントは再表示できない仕様。GoBack も仕入れが下限）
- シーン移動（SceneTransitionService があれば経由、無いシーンは既存Presenterと同様に SceneManager.LoadScene）
  - 店シーンからの移動時は GameFlowManager の遷移前処理と同じ内容でセーブ（Item/SellOrder/Portfolio/Toms+GameFlowIndex）
  - タイトル / 村（StartModeData=NewGame）/ 準備（StartModeData=NewGame）
  - 店（続きから。StartModeData=Continue。進行中ランが無ければ無効）/ 店（新規ラン。現スロット上書き）
  - 配信: 店シーンのみ。NextTurn の配信分岐と同じ手順で BattleInputData を作成（次の配信ダンジョン、無ければ先頭）。
    GameFlowIndex は現在位置のままなので帰還後もターンは進まない（テスト用）
  - リザルト / ゲームオーバー: 進行中ランがある（または店シーン）時のみ。各画面のボタンを押すと通常どおりランは削除される
  - イベント: 無効（EventScene が EditorBuildSettings 未登録、かつ EventInputData の事前設定が必要）

### オート（DebugMenuView.AutoPlay.cs / DebugAutoPlayer.cs）
プレイ中の実ゲーム画面を自動で進める。エディタ専用のヘッドレス試走（Editor/AutoPlay）とは別物で、Jev（LLM）は使わない。
- 仕組み: `ConstructAutoPlay`（追加の [Inject]）で同じ GameObject に `DebugAutoPlayer` を AddComponent し依存を TryResolve。
  UniTask のループで「ステップ → ウェイト」を繰り返す。操作は既存 View の Subject（ボタン押下と同じ経路）か Presenter/Model の公開APIのみ。
  ボタンが private な View（EventView/DebtView/BattleResultView 等）はリフレクションで SerializeField を読み、Button.onClick を呼ぶ（書き換えはしない）
- 継続状態（実行中/設定/ログ/所持金推移）は static。シーン遷移（店⇄配信）で DebugMenuView が作り直されても新シーンのドライバが引き継ぐ。
  同時に動くのは最後に起動した1体のみ
- UI: 開始 / 一時停止・再開 / 停止、進めるターン数（1/5/10/∞。新しいターンの頭で停止）、状態（いま何をしているか・一時停止理由）、
  経過ターン、所持金推移（ターンごと最大12件）、ログ（直近10行。コンソールにも `[AutoPlay]` で出力）
- 単発: このターンだけ自動（=1ターン）/ 仕入れだけ自動（鍛冶屋でお任せ購入→閉じて停止）/ 陳列だけ自動（オート陳列して停止）
- 戦略: 標準（お任せ仕入れ＋オート陳列）/ 陳列のみ（買わない）/ 何もしない（素通し）。仕入れ予算 100/75/50/25%
  （所持金から「2ターン以内に来る納税額」を残した額に対する割合。AutoPlayGreedyBot と同じ考え方。方針は Recommend 固定）
- ウェイト 0.05〜2秒。「timeScale に連動」ON なら scaled time で待つ（x4 で実時間1/4。timeScale=0 だと止まる）。timeScale x1/2/4/8 ボタン
- 1ターンの進め方（店シーン）
  - イベント: EventView の確認ボタンを押してページ送り→確定（TomsShopPresenter が効果適用・仕入れへ前進）。保留イベントが無ければ AdvanceTurnPhase
  - 仕入れ: 標準なら `TomsShopView.OnBlacksmithClicked` → 鍛冶屋で `OnAutoBuyBudgetConfirmed(予算, Recommend)` → `OnCloseRequested`。それ以外は「次へ」
  - 陳列: 陳列のみ/標準なら `ItemSelectionPresenter.OnOpenSelectionPanel` → `OnAutoDisplayRequested` → 次ステップで閉じる。何もしないなら「次へ」
  - 営業: `OnStartShopClicked`（演出後に TurnEndSummary）→ サマリー `OnConfirmClicked`（=NextTurn）。
    NextTurn でフロー位置が進んだら再送しない（二重の日送り防止）
  - 割り込み: 強制納税（払えれば支払い。破産画面なら停止）/ レリック3択（先頭を獲得）/ 朝レポート（閉じる）
  - 配信日の寄り道ポップアップ: 標準は「鍛冶屋へ寄る」→仕入れ→「配信を始める」、それ以外は「このまま配信へ」
- 配信シーン（FightScene）: 品出し画面でオート選択→確定（購読前は5秒ごとに再送）/ 配信中は自動進行を待つ（在庫切れ補充ポップアップは「補充しない」で閉じる）/
  リザルトの確認ボタンで店へ戻る → 店シーンで自動再開。介入カード等の配信中操作はしない
- 会話（Utage）: ON なら Page.InputSendMessage で送り、選択肢は先頭（OFF なら手で進めるまで待つ）
- 汎用ポップアップ（PopUpManager）: 閉じるだけのお知らせ・品出しゼロ確認・配信日の判断・補充確認は自動。
  それ以外の確認ダイアログは一時停止して理由を表示し、手で選んで閉じると自動で再開
- 安全装置
  - 店ホーム/鍛冶屋/サマリー以外の画面（情報屋・新聞など）を開いていたら一時停止（閉じると自動再開）
  - LogError/例外を検出したら一時停止（設定でOFF可）。シーン切替直後3秒は対象外（既存の初期化・後始末由来のエラーで止まらないように）
  - ステップ内の例外は停止＋ログ。同じ状態のまま30秒進まなければ一時停止（配信中・会話中・演出待ちは対象外）
  - 店/配信以外のシーン（タイトル・村・リザルト等）では「未対応」で停止
  - セーブ削除・リセット・値の直接書き換えなどのデバッグ操作は呼ばない（通常プレイと同じ経路のみ）

## 変更の永続化ポリシー
値を変更したら対応する Save を即時呼ぶ（PlayerMoney→SavePlayerMoney、預金/村資金/施設→Meta SaveData、
勇者→SaveHeroData、ダンジョン→repository.Save、在庫→ItemModel.SaveData）。
ReactiveProperty 経由の変更なので購読中のUIには即反映される。

### 便利（DebugMenuView.Utility.cs）
- お金: +1,000,000 / 最大化(99,999,999) / 借入元本を0に（TomsModel.BorrowedPrincipal。SavePlayerMoney）
- アイテム: 全在庫0 / 全アイテム解放（鍛冶屋Lvを全アイテムの RequiredLevel 最大値まで引き上げ） / 個別在庫 +1・+10（未解放はLv表示）
- イベント: EventDataLoader.LoadAll() の一覧（id/title絞り込み）から TomsEventExecutor.Execute で強制実行（コマンド効果のみ・演出UIなし）
- 新聞・情報: DungeonIntelModel.RevealAll（弱点全開示・起動中のみ）、購読数/スクラップ数の表示
- セーブ: 現スロットのフォルダパス表示、エクスプローラで開く（Windowsのみ）、今すぐ全部保存（Toms/Item/Hero/Status/Relic/Portfolio/Dungeon/SellOrder/Machine/Newspaper/Meta）
- 表示: FPS表示（DebugUtilityOverlay。メニューを閉じても右上に表示）、警告/エラー数と直近8件（Application.logMessageReceived。Boot時から収集）、一時停止(timeScale=0)トグル
- 勇者: Lv/EXP/HP/ATK/DEF/戦術/装備の表示、HP全回復（HeroStatusData の現Lv最大HP、SaveHeroData）
- 依存Modelは ConstructUtility で任意解決。無いシーンでは該当セクションは非表示/メッセージ表示
