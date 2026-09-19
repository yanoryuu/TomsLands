# 市場シミュレーション（Jev / ローカルABM）

価格変動を「乱数で直接決める」方式から、「仮想トレーダーの注文フローの結果として決まる」
エージェントベースモデル（ABM）へ置き換えるための検証基盤。

**現状: 検証段階。既存の `ItemModel.ApplyShopTurnEconomy` には未接続。**

---

## 1. なぜやるのか

現行の価格変動（`ItemModel.ApplyShopTurnEconomy`）は、需要帯で区分された一様乱数：

```csharp
float s1Rate = Random.Range(s1Min, s1Max);   // 需要帯ごとのレンジ内で一様
```

安定していて調整しやすい反面、実際の株価が持つ性質が欠けている。

| 性質 | 説明 | 指標 |
|---|---|---|
| ファットテール | たまに大暴騰・大暴落が起きる | 尖度（正規分布=3、実市場=5〜10） |
| ボラティリティ・クラスタリング | 荒れる時期と凪の時期が交互に来る | \|リターン\|のラグ1自己相関（実市場=0.1〜0.3） |
| 予測不能性 | 単純な外挿では読めない | リターンのラグ1自己相関（実市場≒0） |

仕入れ画面をトレード端末風に作り込んでいる以上、チャートが「ただのノイズ」に
見えるのはもったいない。ここを作り込む。

---

## 2. 構成

### ランタイム（出荷対象・外部API不要）

| ファイル | 役割 |
|---|---|
| `Assets/Scripts/TomsShop/Market/OrderFlowPriceEngine.cs` | 純注文 → 価格変動率。平方根マーケットインパクト |
| `Assets/Scripts/TomsShop/Market/LocalAbm.cs` | ローカルABM本体。5種のトレーダーが売買する |
| `Assets/Scripts/TomsShop/Market/MarketStatistics.cs` | 尖度・自己相関・最大DDの計測器 |
| `Assets/Scripts/TomsShop/Market/MarketModelPreset.cs` | キャリブレーション済みパラメータの保存先（SO） |

### Editor 専用（開発時のみ・ビルドに含まれない）

| ファイル | 役割 |
|---|---|
| `Assets/Scripts/Editor/Market/JevApi.cs` | TypeSafe Jev の HTTP クライアント |
| `Assets/Scripts/Editor/Market/JevTraderRoster.cs` | ペルソナ定義・state構築・注文フロー変換 |
| `Assets/Scripts/Editor/Market/JevReferenceRun.cs` | Jev で「お手本」価格系列を生成するランナー |
| `Assets/Scripts/Editor/Market/AbmCalibrator.cs` | お手本の統計量へ ABM を合わせ込む山登り探索 |
| `Assets/Scripts/Editor/Market/JevMarketSimulator.cs` | 比較・キャリブレーション用 EditorWindow |

起動: `Tools/Market/Jev Market Simulator`

---

## 3. 価格の決まり方

```
各トレーダー i について:
  size_i = capital_i × stance_i × confidence_i      // stance は -1（全力売り）〜 +1（全力買い）

銘柄 j への純注文:
  netOrder_j = Σ_i  size_i × P_i(j)                 // P_i(j) は銘柄選択の確率分布
  netOrder_j /= √N                                  // 人数でボラ水準が変わらないよう正規化

価格インパクト（Kyle 型・平方根）:
  Δlog(price_j) = λ × sign(netOrder_j) × √(|netOrder_j| / depth_j)
  depth_j = baseDepth × (1 + stock) × max(0.1, demand)
```

戻り値は既存の `s1Rate` と同じ意味（1.0 = 据え置き）なので、
既存のストップ高／ストップ安クランプにそのまま接続できる。

**重要:** Jev の銘柄選択は `argmax` ではなく**確率分布全体**を資金配分として使う。
argmax を使うと値動きが階段状になり、30人でも滑らかにならない。

---

## 4. Jev（TypeSafe System One Model）の使い方

### API 仕様

