/**
 * GameConst / items / balance リモートコンフィグ配信GAS
 * スプレッドシート → エンベロープJSON → Firebase Storage(GCS)
 *
 * 配信物（同一バケット・パス違い）:
 *   gameconst : config/production/gameconst.json   ← data / meta
 *   items     : config/production/items.json        ← items / items_meta
 *   balance   : config/production/balance.json      ← bal_* / balance_meta
 *
 * 必要なスクリプトプロパティ:
 *   SA_CLIENT_EMAIL / SA_PRIVATE_KEY / STORAGE_BUCKET
 * 必要なライブラリ:
 *   OAuth2 for Apps Script（識別子 OAuth2）
 *
 * 2026-08 更新:
 *   - villageFacilities を専用リーダーに変更（1行=1施設×1レベル → levels[] にグループ化）
 *   ※ レリックは配信しない（Unity上のScriptableObjectで管理する方針・2026-08-31決定）
 */

// ============================================================
// 定数
// ============================================================
const DATA_SHEET = 'data';
const META_SHEET = 'meta';
const ITEMS_SHEET = 'items';
const ITEMS_META_SHEET = 'items_meta';

const GAMECONST_OBJECT_PATH = 'config/production/gameconst.json';
const ITEMS_OBJECT_PATH = 'config/production/items.json';
const BALANCE_OBJECT_PATH = 'config/production/balance.json';

const OUTPUT_FILENAME = 'gameconst.json';

// items 許可セット
const ITEM_TYPES = ['Weapon', 'Armor', 'Tool'];
const ITEM_ATTRS = ['Fire', 'Water', 'Earth', 'Wind', 'Light', 'Dark'];

// balance シート
const BALANCE_META_SHEET = 'balance_meta';
const BALANCE_SINGLE = {
  shopEconomy: 'bal_shopEconomy',
  gameBalance: 'bal_gameBalance',
  battlePrice: 'bal_battlePrice'
};
const BALANCE_LISTS = {
  advertisements: 'bal_advertisements',
  buzzEffects: 'bal_buzzEffects',
  followerMilestones: 'bal_followerMilestones',
  enemies: 'bal_enemies',
  dungeons: 'bal_dungeons',
  events: 'bal_events'
  // villageFacilities は入れ子構造のため専用リーダーで処理（下方の readVillageFacilities_）
};
const BALANCE_HEROLEVELS_SHEET = 'bal_heroLevels';
const VILLAGE_FACILITIES_SHEET = 'villageFacilities';

// ============================================================
// メニュー
// ============================================================
function onOpen() {
  SpreadsheetApp.getUi()
    .createMenu('GameConst')
    .addItem('JSONを生成（ログ表示）', 'previewJson')
    .addItem('JSONをファイル化（Drive保存）', 'exportJsonToDrive')
    .addItem('Firebaseへアップロード(定数)', 'uploadToFirebase')
    .addSeparator()
    .addItem('Firebaseへアップロード（アイテム）', 'uploadItems')
    .addItem('Firebaseへアップロード（バランス）', 'uploadBalance')
    .addToUi();
}

// ============================================================
// GameConst
// ============================================================
function previewJson() {
  const json = buildEnvelopeJson();
  Logger.log(json);
  SpreadsheetApp.getUi().alert('JSONを生成しました。表示 > ログ で確認できます。\n\n' + json);
}

function exportJsonToDrive() {
  const json = buildEnvelopeJson();
  const files = DriveApp.getFilesByName(OUTPUT_FILENAME);
  if (files.hasNext()) {
    files.next().setContent(json);
    SpreadsheetApp.getUi().alert('Drive の既存 ' + OUTPUT_FILENAME + ' を更新しました。');
  } else {
    DriveApp.createFile(OUTPUT_FILENAME, json, MimeType.PLAIN_TEXT);
    SpreadsheetApp.getUi().alert('Drive に ' + OUTPUT_FILENAME + ' を作成しました。');
  }
}

