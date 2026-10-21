# Steam 実績（トコのトムトムランド / App ID 4535810）

実装: `Assets/Scripts/Steam/SteamManager.cs`、`Assets/Scripts/Achievements/`。デバッグ: F11 の「実績」タブ（Docs/DebugMenu_Spec.md）。

## 仕組み

- **Steamworks.NET 2025.164.1**（UPM の git URL で固定。`Packages/manifest.json`）。Standalone（Windows/Linux/Mac）でだけ使い、Android などでは Steam のコードを丸ごと外す。
  - Steamworks.NET が Scripting Define Symbols に `STEAMWORKS_NET` を勝手に足さないよう、`ProjectSettings/SteamworksNETSettings.json` で止めてある（このコードは使っていない）。
- **SteamManager**: Boot シーン（BootLifetimeScope）が1つ作り、DontDestroyOnLoad で全シーンに残す。`SteamAPI.InitEx` → 毎フレーム `RunCallbacks` → 終了時に `Shutdown`。
  - Steam が起動していない・Steam 外の起動・DLL が無いときは `Initialized=false` のまま何もしない。例外は出さず、ゲームはそのまま動く。
  - エディタで Boot 以外のシーンから再生したときは SteamManager が無いので、実績は「解除したつもり」のログだけになる。
- **AchievementService**（シーンを跨いで1つ）: 解除はまず手元の記録 `persistentDataPath/achievements.json` に残す。Steam が使えれば `SetAchievement` / `SetStat` を呼び、`StoreStats` は解除時・30秒おき・終了時にまとめて送る。
  - Steam が無いときに解除したものは、次に Steam につながったときに送る。Steam 側にあって手元に無いものは、手元に取り込む。
  - ログ: `[Achievement] 解除: API名「表示名」 ← 理由 / Steam へ送信 | Steam なし（解除したつもり…） | 隔離中`
- **AchievementTracker**（店 GameLifetimeScope と村 VillageLifetimeScope の EntryPoint）: その場の Model を購読して条件を判定する。
  - 1ラン内で数えるもの（二枚舌・収集家・バズ連続など）は `slot_N/achievementRun.json` に持つ。ラン内ファイルなので RunSaveCleaner が消す。
- **AchievementEvents**: Model の外で起きる出来事の受け口。呼んでいるのは2か所。
  - BattleResultHandler: 配信の精算（BattleOutputData を消す直前）
  - ResultModel.BuildStatistics: クリアの集計
- **エディタの自動テストプレイ**（Jev AutoPlay のヘッドレス）でも同じ判定が走る。
  - 記録は隔離フォルダ（`SaveSlotManager.DevRootOverride`）に置き、Steam には送らない。
  - 警告ログの `[Achievement]` で、ボットが届いた実績が分かる。

## 実績の一覧（22個）

「隠す」はおすすめ。○ = 隠し実績にする。
- 誤報には乗らない: 誤報の存在は自分で気づく驚きにしたい
- 首の皮一枚: 狙うと危ない小ネタ

