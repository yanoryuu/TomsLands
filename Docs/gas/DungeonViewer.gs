/**
 * ダンジョン構成ビューア（2026-10-30）
 *
 * シート:
 *   dungeonPhases : dungeonId / level / phase / enemyIds（カンマ区切り・出る順） / note
 *                   1行=1ダンジョン×1レベル×1フェーズ。Unity の DungeonLevelData.phases と同じ並び。
 *   bal_enemies   : 敵の基本値（id = EnemyData.enemyId）
 *   bal_dungeons  : enemyHpRate / enemyAtkRate / enemyDefRate（ダンジョンごとの敵の倍率。ビューアの表示に掛ける）
 *
 * メニュー「GameConst」→「ダンジョン構成ビューア」「ダンジョン構成をチェック」から使う。
 * ※ dungeonPhases はまだ配信しない（Unity 側の受け口が未実装）。readDungeonPhases_() は下に用意だけしてある。
 */

const DUNGEON_PHASES_SHEET = 'dungeonPhases';
const DV_ELEMENTS = ['None', 'Water', 'Fire', 'Wood', 'Light', 'Dark']; // Unity の ElementType の並び（int 値）

// ============================================================
// メニューから呼ぶ
// ============================================================
function showDungeonViewer() {
  const html = HtmlService.createHtmlOutputFromFile('DungeonViewerUI')
    .setWidth(1150)
    .setHeight(820);
  SpreadsheetApp.getUi().showModelessDialog(html, 'ダンジョン構成ビューア');
}

function validateDungeonPhases() {
  const data = dvLoad_();
  const issues = dvValidate_(data);
  const ui = SpreadsheetApp.getUi();
  if (issues.length === 0) {
    ui.alert('ダンジョン構成チェック', '問題は見つかりませんでした（' + data.phases.length + ' 行）。', ui.ButtonSet.OK);
    return;
  }
  const lines = issues.slice(0, 80).map(function (s) { return '・' + s.text; });
  if (issues.length > 80) lines.push('…ほか ' + (issues.length - 80) + ' 件');
  ui.alert('ダンジョン構成チェック（' + issues.length + ' 件）', lines.join('\n'), ui.ButtonSet.OK);
}

// ============================================================
// HTML（google.script.run）から呼ぶ
// ============================================================

/** ビューアの表示に要るものを全部返す。 */
function dvGetViewerData() {
  const data = dvLoad_();
  data.issues = dvValidate_(data);
  return data;
}

/**
 * 1フェーズ分の敵を書き換える（無ければ行を足す）。enemyIds は配列（出る順）。
 * bal_enemies に無い id があれば書かずにエラーにする。
 */
function dvSavePhase(dungeonId, level, phase, enemyIds) {
  dungeonId = String(dungeonId).trim();
  level = Number(level);
  phase = Number(phase);
  const ids = (enemyIds || []).map(function (s) { return String(s).trim(); }).filter(function (s) { return s !== ''; });
  if (ids.length === 0) throw new Error('敵が 1 体もいません。フェーズを消すときは「このフェーズを消す」を使ってください。');

  const data = dvLoad_();
  if (!dvDungeonById_(data, dungeonId)) throw new Error('bal_dungeons に無い dungeonId: ' + dungeonId);
  const unknown = ids.filter(function (id) { return !data.enemies[id]; });
  if (unknown.length > 0) throw new Error('bal_enemies に無い enemyId: ' + unknown.join(', '));
  if (!(level >= 1 && level <= 5)) throw new Error('level は 1〜5: ' + level);
  if (!(phase >= 1)) throw new Error('phase は 1 以上: ' + phase);

  const sheet = dvPhasesSheet_();
  const row = dvFindPhaseRow_(data, dungeonId, level, phase);
  const col = data.phaseCols;
  if (row > 0) {
    sheet.getRange(row, col.enemyIds + 1).setValue(ids.join(','));
  } else {
    const width = sheet.getLastColumn();
    const values = [];
    for (let i = 0; i < width; i++) values.push('');
    values[col.dungeonId] = dungeonId;
    values[col.level] = level;
    values[col.phase] = phase;
    values[col.enemyIds] = ids.join(',');
    const target = dvInsertRowAfterLevel_(sheet, data, dungeonId, level, phase);
    sheet.getRange(target, 1, 1, width).setValues([values]);
  }
  return dvGetViewerData();
}