- `POST https://api.typesafe.ai/v1/systemone` / `Authorization: Bearer` / model `jev-latest`
- 質問型: **Choice**(最大255択・確率分布+confidence) / **Score**(2〜10段階・小数) / **Noul**(0〜1・confidence無し)
- 入力 $0.042 / 1M tokens、**出力無料**
- 64k tokens/req、250k tokens/秒、1200 req/分
- **同一 state に対する複数質問は1リクエストで並列評価**される（コスト約1/12、レイテンシほぼ不変）
- **英語が主言語**。state / instructions / criteria はすべて英語で書くこと

### 1リクエストの構造

state に市場全体のスナップショットを置き、questions にトレーダー1人あたり2問を並べる。

- `{id}_pick` … Choice。「どの銘柄が最も確信度の高いポジションか」→ 確率分布＝資金配分
- `{id}_stance` … Score 5段階。「今ターンのポジションの強さ」→ -1〜+1 の売買方向

30人 = 60問で1リクエスト。**実測 323ms / 約12.5k tokens / 約 $0.0005 per ターン。**

### APIキー

**環境変数 `TYPESAFE_API_KEY` のみ**。ファイル読み込みのフォールバックは意図的に実装していない
（会話ログ・Git への漏洩経路を作らないため）。

- 登録は GUI 推奨（`Win+R` → `sysdm.cpl` → 詳細設定 → 環境変数 → ユーザー環境変数）
  PowerShell だと `ConsoleHost_history.txt` にキーが平文で残る
- **Unity Hub から再起動すること。** Unity だけ再起動しても Hub の古い環境を継承して反映されない

---

## 5. 実測結果（2026-09-19）

### ペルソナ比率の探索（Jev・40ターン×8銘柄・λ=0.12・n=320）

| 構成 | Momentum / Contrarian / Demand / MM / Noise | kurt | \|r\|acf1 | racf1 |
|---|---|---|---|---|
| A 既定 | .27 / .20 / .13 / .13 / .27 | 4.33 | 0.571 | 0.427 |
| **B 逆張り+MM厚** | **.15 / .30 / .10 / .25 / .20** | **4.86** | **0.566** | **0.407** |
| C MM支配 | .05 / .35 / .05 / .45 / .10 | 4.46 | 0.427 | −0.066 |
| D 順張りゼロ | .00 / .30 / .10 / .30 / .30 | 4.18 | 0.407 | 0.271 |

構成 B を採用。C は racf1 ≒ 0 で実市場としては最も正確だが、それは
「チャートを読んでも報われない」ことを意味する。相場を読む腕が成立することを
優先し、racf1 が残る B を選んだ。

λ は 0.12。上げると σ は上がるが**尖度は下がる**（価格が上下限に張り付いてテールが切られる）。

### お手本（Jev 参照ラン）

構成 B・λ=0.12・60ターン×8銘柄×4シード、**n=1920**。
平均: **σ=0.0180 kurt=3.95 \|r\|acf1=0.618 racf1=0.450**

### 採用モデルの決定

**目標をそのまま合わせた設定（trendy）はゲームとして破綻していた。**
統計量は最もお手本に近かったが、実際の値動きは40ターン級の一方通行で、
8銘柄中5銘柄がストップ高／安に到達してそこで凍結した。
racf1 0.575 の正体は「序盤の向きを見れば残りが全部わかる相場」だった。

racf1 の目標のみ 0.15 へ下げて再探索した moderate は 60ターンでは無傷。
しかし**ホライズンを伸ばすと破綻する**ことが判明した。

| 張り付き銘柄数（8銘柄×3シード=24） | 60T | 120T | 200T |
|---|---|---|---|
| moderate | 0 / 24 | **15 / 24** | **16 / 24** |
| **anchored（採用）** | **0 / 24** | **0 / 24** | **0 / 24** |

原因は **valueGain（逆張り勢の基準価格アンカー）が最適化に切り捨てられていた**こと
（moderate で 0.15）。価格を基準値へ引き戻す力が無いため、長期では上下限まで走る。
`AbmSearchBounds` で valueGain に下限 2.0 を課し、最大DDに片側ペナルティを掛けて
再探索したものが **anchored**（valueGain 11.31）。

