"""人格の指標 M1〜M14（Docs/Persona_Formation_Design.md の 6 章）を判断ログから数える。標準ライブラリだけ・読み取りだけ。

使い方（プロジェクト直下から）:
  py -3 -X utf8 tools/persona_play/metrics/persona_metrics.py --marathon Recordings/marathon_20261028_0215
  py -3 -X utf8 tools/persona_play/metrics/persona_metrics.py --logs "Recordings/persona_log_*_0953_*.jsonl" \
      --logs "Recordings/consult_log_20261028_0953_*.jsonl" --persona consult --out <フォルダ>

入力:
  persona_log_*.jsonl  … 1 行 = 1 判断（PersonaLog.cs）。question=resolve の行が結果（ref=decisionId, reward=G）
  consult_log_*.jsonl  … 1 行 = 1 セリフか 1 判断（PersonaConsult.cs）。decision に 2 人の案・確率・関係・聞いたか
出力: 表（標準出力）、metrics.json と metrics.md（--marathon のフォルダか --out）。
marathon.py からは append_to_summary(<マラソンのフォルダ>) を呼ぶと summary.md の末尾に表、先頭に赤の行を足す。
"""
from __future__ import annotations

import argparse
import glob
import json
import math
import os
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

# ---------------------------------------------------------------------------
# 定数（設計書 3・5・6 章）
# ---------------------------------------------------------------------------

#: 段階の質問と段階の数（PersonaRequests.cs の *Levels）
STEP_LEVELS = {"budget": 6, "support": 4, "intensity": 5, "focus_share": 5, "village_budget": 5}
#: 大胆さ（M1・M14）に使う段階の質問
BOLD_QUESTIONS = ("budget", "intensity", "support")
#: 「自由」の質問（M2・M3・M8 の対象）。署名の質問は人格ごとに除く
FREE_QUESTIONS = ("budget", "buy_style", "newspaper", "intel", "support", "stream_side", "intensity",
                  "focus_share", "village_budget", "intervene")
#: 署名の質問（偏ってよい。設計書 5.3.3・9 章）
SIGNATURE = {"zundamon": set(), "metan": {"newspaper", "intel"}}
#: 判断ではない行
NON_DECISION = {"resolve", "stream_result", "run_result", "village_result"}
#: 取り返しの手（hard）を見る質問
HARD_QUESTIONS = ("intel", "stream_side", "support")

PERSONAS = ("zundamon", "metan")
PERSONA_JA = {"zundamon": "ずんだもん", "metan": "めたん", "consult": "相談", "pair": "2 人"}
SPEAKER = {"z": "zundamon", "m": "metan"}

#: 3 章の特性（M14 で比べる）
TRAITS = {"zundamon": {"risk": 0.85, "lossAversion": 1.0, "receptivity": 0.30},
          "metan": {"risk": 0.30, "lossAversion": 2.25}}
KL_EPS = {"zundamon": 0.15, "metan": 0.35}       # M6（nat）
H_MIN = {"zundamon": 1.0, "metan": 0.5}          # M3（bit）
KEV_OFF = {"zundamon": (0.50, 0.75), "metan": (0.25, 0.50)}   # M5
TOP_SHARE_MAX = 0.85                             # M2
M1_MIN, M1_RED = 0.25, 0.15
M4_Z_WIN_MINUS_LOSS = 0.3                        # 段
M4_Z_HARD_PT = 0.10                              # 取り返しの手の率（負け後 − 勝ち後）
M4_M_LOSS_MINUS_WIN = -0.3                       # 段
#: ずんだもんの「勝った後」の判断（予算）がこれ未満なら M4 を判定しない（赤にもしない）。
#: 負けが基本で勝ちはたまの驚き、という設計（ユーザー決定 2026-10-28）なので、勝ち後は少なくて当たり前
M4_Z_MIN_WIN = 20
M4_FEW_WINS = "件数不足（判定しない）"
M7_MIN = 0.80
M8_MAX = 0.10
M9_DISAGREE = (0.30, 0.45)
M9_CONDITION = (0.50, 0.65)
M10_RATIO = (1 / 5, 1 / 3)                       # マウント / だから言ったでしょ
M11_HISO_MAX, M11_AMAHERO_MIN = 0.25, 0.35
M12_MAX_PER_RUN, M12_MIN_GAP = 3, 20
M13_MEMES = (3.0, 5.0)                           # 10 分あたり
M14_MAX = 0.15
RED_METRICS = ("M2", "M4", "M9", "M10")
#: 気分で「負けた後 / 勝った後」を分ける（P2 の mood。4 章: sad は負けで上がり、conf・smug は勝ちで上がる）
MOOD_CONF_BASE = {"zundamon": 0.5, "metan": 0.0}
SAD_LOSS, SAD_CALM, CONF_WIN, SMUG_WIN = 0.15, 0.05, 0.10, 0.10
MIN_N = 5

BLOCK_BEGIN = "<!-- persona_metrics:begin -->"
BLOCK_END = "<!-- persona_metrics:end -->"
RED_BEGIN = "<!-- persona_metrics:red -->"


# ---------------------------------------------------------------------------
# 読み込み
# ---------------------------------------------------------------------------

@dataclass
class Decision:
    mode: str            # zundamon / metan / consult（どの遊び方のログか）
    role: str            # zundamon / metan（誰の判断か。相談ではずんだもん = 案、めたん = 助言）
    run: str
    seq: int             # 読み込み順（ファイル名の時刻順 → 行順）
    question: str
    choice: str
    probs: dict | None = None
    final_probs: dict | None = None
    did: str | None = None
    reward: float | None = None
    why: str | None = None
    #: rules 4（P2/P3）の項目。判断した時点の気分・選んだ手の大胆さ b・下振れ d・難しい道 hard・人格の事前分布 p_kev·exp(S)
    mood: dict | None = None
    b: float | None = None
    d: float | None = None
    hard: float | None = None
    prior: dict | None = None


@dataclass
class Line:
    mode: str
    role: str
    run: str
    seq: int
    text: str
    time: str | None = None
    meme: str | None = None
    e: str | None = None
    kind: str | None = None


@dataclass
class Data:
    decisions: list = field(default_factory=list)
    lines: list = field(default_factory=list)
    #: (mode, run) → [(seq, kind, payload)] の出来事列（判断と結果）。M4 用
    events: dict = field(default_factory=lambda: defaultdict(list))
    consult_decisions: list = field(default_factory=list)   # consult_log の decision（dict + run）
    consult_followups: Counter = field(default_factory=Counter)
    files: list = field(default_factory=list)
    run_times: dict = field(default_factory=dict)            # (mode, run) → [time 文字列]


def load_jsonl(path) -> list[dict]:
    """1 行ずつ読む。書きかけの最終行や壊れた行は飛ばす。"""
    out = []
    with open(path, encoding="utf-8") as f:
        for raw in f:
            raw = raw.strip()
            if not raw:
                continue
            try:
                out.append(json.loads(raw))
            except json.JSONDecodeError:
                continue
    return out


