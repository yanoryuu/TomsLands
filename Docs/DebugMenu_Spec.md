# デバッグメニュー仕様（F12）

## 目的
開発・テストプレイ中に、経済・レリック・各種レベルを即座に確認/変更できるチート画面。

## リリースビルドからの除外（必須要件）
- コード全体を `#if UNITY_EDITOR || DEVELOPMENT_BUILD` で囲み、リリースビルドにはコンパイル自体されない
  - 対象: `Assets/Scripts/Debug/DebugMenuView.cs` 本体と、各 LifetimeScope の登録ブロック
- さらにランタイムガードとして `Debug.isDebugBuild`（エディタでは常にtrue）を確認し、
  万一 development フラグなしで混入しても表示・入力処理を一切行わない

## 起動・表示
- F12 キーで開閉（IMGUI/OnGUI オーバーレイ。シーン配線不要）
- 各シーンの LifetimeScope から `RegisterComponentOnNewGameObject` で自動生成
  - GameLifetimeScope（TomsShop）/ VillageLifetimeScope / BattleLifetimeScope
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

## 変更の永続化ポリシー
値を変更したら対応する Save を即時呼ぶ（PlayerMoney→SavePlayerMoney、預金/村資金/施設→Meta SaveData、
勇者→SaveHeroData、ダンジョン→repository.Save、在庫→ItemModel.SaveData）。
ReactiveProperty 経由の変更なので購読中のUIには即反映される。
