# メタ進行 + 準備シーン Unityエディタ配線手順

feature/meta-preparation ブランチで実装した「メタ進行（スロット=プロフィール化）」と
「出撃準備シーン」のエディタ手作業手順。
**未配線でもコンパイル・起動する**: 準備シーンのUIが未配線の間は従来通り素通りして TomsShop へ遷移し、
メタ通貨の獲得・保存だけが有効になる。

> **2026-08 更新（難易度の移設・持ち込み廃止）**
> - **難易度選択はタイトルから撤去**し、準備シーンで選ぶ（かんたん=Short/ふつう=Medium/むずかしい=Long。
>   タイトルは NewGame→保存先スロット選択に直行し、仮の既定値=ふつうを StartModeData に入れる。
>   出撃時に `StartModeData.SetFlowSelection(選択難易度, useAuto)` で確定）
> - **持ち込みアイテムは廃止**（レリックだけ持ち込む）。旧CarrySection は DifficultySection
>   （かんたん/ふつう/むずかしい の3ボタン+チェック）に置き換え済み。
>   RunSetupData.CarryItemIds/Counts と GameLifecycleHandler の持ち込み消費コードは削除

## 仕様の要点

- **スロット=プロフィール化**: 1スロット = メタ進行（metaData.json）+ 進行中のラン(0〜1個)。
  ラン終了（クリア/破産）でラン内セーブは消えるが、metaData.json は残る。
  スロット削除はプロフィールごと削除。「続きから」の可否は従来通り tomsData.json の有無で判定。
- **メタ通貨「信用」**: ラン終了時に獲得（クリア: floor(純資産/5,000) + ランクボーナス(S200/A120/B70/C40/D20)
  + 到達ターン×2。破産: ターン×2のみ＝無駄なランを無くす）。
- **準備シーン**（新規ラン時のみ経由。続きからはスキップ）:
  - 借入: メタ通貨で解放した借入枠（0/5,000/10,000/20,000G）の範囲で借りる。
    借入額は初期資金に加算され、**初回返済に利息+50%付きで上乗せ**される（借入レバレッジ）
  - 難易度: このランの難易度をここで選ぶ（タイトルでは選ばない）
  - スターターレリック: 呪い以外の Common レリックから1個（無料）
  - スタートダッシュ（村資金で購入）: 宣伝ビラ（注目+20/フォロワー+100）・
    目利きの手引き（全需要+15%）・初回減免状（最初の納付金-30%）。
    価格は 100,000 / 150,000 / 200,000G で、序盤は買えない前提（村資金が貯まってからの選択肢）
- 数値は全て `GameConstData.preparation`（リモート配信対象）。
- 受け渡しは新規SO `RunSetupData`（SceneData規約）。アセット未作成でも static フォールバックで動作する。

## 1. アセット作成

1. `Assets/Resources_moved/SceneData/RunSetupData.asset` はコミット済みで、
   アドレス **`SceneData/RunSetupData`** で Addressables 登録済み（生成ツールは役目を終えたため削除済み）。

## 2. 準備シーンのUI構築（PreparationScene.unity）

既存の PreparationScene には Canvas / PreparationPanel / LifeTimeScope が既にある。

1. 汎用選択スロットのプレハブ `PreparationChoiceSlot.prefab` を作成
   （`ItemSelectionSlot.prefab` 複製ベース。ルートに `PreparationChoiceSlot`:
   icon / name / count / **infoText（効果表示行）** / selectButton(本体) / minusButton / highlight）。
   ※ infoText には持ち込みアイテム=「種別・属性・基準価格・配当」、レリック=説明文が入る（配線済み）。
2. PreparationPanel 配下に以下を作り、`PreparationView` コンポーネント（Panelのルート等に付与）に割り当てる:
   - ヘッダー: MetaCurrencyText（信用表示）/ DifficultyText / MessageText
   - 借入セクション: BorrowAmountText / +ボタン / -ボタン（1,000G刻み）/ CreditLineText /
     枠拡張ボタン + コストText
   - 難易度セクション: Easy/Normal/Hard Button ×3 + 各チェックマークGameObject（配線済み）
   - スターターレリックセクション: ScrollView の Content → **Relic Catalog Parent**
   - スタートダッシュ: ボタン×3 + ラベルText×3 + チェックマークGameObject×3
   - **出撃ボタン（必須。これが未配線の間はシーンが素通りになる）** / 戻るボタン
3. `PreparationLifetimeScope` の Inspector → **Preparation View** が割り当て済みか確認。

## 2-1. レイアウトC（2026-10 作り直し）で追加した任意参照

PreparationScene はモックアップC（`Docs/mockups/prep_C_summary.png`、実装キャプチャ `prep_C_impl.png`）の
配置で組み直し済み（左: 難易度・スターターレリック・スタートダッシュ / 右: 持ち込み資金・メッセージ・出店/戻る）。
以下の `PreparationView` フィールドは**すべて任意**で、未配線なら従来の表示・挙動のまま動く。シーンでは全部配線済み。

