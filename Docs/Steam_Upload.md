# Steam へのビルドのアップロード（SteamPipe）

トコのトムトムランド（App ID 4535810）の Windows ビルドを Steam に上げる手順。

## 置き場所
| もの | 場所 |
|---|---|
| Steamworks SDK 1.65 | `D:\Tools\steamworks_sdk_165\sdk\` |
| ContentBuilder（steamcmd） | `D:\Tools\steamworks_sdk_165\sdk\tools\ContentBuilder\` |
| アプリのビルド設定 | `ContentBuilder\scripts\app_build_4535810.vdf` |
| Windows depot の設定 | `ContentBuilder\scripts\depot_build_4535811.vdf` |
| アップロードするビルド | `D:\UnityGame\TomsShop_Steam\`（Unity の Windows ビルドをここに出す） |
| アップロードの実行 | `ContentBuilder\run_tomslands_build.bat` |

## 初回だけ
1. Steamworks の管理画面「SteamPipe > Depots」で、Windows の depot の番号を確かめる。`4535811`（App ID + 1）と違うなら、`app_build_4535810.vdf` の `Depots` と `depot_build_4535811.vdf` の `DepotID` とファイル名を直す。
2. 「Installation > General」で起動の設定に `TomSLand.exe` を登録する。

## 毎回
1. Unity で **Development Build を外して** Windows ビルドを `D:\UnityGame\TomsShop_Steam\` に出す（ビルド前チェック `BuildPreflight` が自動で走る）。
   - `steam_appid.txt` は開発ビルドにだけ入る（`Editor/SteamAppIdPostBuild.cs`）。製品ビルドには入らないが、入っていても depot の設定で除外される。
   - `*.pdb`、`*_BurstDebugInformation_DoNotShip`、`*_BackUpThisFolder_ButDontShipItWithYourGame` も除外される。
2. `app_build_4535810.vdf` の `Desc` を版（例: `v1.4`）に書き換える。中身だけ確かめたいときは `preview` を `1` にする（アップロードしない）。
3. コマンドプロンプトで実行する:
   ```
   D:\Tools\steamworks_sdk_165\sdk\tools\ContentBuilder\run_tomslands_build.bat <Steamworks のアカウント名>
   ```
   パスワードと Steam Guard のコードは steamcmd がその場で聞く（ファイルには保存しない）。
4. 管理画面「SteamPipe > Builds」で上がったビルドを選び、公開するブランチ（default やベータ）に割り当てる。

## 体験版
体験版は別の App ID になる。体験版のアプリを作ったら、`app_build_<体験版のID>.vdf` と depot の設定を同じ形で足す。本編の実績は体験版に入れない（`Docs/SteamAchievements.md`）。
