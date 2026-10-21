# 体験版（DEMO_BUILD）

トコのトムトムランドの体験版は、製品版と**同じプロジェクト**で、スクリプト定義 `DEMO_BUILD` を入れてビルドする。Steam では製品版とは**別のアプリ**（体験版の App ID は未取得）。

切り替えと定数は `Assets/Scripts/Demo/DemoBuild.cs` の1か所にまとめてある。コードは `DemoBuild.IsDemo` を見て分岐する。

## 遊べる範囲
| | 製品版 | 体験版 |
|---|---|---|
| ランの長さ | かんたん／ふつう／むずかしい（準備シーンで選ぶ） | **かんたん（Short）だけ**。`GameLifecycleHandler` が Short に固定する |
| 始め方 | 初回は店に直行、2回目からは 村 → 準備シーン | **いつも店に直行**（村・準備シーンを通らない） |
| チュートリアル | — | 製品版と同じ |
| ラン終了後（クリア・破産） | 村へ（精算して村資金に） | **「製品版へ」のポップアップ** → タイトルへ。村資金・ランの記録（metaData の runs）は付けない |
| 2周目 | 巻き戻りの会話（`Run_Opening_Second`）など | 無い。もう一度「はじめから」で遊ぶと、会話なしでかんたんの1日目から（ランの記録を付けないので、いつも「1周目」扱い） |
| セーブスロット | 3つ | **スロット1だけ**。スロット選択の画面は出さない。「続きから」はスロット1に進行中のランがあるときだけ押せる |
| Steam 実績 | 22個 | **入れない**（解除処理も呼ばない） |

## 「製品版へ」のポップアップ
既存の中サイズのポップアップ（PopupScene）をそのまま使う。新しい素材は無い。
- 見出し: 体験版はここまで
- 本文: 今回の儲け（リザルトと同じ式。破産はこのランの最高の儲け）、ランク（破産は「なし（破産）」）、「製品版では、村を育てると長い商いに出られます。」
- 「ストアページへ」: 製品版（App 4535810）のストアページを Steam のオーバーレイで開く。オーバーレイが使えない（Steam 外の起動・初期化に失敗）ときはブラウザで https://store.steampowered.com/app/4535810/ を開く。押してもポップアップは閉じない（`PopUpData.KeepOpenOnConfirm`）。
- 「タイトルへ」（B/Esc も同じ）: タイトルへ戻る。
- リザルト・破産画面のボタンは「次へ」になる。破産画面の「村へ +○G」の行は出さない（リザルトの村資金の行はそのまま）。

ラン内セーブは「次へ」を押した時点で消す（製品版と同じく、終わったランが「続きから」に戻らないように）。

## タイトル画面
右上に「体験版」と1行出す（タイトルのボタンと同じフォント・マテリアル、`TitleView.ShowDemoBadge`）。