function uploadToFirebase() {
  const ui = SpreadsheetApp.getUi();
  try {
    const json = buildEnvelopeJson();
    uploadJsonToStorage(GAMECONST_OBJECT_PATH, json);
    ui.alert('gameconst.json をアップロードしました。\nパス: ' + GAMECONST_OBJECT_PATH);
  } catch (e) {
    ui.alert('アップロード失敗: ' + e.message);
  }
}

function buildEnvelopeJson() {
  const data = readDataSheet_();
  const meta = readMetaSheet_(META_SHEET);
  return JSON.stringify({
    version: meta.version,
    schemaVersion: meta.schemaVersion,
    updatedAt: new Date().toISOString(),
    data: data
  }, null, 2);
}

function readDataSheet_() {
  return readKeyValueTypeSheet_(DATA_SHEET);
}

// ============================================================
// items
// ============================================================
function uploadItems() {
  const ui = SpreadsheetApp.getUi();
  try {
    const envelope = buildItemsEnvelope();
    uploadJsonToStorage(ITEMS_OBJECT_PATH, JSON.stringify(envelope, null, 2));
    ui.alert('items.json をアップロードしました（' + envelope.items.length + '件 / version ' + envelope.version + '）');
  } catch (e) {
    ui.alert('アップロード失敗: ' + e.message);
  }
}

function buildItemsEnvelope() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const meta = readMetaSheet_(ITEMS_META_SHEET);
  if (meta.schemaVersion !== 1) {
    throw new Error('items_meta.schemaVersion は 1 にしてください（現在 ' + meta.schemaVersion + '）');
  }

  const sheet = ss.getSheetByName(ITEMS_SHEET);
  if (!sheet) throw new Error('シート "' + ITEMS_SHEET + '" がありません。');

  const last = sheet.getLastRow();
  const rows = last >= 2 ? sheet.getRange(2, 1, last - 1, 11).getValues() : [];
  const items = [];

  rows.forEach(function (r, i) {
    const line = i + 2;
    const itemId = String(r[0]).trim();
    if (!itemId) return;

    const item = {
      itemId: itemId,
      itemName: String(r[1] || '').trim(),
      basePrice: reqInt_(r[2], line, 'basePrice'),
      initialStock: reqInt_(r[3], line, 'initialStock'),
      maxStock: reqInt_(r[4], line, 'maxStock'),
      initialDisplayStock: reqInt_(r[5], line, 'initialDisplayStock'),
      itemType: String(r[6]).trim(),
      itemAttribute: String(r[7]).trim(),
      requiredLevel: reqInt_(r[8], line, 'requiredLevel'),
      salesRate: reqFloat_(r[9], line, 'salesRate'),
      description: String(r[10] || '').trim()
    };

    if (item.basePrice < 0) throw new Error(line + '行: basePrice は0以上');
    if (item.maxStock < 1) throw new Error(line + '行: maxStock は1以上');
    if (item.initialStock < 0) throw new Error(line + '行: initialStock は0以上');
    if (item.initialDisplayStock < 0) throw new Error(line + '行: initialDisplayStock は0以上');
    if (item.requiredLevel < 0) throw new Error(line + '行: requiredLevel は0以上');
    if (!(item.salesRate >= 0.1 && item.salesRate <= 5.0)) throw new Error(line + '行: salesRate は 0.1〜5.0');
    if (ITEM_TYPES.indexOf(item.itemType) < 0) throw new Error(line + '行: itemType 不正 (' + item.itemType + ')');
    if (ITEM_ATTRS.indexOf(item.itemAttribute) < 0) throw new Error(line + '行: itemAttribute 不正 (' + item.itemAttribute + ')');
    if (item.initialStock > item.maxStock) throw new Error(line + '行: initialStock が maxStock 超過');
    if (item.initialDisplayStock > item.initialStock) throw new Error(line + '行: initialDisplayStock が initialStock 超過');

    items.push(item);
  });

  if (items.length === 0) throw new Error('items シートに有効なアイテムがありません。');

  return {
    version: meta.version,
    schemaVersion: meta.schemaVersion,
    updatedAt: new Date().toISOString(),
    items: items
  };
}

