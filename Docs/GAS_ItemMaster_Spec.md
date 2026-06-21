# GAS実装仕様書：アイテムマスター配信（items.json）

対象: Google Apps Script（既存の GameConst 配信GASに**追記**する形）を実装するAI向け。
最終更新: 2026-06-19

---

## 0. 背景とゴール

既存で「スプレッドシート → GAS → Firebase Storage」で **`gameconst.json`** を配信する仕組みが動いている。
今回はそれに **アイテムマスターの上書き配信 `items.json`** を追加する。

- **同じスプレッドシート内に新しいシートを2枚追加**して記入するスタイル。
  - `items` … アイテム1件＝1行のデータ本体
  - `items_meta` … 配信メタ情報（version / schemaVersion）
- **既存と同じバケット**にアップロードする。出力先だけ `gameconst.json` → `items.json` に変える。

### 配信先URL（既存と同一バケット・パスだけ変更）
- 既存: `https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/gameconst.json`
- 追加: `https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/items.json`
  - バケット名: `tokotomland.firebasestorage.app`
  - オブジェクトパス: `config/production/items.json`
  - 認証・アップロード手段・公開設定・`Cache-Control` は **既存の gameconst アップロード処理をそのまま再利用**（出力パスと中身だけ差し替え）。

---

## 1. 入力シート仕様

### シート `items`（アイテム本体・1行=1アイテム）
- シート名は正確に `items`（小文字）。
- 1行目はヘッダ。2行目以降がデータ。
- 列順・列名（ヘッダ）は以下の通り：

| 列 | ヘッダ | 型 | 必須 | 説明 |
|---|---|---|---|---|
| A | itemId | string | ✅ | アイテムID（UnityのItemData.itemIdと一致） |
| B | itemName | string |  | 空ならゲーム側のデフォルト名を維持 |
| C | basePrice | int | ✅ | 基準価格（0以上） |
| D | initialStock | int | ✅ | 初期在庫（0以上） |
| E | maxStock | int | ✅ | 最大在庫（1以上） |
| F | initialDisplayStock | int | ✅ | 初期陳列数（0以上） |
| G | itemType | string | ✅ | `Weapon` / `Armor` / `Tool` のいずれか |
| H | itemAttribute | string | ✅ | `Fire` / `Water` / `Earth` / `Wind` / `Light` / `Dark` のいずれか |
| I | requiredLevel | int | ✅ | 必要鍛冶レベル（0以上） |
| J | salesRate | float | ✅ | 売れやすさ倍率（0.1〜5.0） |
| K | description | string |  | 空ならデフォルト説明を維持 |

- **itemId が空の行はスキップ**（末尾の空行対策）。

### シート `items_meta`（配信メタ・key/value）
- シート名は正確に `items_meta`。A列=key、B列=value。1行目ヘッダ。

| | A (key) | B (value) |
|---|---|---|
| 1 | key | value |
| 2 | version | 1 |
| 3 | schemaVersion | 1 |

- `version`: 配信のたびに +1 する（Unity側の差分検知・ログ基準）。
- `schemaVersion`: **必ず 1**（Unityの`ItemMaster.ExpectedSchemaVersion=1`と一致しないと適用されない）。

---

## 2. 出力JSON仕様（厳守：Unityの`JsonUtility`がフィールド名で解釈する）

```json
{
  "version": 1,
  "schemaVersion": 1,
  "updatedAt": "2026-06-19T00:00:00.000Z",
  "items": [
    {
      "itemId": "sword_01",
      "itemName": "炎の剣",
      "basePrice": 500,
      "initialStock": 5,
      "maxStock": 99,
      "initialDisplayStock": 2,
      "itemType": "Weapon",
      "itemAttribute": "Fire",
      "requiredLevel": 1,
      "salesRate": 1.2,
      "description": "よく燃える剣。"
    }
  ]
}
```

### 型・整形ルール（重要）
- **フィールド名は上記と完全一致**（camelCase）。綴り違いは反映されない。
- **数値は必ずJSONの数値型**で出力する（文字列にしない）。
  - `basePrice / initialStock / maxStock / initialDisplayStock / requiredLevel` → 整数
  - `salesRate` → 小数可（例 `1`, `1.2`）
  - `version / schemaVersion` → 整数
  - GASでは `Number(cell)` / `parseInt` / `parseFloat` で数値化してから格納する。`"500"` のような文字列で出すと Unity 側で 0 になる。
- `itemType` / `itemAttribute` は **文字列**。前後空白は `trim()`。値は許可セット（上記）に限定。
- `itemName` / `description` が空欄なら **空文字 `""`** を出力（Unity側で「デフォルト維持」と解釈される）。
- `items` は要素1件でも**必ず配列**。
- `updatedAt` は `new Date().toISOString()`。
- 出力は1つのJSONオブジェクト（エンベロープ）。`Content-Type: application/json`。

---

## 3. バリデーション（GAS側で弾く＝Unityに不正を送らない）

アップロード前に各行を検査し、1件でも不正なら **アップロードを中止してエラー表示**（Unity側も不正payloadは全体不採用になるため、GASで止める方が安全）。

- `itemId` 非空
- `basePrice >= 0`、`maxStock >= 1`、`initialStock >= 0`、`initialDisplayStock >= 0`、`requiredLevel >= 0`
- `0.1 <= salesRate <= 5.0`
- `itemType ∈ {Weapon, Armor, Tool}`
- `itemAttribute ∈ {Fire, Water, Earth, Wind, Light, Dark}`
- `schemaVersion === 1`