| # | API 名 | 表示名 | 説明（Steam に出す文） | 判定（実装） | 隠す | 進捗 Stat |
|---|---|---|---|---|---|---|
| 1 | FIRST_INCOME | はじめての入金 | 初めて商品が売れて入金された | 店の売り注文が約定して入金額 > 0（`SellOrderModel.OnSettled`）、または配信で売上 > 0 | | |
| 2 | FIRST_STREAM | 初配信 | 配信を最後まで見届けた | 配信の結果が勝ち／負け（途中終了＝配信終了ボタンは数えない） | | |
| 3 | SCRAP_PREDICTION | 読み的中 | スクラップした記事の効き先が実際に値上がりした | 値上がりを報じる本物の記事（実需あり・trendDelta か demandKick が正）をスクラップする。その時点の効き先の値段を控え、発効日から決着の翌日までのどこかで効き先のどれかがそれより高くなったら解除。毎日の経済処理の後に判定する | | |
| 4 | EQUIP_AHEAD | 先回り | 装備させた品を抱えたまま勇者が勝ち、値上がりした | 勇者が勝った配信で、装備（武器・防具）のどちらかを在庫に1個以上持っている（勝利ボーナスで値段×5） | | |
| 5 | DEMON_DIVIDEND | 配当確定 | 魔王軍に出資して配当を受け取った | 勇者が負けた配信で、そのダンジョンを魔王軍支援で初期 Lv より上げてあり、防衛報酬 > 0 | | |
| 6 | BOTH_SIDES | 二枚舌 | 1回の出店で勇者側でも魔王側でも儲けた | 同じランで 4（勇者側）と 5（魔王側）の両方の条件を満たした | | |
| 7 | LONG_SHOT | 大穴 | クリア見込みが不利以下の勇者が勝ち、装備で儲けた | 配信日に入った時点のクリア見込みが 35% 未満で、4 の条件を満たした | | |
| 8 | NOT_FOOLED | 誤報には乗らない | スクラップした誤報を確認して見抜いた | スクラップした記事を確認して誤報だと分かった（ScrapState.ConfirmedMiss） | ○ | |
| 9 | DAILY_100K | 一日十万 | 1日の入金が 100,000G を超えた | 同じ日の売れた分の入金（店の約定＋配信の売上）の合計 > 100,000G | | MAX_DAILY_INCOME（100001） |
| 10 | MILLIONAIRE | 百万長者 | 所持金が 1,000,000G に届いた | 所持金 ≥ 1,000,000G | | MAX_MONEY（1000000） |
| 11 | TEN_MILLIONAIRE | 千万長者 | 所持金が 10,000,000G に届いた | 所持金 ≥ 10,000,000G | | MAX_MONEY（10000000） |
| 12 | BUZZ_WAVE | バズの波 | バズを3日続けた | 日送りのバズ判定の後、バズ中（炎上は除く）の日が3日続いた | | BEST_BUZZ_STREAK（3） |
| 13 | POPULAR_SHOP | 人気店 | フォロワーが 10,000人に届いた | フォロワー ≥ 10,000 | | MAX_FOLLOWERS（10000） |
| 14 | GOLEM_WORKSHOP | ゴーレム工房 | ゴーレムを3台置いた | 置いてあるゴーレム（毎朝お金を生むマシン＝貯金ゴーレム小/中/黄金）が3台以上 | | MAX_GOLEMS（3） |
| 15 | ALL_REPAID | 皆済 | 1回の出店で返済をすべて期日に払い、出店を終えた | クリアした時点で返済回数 ≥ 期日の回数（最終日の前日までに来た期日。返済が1回も無いランは数えない）。期日に払えないと破産なので、実質「返済のあるランをクリア」 | | |
| 16 | BY_A_HAIR | 首の皮一枚 | 所持金 1,000G 未満で返済日を越えた | 返済した直後（返済回数が増えたとき）の所持金 < 1,000G。期日前の自主返済も含む | ○ | |
| 17 | ALL_PAPERS | 全紙購読 | 新聞5社をすべて購読した | これまでに購読した社（ラン跨ぎで数える）が新聞社の数（5）に届いた。同時に購読できるのは最大3社なので、同時ではない | | PAPERS_SUBSCRIBED（5） |
| 18 | COLLECTOR | 収集家 | 1回の出店でレリックを10個集めた | このランで手にしたレリックの種類（入れ替えで手放しても減らない）が10個 | | MAX_RELICS_IN_RUN（10） |
| 19 | FIRST_CLEAR | 初クリア | かんたん（Short）をクリアした | Short をクリア（リザルトの集計時） | | CLEARS_SHORT（1） |
| 20 | LONG_TRADER | 長い商い | ふつう（Normal）とむずかしい（Long）をクリアした | CLEARS_NORMAL と CLEARS_LONG がどちらも 1 以上 | | |
| 21 | RANK_S | ランク S | ランク S でクリアした | リザルトのランクが S | | |
| 22 | VILLAGE_REVIVAL | 村おこし | 村の施設をすべて建てた | `VillageModel.AreAllFacilitiesBuilt()`（施設データにある全施設が Lv1 以上。施設の数は決め打ちしない）。村に入ったときと投資したときに判定 | | |

補足:
- 7 大穴の「不利以下」は、PR #105 のクリア見込み5段階がまだ main に無いので、`ClearProbabilityCalculator` の % が 35% 未満で判定している（`AchievementDefinitions.LongShotClearPct`）。#105 のマージ後に段階判定へ差し替える。
- 8 は「誤報と分かった記事の逆に張って儲ける」の判定が難しいため、近い条件「スクラップした誤報を確認して見抜く」にした。
- 9 の入金に、配当・債券・レリックの換金・イベントでの増減は入れない（売れた分だけ）。
- クリアの数え上げ（19〜21・15）は1ランで1回だけ。リザルトで終了して続きから戻っても二重に数えない。