_RUN_RE = re.compile(r"(\d{8}_\d{4}_\d+_[zmc])")


def run_of(path, row: dict | None = None) -> str:
    if row and row.get("run"):
        return row["run"]
    m = _RUN_RE.search(Path(path).name)
    return m.group(1) if m else Path(path).stem


def mode_of(path, rows: list[dict]) -> str:
    for r in rows:
        if r.get("run"):
            suf = r["run"].rsplit("_", 1)[-1]
            return {"z": "zundamon", "m": "metan", "c": "consult"}.get(suf, r.get("persona", "zundamon"))
    if any("proposal" in r for r in rows):
        return "consult"
    for r in rows:
        if r.get("persona"):
            return r["persona"]
    return "zundamon"


def files_for_marathon(marathon_dir) -> list[Path]:
    d = Path(marathon_dir)
    tag = d.name[len("marathon_"):] if d.name.startswith("marathon_") else d.name
    rec = d.parent
    files = sorted(rec.glob(f"persona_log_*_{tag}_*.jsonl")) + sorted(rec.glob(f"consult_log_{tag}_*.jsonl"))
    return files


def load(files) -> Data:
    data = Data()
    seq = 0
    persona_files = sorted((Path(f) for f in files if Path(f).name.startswith("persona_log_")), key=lambda p: p.name)
    consult_files = sorted((Path(f) for f in files if Path(f).name.startswith("consult_log_")), key=lambda p: p.name)
    for path in persona_files:
        rows = load_jsonl(path)
        data.files.append(str(path))
        mode = mode_of(path, rows)
        rewards = {r["ref"]: r["reward"] for r in rows if r.get("question") == "resolve" and r.get("ref") and r.get("reward") is not None}
        for r in rows:
            seq += 1
            run = run_of(path, r)
            q = r.get("question")
            if r.get("time"):
                data.run_times.setdefault((mode, run), []).append(r["time"])
            if q == "resolve":
                if r.get("reward") is not None:
                    data.events[(mode, run)].append((seq, "resolve", float(r["reward"])))
                continue
            role = r.get("persona") or mode
            # 一言（単独のときだけ。相談の一言は consult_log の方が正本）
            if mode != "consult" and r.get("line"):
                data.lines.append(Line(mode, role, run, seq, r["line"], r.get("time"), r.get("meme")))
            if q in NON_DECISION or q is None or r.get("fallback"):
                continue
            choice = r.get("proposal") if mode == "consult" and r.get("proposal") is not None else r.get("choice")
            if choice is None:
                continue
            d = Decision(mode, "zundamon" if mode == "consult" else role, run, seq, q, str(choice),
                         r.get("probabilities"), r.get("finalProbabilities"), r.get("decisionId"),
                         rewards.get(r.get("decisionId")), r.get("why"),
                         r.get("mood"), r.get("b"), r.get("d"), r.get("hard"), r.get("priorProbabilities"))
            data.decisions.append(d)
            data.events[(mode, run)].append((seq, "decision", d))
    for path in consult_files:
        rows = load_jsonl(path)
        data.files.append(str(path))
        for r in rows:
            seq += 1
            run = run_of(path, r)
            dec = r.get("decision")
            if dec:
                dd = dict(dec)
                dd["run"] = run
                data.consult_decisions.append(dd)
                m = dec.get("m")
                if m and m.get("key") is not None:
                    data.decisions.append(Decision("consult", "metan", run, seq, dec.get("question") or r.get("question") or "",
                                                   str(m["key"]), m.get("probs"), None, None, None, m.get("why"),
                                                   dec.get("moodM")))
            kind = r.get("kind")
            if kind in ("told", "mount", "thanks", "blame"):
                data.consult_followups[kind] += 1
            if r.get("line") and r.get("speaker") in SPEAKER:
                data.lines.append(Line("consult", SPEAKER[r["speaker"]], run, seq, r["line"], r.get("time"),
                                       r.get("meme"), r.get("e"), kind))
            if r.get("time"):
                data.run_times.setdefault(("consult", run), []).append(r["time"])
    return data


# ---------------------------------------------------------------------------
# 計算の部品
# ---------------------------------------------------------------------------

def entropy_bits(c: Counter) -> float:
    n = sum(c.values())
    return max(0.0, -sum(v / n * math.log2(v / n) for v in c.values() if v)) if n else 0.0


def kl_nat(p: dict, q: dict, eps: float = 1e-9) -> float:
    keys = set(p) | set(q)
    sp = sum(max(0.0, float(p.get(k, 0))) for k in keys) or 1.0
    sq = sum(max(0.0, float(q.get(k, 0))) for k in keys) or 1.0
    out = 0.0
    for k in keys:
        a = max(0.0, float(p.get(k, 0))) / sp
        b = max(eps, float(q.get(k, 0)) / sq)
        if a > 0:
            out += a * math.log(a / b)
    return max(0.0, out)


def js_distance(c1: Counter, c2: Counter) -> float:
    n1, n2 = sum(c1.values()), sum(c2.values())
    if not n1 or not n2:
        return 0.0
    keys = set(c1) | set(c2)
    p = {k: c1.get(k, 0) / n1 for k in keys}
    q = {k: c2.get(k, 0) / n2 for k in keys}
    m = {k: (p[k] + q[k]) / 2 for k in keys}

    def kl2(a):
        return sum(a[k] * math.log2(a[k] / m[k]) for k in keys if a[k] > 0)
    return math.sqrt(max(0.0, 0.5 * kl2(p) + 0.5 * kl2(q)))


def percentile(xs: list[float], pct: float) -> float:
    xs = sorted(xs)
    if not xs:
        return float("nan")
    k = (len(xs) - 1) * pct / 100
    lo, hi = math.floor(k), math.ceil(k)
    return xs[lo] + (xs[hi] - xs[lo]) * (k - lo)


def step_norm(q: str, choice: str) -> float | None:
    if q not in STEP_LEVELS:
        return None
    try:
        return int(choice) / (STEP_LEVELS[q] - 1)
    except ValueError:
        return None


def is_hard(d: Decision) -> bool | None:
    if d.question not in HARD_QUESTIONS:
        return None
    if d.why == "hard_route":
        return True
    if d.question == "intel":
        return d.choice == "false"
    if d.question == "stream_side":
        return d.choice == "demon"
    return d.choice == str(STEP_LEVELS["support"] - 1)


def mean(xs):
    return sum(xs) / len(xs) if xs else None


def fmt(x, nd=2, sign=False):
    if x is None or (isinstance(x, float) and math.isnan(x)):
        return "-"
    return f"{x:+.{nd}f}" if sign else f"{x:.{nd}f}"


def in_range(x, lo=None, hi=None):
    if x is None:
        return None
    return (lo is None or x >= lo) and (hi is None or x <= hi)


def metric(mid, name, who, value, display, target, ok, note="", detail=None):
    return {"id": mid, "name": name, "who": who, "value": value, "display": display, "target": target,
            "ok": ok, "note": note, "detail": detail or {}}


# ---------------------------------------------------------------------------
# 指標
# ---------------------------------------------------------------------------