// ============================================================
// balance
// ============================================================
function uploadBalance() {
  const ui = SpreadsheetApp.getUi();
  try {
    const envelope = buildBalanceEnvelope();
    uploadJsonToStorage(BALANCE_OBJECT_PATH, JSON.stringify(envelope, null, 2));
    ui.alert('balance.json をアップロードしました（version ' + envelope.version + '）');
  } catch (e) {
    ui.alert('アップロード失敗: ' + e.message);
  }
}

function buildBalanceEnvelope() {
  const meta = readMetaSheet_(BALANCE_META_SHEET);
  if (meta.schemaVersion !== 1) {
    throw new Error('balance_meta.schemaVersion は 1 にしてください（現在 ' + meta.schemaVersion + '）');
  }

  const envelope = {
    version: meta.version,
    schemaVersion: meta.schemaVersion,
    updatedAt: new Date().toISOString()
  };

  // 単一区画（中身が空なら区画ごと省略）
  Object.keys(BALANCE_SINGLE).forEach(function (outKey) {
    const obj = readKeyValueTypeSheet_(BALANCE_SINGLE[outKey]);
    if (Object.keys(obj).length > 0) envelope[outKey] = obj;
  });

  // リスト区画（空配列なら区画ごと省略）
  Object.keys(BALANCE_LISTS).forEach(function (outKey) {
    const arr = readListSheet_(BALANCE_LISTS[outKey]);
    if (arr.length > 0) envelope[outKey] = arr;
  });

  // villageFacilities（1行=1施設×1レベル → levels[] にグループ化）
  const facilities = readVillageFacilities_();
  if (facilities.length > 0) envelope.villageFacilities = facilities;

  // heroLevels（空なら省略）
  const hero = readHeroLevels_();
  if (hero.length > 0) envelope.heroLevels = hero;

  return envelope;
}

/**
 * 行シートを配列化（部分上書き）。
 * 1列目=id（常に文字列・常に出力）。2列目以降のヘッダは "field:type"（type省略時string）。
 * 値が入ったセルだけ出力（空セルは含めない＝既定値保持）。
 */
function readListSheet_(sheetName) {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(sheetName);
  if (!sheet) throw new Error('シート "' + sheetName + '" がありません。');
  const values = sheet.getDataRange().getValues();
  if (values.length < 2) return [];

  const header = values[0].map(function (h) { return String(h).trim(); });
  const result = [];

  for (let r = 1; r < values.length; r++) {
    const row = values[r];
    const idRaw = String(row[0]).trim();
    if (idRaw === '') continue;

    const obj = { id: idRaw };
    for (let c = 1; c < header.length; c++) {
      const head = header[c];
      if (head === '') continue;
      const cell = row[c];
      if (cell === '' || cell === null || cell === undefined) continue;

      const parts = head.split(':');
      const field = parts[0].trim();
      const type = (parts[1] || 'string').trim().toLowerCase();
      obj[field] = castCell_(cell, type, sheetName + '!' + head + '(行' + (r + 1) + ')');
    }
    result.push(obj);
  }
  return result;
}

/**
 * villageFacilities: 1行=1施設×1レベル。
 * ヘッダ: id / facilityName / requiredHallLevel / level / cost / effectText
 * （任意: startBonusKey / startBonusValue / unlockRelicTier。型サフィックス不要）
 * id ごとに level 昇順でグループ化し、levels[] を持つ1オブジェクトに変換する。
 * ※ levels は Unity 側で全置換されるため、載せる施設は全レベル行を書くこと。
 */
