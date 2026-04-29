// parse_grid.js — converts Grid.xlsx into Grid.json for Unity consumption
// Usage: node Data/parse_grid.js
// Requires: xlsx npm package (npm install -g xlsx)

const XLSX = require('C:/Users/Liron/AppData/Roaming/npm/node_modules/xlsx');
const fs   = require('fs');
const path = require('path');

const INPUT  = path.join(__dirname, 'Grid.xlsx');
const OUTPUT = path.join(__dirname, 'Grid.json');

// ── Color → CellType mapping ────────────────────────────────────────────────
// Colors are 6-char RGB hex strings as returned by the xlsx library (fgColor.rgb).
// Theme-based fills (e.g. the dark sorter system) are identified by label instead.
const COLOR_MAP = {
  'FF0000': 'Storage',     // storage slots (numbered)
  'CAEEFB': 'InductZone',  // induct zone — no robots
  'FBE3D6': 'Staging',     // staging area (St1–St90)
  'FFC000': 'EmptyBuffer',  // empty cart buffers (E1–E14)
  // Sort chutes appear in three green shades depending on group
  '8ED973': 'SortChute',
  'B4E5A2': 'SortChute',
  '3B7D23': 'SortChute',
  // Feeder queue shares the same pink as S29
  'F2CFEE': 'SortChuteOrQueue', // resolved to SortChute or FeederQueue by label below
};

// Direction labels → corridor type
const DIRECTION_VALUES = new Set(['up', 'down', 'left', 'right', 'any']);

// No-robot zone labels (sorter system merged cells)
const NO_ROBOT_LABELS = new Set(['Diverter', 'Singulator', 'Feeder']);
const SYSTEM_REGION_VALUES = new Set(['None', 'Diverter', 'Singulator', 'Feeder']);

function getCellType(value, rgbColor, themeKey) {
  const v = value !== null && value !== undefined ? String(value).trim() : '';

  // Direction → corridor
  if (DIRECTION_VALUES.has(v.toLowerCase())) return 'Corridor';

  // Explicit zone labels
  if (v.startsWith('Induct Zone')) return 'InductZone';
  if (NO_ROBOT_LABELS.has(v))     return 'SorterSystem';

  // Named zones by label prefix
  if (/^St\d+$/.test(v)) return 'Staging';
  if (/^S\d+$/.test(v))  return 'SortChute';
  if (/^E\d+$/.test(v))  return 'EmptyBuffer';
  if (/^Q\d+$/.test(v))  return 'FeederQueue';
  if (/^C\d+$/.test(v))  return 'Charging';

  // Numeric → storage slot
  if (v !== '' && !isNaN(Number(v)) && Number(v) > 0) return 'Storage';

  // Fall back to color
  if (rgbColor) {
    const mapped = COLOR_MAP[rgbColor.toUpperCase()];
    if (mapped === 'SortChuteOrQueue') return 'SortChute'; // default; label overrides above
    if (mapped) return mapped;
  }

  // Theme-based dark fill with no label → sorter system
  if (themeKey !== null) return 'SorterSystem';

  return 'Empty';
}

function getDirection(value) {
  const v = value !== null && value !== undefined ? String(value).trim().toLowerCase() : '';
  if (DIRECTION_VALUES.has(v)) return v;
  return null;
}

function isNoRobotType(cellType) {
  return cellType === 'InductZone' || cellType === 'SorterSystem';
}

function normalizeSystemRegion(value) {
  const v = value !== null && value !== undefined ? String(value).trim() : '';
  return SYSTEM_REGION_VALUES.has(v) ? v : 'None';
}

function buildMergedSystemRegionMap(sheet) {
  const mergedRegionByCell = new Map();
  for (const merge of sheet['!merges'] || []) {
    const topLeftAddr = XLSX.utils.encode_cell({ r: merge.s.r, c: merge.s.c });
    const topLeftCell = sheet[topLeftAddr];
    const topLeftValue = topLeftCell && topLeftCell.v !== undefined && topLeftCell.v !== null
      ? String(topLeftCell.v).trim()
      : '';

    if (!NO_ROBOT_LABELS.has(topLeftValue))
      continue;

    for (let r = merge.s.r; r <= merge.e.r; r++) {
      for (let c = merge.s.c; c <= merge.e.c; c++) {
        mergedRegionByCell.set(`${r}:${c}`, topLeftValue);
      }
    }
  }

  return mergedRegionByCell;
}

// ── Main parse ───────────────────────────────────────────────────────────────
const wb = XLSX.readFile(INPUT, { cellStyles: true });
const ws = wb.Sheets[wb.SheetNames[0]];
const range = XLSX.utils.decode_range(ws['!ref']);
const mergedSystemRegionByCell = buildMergedSystemRegionMap(ws);

const cells = [];

for (let r = range.s.r; r <= range.e.r; r++) {
  for (let c = range.s.c; c <= range.e.c; c++) {
    const addr = XLSX.utils.encode_cell({ r, c });
    const cell = ws[addr];

    const value = cell ? cell.v : null;

    // Extract fill color
    let rgbColor  = null;
    let themeKey  = null;
    if (cell && cell.s && cell.s.patternType === 'solid' && cell.s.fgColor) {
      const fg = cell.s.fgColor;
      if (fg.rgb && fg.rgb !== '00000000' && fg.rgb !== 'FFFFFFFF' && fg.rgb !== 'FF000000') {
        rgbColor = fg.rgb.length === 8 ? fg.rgb.substring(2) : fg.rgb; // strip alpha if ARGB
      } else if (fg.theme !== undefined) {
        themeKey = `theme:${fg.theme},tint:${(fg.tint || 0).toFixed(2)}`;
      }
    }

    const cellType  = getCellType(value, rgbColor, themeKey);
    const direction = getDirection(value);
    const label     = (value !== null && value !== undefined) ? String(value).trim() : '';
    const mergedSystemRegion = mergedSystemRegionByCell.get(`${r}:${c}`) || 'None';
    const systemRegion = normalizeSystemRegion(
      mergedSystemRegion !== 'None' ? mergedSystemRegion : label
    );

    // row/col are 1-based to match Excel notation
    cells.push({
      row:      r + 1,
      col:      c + 1,
      label:    label,
      cellType: cellType,
      systemRegion: systemRegion,
      direction: direction,        // "up"|"down"|"left"|"right"|"any"|null
      isNoRobot: isNoRobotType(cellType),
      rgbColor:  rgbColor,
    });
  }
}

// ── Merge info (no-robot zone extents) ──────────────────────────────────────
const merges = (ws['!merges'] || []).map(m => ({
  r1: m.s.r + 1, c1: m.s.c + 1,
  r2: m.e.r + 1, c2: m.e.c + 1,
}));

const output = {
  rows:   range.e.r + 1,
  cols:   range.e.c + 1,
  cells:  cells,
  merges: merges,
};

fs.writeFileSync(OUTPUT, JSON.stringify(output, null, 2), 'utf8');
console.log(`Wrote ${cells.length} cells (${range.e.r + 1} rows × ${range.e.c + 1} cols) to ${OUTPUT}`);
console.log(`Merges: ${merges.length}`);

// Print a summary of cell type counts
const counts = {};
cells.forEach(c => { counts[c.cellType] = (counts[c.cellType] || 0) + 1; });
console.log('Cell type counts:', counts);