| フィールド | 配線するもの | 配線時の挙動（未配線時） |
|---|---|---|
| `villageFundsLabelText` | 「村資金」の固定ラベル（金額の上に置く） | `metaCurrencyText` に金額だけ `N,NNNG` を出す（「村資金 N G」の1行） |
| `carryLabelText` | 「持ち込み」の固定ラベル（金額の横に置く） | `borrowAmountText` に金額だけを出す（「持ち込み N G」の1行） |
| `carrySlider` | 持ち込み額の Slider（±ボタンの下） | 0〜持ち込み可能額を 1,000G 刻みで操作。±ボタン・再描画時は `SetValueWithoutNotify` で同期、上限0なら操作不可（±ボタンのみ） |
| `difficultySelectedSprite` / `difficultyUnselectedSprite` | `新聞/4−0購読する`（金）/ `新聞/4−1購読する`（茶） | 選択に応じて難易度ボタンの画像を切り替える。**両方**配線時のみ有効（画像は変えない） |
| `relicScroll` / `relicPrevButton` / `relicNextButton` | レリック一覧の ScrollRect と ◀ ▶ ボタン | ◀ ▶ で横スクロール（スクロールのみ） |
| `flyerPriceText` / `appraisalPriceText` / `gracePriceText` | スタートダッシュ各カードの価格札 TMP | 名前と価格 `N,NNNG` を分けて表示（名前ラベルに「名前（N,NNNG）」の1行） |

- 持ち込みの丸め・クランプは `PreparationModel.SetCarry(amount, carryMax)`（2026-10 から 10,000G 刻み。上限が刻みの倍数でなくても上限ちょうどまで可）。
  上限は村の銀行レベルで 5万 / 10万 / 20万 / 40万G（`preparation.bankCarryLimits`）。持ち込みはクリアすると村資金へ戻るが、破産すると戻らない。
- 難易度ボタンの文字の下に「配信N回・A〜B日」を出す（`PreparationView.SetDifficultyDetails`。日数はモード定義の dungeonCount × (店の日 min〜max + 配信の日1)）。
- スターターレリックの候補: 呪い以外の Common。村の冒険者ギルド Lv1 以上なら、ギルドで解禁済みのレアも並ぶ。
  Presenter が `OnCarrySliderChanged` を購読して `RefreshAll` する。
- スタートダッシュの価格は `GameConstSettings.asset` の `preparation.flyerCost / appraisalCost / graceCost`
  （2026-10 から 300,000 / 500,000 / 700,000G。村の施設の Lv1 と取り合う値段）。Presenter が名前と価格を分けて `UpdateStartDash` に渡す。
- 画面を開くときに `UIFx.PanelOpen`、レリック・難易度などの選択時に `Pop` を入れている。
- 使っている素材: 背景・スタートダッシュのアイコン・チェックバッジ・金の選択枠は `Assets/UI/Preparation/`。
  ボタン・枠は既存素材（追加/文字なし0−0、新聞/4−0・4−1購読する・8紙面ベース・9題字枠、鍛冶屋・道具屋/6枠・7・10・11、
  基本/3ゴールド）、スライダーは鍛冶屋と同じスプライト。
- `PreparationChoiceSlot.prefab` は 196x274 の縦長カードに変更済み（このシーンでのみ使用）。
- PreparationScene を直接 Play すると BootScene に戻される。見た目の確認は Boot 後に準備シーンへ遷移させて行う。

## 3. タイトルのスロット表示（任意）

`SaveSlotInfo` に HasProfile / MetaCurrency / TotalRuns / BestRank を追加済み。
`SaveSlotView` に「周回数◯ / 信用◯ / ベスト◯」の2行目表示を足す場合は
`Bind(info)` を拡張する（未対応でも動作に影響なし）。

## 4. 動作確認チェックリスト

1. ニューゲーム → 準備シーンが開く（UI未配線なら素通り）。戻るでタイトルへ。
2. 借入 5,000G で出撃 → 初期資金 15,000G。Turn10 の返済額が 5,000 + 5,000×1.5 = 12,500G になる
   （ホームの次回返済表示も同額）。
3. 持ち込みアイテムが初期在庫に入っている。スターターレリックを所持している。
4. スタートダッシュ: 宣伝ビラ→開始時ステータス加算 / 目利き→初期需要が高め / 猶予証→初回返済-30%。
   信用が足りないと出撃時に弾かれる。
5. ランをクリア → リザルトのボタンで信用が加算され（Console）、スロットからランが消えるが
   次のニューゲーム時の準備シーンで信用が残っている。
6. 破産 → ターン×2 の信用だけ獲得。
7. スロット削除 → メタ進行ごと消える。
8. 準備シーンで信用を使って借入枠を拡張 → 次のランでも枠が維持される。

## 備考（今回スコープ外・今後の拡張）

- リザルト/ゲームオーバーの「もう一度」は現状タイトル経由（準備シーン直行は将来対応）。
- 持ち込み枠数・上位アイテム解禁・恒久パークのメタ通貨購入は器のみ（baseCarrySlots 等の設定値）。
- 配信（FightScene）側へのレリック効果適用は未対応。