function readVillageFacilities_() {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(VILLAGE_FACILITIES_SHEET);
  if (!sheet) return []; // シート未作成なら区画ごと省略
  const values = sheet.getDataRange().getValues();
  if (values.length < 2) return [];

  const header = values[0].map(function (h) { return String(h).trim().split(':')[0]; });
  const col = {};
  header.forEach(function (h, i) { if (h !== '' && !(h in col)) col[h] = i; });
  ['id', 'level', 'cost', 'effectText'].forEach(function (req) {
    if (!(req in col)) throw new Error(VILLAGE_FACILITIES_SHEET + ': 列 "' + req + '" がありません。');
  });

  const byId = {};
  const order = [];
  for (let r = 1; r < values.length; r++) {
    const row = values[r];
    const id = String(row[col.id]).trim();
    if (id === '') continue;
    const ctx = VILLAGE_FACILITIES_SHEET + '(行' + (r + 1) + ')';

    if (!byId[id]) {
      const obj = { id: id };
      if ('facilityName' in col && String(row[col.facilityName]).trim() !== '')
        obj.facilityName = String(row[col.facilityName]).trim();
      if ('requiredHallLevel' in col && row[col.requiredHallLevel] !== '')
        obj.requiredHallLevel = castCell_(row[col.requiredHallLevel], 'int', ctx + '!requiredHallLevel');
      byId[id] = { obj: obj, rows: [] };
      order.push(id);
    }

    const entry = {
      cost: castCell_(row[col.cost], 'int', ctx + '!cost'),
      effectText: String(row[col.effectText])
    };
    if ('startBonusKey' in col && String(row[col.startBonusKey]).trim() !== '') {
      entry.startBonusKey = String(row[col.startBonusKey]).trim();
      entry.startBonusValue = castCell_(row[col.startBonusValue], 'float', ctx + '!startBonusValue');
    }
    if ('unlockRelicTier' in col && row[col.unlockRelicTier] !== '')
      entry.unlockRelicTier = castCell_(row[col.unlockRelicTier], 'int', ctx + '!unlockRelicTier');

    byId[id].rows.push({ level: castCell_(row[col.level], 'int', ctx + '!level'), entry: entry });
  }

  return order.map(function (id) {
    const g = byId[id];
    g.rows.sort(function (a, b) { return a.level - b.level; });
    g.obj.levels = g.rows.map(function (x) { return x.entry; });
    return g.obj;
  });
}

/** heroLevels を全置換配列で返す（id無し・PascalCase + 型サフィックス）。 */
function readHeroLevels_() {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(BALANCE_HEROLEVELS_SHEET);
  if (!sheet) throw new Error('シート "' + BALANCE_HEROLEVELS_SHEET + '" がありません。');
  const values = sheet.getDataRange().getValues();
  if (values.length < 2) return [];

  const header = values[0].map(function (h) { return String(h).trim(); });
  const result = [];
  for (let r = 1; r < values.length; r++) {
    const row = values[r];
    if (String(row[0]).trim() === '') continue;
    const obj = {};
    for (let c = 0; c < header.length; c++) {
      const head = header[c];
      if (head === '') continue;
      const parts = head.split(':');
      const field = parts[0].trim();
      const type = (parts[1] || 'int').trim().toLowerCase();
      obj[field] = castCell_(row[c], type, 'heroLevels!' + head + '(行' + (r + 1) + ')');
    }
    result.push(obj);
  }
  return result;
}

// ============================================================
// 共通：シート読み取り・型変換・アップロード・認証
// ============================================================

/** key/value/type シートをオブジェクト化（gameconst data / balance単一区画 共用）。 */
function readKeyValueTypeSheet_(sheetName) {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(sheetName);
  if (!sheet) throw new Error('シート "' + sheetName + '" がありません。');
  const rows = sheet.getDataRange().getValues();
  const out = {};
  for (let i = 1; i < rows.length; i++) {
    const key = String(rows[i][0]).trim();
    if (key === '') continue;
    out[key] = convertValue_(key, rows[i][1], String(rows[i][2]).trim().toLowerCase());
  }
  return out;
}

/** key/value メタシート → { version, schemaVersion }。 */
function readMetaSheet_(sheetName) {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(sheetName);
  if (!sheet) throw new Error('シート "' + sheetName + '" がありません。');
  const rows = sheet.getDataRange().getValues();
  const map = {};
  for (let i = 1; i < rows.length; i++) {
    const key = String(rows[i][0]).trim();
    if (key === '') continue;
    map[key] = rows[i][1];
  }
  return {
    version: toInt_(sheetName + '.version', map['version']),
    schemaVersion: toInt_(sheetName + '.schemaVersion', map['schemaVersion'])
  };
}

