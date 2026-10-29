export const meta = {
  name: 'balance-panel',
  description: 'Give the same balance problems to 3 proposers with different lenses, then score every proposal with 2 judges',
  whenToUse: 'TomsLands のマスタ（バランス値）を直す前の審議。args = { brief, problems, scratchDir }',
  phases: [
    { title: 'Propose', detail: '3 independent proposers, same brief, different lenses' },
    { title: 'Judge', detail: '2 judges score every proposal 0-100' },
  ],
}

// args.brief: ゲームの目標・今の値・データの場所・制約（SKILL.md の「審議に渡す前提」を埋めたもの）
// args.problems: 問題点の一覧（"P1. ..." の形の文字列）
// args.scratchDir: 読むだけのスクリプトや出力を置いてよい場所（_scratch_wt04 の下）
const BRIEF = `${args.brief}

Problems (verify each against code and data before relying on it):
${args.problems}

READ-ONLY: do not edit any project file, spreadsheet, or config. You may write scratch scripts/outputs only under ${args.scratchDir}. Write in Japanese. Avoid the phrases 「効いて」「効いた」「〜が効いてくる」「全額○回目」「回目」.`

const PROPOSAL_SCHEMA = {
  type: 'object',
  properties: {
    proposals: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string', description: 'lens prefix + problem, e.g. L1-P1a' },
          problems: { type: 'array', items: { type: 'string' } },
          title: { type: 'string' },
          change: { type: 'string', description: 'exact change: sheet/key or C# file/function, current -> new' },
          kind: { type: 'string', enum: ['sheet', 'csharp', 'ai', 'process', 'no_change'] },
          rationale: { type: 'string', description: 'why, with numbers you checked in code/data' },
          risks: { type: 'string' },
          measure: { type: 'string', description: 'metric, target, sample size after applying' },
          order: { type: 'string', description: 'when to apply relative to other changes / save resets' },
        },
        required: ['id', 'problems', 'title', 'change', 'kind', 'rationale', 'risks', 'measure', 'order'],
      },
    },
    sequencing: { type: 'string' },
    disagreements: { type: 'string', description: 'where the analysis or pending plan looks wrong' },
  },
  required: ['proposals', 'sequencing', 'disagreements'],
}

const LENSES = [
  { key: 'L1', name: '経済・データ', prompt: 'Lens: economist / data analyst. Ground every proposal in numbers (recompute from logs where needed), check the actual code paths, prefer changes whose effect you can quantify and measure. A small simulation is welcome.' },
  { key: 'L2', name: 'プレイ体験', prompt: 'Lens: game designer focused on the human player and the core appeal (読み→賭け→的中→大儲け). Ask what each problem means for fun, tension, readable decisions and fairness, not just for the AI. Say when a "problem" is actually fine.' },
  { key: 'L3', name: '慎重・段取り', prompt: 'Lens: cautious producer. Minimize risk to the live config (the demo reads it) and to the ongoing AI learning; prefer the smallest change that fixes the root cause; think hard about ordering and about what earlier fixes or save resets invalidate; call out changes that should NOT be made now.' },
]

const SCORE_SCHEMA = {
  type: 'object',
  properties: {
    scores: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          score: { type: 'number', description: '0-100 = sum of the four sub-scores' },
          fixesRootCause: { type: 'number', description: '0-25' },
          evidence: { type: 'number', description: '0-25: grounded in checked code/data' },
          playerExperience: { type: 'number', description: '0-25' },
          riskAndCost: { type: 'number', description: '0-25: higher = safer, cheaper, measurable' },
          comment: { type: 'string' },
        },
        required: ['id', 'score', 'fixesRootCause', 'evidence', 'playerExperience', 'riskAndCost', 'comment'],
      },
    },
    recommendedPlan: { type: 'string', description: 'adopt now / at next stop / after reset / later / never, in order' },
    duplicatesOrConflicts: { type: 'string' },
  },
  required: ['scores', 'recommendedPlan', 'duplicatesOrConflicts'],
}

phase('Propose')
const proposals = await parallel(LENSES.map(l => () =>
  agent(`${BRIEF}\n\n${l.prompt}\n\nTask: for EACH problem give your proposal(s) or an explicit no_change with reasons. Prefix ids with ${l.key}. Say plainly when you could not verify something. Then give the overall sequencing and where you disagree with the analysis or pending plan.`,
    { label: `propose:${l.name}`, phase: 'Propose', schema: PROPOSAL_SCHEMA })))

const all = proposals.map((p, i) => (p ? { lens: LENSES[i].name, ...p } : null)).filter(Boolean)
log(`proposals: ${all.reduce((n, p) => n + p.proposals.length, 0)} from ${all.length} lenses`)
const listing = JSON.stringify(all, null, 1)

const JUDGES = [
  'Judge A: rigorous reviewer. Spot-check the key factual claims of proposals against code and data (read files, run read-only scripts). Penalize wrong or unverified evidence.',
  'Judge B: player-experience and production judge. Weigh fun/readability for a human player, risk to the live demo config and AI learning, implementation cost, measurability.',
]

phase('Judge')
const judgements = await parallel(JUDGES.map((j, i) => () =>
  agent(`${BRIEF}\n\n${j}\n\nScore EVERY proposal 0-100 with four 0-25 sub-scores (fixesRootCause, evidence, playerExperience, riskAndCost; score = sum). Be calibrated and independent; do not favor a lens. Then give a recommended plan and note duplicates/conflicts.\n\nPROPOSALS:\n${listing}`,
    { label: `judge:${i === 0 ? 'A' : 'B'}`, phase: 'Judge', schema: SCORE_SCHEMA })))

return { proposals: all, judgements: judgements.filter(Boolean) }
