# GameConst リモートコンフィグ 仕様書（ドラフト）

最終更新: 2026-06-17 / ステータス: **検討用ドラフト**（サーバー通信層は未着手）

ゲーム調整値（`GameConst`）を **SpreadSheet で編集 → サーバー配信 → ゲームが差分検知してダウンロード** できるようにするための仕様。
本書はサーバー通信フェーズを別途設計・相談するための土台。実装済みの基盤と、これから決める論点を分けて記載する。

---

## 1. 目的・ゴール

- 非エンジニアが **SpreadSheet** でゲームバランス値を編集できる。
- アプリ更新（ストア審査・再ビルド）なしで値を反映できる。
- 通信失敗・未配信でもゲームが必ず起動する（ベイク済みデフォルトにフォールバック）。

### スコープ
- 対象データ: `GameConstData`（借金・所持金・経験値・各種上限・鍛冶屋コスト等）。
- 将来的に他のマスタ（`ItemData` 等）へ同方式を横展開できる設計にする（が、初版は GameConst のみ）。

---

## 2. 全体フロー

```
[編集者] SpreadSheet で値を編集
     │  (1) エクスポート/変換
     ▼
[変換]   SpreadSheet → JSON 化（GameConstData 形式）＋ version 付与
     │  (2) アップロード
     ▼
[配信]   サーバー / ストレージ / CDN に JSON を配置
     │  (3) 起動時 or 任意のタイミングで取得
     ▼
[ゲーム] version をチェック → 差分あればダウンロード → GameConst.OverrideFromJson() で適用
     │       差分なし or 失敗 → ローカルキャッシュ or ベイク済みデフォルトを使用
     ▼
[反映]   GameConst.XXX 経由で全システムが新しい値を参照
```

---

## 3. 現状（実装済みの基盤）

サーバー層から繋ぐための「受け口」は既に用意済み。

| 要素 | 場所 | 役割 |
|---|---|---|
| `GameConstData` | `Assets/Scripts/Config/GameConstData.cs` | `[Serializable]`。`JsonUtility` でJSON相互変換可能なデータ本体 |
| `GameConstSettings` | `Assets/Scripts/Config/GameConstSettings.cs` | Inspector編集用 ScriptableObject（ベイク済みデフォルト） |
| `GameConst.OverrideFromJson(json)` | `Assets/Scripts/GameConst.cs` | **配信JSONを適用する差し込み口**（最優先される） |
| `GameConst.Override(data)` | 同上 | オブジェクトで直接上書き |
| `GameConst.ToJson()` | 同上 | 現在値のJSON化（差分比較・アップロード用） |
| JSON Import/Export | `Assets/Scripts/Editor/GameConstSettingsTool.cs` | SpreadSheet⇔アセットの手動橋渡し（暫定） |

値の供給優先順位（実装済み）: **Override(配信) > Settingsアセット > 既定値**。

> サーバー通信層を追加する場合、起動シーケンスで「JSON取得 → `GameConst.OverrideFromJson()`」を**`GameConst` の初回アクセスより前**に呼べばよい。

---

## 4. データ仕様

### 4.1 JSON フォーマット（`GameConstData`）
`JsonUtility.ToJson` 互換。フィールド名はC#のフィールドと一致させる必要がある。

```json
{
  "maxDungeonLevel": 5,
  "maxBlackSmithLevel": 5,
  "maxToolShopLevel": 5,
  "maxInfoBrokerLevel": 5,
  "maxItemStock": 99,
  "minItemStock": 0,
  "initMoney": 10000,
  "debtPaymentInterval": 10,
  "debtBaseAmount": 5000,
  "debtMultiplier": 1.8,
  "heroExpPerMob": 10,
  "heroExpPerBoss": 100,
  "heroBaseExpToNextLevel": 100,
  "blackSmithLevelUpCosts": [0, 3000, 6000, 12000, 20000]
}
```

### 4.2 配信エンベロープ（提案・要決定）
差分検知・互換性管理のため、データ本体をメタ情報で包むことを推奨。

```json
{
  "version": 12,                 // 単調増加 or ハッシュ
  "schemaVersion": 1,            // データ構造の互換性管理
  "updatedAt": "2026-06-17T09:00:00Z",
  "data": { /* 上記 GameConstData */ }
}
```
> ※ `JsonUtility` はネスト/配列は扱えるがトップレベルがオブジェクトである必要あり。エンベロープ採用時は対応するラッパクラスを用意する。

### 4.3 SpreadSheet 列設計（提案）
| key | value | type | 備考 |
|---|---|---|---|
| initMoney | 10000 | int | |
| debtMultiplier | 1.8 | float | |
| blackSmithLevelUpCosts | 0,3000,6000,... | csv | 配列はカンマ区切り等のルールを決める |

key-value 縦持ちが変換しやすい。配列・ネストの表現ルールは要決定（→ §9）。

---

## 5. サーバー / 配信層（選択肢）

通信方法は別途相談だが、候補を提示。