## 保存先（製品版と混ざらない）
| | 製品版 | 体験版 |
|---|---|---|
| セーブ（slot_N/）・metaData・実績の手元記録（achievements.json）・リモート配信のキャッシュ（remoteconfig/） | `%USERPROFILE%\AppData\LocalLow\MozaicWorks\TomSLand\` | `%USERPROFILE%\AppData\LocalLow\MozaicWorks\TomSLand_Demo\` |
| Player.log・PlayerPrefs（音量など） | productName = TomSLand | 体験版のビルドは productName = TomSLand_Demo なので別 |

- 根のフォルダは `DemoBuild.DataRoot`（`SaveSlotManager.SlotRoot`・`AchievementStore.GlobalPath`・`RemoteConfigCache` が使う）。
- エディタで `DEMO_BUILD` を入れて再生したときも、セーブは `TomSLand_Demo\` に行く（PlayerPrefs と Utage のフォルダはエディタでは製品版と同じ。Utage のフォルダは今は使っていない）。
- 体験版のセーブは製品版へ引き継がない。

## Steam
- 体験版の App ID は `DemoBuild.DemoSteamAppId`（**今は 0 = 未取得**）。取得したらここだけ書き換える。
- `steam_appid.txt`: 体験版の開発ビルドには `DemoSteamAppId` を書く。0 の間は入れない（本編の 4535810 を入れると体験版が本編として Steam につながる）。製品ビルドには入れない（`Editor/SteamAppIdPostBuild.cs`）。
- App ID が未取得の間や Steam 外の起動では Steam の初期化に失敗するが、そのまま遊べる（`SteamManager` はゲームを止めない）。このとき「ストアページへ」はブラウザで開く。
- 実績: `AchievementTracker` を登録しない、`AchievementService` の Unlock・ReportMax・HandleRunCleared などは何もしない、`AchievementEvents` も何もしない。体験版の App には実績を作らない。

## ビルド
- メニュー **Tools/TomsLands/体験版/体験版をビルド**（開発ビルドは「体験版をビルド（開発ビルド）」）。出力先 `D:\UnityGame\TomsShop_Demo\TomSLand_Demo.exe`。
  - `DEMO_BUILD` はそのビルドにだけ足す（`BuildPlayerOptions.extraScriptingDefines`）。Player Settings の定義は変えない。
  - productName はビルドの間だけ `TomSLand_Demo` にし、終わったら（失敗しても）元に戻す。
  - ビルド前チェック（`BuildPreflight`）は製品版と同じく走る。
- バッチモード: `Unity.exe -batchmode -nographics -quit -projectPath <プロジェクト> -executeMethod DemoBuildMenu.BuildBatch -logFile <ログ>`（失敗は終了コード 1）。
- エディタで体験版を試す: **Tools/TomsLands/体験版/エディタで体験版を試す（DEMO_BUILD を入れる）** で Player Settings（Standalone）に定義を足す／外す（チェックが付いている間は体験版）。**入れたまま製品版をビルドしようとすると、ビルド前に止まる**（`DemoBuildMenu.OnPreprocessBuild`）。

## SteamPipe（アップロードはまだしない）
`tools/steampipe/` に体験版の設定を置いた（`D:\Tools\steamworks_sdk_165\sdk\tools\ContentBuilder\` にも同じものをコピー済み）。
| ファイル | 中身 |
|---|---|
| `scripts/app_build_demo.vdf` | 体験版のアプリのビルド設定。ContentRoot は `D:\UnityGame\TomsShop_Demo\`。**AppID と Depots の番号は仮の 0** |
| `scripts/depot_build_demo.vdf` | 体験版の Windows depot。**DepotID は仮の 0**。除外は製品版と同じ（pdb・Burst のデバッグ情報・steam_appid.txt など） |
| `run_tomslands_demo_build.bat` | アップロードの実行（`run_tomslands_demo_build.bat <アカウント名>`）。AppID・DepotID が 0 のまま、または exe が無いと止まる |

体験版の App を作ったら:
1. Steamworks で体験版のアプリを作る（製品版のストアページの「体験版」）。App ID と Windows の depot の番号を確かめる。
2. `DemoBuild.DemoSteamAppId`、`app_build_demo.vdf` の `AppID` と `Depots`、`depot_build_demo.vdf` の `DepotID` を書き換える（ファイル名はそのままでよい）。
3. 体験版のアプリの「Installation > General」で起動の設定に `TomSLand_Demo.exe` を登録する。実績は作らない。
4. 「体験版をビルド」→ `app_build_demo.vdf` の `Desc` を版に → `run_tomslands_demo_build.bat <アカウント名>` → 管理画面でブランチに割り当てる（手順は Docs/Steam_Upload.md と同じ）。

## リモート配信（gameconst・items・balance）
体験版も製品版と**同じ** `config/production/` を読む（`DemoBuild.DemoRemoteConfigEnvironment = "production"`）。
- かんたんのランは製品版と同じ値で遊べるべきなので、同じ値でよい。製品版の調整がそのまま体験版にも届く。
- 配信が読めないときは同梱の値で動く（製品版と同じ）。キャッシュは体験版のフォルダに別に持つ。
- 体験版だけ値を変えたくなったら: サーバーに `config/demo/` を作って gameconst/items/balance を置き、`DemoRemoteConfigEnvironment` を `"demo"` にする（`DemoBuild.RemoteConfigUrl` が URL の `/config/production/` を差し替える）。GAS のアップロード先に demo を足す必要がある。

## 確かめ方
- コンパイル: `DEMO_BUILD` なし／ありの両方（バッチモードなら `-executeMethod DemoBuildMenu.AddDefineBatch` で入れてから開き直し、確認後に `RemoveDefineBatch`）。
- 体験版のセーブは `TomSLand_Demo\` にだけできる。製品版の `TomSLand\` の slot_N・metaData.json・achievements.json は変わらない。