def decisions_of(data: Data, mode: str, role: str) -> list[Decision]:
    return [d for d in data.decisions if d.mode == mode and d.role == role]


def boldness(ds: list[Decision]) -> tuple[float | None, dict]:
    per_q = {}
    for q in BOLD_QUESTIONS:
        xs = [step_norm(q, d.choice) for d in ds if d.question == q]
        xs = [x for x in xs if x is not None]
        if xs:
            per_q[q] = (mean(xs), len(xs))
    return (mean([v for v, _ in per_q.values()]) if per_q else None), per_q


def mood_state(d: Decision) -> str | None:
    """判断した時点の気分で、負けた後（loss）/ 勝った後（win）/ どちらでもない（None）。気分が無ければ None。"""
    m = d.mood
    if not m:
        return None
    sad = float(m.get("sad") or 0)
    conf = float(m.get("conf") or 0)
    smug = float(m.get("smug") or 0)
    # 負けた後: しょんぼり、または負けの後の 8 判断（lossStreak。rules 4.1 から気分に出る）
    if m.get("label") == "sad" or sad >= SAD_LOSS or int(m.get("lossStreak") or 0) > 0:
        return "loss"
    # 勝った後: 自信・ドヤ、または最後の出来事が勝ち（last。rules 4.1 から）
    if m.get("label") in ("conf_high", "smug") or m.get("last") == "win" \
            or (sad < SAD_CALM and (smug >= SMUG_WIN or conf >= MOOD_CONF_BASE.get(d.role, 0) + CONF_WIN)):
        return "win"
    return None


def mood_band(d: Decision) -> str:
    """M8 の気分帯（PersonaMood の最大の気分のラベル）。"""
    return (d.mood or {}).get("label") or "neutral"


def m1(data, mode, z, m):
    bz, qz = boldness(z)
    bm, qm = boldness(m)
    if bz is None or bm is None:
        return metric("M1", "大胆さの差（Z − M）", PERSONA_JA["pair"], None, "-", f"≥ {M1_MIN}", None,
                      "2 人の判断が揃っていない")
    common = [q for q in BOLD_QUESTIONS if q in qz and q in qm]
    diff = mean([qz[q][0] - qm[q][0] for q in common])
    det = {q: {"zundamon": round(qz[q][0], 3), "metan": round(qm[q][0], 3), "nZ": qz[q][1], "nM": qm[q][1]} for q in common}
    per = " / ".join(f"{q} {qz[q][0] - qm[q][0]:+.2f}" for q in common)
    note = per + ("（0.15 未満は赤）" if diff is not None and diff < M1_RED else "")
    return metric("M1", "大胆さの差（Z − M）", PERSONA_JA["pair"], diff, fmt(diff, sign=True), f"≥ {M1_MIN}",
                  in_range(diff, M1_MIN), note, det)


def free_counts(ds: list[Decision], role: str, min_n: int):
    out = {}
    for q in FREE_QUESTIONS:
        if q in SIGNATURE.get(role, set()):
            continue
        c = Counter(d.choice for d in ds if d.question == q)
        if sum(c.values()) >= min_n:
            out[q] = c
    return out


def m2(ds, role, who, min_n):
    fc = free_counts(ds, role, min_n)
    if not fc:
        return metric("M2", "自由な質問の固まり（最多の手の割合の最大）", who, None, "-", f"全部 ≤ {TOP_SHARE_MAX}", None, "判断が少ない")
    shares = {q: c.most_common(1)[0][1] / sum(c.values()) for q, c in fc.items()}
    worst = max(shares, key=shares.get)
    bad = [f"{q} {shares[q]:.2f}" for q in sorted(shares, key=lambda k: -shares[k]) if shares[q] > TOP_SHARE_MAX]
    note = ("超え: " + ", ".join(bad)) if bad else f"最大 {worst}"
    det = {q: {"top": c.most_common(1)[0][0], "share": round(shares[q], 3), "n": sum(c.values())} for q, c in fc.items()}
    return metric("M2", "自由な質問の固まり（最多の手の割合の最大）", who, shares[worst], f"{shares[worst]:.2f}（{worst}, n={sum(fc[worst].values())}）",
                  f"全部 ≤ {TOP_SHARE_MAX}", shares[worst] <= TOP_SHARE_MAX, note, det)


def m3(ds, role, who, min_n):
    fc = {q: c for q, c in free_counts(ds, role, min_n).items() if q not in ("newspaper", "intel")}   # 2 択は上限 1 bit なので除く
    target = H_MIN[role]
    if not fc:
        return metric("M3", "質問ごとのエントロピーの最小（bit）", who, None, "-", f"≥ {target}", None, "判断が少ない")
    hs = {q: entropy_bits(c) for q, c in fc.items()}
    worst = min(hs, key=hs.get)
    low = [f"{q} {hs[q]:.2f}" for q in sorted(hs, key=hs.get) if hs[q] < target]
    return metric("M3", "質問ごとのエントロピーの最小（bit）", who, hs[worst], f"{hs[worst]:.2f}（{worst}）", f"≥ {target}",
                  hs[worst] >= target, ("下回り: " + ", ".join(low)) if low else "", {q: round(h, 3) for q, h in hs.items()})


def loss_reaction(data: Data, mode: str, role: str):
    """負けた次 / 勝った次。段: 同じ質問の 1 つ前の判断の結果で分ける（設計書の集計と同じ）。
    取り返しの手: そのランで直前に出た結果（どの質問でも）で分ける。"""
    seqs = defaultdict(list)
    for d in data.decisions:
        if d.mode == mode and d.role == role and d.question == "budget":
            seqs[d.run].append(d)
    after_loss, after_win = [], []
    for run, s in seqs.items():
        s.sort(key=lambda d: d.seq)
        for prev, cur in zip(s, s[1:]):
            if prev.reward is None:
                continue
            try:
                lvl = int(cur.choice)
            except ValueError:
                continue
            (after_loss if prev.reward < 0 else after_win).append(lvl)
    hard_loss, hard_win = [], []
    for (md, run), evs in data.events.items():
        if md != mode:
            continue
        last = None
        for _, kind, payload in sorted(evs, key=lambda e: e[0]):
            if kind == "resolve":
                last = payload
            elif payload.role == role and last is not None:
                h = is_hard(payload)
                if h is not None:
                    (hard_loss if last < 0 else hard_win).append(1.0 if h else 0.0)
    return after_loss, after_win, hard_loss, hard_win


def m4(data, mode, role, who):
    """rules 4 のログ（気分 mood・選んだ手の hard がある）なら、判断した時点の気分で負けた後 / 勝った後を分けて数える（P3）。
    無ければ前の結果の符号で分ける古い数え方。"""
    ds = [d for d in data.decisions if d.mode == mode and d.role == role]
    if any(d.mood for d in ds):
        return m4_mood(ds, role, who)
    return m4_legacy(data, mode, role, who)