/** type（int/float/string/bool/int[]）に応じて値変換。 */
function convertValue_(key, rawValue, type) {
  switch (type) {
    case 'int': return toInt_(key, rawValue);
    case 'float': return toFloat_(key, rawValue);
    case 'bool': return (String(rawValue).trim().toLowerCase() === 'true');
    case 'string': return String(rawValue);
    case 'int[]':
      return String(rawValue).split(',')
        .map(function (s) { return s.trim(); })
        .filter(function (s) { return s !== ''; })
        .map(function (s) { return toInt_(key + '[]', s); });
    default:
      throw new Error('未知の type "' + type + '"（key=' + key + '）。int/float/string/bool/int[] のいずれかに。');
  }
}

/** 型サフィックス用キャスト（balanceリスト/heroLevels）。 */
function castCell_(cell, type, ctx) {
  switch (type) {
    case 'int': {
      const n = Number(cell);
      if (!isFinite(n) || Math.floor(n) !== n) throw new Error(ctx + ': int 変換失敗 (' + cell + ')');
      return n;
    }
    case 'float': {
      const n = Number(cell);
      if (!isFinite(n)) throw new Error(ctx + ': float 変換失敗 (' + cell + ')');
      return n;
    }
    case 'bool':
      return String(cell).trim().toLowerCase() === 'true';
    case 'string':
    default:
      return String(cell);
  }
}

/** 必須整数（空セルはエラー）。 */
function reqInt_(cell, line, field) {
  if (cell === '' || cell === null || cell === undefined) throw new Error(line + '行: ' + field + ' が空です');
  const n = Number(cell);
  if (!isFinite(n) || Math.floor(n) !== n) throw new Error(line + '行: ' + field + ' が整数ではありません (' + cell + ')');
  return n;
}

/** 必須小数（空セルはエラー）。 */
function reqFloat_(cell, line, field) {
  if (cell === '' || cell === null || cell === undefined) throw new Error(line + '行: ' + field + ' が空です');
  const n = Number(cell);
  if (!isFinite(n)) throw new Error(line + '行: ' + field + ' が数値ではありません (' + cell + ')');
  return n;
}

function toInt_(key, v) {
  const n = Number(v);
  if (!isFinite(n) || Math.floor(n) !== n) throw new Error('int に変換できません（key=' + key + ', value=' + v + '）。');
  return n;
}

function toFloat_(key, v) {
  const n = Number(v);
  if (!isFinite(n)) throw new Error('float に変換できません（key=' + key + ', value=' + v + '）。');
  return n;
}

/** 任意パスにJSON文字列をアップロード（GCS）。 */
function uploadJsonToStorage(objectPath, jsonString) {
  const bucket = getProp_('STORAGE_BUCKET');
  const url = 'https://storage.googleapis.com/upload/storage/v1/b/'
    + encodeURIComponent(bucket)
    + '/o?uploadType=media&name='
    + encodeURIComponent(objectPath);

  const token = getAccessToken_();
  const res = UrlFetchApp.fetch(url, {
    method: 'post',
    contentType: 'application/json; charset=utf-8',
    headers: { Authorization: 'Bearer ' + token, 'Cache-Control': 'no-cache' },
    payload: jsonString,
    muteHttpExceptions: true
  });

  const code = res.getResponseCode();
  if (code < 200 || code >= 300) {
    throw new Error('HTTP ' + code + ' / ' + res.getContentText());
  }
}

/** サービスアカウントで OAuth2 アクセストークン取得。 */
function getAccessToken_() {
  const service = OAuth2.createService('gcs')
    .setTokenUrl('https://oauth2.googleapis.com/token')
    .setPrivateKey(getProp_('SA_PRIVATE_KEY').replace(/\\n/g, '\n'))
    .setIssuer(getProp_('SA_CLIENT_EMAIL'))
    .setPropertyStore(PropertiesService.getScriptProperties())
    .setScope('https://www.googleapis.com/auth/devstorage.read_write');

  if (!service.hasAccess()) {
    throw new Error('アクセストークン取得失敗: ' + service.getLastError());
  }
  return service.getAccessToken();
}

/** スクリプトプロパティ取得（未設定はエラー）。 */
function getProp_(name) {
  const v = PropertiesService.getScriptProperties().getProperty(name);
  if (!v) throw new Error('スクリプトプロパティ "' + name + '" が未設定です。');
  return v;
}