/** 1フェーズ分の行を消し、後ろのフェーズ番号を 1 つずつ詰める。 */
function dvDeletePhase(dungeonId, level, phase) {
  const data = dvLoad_();
  const row = dvFindPhaseRow_(data, String(dungeonId).trim(), Number(level), Number(phase));
  if (row <= 0) throw new Error('行が見つかりません: ' + dungeonId + ' Lv' + level + ' P' + phase);
  const sheet = dvPhasesSheet_();
  sheet.deleteRow(row);
  // 後ろのフェーズ番号を詰める
  const after = dvLoad_();
  after.phases.forEach(function (p) {
    if (p.dungeonId === dungeonId && p.level === Number(level) && p.phase > Number(phase)) {
      sheet.getRange(p.row, after.phaseCols.phase + 1).setValue(p.phase - 1);
    }
  });
  return dvGetViewerData();
}

// ============================================================
// 配信用（TODO: まだ buildBalanceEnvelope からは呼ばない）
// ============================================================

/**
 * TODO(dungeonPhases 配信): Unity 側（DungeonLevelData.phases をリモートで差し替える受け口）ができたら、
 * buildBalanceEnvelope() に
 *     const dungeonPhases = readDungeonPhases_();
 *     if (dungeonPhases.length > 0) envelope.dungeonPhases = dungeonPhases;
 * を足す。それまでは呼ばないこと（今は配信物に入れない）。
 *
 * 出力: [ { dungeonId, level, phase, enemyIds: [ 'demon9', 'demon10', ... ] }, ... ]
 *   dungeonId / level / phase の順に並べる。enemyIds が空の行は出さない。
 */
function readDungeonPhases_() {
  const data = dvLoad_();
  return data.phases
    .filter(function (p) { return p.enemyIds.length > 0; })
    .map(function (p) {
      return { dungeonId: p.dungeonId, level: p.level, phase: p.phase, enemyIds: p.enemyIds.slice() };
    })
    .sort(function (a, b) {
      if (a.dungeonId !== b.dungeonId) return a.dungeonId < b.dungeonId ? -1 : 1;
      if (a.level !== b.level) return a.level - b.level;
      return a.phase - b.phase;
    });
}

// ============================================================
// 読み取り・チェック（内部）
// ============================================================

function dvPhasesSheet_() {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(DUNGEON_PHASES_SHEET);
  if (!sheet) throw new Error('シート "' + DUNGEON_PHASES_SHEET + '" がありません。');
  return sheet;
}

/** ヘッダ行 → { 列名（型サフィックスを除く）: 列番号(0始まり) } */
function dvHeaderCols_(headerRow) {
  const col = {};
  headerRow.forEach(function (h, i) {
    const name = String(h).trim().split(':')[0].trim();
    if (name !== '' && !(name in col)) col[name] = i;
  });
  return col;
}

function dvNum_(v, def) {
  if (v === '' || v === null || v === undefined) return def;
  const n = Number(v);
  return isFinite(n) ? n : def;
}

