# メタ進行 + 準備シーン Unityエディタ配線手順

feature/meta-preparation ブランチで実装した「メタ進行（スロット=プロフィール化）」と
「出撃準備シーン」のエディタ手作業手順。
**未配線でもコンパイル・起動する**: 準備シーンのUIが未配線の間は従来通り素通りして TomsShop へ遷移し、
メタ通貨の獲得・保存だけが有効になる。

## 仕様の要点

- **スロット=プロフィール化**: 1スロット = メタ進行（metaData.json）+ 進行中のラン(0〜1個)。
  ラン終了（クリア/破産）でラン内セーブは消えるが、metaData.json は残る。
  スロット削除はプロフィールごと削除。「続きから」の可否は従来通り tomsData.json の有無で判定。
- **メタ通貨「信用」**: ラン終了時に獲得（クリア: floor(純資産/5,000) + ランクボーナス(S200/A120/B70/C40/D20)
  + 到達ターン×2。破産: ターン×2のみ＝無駄なランを無くす）。
- **準備シーン**（新規ラン時のみ経由。続きからはスキップ）:
  - 借入: メタ通貨で解放した借入枠（0/5,000/10,000/20,000G）の範囲で借りる。
    借入額は初期資金に加算され、**初回返済に利息+50%付きで上乗せ**される（借入レバレッジ）
  - 持ち込み: requiredLevel==1 のアイテムを合計2個まで初期在庫に（枠は将来メタ拡張）
  - スターターレリック: 呪い以外の Common レリックから1個（無料）
  - スタートダッシュ（メタ通貨消費）: 宣伝ビラ（注目+20/フォロワー+100）・
    目利きの手引き（全需要+15%）・返済猶予証（初回返済-30%）
- 数値は全て `GameConstData.preparation`（リモート配信対象）。
- 受け渡しは新規SO `RunSetupData`（SceneData規約）。アセット未作成でも static フォールバックで動作する。

## 1. アセット作成

1. Tools > TomsLands > データ生成 > SceneDataアセット生成 を実行（`RunSetupData.asset` が追加生成される）。
2. 生成された `Assets/Resources/SceneData/RunSetupData.asset` を Addressables に登録し、
   アドレスを **`SceneData/RunSetupData`** にする（他の SceneData と同じ）。

## 2. 準備シーンのUI構築（PreparationScene.unity）

既存の PreparationScene には Canvas / PreparationPanel / LifeTimeScope が既にある。

1. 汎用選択スロットのプレハブ `PreparationChoiceSlot.prefab` を作成
   （`ItemSelectionSlot.prefab` 複製ベース。ルートに `PreparationChoiceSlot`:
   icon / name / count / selectButton(本体) / minusButton / highlight）。
2. PreparationPanel 配下に以下を作り、`PreparationView` コンポーネント（Panelのルート等に付与）に割り当てる:
   - ヘッダー: MetaCurrencyText（信用表示）/ DifficultyText / MessageText
   - 借入セクション: BorrowAmountText / +ボタン / -ボタン（1,000G刻み）/ CreditLineText /
     枠拡張ボタン + コストText
   - 持ち込みセクション: ScrollView の Content → **Carry Catalog Parent**、
     Choice Slot Prefab、CarryCounterText（「持ち込み 1/2」）
   - スターターレリックセクション: ScrollView の Content → **Relic Catalog Parent**
   - スタートダッシュ: ボタン×3 + ラベルText×3 + チェックマークGameObject×3
   - **出撃ボタン（必須。これが未配線の間はシーンが素通りになる）** / 戻るボタン
3. `PreparationLifetimeScope` の Inspector → **Preparation View** が割り当て済みか確認。

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