| 方式 | 概要 | 長所 | 短所 |
|---|---|---|---|
| **A. 静的JSON + CDN/Storage** | GCS/S3/Firebase Hosting等にJSON配置、HTTP GET | 最小構成・低コスト・キャッシュ容易 | 認証・動的制御は弱い |
| **B. Firebase Remote Config** | Googleの設定配信SaaS | 差分配信・A/Bテスト・条件配信が標準装備 | 1パラメータ容量制限・GameConst全体を1キーに詰める工夫が要る |
| **C. 専用APIサーバー** | 自前API（REST） | 柔軟・認証・ログ | 構築運用コスト大 |
| **D. Addressables リモートカタログ** | 既存Addressables配信機構でSO自体を更新 | 既存移行と統合・他アセットと一括 | バランス値のたびにバンドルビルドが必要で重い |

SpreadSheet→サーバーの変換は **GAS（Google Apps Script）** または CI で JSON 生成→アップロードが定番。

推奨初版: **A（静的JSON）+ GAS変換**。シンプルで「SpreadSheet編集→反映」が最短。将来Bへ移行余地あり。

---

## 6. 差分検知・バージョニング

- サーバーに **軽量な version エンドポイント**（または同一JSON内の`version`）を置く。
- ゲームは起動時に version のみ取得 → ローカル保存の version と比較。
  - 同一 → ダウンロードスキップ（キャッシュ使用）。
  - 差分 → 本体JSONをダウンロード→キャッシュ更新→適用。
- version 方式の候補: 単調増加整数 / コンテンツハッシュ(ETag) / updatedAt。
- HTTP の `ETag` / `If-None-Match`、`Cache-Control` を使えばサーバー側versionAPI無しでも差分取得可能（方式Aと相性良）。

---

## 7. ゲーム側 通信層 設計（提案クラス構成）

```
IRemoteConfigSource          // 取得の抽象（HTTP / Firebase / Mock を差し替え可能に）
  └ HttpRemoteConfigSource   // UnityWebRequest or UniTask で JSON 取得
RemoteConfigCache            // PersistentDataPath にJSON+versionを保存/読込
RemoteConfigService          // フロー制御: version比較→取得→キャッシュ→GameConst.OverrideFromJson
RemoteConfigBootstrap        // 起動シーケンスでServiceを駆動（GameConst初回アクセス前）
```

- 非同期は既存依存の **UniTask** を使用（`com.cysharp.unitask` 導入済み）。
- DIは **VContainer**（導入済み）で `RemoteConfigService` を登録。
- 取得タイミング: **起動スプラッシュ/タイトル**で完了させ、ゲーム本編開始前に適用（途中差し替えによる不整合を避ける）。
- 適用は `GameConst.OverrideFromJson(json)` を呼ぶだけ。

### フォールバック順序（堅牢性）
1. サーバー取得成功 → 適用
2. 失敗 → ローカルキャッシュ（前回成功分）を適用
3. キャッシュ無し → ベイク済み `GameConstSettings`（=ビルド同梱）

---

## 8. エラーハンドリング / 運用

- タイムアウト・オフライン・不正JSON時は**必ずフォールバック**し、起動を止めない。
- `schemaVersion` 不一致時の方針（無視 / 既知フィールドのみ適用 / デフォルト）を決める。
- 不正値ガード: 受信後に範囲バリデーション（負の所持金・0除算になる倍率等を弾く）。
- ログ/計測: どのversionが適用されたかを記録（不具合解析・QA用）。

---

## 9. 未決論点（次回相談したいこと）

1. **配信方式**: A(静的JSON) / B(Firebase Remote Config) / C(自前API) / D(Addressables) のどれを採るか。
2. **認証の要否**: 公開JSONで良いか、署名/トークンが要るか（改ざん・盗用対策）。
3. **version方式**: 整数 / ハッシュ(ETag) / updatedAt。
4. **取得タイミングと頻度**: 起動時のみ / 一定間隔 / 手動リロード。プレイ中の途中適用を許すか。
5. **SpreadSheet→JSON変換手段**: GAS / CIスクリプト / 手動Export（暫定の現状）。
6. **SpreadSheetでの配列・ネスト表現ルール**（`blackSmithLevelUpCosts` 等）。
7. **エンベロープ採用可否**（version同梱 or 別エンドポイント）。
8. **対象範囲の拡張**: GameConst以外（ItemData等）も同方式に乗せるか。
9. **環境分け**: dev/staging/production のJSONをどう分離するか。

---

## 10. 段階的実装ステップ（想定）

1. （済）`GameConstData` / `GameConstSettings` / `GameConst`ファサード / JSON入出力。
2. SpreadSheet → JSON 変換手段の確立（GAS等）＋配信先決定。
3. `IRemoteConfigSource` + `HttpRemoteConfigSource`（取得）実装。
4. `RemoteConfigCache`（永続キャッシュ）実装。
5. `RemoteConfigService`（version比較→取得→適用）実装 + VContainer登録。
6. 起動シーケンスへ組み込み（GameConst初回アクセス前に適用）。
7. バリデーション・フォールバック・ログ整備。
8. 環境分け・運用フロー整備。

---

## 参考: 既存技術スタック（流用可能）
- 非同期: **UniTask**（導入済み）
- DI: **VContainer**（導入済み）
- アセット配信: **Addressables 2.2.2**（導入済み・移行中）
- JSON: `JsonUtility`（標準）/ 必要なら `com.unity.nuget.newtonsoft-json`（導入済み、ネスト・dictionaryに強い）
```