def m4_mood(ds, role, who):
    def steps(state):
        return [int(d.choice) for d in ds if d.question == "budget" and d.choice.isdigit() and mood_state(d) == state]
    al, aw = steps("loss"), steps("win")
    if len(al) < 3 or len(aw) < 3:
        return metric("M4", "負けへの反応（気分で分ける）", who, None, "-", "", None,
                      f"気分が負け後 {len(al)} 件・勝ち後 {len(aw)} 件で少ない")
    d_steps = mean(al) - mean(aw)
    det = {"split": "mood", "budgetAfterLoss": round(mean(al), 3), "budgetAfterWin": round(mean(aw), 3), "nLoss": len(al), "nWin": len(aw)}
    dl = [d.d for d in ds if d.d is not None and mood_state(d) == "loss"]
    dw = [d.d for d in ds if d.d is not None and mood_state(d) == "win"]
    if dl and dw:
        det.update({"downsideAfterLoss": round(mean(dl), 3), "downsideAfterWin": round(mean(dw), 3)})
    if role == "zundamon":
        # 取り返しの手: 同じ質問どうしで負けた後 − 勝った後（件数の少ない方で重み）。質問の混ざり方（配信の日かどうか）の差を数えない
        hq = {d.question for d in ds if d.hard is not None and d.hard > 0}
        hl, hw = [], []
        num = den = 0.0
        for q in sorted(hq):
            ql = [float(d.hard) for d in ds if d.question == q and d.hard is not None and mood_state(d) == "loss"]
            qw = [float(d.hard) for d in ds if d.question == q and d.hard is not None and mood_state(d) == "win"]
            hl += ql
            hw += qw
            if ql and qw:
                wq = min(len(ql), len(qw))
                num += wq * (mean(ql) - mean(qw))
                den += wq
        d_hard = num / den if den else None
        det.update({"hardAfterLoss": None if not hl else round(mean(hl), 3), "hardAfterWin": None if not hw else round(mean(hw), 3),
                    "nHardLoss": len(hl), "nHardWin": len(hw), "hardQuestions": sorted(hq)})
        ok = (-d_steps >= M4_Z_WIN_MINUS_LOSS) and (d_hard is not None and d_hard >= M4_Z_HARD_PT)
        if len(aw) < M4_Z_MIN_WIN:
            ok = None
            det["fewWins"] = True
        disp = f"勝ち後−負け後 {-d_steps:+.2f} 段 / hard {('-' if d_hard is None else f'{d_hard * 100:+.0f}pt')}"
        return metric("M4", "負けへの反応（気分で分ける）", who, -d_steps, disp, f"勝ち後 +{M4_Z_WIN_MINUS_LOSS} 段以上・hard +{M4_Z_HARD_PT * 100:.0f}pt 以上",
                      ok, _few_wins(aw) + f"予算 負け後 {mean(al):.2f}（{len(al)}）/ 勝ち後 {mean(aw):.2f}（{len(aw)}）・hard 負け後 {len(hl)} / 勝ち後 {len(hw)} 件", det)
    return metric("M4", "負けへの反応（気分で分ける）", who, d_steps, f"負け後−勝ち後 {d_steps:+.2f} 段", f"≤ {M4_M_LOSS_MINUS_WIN} 段",
                  d_steps <= M4_M_LOSS_MINUS_WIN, f"予算 負け後 {mean(al):.2f}（{len(al)}）/ 勝ち後 {mean(aw):.2f}（{len(aw)}）", det)


def _few_wins(aw):
    """ずんだもんの勝ち後が少ないときの注記（判定しない理由）。"""
    if len(aw) >= M4_Z_MIN_WIN:
        return ""
    return f"{M4_FEW_WINS}: 勝ち後 {len(aw)} 件 < {M4_Z_MIN_WIN}（負けが基本の設計で勝ちは少ない）。"


def m4_legacy(data, mode, role, who):
    al, aw, hl, hw = loss_reaction(data, mode, role)
    if len(al) < 3 or len(aw) < 3:
        return metric("M4", "負けへの反応", who, None, "-", "", None, f"負け後 {len(al)} 件・勝ち後 {len(aw)} 件で少ない")
    d_steps = mean(al) - mean(aw)          # 負け後 − 勝ち後（段）
    det = {"budgetAfterLoss": round(mean(al), 3), "budgetAfterWin": round(mean(aw), 3), "nLoss": len(al), "nWin": len(aw)}
    if role == "zundamon":
        d_hard = (mean(hl) - mean(hw)) if hl and hw else None
        det.update({"hardAfterLoss": None if not hl else round(mean(hl), 3), "hardAfterWin": None if not hw else round(mean(hw), 3),
                    "nHardLoss": len(hl), "nHardWin": len(hw)})
        ok = (-d_steps >= M4_Z_WIN_MINUS_LOSS) and (d_hard is not None and d_hard >= M4_Z_HARD_PT)
        if len(aw) < M4_Z_MIN_WIN:
            ok = None
            det["fewWins"] = True
        disp = f"勝ち後−負け後 {-d_steps:+.2f} 段 / hard {('-' if d_hard is None else f'{d_hard * 100:+.0f}pt')}"
        return metric("M4", "負けへの反応", who, -d_steps, disp, f"勝ち後 +{M4_Z_WIN_MINUS_LOSS} 段以上・hard +{M4_Z_HARD_PT * 100:.0f}pt 以上",
                      ok, _few_wins(aw) + f"予算 負け後 {mean(al):.2f}（{len(al)}）/ 勝ち後 {mean(aw):.2f}（{len(aw)}）", det)
    return metric("M4", "負けへの反応", who, d_steps, f"負け後−勝ち後 {d_steps:+.2f} 段", f"≤ {M4_M_LOSS_MINUS_WIN} 段",
                  d_steps <= M4_M_LOSS_MINUS_WIN, f"予算 負け後 {mean(al):.2f}（{len(al)}）/ 勝ち後 {mean(aw):.2f}（{len(aw)}）", det)


def m5(ds, role, who):
    off = n = 0
    for d in ds:
        if not d.probs or d.choice not in d.probs:
            continue
        top = max(d.probs, key=lambda k: d.probs[k])
        n += 1
        off += top != d.choice
    lo, hi = KEV_OFF[role]
    if not n:
        return metric("M5", "kev の 1 位と違う手の率", who, None, "-", f"{lo}〜{hi}", None, "確率つきの判断が無い")
    v = off / n
    return metric("M5", "kev の 1 位と違う手の率", who, v, f"{v:.2f}（{n} 件）", f"{lo}〜{hi}", in_range(v, lo, hi))