function dvLoad_() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();

  // bal_dungeons
  const dungeons = [];
  const dSheet = ss.getSheetByName('bal_dungeons');
  if (dSheet) {
    const v = dSheet.getDataRange().getValues();
    const c = dvHeaderCols_(v[0]);
    for (let r = 1; r < v.length; r++) {
      const id = String(v[r][c.id]).trim();
      if (!id) continue;
      dungeons.push({
        id: id,
        key: dvNum_(v[r][c.key], 99),
        name: 'dungeonName' in c ? String(v[r][c.dungeonName]).trim() : id,
        hpRate: 'enemyHpRate' in c ? dvNum_(v[r][c.enemyHpRate], 1) : 1,
        atkRate: 'enemyAtkRate' in c ? dvNum_(v[r][c.enemyAtkRate], 1) : 1,
        defRate: 'enemyDefRate' in c ? dvNum_(v[r][c.enemyDefRate], 1) : 1
      });
    }
    dungeons.sort(function (a, b) { return a.key - b.key; });
  }

  // bal_enemies
  const enemies = {};
  const eSheet = ss.getSheetByName('bal_enemies');
  if (eSheet) {
    const v = eSheet.getDataRange().getValues();
    const c = dvHeaderCols_(v[0]);
    for (let r = 1; r < v.length; r++) {
      const id = String(v[r][c.id]).trim();
      if (!id) continue;
      const el = 'elementType' in c ? dvNum_(v[r][c.elementType], 0) : 0;
      enemies[id] = {
        id: id,
        name: 'enemyName' in c ? String(v[r][c.enemyName]).trim() : '',
        hp: 'hp' in c ? dvNum_(v[r][c.hp], null) : null,
        atk: 'attackPower' in c ? dvNum_(v[r][c.attackPower], null) : null,
        def: 'defensePower' in c ? dvNum_(v[r][c.defensePower], null) : null,
        element: DV_ELEMENTS[el] || String(el),
        isBoss: 'isBoss' in c ? String(v[r][c.isBoss]).trim().toLowerCase() === 'true' : false,
        row: r + 1
      };
    }
  }

  // dungeonPhases
  const phases = [];
  let phaseCols = { dungeonId: 0, level: 1, phase: 2, enemyIds: 3, note: 4 };
  const pSheet = ss.getSheetByName(DUNGEON_PHASES_SHEET);
  if (pSheet) {
    const v = pSheet.getDataRange().getValues();
    if (v.length > 0) {
      const c = dvHeaderCols_(v[0]);
      ['dungeonId', 'level', 'phase', 'enemyIds'].forEach(function (req) {
        if (!(req in c)) throw new Error(DUNGEON_PHASES_SHEET + ': 列 "' + req + '" がありません。');
      });
      phaseCols = c;
      for (let r = 1; r < v.length; r++) {
        const id = String(v[r][c.dungeonId]).trim();
        if (!id) continue;
        phases.push({
          row: r + 1,
          dungeonId: id,
          level: dvNum_(v[r][c.level], NaN),
          phase: dvNum_(v[r][c.phase], NaN),
          enemyIds: String(v[r][c.enemyIds]).split(',')
            .map(function (s) { return s.trim(); })
            .filter(function (s) { return s !== ''; }),
          note: 'note' in c ? String(v[r][c.note]) : ''
        });
      }
    }
  }

  // dungeonLevels（配当・費用の表示用。"*" と個別 id）
  const levels = [];
  const lSheet = ss.getSheetByName('dungeonLevels');
  if (lSheet) {
    const v = lSheet.getDataRange().getValues();
    const c = dvHeaderCols_(v[0]);
    if (!('dungeonId' in c) && ('id' in c)) c.dungeonId = c.id;
    for (let r = 1; r < v.length; r++) {
      const id = String(v[r][c.dungeonId]).trim();
      if (!id) continue;
      levels.push({
        dungeonId: id,
        level: dvNum_(v[r][c.level], NaN),
        levelUpCost: 'levelUpCost' in c ? dvNum_(v[r][c.levelUpCost], null) : null,
        rewardGold: 'rewardGold' in c ? dvNum_(v[r][c.rewardGold], null) : null
      });
    }
  }

  return { dungeons: dungeons, enemies: enemies, phases: phases, phaseCols: phaseCols, levels: levels };
}

function dvDungeonById_(data, id) {
  for (let i = 0; i < data.dungeons.length; i++) if (data.dungeons[i].id === id) return data.dungeons[i];
  return null;
}

function dvFindPhaseRow_(data, dungeonId, level, phase) {
  for (let i = 0; i < data.phases.length; i++) {
    const p = data.phases[i];
    if (p.dungeonId === dungeonId && p.level === level && p.phase === phase) return p.row;
  }
  return -1;
}

