"""ミーム辞書（memes_YYYY-MM.json）をオフラインで検査し、C# の選び方をまねた一言のサンプルを出す。

標準ライブラリだけ。Unity も Ollama も使わない。

    py -3 -I tools/persona_play/memes/check_lexicon.py                       # 既定: memes_2026-10.json
    py -3 -I tools/persona_play/memes/check_lexicon.py path/to/memes.json --seed 7 --runs 2000

検査（どれかに当たれば終了コード 1）:
- 必須項目（digest.mjs の REQUIRED と question_keys）が欠けていない
- who が zundamon / both / metan_reluctant のどれか
- question_keys が PersonaLines.cs のテンプレートのキー（"question" / "question|帯" / "question|win/lose"）にある
- 使わない言い回し（「効いて」「効いた」「回目」）が phrase / meaning / zundamon_line / metan_reaction に無い
- id が重複していない

サンプルは Assets/Scripts/Debug/AutoPlay/Jev/PersonaLines.cs の ApplyMeme と同じ規則（確率・上限・tone の寄せ・
頭の 1 文とのつなぎ・めたんの「知らないけど」）。C# と同じ乱数列にはならない（System.Random と Python の random は別物）。
"""
import argparse
import json
import random
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
LINES_CS = ROOT / "Assets/Scripts/Debug/AutoPlay/Jev/PersonaLines.cs"
DEFAULT_LEXICON = HERE / "memes_2026-10.json"

BANNED = ("効いて", "効いた", "回目")
REQUIRED = ("id", "phrase", "meaning", "usage", "tone", "who", "zundamon_line", "metan_reaction", "sources", "trend_score", "question_keys")
WHO = ("zundamon", "both", "metan_reluctant")
TONES = ("hype", "sad", "smug", "deadpan", "panic", "hero", "surprise")
RETIRE_BELOW = 0.1

# ---- PersonaLines.cs と同じ定数 ----
P_Z, P_Z_CLIMAX, P_Z_BIG = 0.15, 0.3, 0.4
P_M, P_M_CLIMAX = 0.08, 0.15
MAX_Z, MAX_M_REACT, MAX_M_RELUCTANT = 5, 3, 1
MAX_COMBINED = 48
MAX_ORD_Z, MAX_ORD_M = 2, 1
MAX_NONRESULT_Z, MAX_NONRESULT_M = 3, 2
GAP_ORDINARY, GAP_CLIMAX, GAP_RESULT = 48, 24, 24
MOUNT_SMUG, LOSE_SAD, OVERCONF_HYPE = 3.0, 2.0, 1.5


def load_templates(cs_path):
    """PersonaLines.cs の Templates 辞書を {"persona|question(|band)": [行, ...]} で読む。"""
    text = cs_path.read_text(encoding="utf-8")
    templates = {}
    for m in re.finditer(r'\["((?:zundamon|metan)\|[^"]+)"\]\s*=\s*new\[\]\s*\{(.*?)\n\s*\},', text, re.S):
        templates[m.group(1)] = re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(2))
    return templates


def valid_keys(templates):
    keys = set()
    for k in templates:
        rest = k.split("|", 1)[1]
        keys.add(rest)
        keys.add(rest.split("|", 1)[0])
    return keys


def is_clean(line):
    return not line or not any(b in line for b in BANNED)


def usable(m):
    """PersonaMemeLexicon.Usable と同じ。"""
    if not m.get("id") or not m.get("question_keys"):
        return False
    if m.get("retired") is True or str(m.get("status", "")).lower() == "retired":
        return False
    w = m.get("weight")
    if w is not None and w < RETIRE_BELOW:
        return False
    if selection_weight(m) <= 0:
        return False
    return m.get("who") in WHO and bool(m.get("zundamon_line")) and bool(m.get("metan_reaction"))


def selection_weight(m):
    w = m.get("weight")
    return max(0.0, float(m.get("trend_score", 1))) * max(0.0, float(1.0 if w is None else w))


def validate(data, keys):
    errors, warnings = [], []
    memes = data.get("memes")
    if not isinstance(memes, list) or not memes:
        return ["memes が無いか空"], warnings
    seen = set()
    for i, m in enumerate(memes):
        name = m.get("id") or f"#{i}"
        for f in REQUIRED:
            if f not in m or m[f] in (None, "", []):
                errors.append(f"{name}: 必須項目 {f} が無い")
        if m.get("id") in seen:
            errors.append(f"{name}: id が重複")
        seen.add(m.get("id"))
        if m.get("who") not in WHO:
            errors.append(f"{name}: who が不正 ({m.get('who')})")
        if m.get("tone") not in TONES:
            warnings.append(f"{name}: tone が想定外 ({m.get('tone')})")
        for k in m.get("question_keys") or []:
            if k not in keys:
                errors.append(f"{name}: question_keys の {k!r} が PersonaLines.cs に無い")
        for f in ("phrase", "meaning", "zundamon_line", "metan_reaction"):
            v = m.get(f) or ""
            for b in BANNED:
                if b in v:
                    errors.append(f"{name}: {f} に使わない言い回し「{b}」")
        if m.get("weight") is not None and m["weight"] < RETIRE_BELOW:
            warnings.append(f"{name}: weight {m['weight']} < {RETIRE_BELOW}（使わない）")
    return errors, warnings