def m6(ds, role, who):
    eps = KL_EPS[role]
    with_prior = [d for d in ds if d.prior and d.final_probs]
    if with_prior:
        # rules 4（P3/P4）: π_prior = p_kev·exp(S)（priorProbabilities。技能なし）。KL 上限で縮めるのと同じ量
        kls = [kl_nat(d.final_probs, d.prior) for d in with_prior]
        note = "π_prior = priorProbabilities（p_kev·exp(S)、技能なし）"
    else:
        kls = [kl_nat(d.final_probs, d.probs) for d in ds if d.probs and d.final_probs]
        note = "π_prior = kev の確率（priorProbabilities が無いログ。学習器の寄せだけを測る）"
    if not kls:
        return metric("M6", "人格への KL の 95 パーセンタイル（nat）", who, None, "-", f"≤ {eps}", None,
                      "finalProbabilities が無い（めたんの助言は consult_log に学習後の確率が無い）")
    v = percentile(kls, 95)
    return metric("M6", "人格への KL の 95 パーセンタイル（nat）", who, v, f"{v:.2f}（{len(kls)} 件）", f"≤ {eps}", v <= eps, note,
                  {"mean": round(mean(kls), 4), "n": len(kls)})


def nb_accuracy(recs: list[tuple[str, str, str]]) -> tuple[float | None, int]:
    """recs = (label, run, token)。ラン単位で抜いて残りで学ぶ素朴ベイズ（判断 1 件で当てる）。"""
    labels = sorted({r[0] for r in recs})
    if len(labels) < 2:
        return None, 0
    ok = tot = 0
    for hold in sorted({r[1] for r in recs}):
        tr = [x for x in recs if x[1] != hold]
        te = [x for x in recs if x[1] == hold]
        cnt = {p: Counter(x[2] for x in tr if x[0] == p) for p in labels}
        prior = {p: sum(1 for x in tr if x[0] == p) for p in labels}
        if any(v == 0 for v in prior.values()):
            continue
        tot_n = {p: sum(cnt[p].values()) for p in labels}
        vocab = len({x[2] for x in tr}) + 1
        for x in te:
            s = {p: math.log((cnt[p][x[2]] + 1) / (tot_n[p] + vocab)) for p in labels}
            ok += max(s, key=s.get) == x[0]
            tot += 1
    return (ok / tot if tot else None), tot


def m7(recs, m2_metan_ok):
    acc, n = nb_accuracy(recs)
    if acc is None:
        return metric("M7", "誰の判断か当てる（素朴ベイズ・ラン単位の交差検証）", PERSONA_JA["pair"], None, "-", f"≥ {M7_MIN}", None,
                      "2 人の判断が揃っていない")
    ok = acc >= M7_MIN and bool(m2_metan_ok)
    note = "" if m2_metan_ok else "めたんの M2（固まり）が範囲外なので、固まりで水増しされた値"
    return metric("M7", "誰の判断か当てる（素朴ベイズ・ラン単位の交差検証）", PERSONA_JA["pair"], acc, f"{acc:.3f}（{n} 件）",
                  f"≥ {M7_MIN}（M2 を満たした上で）", ok, note)


def m8(ds, role, who, min_n):
    """前半と後半のずれ。気分（mood）があるログでは、同じ気分帯どうしで前半と後半を比べ、帯の件数で重み付けした平均を質問ごとに出す
    （気分で手が動くのは設計どおりなので、気分の配分が前後で違うだけのずれは数えない）。気分が無ければ質問ごとにそのまま比べる。"""
    if any(d.mood for d in ds):
        return m8_mood(ds, role, who, min_n)
    by_q = defaultdict(list)
    for d in sorted(ds, key=lambda d: d.seq):
        if d.question in FREE_QUESTIONS:
            by_q[d.question].append(d.choice)
    js = {}
    for q, xs in by_q.items():
        if len(xs) < 2 * min_n:
            continue
        h = len(xs) // 2
        js[q] = js_distance(Counter(xs[:h]), Counter(xs[h:]))
    if not js:
        return metric("M8", "前半と後半のずれ（JS 距離の最大）", who, None, "-", f"≤ {M8_MAX}", None, "判断が少ない")
    worst = max(js, key=js.get)
    big = [f"{q} {js[q]:.2f}" for q in sorted(js, key=lambda k: -js[k]) if js[q] > M8_MAX]
    return metric("M8", "前半と後半のずれ（JS 距離の最大）", who, js[worst], f"{js[worst]:.2f}（{worst}）", f"≤ {M8_MAX}",
                  js[worst] <= M8_MAX, ("超え: " + ", ".join(big)) if big else "", {q: round(v, 3) for q, v in js.items()})


def null_js(c1: Counter, c2: Counter, reps: int = 60, seed: int = 1) -> float:
    """同じ分布から n1・n2 件を引いたときにも出る JS 距離（2 つを混ぜて並べ替えた平均）。件数が少ないほど大きい。"""
    import random
    pool = [k for k, v in c1.items() for _ in range(v)] + [k for k, v in c2.items() for _ in range(v)]
    n1 = sum(c1.values())
    rng = random.Random(seed)
    acc = 0.0
    for _ in range(reps):
        rng.shuffle(pool)
        acc += js_distance(Counter(pool[:n1]), Counter(pool[n1:]))
    return acc / reps


def m8_mood(ds, role, who, min_n):
    """同じ気分帯どうしの前半 / 後半の JS 距離から、その件数で偶然にも出る JS（並べ替え）を引いた「ずれの超過」を、
    帯の件数で重み付けして質問ごとに平均する（気分で手が動くのは設計どおり・件数の少ない帯の揺れはずれではない）。"""
    by_q = defaultdict(list)
    for d in sorted(ds, key=lambda d: d.seq):
        if d.question in FREE_QUESTIONS and d.mood:
            by_q[d.question].append(d)
    js, raw, banded = {}, {}, {}
    for q, xs in by_q.items():
        if len(xs) < 2 * min_n:
            continue
        h = len(xs) // 2
        first, second = xs[:h], xs[h:]
        raw[q] = js_distance(Counter(d.choice for d in first), Counter(d.choice for d in second))
        tot = acc = acc_raw = 0.0
        for band in sorted({mood_band(d) for d in xs}):
            c1 = Counter(d.choice for d in first if mood_band(d) == band)
            c2 = Counter(d.choice for d in second if mood_band(d) == band)
            n1, n2 = sum(c1.values()), sum(c2.values())
            if n1 < 3 or n2 < 3:
                continue
            w = n1 + n2
            j = js_distance(c1, c2)
            acc_raw += w * j
            acc += w * max(0.0, j - null_js(c1, c2))
            tot += w
        if tot:
            js[q] = acc / tot
            banded[q] = acc_raw / tot
    if not js:
        return metric("M8", "前半と後半のずれ（同じ気分帯・偶然を引いた JS の最大）", who, None, "-", f"≤ {M8_MAX}", None, "判断が少ない")
    worst = max(js, key=js.get)
    big = [f"{q} {js[q]:.2f}" for q in sorted(js, key=lambda k: -js[k]) if js[q] > M8_MAX]
    note = (("超え: " + ", ".join(big) + " ") if big else "") + \
        f"（{worst}: 気分帯の JS {banded.get(worst, 0):.2f}・気分を分けない JS {raw.get(worst, 0):.2f}。値は偶然の JS を引いた超過）"
    return metric("M8", "前半と後半のずれ（同じ気分帯・偶然を引いた JS の最大）", who, js[worst], f"{js[worst]:.2f}（{worst}）", f"≤ {M8_MAX}",
                  js[worst] <= M8_MAX, note, {"excess": {q: round(v, 3) for q, v in js.items()},
                                               "banded": {q: round(v, 3) for q, v in banded.items()},
                                               "raw": {q: round(v, 3) for q, v in raw.items()}})