/** 新しいフェーズ行を入れる位置（同じダンジョン×レベルの最後の行の次。無ければ末尾）。行を挿入して行番号を返す。 */
function dvInsertRowAfterLevel_(sheet, data, dungeonId, level, phase) {
  let last = -1;
  data.phases.forEach(function (p) {
    if (p.dungeonId === dungeonId && p.level === level && p.phase < phase && p.row > last) last = p.row;
  });
  if (last < 0) {
    data.phases.forEach(function (p) {
      if (p.dungeonId === dungeonId && p.level < level && p.row > last) last = p.row;
    });
  }
  if (last < 0) return sheet.getLastRow() + 1;
  sheet.insertRowAfter(last);
  return last + 1;
}

/**
 * dungeonPhases のチェック。[{ level: 'error'|'warn', dungeonId, lv, text }]
 *   - bal_dungeons に無い dungeonId / level が 1〜5 でない / phase が 1 以上の整数でない
 *   - 同じダンジョン×レベル×フェーズの行が 2 つ以上
 *   - フェーズ番号が 1 から続いていない
 *   - 敵が空のフェーズ
 *   - bal_enemies に無い enemyId
 *   - ボス（isBoss）が最後のフェーズ以外にいる
 *   - ダンジョン×レベルに行が 1 つも無い（warn: アセットの構成のまま）
 */
function dvValidate_(data) {
  const issues = [];
  function add(kind, p, text) {
    issues.push({ kind: kind, dungeonId: p ? p.dungeonId : '', lv: p ? p.level : 0,
      text: (p ? p.dungeonId + ' Lv' + p.level + ' P' + p.phase + '（' + p.row + '行）: ' : '') + text });
  }

  const groups = {};
  data.phases.forEach(function (p) {
    if (!dvDungeonById_(data, p.dungeonId)) add('error', p, 'bal_dungeons に無い dungeonId');
    if (!(p.level >= 1 && p.level <= 5 && Math.floor(p.level) === p.level)) add('error', p, 'level は 1〜5 の整数に');
    if (!(p.phase >= 1 && Math.floor(p.phase) === p.phase)) add('error', p, 'phase は 1 以上の整数に');
    if (p.enemyIds.length === 0) add('error', p, '敵がいない（空のフェーズ）');
    p.enemyIds.forEach(function (id) {
      if (!data.enemies[id]) add('error', p, 'bal_enemies に無い enemyId "' + id + '"');
    });
    const key = p.dungeonId + '\u0000' + p.level;
    (groups[key] = groups[key] || []).push(p);
  });

  Object.keys(groups).forEach(function (key) {
    const list = groups[key].slice().sort(function (a, b) { return a.phase - b.phase; });
    const seen = {};
    list.forEach(function (p) {
      if (seen[p.phase]) add('error', p, 'フェーズ番号が重複（' + seen[p.phase] + '行と）');
      seen[p.phase] = p.row;
    });
    const nums = Object.keys(seen).map(Number).sort(function (a, b) { return a - b; });
    if (nums.some(function (n, i) { return n !== i + 1; })) {
      add('warn', list[0], 'フェーズ番号が 1 から続いていない（' + nums.map(function (n) { return 'P' + n; }).join(', ') + '）');
    }
    const maxPhase = list.length > 0 ? list[list.length - 1].phase : 0;
    list.forEach(function (p) {
      if (p.phase === maxPhase) return;
      p.enemyIds.forEach(function (id) {
        const e = data.enemies[id];
        if (e && e.isBoss) add('warn', p, 'ボス "' + id + '" が最後のフェーズ（P' + maxPhase + '）以外にいる');
      });
    });
  });

  data.dungeons.forEach(function (d) {
    for (let lv = 1; lv <= 5; lv++) {
      if (!groups[d.id + '\u0000' + lv]) {
        issues.push({ kind: 'warn', dungeonId: d.id, lv: lv, text: d.id + ' Lv' + lv + ': 行が無い（アセットの構成のまま）' });
      }
    }
  });
  return issues;
}