class Run:
    def __init__(self, memes, rng):
        self.memes, self.rng = memes, rng
        self.used, self.count, self.reluctant = set(), 0, 0
        self.ordinary, self.nonresult, self.lines, self.last_meme_line = 0, 0, 0, -1


def first_sentence(line):
    idx = [line.find(c) for c in "！。？!?" if line.find(c) >= 0]
    return line[: min(idx) + 1] if idx else line


def apply_meme(persona, question, band_or_outcome, result, template_line, choice, pct, big_swing, run, force=False):
    """PersonaLines.ApplyMeme をまねる。戻り値 (line, meme_id or None, cue or None, p)。"""
    z, mt = persona == "zundamon", persona == "metan"
    run.lines += 1
    climax = result or (band_or_outcome == "High" and question in ("budget", "stream_side"))
    can_react = run.count < (MAX_Z if z else MAX_M_REACT)
    can_rel = mt and run.reluctant < MAX_M_RELUCTANT
    if not climax and run.ordinary >= (MAX_ORD_Z if z else MAX_ORD_M):
        can_react = False
    if not result and run.nonresult >= (MAX_NONRESULT_Z if z else MAX_NONRESULT_M):
        can_react = False
    gap = GAP_RESULT if result else GAP_CLIMAX if climax else GAP_ORDINARY
    if run.last_meme_line >= 0 and run.lines - run.last_meme_line < gap:
        return template_line, None, None, 0.0
    if not can_react and not can_rel:
        return template_line, None, None, 0.0
    key_band = f"{question}|{band_or_outcome}"
    mount = z and result and band_or_outcome == "win"
    lose = result and band_or_outcome == "lose"
    overconf = z and not result and band_or_outcome == "High" and question in ("budget", "intensity", "stream_side")
    cands, weights = [], []
    for m in run.memes:
        if m["id"] in run.used:
            continue
        qk = m["question_keys"]
        if question not in qk and key_band not in qk:
            continue
        rel = m["who"] == "metan_reluctant"
        if z and rel:
            continue
        if mt and (not can_rel if rel else not can_react):
            continue
        w = selection_weight(m)
        if mount and m.get("tone") == "smug":
            w *= MOUNT_SMUG
        if lose and m.get("tone") in ("sad", "panic"):
            w *= LOSE_SAD
        if overconf and m.get("tone") == "hype":
            w *= OVERCONF_HYPE
        if w > 0:
            cands.append(m)
            weights.append(w)
    if not cands:
        return template_line, None, None, 0.0
    p = (P_Z_BIG if result and big_swing else P_Z_CLIMAX if climax else P_Z) if z else (P_M_CLIMAX if climax else P_M)
    if not force and run.rng.random() >= p:
        return template_line, None, None, p
    meme = run.rng.choices(cands, weights=weights)[0]
    cue, reluctant = None, False
    if z:
        line = meme["zundamon_line"]
        lead = first_sentence(template_line)
        if lead and lead[-1] not in "！。？!?":
            lead += "。"
        detail = (choice and choice in lead) or (pct and pct in lead)
        if detail and len(lead) + 1 + len(line) <= MAX_COMBINED:
            line = lead + " " + line
    elif meme["who"] == "metan_reluctant":
        reluctant = True
        line = meme["metan_reaction"]
        if "知らない" not in line and "元ネタじゃない" not in line:
            line += "……知らないけど"
        cue = meme["zundamon_line"]
    else:
        line, cue = meme["metan_reaction"], meme["zundamon_line"]
    run.used.add(meme["id"])
    if not line or not is_clean(line):
        return template_line, None, None, p
    if reluctant:
        run.reluctant += 1
    else:
        run.count += 1
        if not climax:
            run.ordinary += 1
        if not result:
            run.nonresult += 1
    run.last_meme_line = run.lines
    return line, meme["id"], cue, p


def template_line(templates, persona, question, band, choice, pct, rng):
    for k in (f"{persona}|{question}|{band}", f"{persona}|{question}", f"{persona}|fallback"):
        if k in templates:
            return rng.choice(templates[k]).replace("{choice}", choice or "").replace("{pct}", pct or "")
    return ""


# (人格, 質問, 帯 or 結果, 結果か, 選んだもの, 割合, 大きく動いたか, 説明)
SITUATIONS = [
    ("zundamon", "budget", "High", False, None, "100%", False, "自信過剰の予算（山場）"),
    ("zundamon", "stream_side", "High", False, "魔王軍", None, False, "全力で魔王軍に張る（山場）"),
    ("zundamon", "buy_style", "Mid", False, "割安買い", None, False, "仕入れ方針"),
    ("zundamon", "intensity", "High", False, None, "100%", False, "スパチャの勢い（自信過剰）"),
    ("zundamon", "stream_result", "win", True, None, None, False, "配信で読みが当たった（マウント）"),
    ("zundamon", "stream_result", "lose", True, None, None, True, "配信で大負け（大きく動いた）"),
    ("zundamon", "run_result", "lose", True, None, None, True, "ランが赤字"),
    ("metan", "stream_result", "win", True, None, None, False, "めたん: 配信の勝ち（反応）"),
    ("metan", "budget", "High", False, None, "50%", False, "めたん: 予算 High（渋々使う候補あり）"),
    ("metan", "intel_no", "Low", False, None, None, False, "めたん: 情報を買わない"),
]