def m9(data):
    rates = {}
    for rel in ("disagree", "condition"):
        xs = [d for d in data.consult_decisions if d.get("relation") == rel and isinstance(d.get("followed"), bool)]
        rates[rel] = (sum(1 for d in xs if d["followed"]), len(xs))
    (fd, nd), (fc, nc) = rates["disagree"], rates["condition"]
    if nd < MIN_N and nc < MIN_N:
        return metric("M9", "助言を聞く率（反対 / 条件つき）", PERSONA_JA["zundamon"], None, "-", "", None, "相談の判断が少ない")
    rd = fd / nd if nd else None
    rc = fc / nc if nc else None
    ok = bool(in_range(rd, *M9_DISAGREE)) and bool(in_range(rc, *M9_CONDITION))
    return metric("M9", "助言を聞く率（反対 / 条件つき）", PERSONA_JA["zundamon"], rd,
                  f"{fd}/{nd} = {fmt(rd, 3)} / {fc}/{nc} = {fmt(rc, 3)}",
                  f"反対 {M9_DISAGREE[0]}〜{M9_DISAGREE[1]} / 条件つき {M9_CONDITION[0]}〜{M9_CONDITION[1]}", ok,
                  detail={"disagree": {"followed": fd, "n": nd}, "condition": {"followed": fc, "n": nc}})


def m10(data):
    c = data.consult_followups
    mount, told = c["mount"], c["told"]
    if mount + told == 0:
        return metric("M10", "気分の弧（マウント : 泣き）", PERSONA_JA["zundamon"], None, "-", "1:3〜1:5", None, "後日談が無い")
    ratio = mount / told if told else float("inf")
    disp = f"{mount} : {told}" + (f"（1:{told / mount:.1f}）" if mount else "")
    return metric("M10", "気分の弧（マウント : 泣き）", PERSONA_JA["zundamon"], ratio if told else None, disp, "1:3〜1:5",
                  bool(in_range(ratio, *M10_RATIO)), f"お礼 {c['thanks']}・外れの文句 {c['blame']}", dict(c))


def m11(data, mode):
    zl = [l for l in data.lines if l.mode == mode and l.role == "zundamon" and l.e]
    if not zl:
        return metric("M11", "ずんだもんの表情（hiso / ama+hero）", PERSONA_JA["zundamon"], None, "-",
                      f"≤ {M11_HISO_MAX} / ≥ {M11_AMAHERO_MIN}", None, "persona_log に表情（e）が無い（相談ログだけにある）")
    c = Counter(l.e for l in zl)
    n = len(zl)
    hiso, ah = c["hiso"] / n, (c["ama"] + c["hero"]) / n
    ok = hiso <= M11_HISO_MAX and ah >= M11_AMAHERO_MIN
    return metric("M11", "ずんだもんの表情（hiso / ama+hero）", PERSONA_JA["zundamon"], hiso, f"{hiso:.0%} / {ah:.0%}（{n} 行）",
                  f"≤ {M11_HISO_MAX:.0%} / ≥ {M11_AMAHERO_MIN:.0%}", ok, ", ".join(f"{k} {v}" for k, v in c.most_common(5)), dict(c))


def m12(data, mode, role, who):
    ls = [l for l in data.lines if l.mode == mode and l.role == role]
    if not ls:
        return metric("M12", "一言の繰り返し（1 ランの最多回数 / 同じ行の最小間隔）", who, None, "-",
                      f"≤ {M12_MAX_PER_RUN} 回 / ≥ {M12_MIN_GAP} 行", None, "一言が無い")
    by_run = defaultdict(list)
    for l in sorted(ls, key=lambda l: l.seq):
        by_run[l.run].append(l.text)
    worst_cnt, worst_line, min_gap, gap_line = 0, "", None, ""
    over = 0
    for run, xs in by_run.items():
        c = Counter(xs)
        t, n = c.most_common(1)[0]
        if n > worst_cnt:
            worst_cnt, worst_line = n, t
        over += sum(1 for v in c.values() if v > M12_MAX_PER_RUN)
        last = {}
        for i, t in enumerate(xs):
            if t in last:
                g = i - last[t]
                if min_gap is None or g < min_gap:
                    min_gap, gap_line = g, t
            last[t] = i
    total = Counter(l.text for l in ls)
    ok = worst_cnt <= M12_MAX_PER_RUN and (min_gap is None or min_gap >= M12_MIN_GAP)
    note = f"最多「{worst_line[:24]}」/ 間隔最小「{gap_line[:24]}」/ 4 回以上の行 {over}（ラン × 行）/ 種類 {len(total)} / {len(ls)}"
    return metric("M12", "一言の繰り返し（1 ランの最多回数 / 同じ行の最小間隔）", who, worst_cnt,
                  f"{worst_cnt} 回 / {('-' if min_gap is None else min_gap)} 行", f"≤ {M12_MAX_PER_RUN} 回 / ≥ {M12_MIN_GAP} 行", ok, note,
                  {"distinct": len(total), "lines": len(ls), "minGap": min_gap, "maxPerRun": worst_cnt,
                   "topOverall": total.most_common(3)})


def run_minutes(data: Data, mode: str) -> float:
    tot = 0.0
    for (md, run), ts in data.run_times.items():
        if md != mode or len(ts) < 2:
            continue
        parsed = []
        for t in ts:
            try:
                parsed.append(datetime.fromisoformat(t))
            except ValueError:
                pass
        if len(parsed) >= 2:
            tot += (max(parsed) - min(parsed)).total_seconds() / 60
    return tot


def m13(data, mode, role, who):
    ls = [l for l in data.lines if l.mode == mode and (role is None or l.role == role)]
    mins = run_minutes(data, mode)
    memes = sum(1 for l in ls if l.meme)
    if not mins:
        return metric("M13", "持ちネタの頻度（ミーム / 10 分）", who, None, "-", f"{M13_MEMES[0]:.0f}〜{M13_MEMES[1]:.0f}", None, "時刻が無い")
    v = memes / mins * 10
    note = (f"ミーム {memes} 回 / 実時間 {mins:.0f} 分（マラソンは x2・会話飛ばしなので動画の 10 分とは違う）。"
            "めたんのまっすぐな褒め・めたんの崩れはログに印が無く数えられない")
    return metric("M13", "持ちネタの頻度（ミーム / 10 分）", who, v, f"{v:.2f}", f"{M13_MEMES[0]:.0f}〜{M13_MEMES[1]:.0f}",
                  in_range(v, *M13_MEMES), note, {"memes": memes, "minutes": round(mins, 1), "lines": len(ls)})