エラー時は `Browser.msgBox` / `SpreadsheetApp.getUi().alert` で「どの行の何が不正か」を表示する。

---

## 4. GAS実装の要件

1. スプレッドシートのカスタムメニューに項目を追加（既存メニューに追記でよい）：
   例「Firebaseへアップロード（アイテム）」。
2. その関数で `items` と `items_meta` を読み、§2の形に組み立て、§3で検証し、
   **既存の gameconst アップロード関数（GCSへPUTする処理）を再利用**して
   オブジェクトパス `config/production/items.json` に上げる。
3. 既存のアップロードユーティリティが「(パス, 文字列) を受け取ってGCSに上げる」形になっていない場合は、
   gameconst側の処理を汎用化して `uploadJsonToStorage(objectPath, jsonString)` のように切り出し、
   gameconst / items の両方から呼ぶ。

### 参考コード（Apps Script・シート読み取り〜envelope生成部分）
> アップロード部分は既存処理を呼ぶ前提。ここはそのまま使える組み立てロジック。

```javascript
function buildItemsEnvelope() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();

  // --- items_meta ---
  const metaSheet = ss.getSheetByName('items_meta');
  const meta = {};
  const metaValues = metaSheet.getRange(2, 1, metaSheet.getLastRow() - 1, 2).getValues();
  metaValues.forEach(function (row) {
    const key = String(row[0]).trim();
    if (key) meta[key] = row[1];
  });
  const version = parseInt(meta['version'], 10);
  const schemaVersion = parseInt(meta['schemaVersion'], 10);

  // --- items ---
  const sheet = ss.getSheetByName('items');
  const last = sheet.getLastRow();
  const rows = last >= 2 ? sheet.getRange(2, 1, last - 1, 11).getValues() : [];

  const TYPES = ['Weapon', 'Armor', 'Tool'];
  const ATTRS = ['Fire', 'Water', 'Earth', 'Wind', 'Light', 'Dark'];
  const items = [];

  rows.forEach(function (r, i) {
    const itemId = String(r[0]).trim();
    if (!itemId) return; // 空行スキップ

    const item = {
      itemId: itemId,
      itemName: String(r[1] || '').trim(),
      basePrice: Number(r[2]),
      initialStock: Number(r[3]),
      maxStock: Number(r[4]),
      initialDisplayStock: Number(r[5]),
      itemType: String(r[6]).trim(),
      itemAttribute: String(r[7]).trim(),
      requiredLevel: Number(r[8]),
      salesRate: Number(r[9]),
      description: String(r[10] || '').trim()
    };

    // バリデーション（1件でも不正なら例外で中止）
    const line = i + 2;
    if (!(item.basePrice >= 0)) throw new Error(line + '行: basePrice 不正');
    if (!(item.maxStock >= 1)) throw new Error(line + '行: maxStock 不正');
    if (!(item.initialStock >= 0) || !(item.initialDisplayStock >= 0)) throw new Error(line + '行: stock 不正');
    if (!(item.requiredLevel >= 0)) throw new Error(line + '行: requiredLevel 不正');
    if (!(item.salesRate >= 0.1 && item.salesRate <= 5.0)) throw new Error(line + '行: salesRate 範囲外');
    if (TYPES.indexOf(item.itemType) < 0) throw new Error(line + '行: itemType 不正(' + item.itemType + ')');
    if (ATTRS.indexOf(item.itemAttribute) < 0) throw new Error(line + '行: itemAttribute 不正(' + item.itemAttribute + ')');

    items.push(item);
  });

  if (schemaVersion !== 1) throw new Error('items_meta.schemaVersion は 1 にしてください');

  return {
    version: version,
    schemaVersion: schemaVersion,
    updatedAt: new Date().toISOString(),
    items: items
  };
}

function uploadItems() {
  try {
    const envelope = buildItemsEnvelope();
    const json = JSON.stringify(envelope); // 数値は数値のまま出力される
    // ↓ 既存の gameconst アップロード処理を再利用（パスと中身だけ差し替え）
    uploadJsonToStorage('config/production/items.json', json);
    SpreadsheetApp.getUi().alert('items.json をアップロードしました（' + envelope.items.length + '件 / version ' + envelope.version + '）');
  } catch (e) {
    SpreadsheetApp.getUi().alert('アップロード失敗: ' + e.message);
  }
}
```

> `uploadJsonToStorage(objectPath, jsonString)` は既存の gameconst 用アップロード関数をそのまま流用／汎用化したもの。
> 既存処理が `Cache-Control: no-cache` を付けているなら items 側も同様にする（更新が反映されない問題の回避）。

---

## 5. 受け入れ条件（テスト）

1. `items` に1件記入 → メニューからアップロード → 公開URL
   `https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/items.json`
   をブラウザGETして §2 形式のJSONが返る。
2. 数値フィールドが**数値型**（クォート無し）で出力されている。
3. `itemType`/`itemAttribute` が許可外の値だと、アップロードが中止されエラー表示される。
4. `items_meta.version` を上げて再アップロード → URLの version が増える。
5. `schemaVersion` が 1 以外だと中止される。

---

## 6. Unity側の対応状況（実装済み・GAS担当は不要）
- 取得・適用・キャッシュ・フォールバックはUnity側実装済み（`ItemMaster` / `ItemMasterService` / `BootLifetimeScope`）。
- `ItemMaster.ExpectedSchemaVersion = 1`。
- itemName/description が空文字なら既存デフォルトを維持。
- シートに無い itemId はゲーム既定値のまま。
- **GAS担当は本書の §1〜§4 を満たす配信を行えばよい。**