### 最終比較（120ターン×8銘柄・同一シード・n=960）

| | σ | kurt | \|r\|acf1 | racf1 | maxDD | 上下限到達 |
|---|---|---|---|---|---|---|
| Legacy（現行） | 0.0070 | 2.35 | 0.237 | 0.183 | 5.4% | 0 / 8 |
| trendy | 0.0232 | 1.73 | 0.496 | 0.820 | 66.7% | 5 / 8 |
| moderate | 0.0173 | 1.82 | 0.134 | 0.185 | 30.9% | 4 / 8 |
| **anchored（採用）** | **0.0142** | **1.99** | **0.100** | **0.069** | **13.0%** | **0 / 8** |

結果は `Assets/Resources_moved/MarketModelPreset.asset` に保存済み。
基準価格の周りを ±12% で往復し、上下限には一度も触れない。

### わかったこと

- **統計量が合っていても値動きが破綻することがある。** 必ずチャートを目視すること
- **評価ホライズンを実際のプレイ長に合わせる。** 60ターンでは moderate の欠陥が見えなかった
- **valueGain に下限を課す。** 課さないと最適化がアンカーを切り捨て、長期で上下限へ走る
- **λ を上げると σ は上がるが尖度は下がる**（上下限でテールが切られるため）
- **不感帯（`inactionBandMax`）が尖度の主ツマミ。** 参加人数の揺らぎがテールを作る
- **人数を増やしても良くならない。** 独立な判断を平均するほど分布は正規分布へ寄る。
  Jev は質問同士が互いの答えを見られない仕様なので、人数増＝独立サンプルの平均そのもの

---

## 6. 残っている課題

> **2026-09-20 更新**: 以下の課題のうち「需要が価格の方向に効かない」問題は v2 設計で解消する。
> 設計: `Docs/Market_Price_v2_Design.md`（2層モデル: 需要から決まる適正値 × 相場の揺らぎ）。


1. **尖度 1.99 は目標 3.95 の半分。**「たまに大暴落」の演出が出ていない
   - 逆張りアンカーを効かせるほど価格が基準へ引き戻され、テールが細くなるトレードオフ
   - 対策案: 戦略スイッチング（成績に応じて順張り↔逆張りを切り替える）の導入
2. **振れ幅 ±12% が売買の妙味として足りるかは要プレイ確認**
3. **`ItemModel` への接続が未実装**。`IShopPriceEngine` を挟み、Legacy を既定に保つ方針

---

## 7. 手順

### お手本を作る
1. `Tools/Market/Jev Market Simulator` を開く
2. 「Jev」にチェック、ターン数 200・銘柄数 8 程度に設定して実行
3. 「直近の Jev 結果を目標値に設定」で `MarketModelPreset` へ取り込む

### ABM を合わせ込む
4. 「プリセットの目標値へ ABM を合わせ込む」を実行（試行回数 400 程度）
5. 結果は `MarketModelPreset` に保存される（目標値・達成値・Loss・実行日時）

### 出荷
6. `MarketModelPreset` をランタイムから読む。Jev 関連コードは Editor アセンブリなので
   ビルドには一切含まれない

---

## 8. 注意点

- **`JevReferenceRun.ExecuteAsync` は `ConfigureAwait(false)` 必須。**
  これが無いと継続がメインスレッドへ戻ろうとし、同期待ちした瞬間に Unity がデッドロックする
  （実際に一度ハングさせた）
- `JevMarketSimulator.RunJevAsync` は逆に**メインスレッドに留まる必要がある**（`Repaint()` を呼ぶため）。
  こちらを同期待ちしてはいけない
- シミュレーションの CSV 出力は `<プロジェクトルート>/MarketSim/` へ。
  `Assets/` の外に置くことで Unity にインポートさせず、ビルドにも混ぜない
- 需要（Demand / Trend）の更新式は3つのエンジンで完全に同一。
  差分が「価格の決まり方」だけに限定されるようにしてある。ここを変えるときは3箇所とも直すこと
  （`JevMarketSimulator` / `JevReferenceRun` / `AbmCalibrator`）