def m14(ds, role, who, receptivity=None):
    est, diffs = {}, {}
    risk, _ = boldness(ds)
    if risk is not None:
        est["risk"] = risk
        diffs["risk"] = abs(risk - TRAITS[role]["risk"])
    if receptivity is not None and "receptivity" in TRAITS[role]:
        est["receptivity"] = receptivity
        diffs["receptivity"] = abs(receptivity - TRAITS[role]["receptivity"])
    if not diffs:
        return metric("M14", "特性の逆推定（3 章の値との差の最大）", who, None, "-", f"≤ {M14_MAX}", None, "推定できる特性が無い")
    worst = max(diffs, key=diffs.get)
    disp = " / ".join(f"{k} {est[k]:.2f}（目標 {TRAITS[role][k]}）" for k in est)
    return metric("M14", "特性の逆推定（3 章の値との差の最大）", who, diffs[worst], f"差 {diffs[worst]:.2f}: {disp}", f"≤ {M14_MAX}",
                  diffs[worst] <= M14_MAX, "λ（損失回避）は気分・下振れ d(a) が無く推定の定義が決まらないので未計測",
                  {"estimates": {k: round(v, 3) for k, v in est.items()}})


def compute(data: Data, persona: str | None = None, min_n: int = MIN_N) -> dict:
    """読み込んだログから指標を数える。persona = zundamon / metan / consult / None（全部）。"""
    modes = sorted({d.mode for d in data.decisions} | {l.mode for l in data.lines})
    results = []
    want = (lambda md: persona is None or persona == md or (persona in PERSONAS and md == persona))
    solo = [md for md in modes if md in PERSONAS]
    if solo and (persona in (None, *PERSONAS)):
        z = decisions_of(data, "zundamon", "zundamon")
        m = decisions_of(data, "metan", "metan")
        roles = [r for r in PERSONAS if r in solo and (persona is None or persona == r)]
        per = {}
        for r in roles:
            ds = z if r == "zundamon" else m
            who = PERSONA_JA[r]
            per[r] = {"M2": m2(ds, r, who, min_n), "M3": m3(ds, r, who, min_n), "M4": m4(data, r, r, who),
                      "M5": m5(ds, r, who), "M6": m6(ds, r, who), "M8": m8(ds, r, who, min_n),
                      "M11": m11(data, r) if r == "zundamon" else None,
                      "M12": m12(data, r, r, who), "M13": m13(data, r, r, who), "M14": m14(ds, r, who)}
        m2_metan = m2(m, "metan", "", min_n)["ok"] if m else None
        pair = []
        if z and m:
            pair.append(m1(data, "solo", z, m))
            recs = [(d.role, d.run, f"{d.question}:{d.choice}") for d in z + m]
            pair.append(m7(recs, m2_metan))
        results.append({"group": "solo", "label": "単独プレイ（ずんだもん / めたん）", "metrics": _order(pair, per)})
    if "consult" in modes and want("consult"):
        z = decisions_of(data, "consult", "zundamon")
        m = decisions_of(data, "consult", "metan")
        m9_ = m9(data)
        fol = [d for d in data.consult_decisions if d.get("relation") in ("disagree", "condition") and isinstance(d.get("followed"), bool)]
        recept = (sum(1 for d in fol if d["followed"]) / len(fol)) if len(fol) >= MIN_N else None
        per = {
            "zundamon": {"M2": m2(z, "zundamon", "ずんだもん（案）", min_n), "M3": m3(z, "zundamon", "ずんだもん（案）", min_n),
                         "M4": m4(data, "consult", "zundamon", "ずんだもん（案）"), "M5": m5(z, "zundamon", "ずんだもん（案）"),
                         "M6": m6(z, "zundamon", "ずんだもん（案）"), "M8": m8(z, "zundamon", "ずんだもん（案）", min_n),
                         "M9": m9_, "M10": m10(data), "M11": m11(data, "consult"),
                         "M12": m12(data, "consult", "zundamon", "ずんだもん"),
                         "M14": m14(z, "zundamon", "ずんだもん（案）", recept)},
            "metan": {"M2": m2(m, "metan", "めたん（助言）", min_n), "M3": m3(m, "metan", "めたん（助言）", min_n),
                      "M4": m4(data, "consult", "metan", "めたん（助言）") if any(d.mood for d in m) else None,
                      "M5": m5(m, "metan", "めたん（助言）"), "M8": m8(m, "metan", "めたん（助言）", min_n),
                      "M12": m12(data, "consult", "metan", "めたん"), "M14": m14(m, "metan", "めたん（助言）")},
        }
        pair = []
        if z and m:
            pair.append(m1(data, "consult", z, m))
            recs = []
            for d in data.consult_decisions:
                q = d.get("question") or ""
                for k, role in (("z", "zundamon"), ("m", "metan")):
                    if d.get(k) and d[k].get("key") is not None:
                        recs.append((role, d["run"], f"{q}:{d[k]['key']}"))
            pair.append(m7(recs, per["metan"]["M2"]["ok"]))
        pair.append(m13(data, "consult", None, "2 人"))
        results.append({"group": "consult", "label": "相談プレイ（ずんだもんの案・めたんの助言）", "metrics": _order(pair, per)})
    red = []
    for g in results:
        for mt in g["metrics"]:
            if mt["id"] in RED_METRICS and mt["ok"] is False:
                red.append(f"{mt['id']} {mt['who']}（{mt['display']}、目標 {mt['target']}）")
    return {"generatedAt": datetime.now().isoformat(timespec="seconds"), "persona": persona or "all", "files": data.files,
            "counts": {"decisions": len(data.decisions), "lines": len(data.lines), "consultDecisions": len(data.consult_decisions)},
            "groups": results, "redFlags": red, "notComputable": NOT_COMPUTABLE}


def _order(pair, per):
    out = list(pair)
    for r in PERSONAS:
        for k, v in (per.get(r) or {}).items():
            if v is not None:
                out.append(v)
    return sorted(out, key=lambda x: (int(x["id"][1:]), x["who"]))


#: いまのログでは数えられない（または代わりの量で数えている）もの。P1〜P6 で足すログの項目
NOT_COMPUTABLE = [
    # M4・M6 は rules 4 のログ（mood・hard・priorProbabilities）があればそれで数える。古いログだけのときは代用のまま
    {"id": "M7", "what": "質問:選択:気分 の分類", "now": "質問:選択 だけ", "need": "P2: 判断の行に `mood`（conf / sad / smug / irk と最大の気分のラベル）"},
    {"id": "M11", "what": "単独プレイの表情", "now": "相談ログ（consult_log の e）だけ", "need": "P2/P6: persona_log の一言の行に `e`（表情）"},
    {"id": "M13", "what": "めたんのまっすぐな褒め・めたんの崩れ", "now": "数えていない（ミームだけ）",
     "need": "P3/P6: 一言の行に `beat`（praise / collapse / mount / cry …）。崩れは P3 の irk の 1 回で `collapse: true`"},
    {"id": "M13", "what": "動画の 10 分あたり", "now": "実時間（x2・会話飛ばし込み）", "need": "P2: persona_log に `timeScale`（consult_log にはある）"},
    {"id": "M14", "what": "λ（損失回避）の逆推定", "now": "未計測", "need": "P2/P3: `mood`・`cashStress`・d(a) が出れば、負け後の下振れ回避の強さから推定"},
    {"id": "M10/M12", "what": "気分の弧の細かい遷移（自信→失敗→泣く→挑戦→マウント）", "now": "後日談の種類の数だけ",
     "need": "P2: 結果の行・後日談の行に `moodBefore` / `moodAfter`"},
]