## Stats（すべて INT）

| API 名 | 意味 | 使う実績 |
|---|---|---|
| MAX_MONEY | 所持金の最高値 | 10, 11 |
| MAX_DAILY_INCOME | 1日の入金の最高値 | 9 |
| BEST_BUZZ_STREAK | バズの最長連続日数 | 12 |
| MAX_FOLLOWERS | フォロワーの最高値 | 13 |
| MAX_GOLEMS | 同時に置いたゴーレムの最多台数 | 14 |
| MAX_RELICS_IN_RUN | 1ランで手にしたレリックの最多数 | 18 |
| PAPERS_SUBSCRIBED | これまでに購読した新聞社の数 | 17 |
| CLEARS_SHORT | Short のクリア回数 | 19 |
| CLEARS_NORMAL | Normal のクリア回数（コード上の GameModeId は Medium） | 20 |
| CLEARS_LONG | Long のクリア回数 | 20 |

どれも「これまでの最大値」として送るので、値は下がらない。

## Steamworks の管理画面でやること（ユーザー作業）

1. **Stats を作る**: Steamworks → アプリの管理（App Admin）→「Stats & Achievements」→「Stats」。上の表の10個を足す。
   - API Name: 表のとおり
   - Type: INT
   - Set By: Client
   - Increment Only: ✓（下がらない）
   - Default: 0
   - Max Change / Min / Max: 空でよい
   - Display Name: 日本語でよい（Steam のプロフィールには出ない）
2. **実績を作る**: 同じ画面の「Achievements」で、上の22個を足す。
   - API Name: 表のとおり（コードと1文字でも違うと解除できない）
   - Display Name / Description: 表の「表示名」「説明」。言語の欄で日本語を入れる。英語は未定なら日本語と同じでよい
   - Hidden: おすすめは 8・16 のみ ✓
   - Progress Stat（進捗バー）: 進捗 Stat がある実績は、その Stat を選び、Min=0・Max=表のかっこ内の値にする。Steam の画面に「3/10」のような進み具合が出る
   - Set By: Client
3. **アイコン**: 実績ごとに2枚、どちらも **256×256 の JPG**。素材が要る（このPRでは作っていない）。
   - 解除後（Achieved）: カラー
   - 未解除（Unachieved）: グレー版
   - 22個 × 2 = 44枚
4. **公開**: 「Publish」タブ →「Prepare for Publishing」→「Publish to Steam」。公開するまでゲームからは解除できない（SetAchievement が false を返し、ログに警告が出る）。
5. **確かめる**: 開発用アカウント（このアプリを持っているアカウント）で Steam を起動したまま、エディタで Boot シーンから再生するか、開発ビルドの exe を起動する。条件を満たすと Steam の通知が出る。F11 の「実績」タブで状態を見られ、「全部リセット」でやり直せる。
6. **体験版**（Steam の別アプリ、`DEMO_BUILD`）は本編の実績を入れない。体験版では AchievementTracker を登録せず、AchievementService の解除・Stats の更新も何もしない（SetAchievement も呼ばない）。体験版の App には実績を作らない（Docs/Demo_Build.md）。

## 起動と steam_appid.txt

- プロジェクト直下の `steam_appid.txt`（中身 `4535810`）は、エディタの再生で Steam につなぐために要る。消すと Steamworks.NET が 480（Spacewar）で作り直すので消さない。
- **開発ビルド**（Build Settings の Development Build にチェック）: ビルド後に `Assets/Scripts/Editor/SteamAppIdPostBuild.cs` が exe の隣へ `steam_appid.txt` を自動でコピーする。Steam を起動しておけば、exe を直接起動しても Steam につながる。
- **製品ビルド**（Development Build なし）: `steam_appid.txt` を入れない。同じ出力先に前の開発ビルドの分が残っていたら消す。
  - 入れると、Steam を通さない起動でも App ID が決まってしまう。
  - Steam のデポにも上げない。
  - 製品は Steam クライアントから起動されるので、ファイルが無くてもつながる。
- `SteamAPI.RestartAppIfNecessary`（Steam 外で起動されたら Steam 経由で起動し直してゲームを閉じる）は呼んでいない。「Steam 外の起動でもゲームを止めない」方針のため。DRM 的に Steam 経由を強制したくなったら SteamManager の初期化の前に足す。