# 1 ランの判断の並び（だいたい 2 周 × 12 営業日・配信 4 回）。Monte Carlo 用
def run_sequence(rng):
    seq = []
    for lap in range(2):
        for day in range(12):
            for q in ("budget", "buy_style", rng.choice(["newspaper_yes", "newspaper_no"]), rng.choice(["intel_yes", "intel_no"]),
                      "support", "focus_share", "weapon"):
                seq.append((q, rng.choice(["Low", "Mid", "High"]), False, False))
            if day % 3 == 2:
                for q in ("stream_side", "intervene", "intensity"):
                    seq.append((q, rng.choice(["Low", "Mid", "High"]), False, False))
                for _ in range(rng.randint(0, 3)):
                    seq.append(("intervene_now", rng.choice(["Low", "Mid", "High"]), False, False))
                seq.append(("stream_result", rng.choice(["win", "lose"]), True, rng.random() < 0.3))
        seq.append(("run_result", rng.choice(["win", "lose"]), True, rng.random() < 0.5))
    return seq


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("lexicon", nargs="?", default=str(DEFAULT_LEXICON))
    ap.add_argument("--seed", type=int, default=20261028)
    ap.add_argument("--runs", type=int, default=1000, help="Monte Carlo のラン数（1 ランのミームの回数の分布）")
    args = ap.parse_args()

    templates = load_templates(LINES_CS)
    keys = valid_keys(templates)
    data = json.loads(Path(args.lexicon).read_text(encoding="utf-8"))
    errors, warnings = validate(data, keys)
    memes = [m for m in data.get("memes", []) if usable(m)]

    print(f"辞書: {args.lexicon}（version {data.get('version')}）")
    print(f"テンプレートのキー: {len(templates)} 個 → 使える question_keys {len(keys)} 個")
    print(f"エントリ: {len(data.get('memes', []))} 件 / 使う {len(memes)} 件"
          f"（ずんだもん {sum(m['who'] == 'zundamon' for m in memes)}・both {sum(m['who'] == 'both' for m in memes)}・"
          f"metan_reluctant {sum(m['who'] == 'metan_reluctant' for m in memes)}）")
    for w in warnings:
        print("  注意:", w)
    for e in errors:
        print("  エラー:", e)
    print("検査:", "OK" if not errors else f"NG（{len(errors)} 件）")

    # ---- サンプル: 場面 → 一言（ミームが出たとき。p はその場面で差し替える確率） ----
    rng = random.Random(args.seed)
    print("\nサンプル（ミームが出た場合の一言。場面ごとに新しいランとして引く。p = その場面で差し替える確率）")
    for i, (persona, q, bo, result, choice, pct, big, label) in enumerate(SITUATIONS, 1):
        band = "Low" if result else bo
        tpl = template_line(templates, persona, q, bo if result else band, choice, pct, rng)
        line, mid, cue, p = apply_meme(persona, q, bo, result, tpl, choice, pct, big, Run(memes, rng), force=True)
        print(f"{i:2}. [{persona} {q}|{bo}] {label}  p={p:.2f}")
        print(f"    テンプレート: {tpl}")
        if mid:
            print(f"    → {line}   (meme={mid}{', 応答のきっかけ: ' + cue if cue else ''})")
        else:
            print("    → （合うミームなし。テンプレートのまま）")

    # ---- 1 ランで何回出るか ----
    for persona in ("zundamon", "metan"):
        counts, reluctant, where, pos = [], 0, [0, 0], []
        mc = random.Random(args.seed + (0 if persona == "zundamon" else 1))
        for _ in range(args.runs):
            run = Run(memes, mc)
            seq = run_sequence(mc)
            for idx, (q, bo, result, big) in enumerate(seq):
                tpl = template_line(templates, persona, q, "Low" if result else bo, "勇者", "50%", mc)
                _, mid, _, _ = apply_meme(persona, q, bo, result, tpl, "勇者", "50%", big, run)
                if mid:
                    pos.append(idx / len(seq))
                    where[0 if result else 1] += 1
            counts.append(run.count)
            reluctant += run.reluctant
        avg = sum(counts) / len(counts)
        hist = {c: counts.count(c) for c in sorted(set(counts))}
        print(f"\n{persona}: 1 ランのミーム（めたんは反応）平均 {avg:.2f} 回、分布 {hist}"
              + (f"、渋々使ったラン {reluctant}/{args.runs}" if persona == "metan" else ""))
        quart = [sum(1 for x in pos if k / 4 <= x < (k + 1) / 4) for k in range(4)]
        print(f"  出た位置（ランを 4 等分）{quart}、結果の一言で {where[0]} 回 / 判断の一言で {where[1]} 回")

    return 1 if errors else 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