# ---------------------------------------------------------------------------
# 出力
# ---------------------------------------------------------------------------

def _ok_mark(ok, m=None):
    if ok is None and m is not None and m.get("detail", {}).get("fewWins"):
        return M4_FEW_WINS
    return "-" if ok is None else ("OK" if ok else "NG")


def format_table(result: dict) -> str:
    out = []
    for g in result["groups"]:
        out.append(f"== {g['label']} ==")
        rows = [("#", "指標", "対象", "値", "目標", "判定")]
        for m in g["metrics"]:
            rows.append((m["id"], m["name"], m["who"], m["display"], m["target"], _ok_mark(m["ok"], m)))
        widths = [max(_w(r[i]) for r in rows) for i in range(6)]
        for r in rows:
            out.append("  ".join(_pad(c, widths[i]) for i, c in enumerate(r)))
        notes = [f"  {m['id']} {m['who']}: {m['note']}" for m in g["metrics"] if m["note"]]
        if notes:
            out.append("  注:")
            out += notes
        out.append("")
    if result["redFlags"]:
        out.append("!! 人格の回帰: " + " / ".join(result["redFlags"]))
    return "\n".join(out)


def _w(s: str) -> int:
    return sum(2 if ord(ch) > 0x2E7F else 1 for ch in str(s))


def _pad(s, w):
    s = str(s)
    return s + " " * (w - _w(s))


def red_line(result: dict) -> str | None:
    if not result["redFlags"]:
        return None
    return "**!!! 人格の回帰**: " + " / ".join(result["redFlags"])


def markdown_block(result: dict) -> str:
    lines = [BLOCK_BEGIN, "## 人格の指標（persona_metrics）", ""]
    rl = red_line(result)
    if rl:
        lines += [rl, ""]
    c = result["counts"]
    lines.append(f"- ログ {len(result['files'])} 本 / 判断 {c['decisions']} 件 / 一言 {c['lines']} 行 / 相談の判断 {c['consultDecisions']} 件"
                 f"（{result['generatedAt']}、目標は Docs/Persona_Formation_Design.md の 6 章）")
    for g in result["groups"]:
        lines += ["", f"### {g['label']}", "", "| # | 指標 | 対象 | 値 | 目標 | 判定 | 注 |", "|---|---|---|---|---|---|---|"]
        for m in g["metrics"]:
            ok = _ok_mark(m["ok"], m)
            ok = f"**{ok}**" if ok == "NG" else ok
            lines.append(f"| {m['id']} | {m['name']} | {m['who']} | {m['display']} | {m['target']} | {ok} | {m['note'].replace('|', '／')} |")
    lines += ["", "未計測・代用（ログに項目を足す段階）: " + "、".join(f"{x['id']} {x['what']}（{x['need'].split(':')[0]}）" for x in result["notComputable"]),
              BLOCK_END]
    return "\n".join(lines)


def write_outputs(result: dict, out_dir) -> tuple[Path, Path]:
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    jp = out / "metrics.json"
    mp = out / "metrics.md"
    jp.write_text(json.dumps(result, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
    mp.write_text(markdown_block(result) + "\n", encoding="utf-8")
    return jp, mp


def run_marathon(marathon_dir, persona: str | None = None, min_n: int = MIN_N) -> dict:
    return compute(load(files_for_marathon(marathon_dir)), persona, min_n)


def append_to_summary(marathon_dir, result: dict | None = None, write_files: bool = True) -> Path:
    """marathon.py 用: summary.md の末尾に指標の表を足し（前に足したものは差し替え）、範囲外があれば先頭（見出しの次）に赤の行を出す。
    metrics.json / metrics.md もマラソンのフォルダに書く。失敗しても例外は呼び出し側で握りつぶしてよい（読み取りだけ）。"""
    d = Path(marathon_dir)
    if result is None:
        result = run_marathon(d)
    if write_files:
        write_outputs(result, d)
    path = d / "summary.md"
    text = path.read_text(encoding="utf-8") if path.exists() else "# 人格マラソンのまとめ\n"
    text = merge_summary(text, result)
    path.write_text(text, encoding="utf-8")
    return path


def merge_summary(text: str, result: dict) -> str:
    # 前回の表と赤の行を消す
    text = re.sub(re.escape(BLOCK_BEGIN) + r".*?" + re.escape(BLOCK_END) + r"\n?", "", text, flags=re.S)
    lines = []
    for l in text.rstrip("\n").split("\n"):
        if RED_BEGIN in l:
            if lines and lines[-1] == "":
                lines.pop()        # 赤の行の前に足した空行も消す
            continue
        lines.append(l)
    rl = red_line(result)
    if rl:
        at = 1 if lines and lines[0].startswith("#") else 0
        lines[at:at] = ["", f"{rl} {RED_BEGIN}"]
    return "\n".join(lines) + "\n\n" + markdown_block(result) + "\n"


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def main(argv=None) -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description="人格の指標 M1〜M14 を判断ログから数える（読み取りだけ）")
    ap.add_argument("--marathon", help="Recordings/marathon_<日時> のフォルダ（判断ログは同じ Recordings から探す）")
    ap.add_argument("--logs", action="append", default=[], help="persona_log_*.jsonl / consult_log_*.jsonl の glob（複数可）")
    ap.add_argument("--persona", choices=["zundamon", "metan", "consult"], help="この遊び方だけ（省略で全部）")
    ap.add_argument("--out", help="metrics.json / metrics.md の書き先（省略時は --marathon のフォルダ。--logs だけなら書かない）")
    ap.add_argument("--no-write", action="store_true", help="表を出すだけ")
    ap.add_argument("--min-n", type=int, default=MIN_N, help=f"質問ごとに数える最小の件数（既定 {MIN_N}）")
    ap.add_argument("--fail-on-red", action="store_true", help="M2・M4・M9・M10 が範囲外なら終了コード 2")
    a = ap.parse_args(argv)
    files = []
    if a.marathon:
        files += files_for_marathon(a.marathon)
    for g in a.logs:
        files += [Path(p) for p in sorted(glob.glob(g))]
    files = sorted(set(map(Path, files)), key=lambda p: p.name)
    if not files:
        print("ログが見つかりません（--marathon か --logs を指定）", file=sys.stderr)
        return 1
    result = compute(load(files), a.persona, a.min_n)
    print(format_table(result))
    out = a.out or (a.marathon if a.marathon else None)
    if out and not a.no_write:
        jp, mp = write_outputs(result, out)
        print(f"\n書きました: {jp} / {mp}")
    return 2 if (a.fail_on_red and result["redFlags"]) else 0


if __name__ == "__main__":
    sys.exit(main())
