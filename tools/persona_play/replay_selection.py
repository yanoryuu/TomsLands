"""人格の選び方を、判断ログ（Recordings/persona_log_*.jsonl）の入力でオフラインに再生する（標準ライブラリだけ）。

C# の PersonaReweighter（PersonaLearning.cs）と PersonaStrategy の選び方（PersonaChooser）を写したもの。
- 学習器はログの判断（decisionId・features）と結果（question = "resolve" の行）を時刻順に流して作り直す。
- 「前」= 2026-10-28 朝までの規則（λ 1.0・リッジ 3・log ±2・未経験の選択肢は予測 0・最尤）。ログの adjust と一致するか確かめる。
- 「後」= 新しい規則（未経験の選択肢は質問の平均に寄せる・小さい log の上限・楽観の上乗せ・余白の規則・場面の倍率・低温の抽選・探索）。
  ログに無い数字（情報料・魔王軍支援の額など）は中立（倍率 1）として扱う。
- 学習器の材料は、ログで実際に選ばれた選択肢の結果だけ（反実仮想の結果は分からない）。

使い方:
    py -3 -X utf8 tools/persona_play/replay_selection.py                       # めたん、2026-10-28 のマラソン（0215）
    py -3 -X utf8 tools/persona_play/replay_selection.py --persona metan --runs 0215 --seeds 20
"""
from __future__ import annotations

import argparse
import collections
import glob
import json
import math
import os
import random

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DIM = 11
D = DIM + 1
REWARD_SCALE = 10000.0
REWARD_CLIP = 5.0
SCORE_QUESTIONS = ("budget", "support", "intensity", "focus_share", "village_budget")
NOUL_THRESHOLD = {"intel": 0.35, "newspaper": 0.4}   # invest_* は 0.5
PREFERRED_FACILITIES = ("bank", "tavern", "hall")


# =====================================================================
# 学習器（PersonaReweighter の写し）
# =====================================================================

class Pair:
    def __init__(self):
        self.n = 0.0
        self.count = 0
        self.sum_r = 0.0
        self.xtx = [0.0] * (D * D)
        self.xty = [0.0] * D
        self.sum_raw = 0.0


class Reweighter:
    def __init__(self, ridge, forget, cap, shrink=0.0, optimism=0.0, utility=None):
        self.ridge, self.forget, self.cap, self.shrink, self.optimism = ridge, forget, cap, shrink, optimism
        #: 人格の効用（rules 4。None = 損益のまま）: dict(loss=λ, power=, lossWeight=, var=)
        self.utility = utility
        self.pairs: dict[str, Pair] = {}
        self._w: dict[str, list | None] = {}

    @staticmethod
    def x(features):
        v = [1.0] + [0.0] * DIM
        for i in range(min(DIM, len(features or []))):
            v[i + 1] = float(features[i])
        return v

    def update(self, q, c, features, reward_gold):
        key = q + "|" + c
        p = self.pairs.get(key)
        if p is None:
            p = self.pairs[key] = Pair()
        r = max(-REWARD_CLIP, min(REWARD_CLIP, reward_gold / REWARD_SCALE))
        if self.utility:
            u = self.utility
            raw = r
            mean = p.sum_raw / p.n if p.n > 0 else raw
            r = raw ** u["power"] if raw >= 0 else -u["lossWeight"] * u["loss"] * (-raw) ** u["power"]
            r -= u["var"] * abs(raw - mean)
            r = max(-REWARD_CLIP, min(REWARD_CLIP, r))
            p.sum_raw = p.sum_raw * self.forget + raw
        x = self.x(features)
        g = self.forget
        p.xtx = [v * g for v in p.xtx]
        p.xty = [v * g for v in p.xty]
        p.n = p.n * g + 1.0
        p.sum_r = p.sum_r * g + r
        p.count += 1
        for i in range(D):
            p.xty[i] += x[i] * r
            for j in range(D):
                p.xtx[i * D + j] += x[i] * x[j]
        self._w.pop(key, None)

    def _solve(self, p):
        a = [[p.xtx[i * D + j] + (self.ridge if i == j else 0.0) for j in range(D)] + [p.xty[i]] for i in range(D)]
        for col in range(D):
            piv = max(range(col, D), key=lambda r: abs(a[r][col]))
            if abs(a[piv][col]) < 1e-12:
                return None
            a[col], a[piv] = a[piv], a[col]
            for r in range(D):
                if r == col:
                    continue
                f = a[r][col] / a[col][col]
                if f:
                    for c in range(col, D + 1):
                        a[r][c] -= f * a[col][c]
        return [a[i][D] / a[i][i] for i in range(D)]

    def predict(self, q, c, features):
        key = q + "|" + c
        p = self.pairs.get(key)
        if p is None or p.count == 0:
            return 0.0
        if key not in self._w:
            self._w[key] = self._solve(p)
        w = self._w[key]
        if w is None:
            return 0.0
        y = sum(wi * xi for wi, xi in zip(w, self.x(features)))
        return max(-REWARD_CLIP, min(REWARD_CLIP, y))

    def question_mean(self, q):
        n = s = 0.0
        for key, p in self.pairs.items():
            if key.split("|", 1)[0] == q:
                n += p.n
                s += p.sum_r
        return s / n if n > 0 else 0.0

    def adjust(self, q, kev, features, lam):
        """(final, adjust)。shrink = 0 なら 2026-10-28 朝までの式。"""
        preds, ns = {}, {}
        mean = self.question_mean(q) if self.shrink > 0 else 0.0
        for c in kev:
            p = self.pairs.get(q + "|" + c)
            n = p.n if p else 0.0
            pred = self.predict(q, c, features) if lam > 0 else 0.0
            if self.shrink > 0:
                pred = (n * pred + self.shrink * mean) / (n + self.shrink)
            preds[c], ns[c] = pred, n
        total = sum(max(0.0, v) for v in kev.values())
        base = sum(max(0.0, kev[c]) * preds[c] for c in kev) / total if total > 0 else 0.0
        adj, final = {}, {}
        for c in kev:
            a = lam * (preds[c] - base)
            if self.optimism > 0:
                a += self.optimism / math.sqrt(1.0 + ns[c])
            a = max(-self.cap, min(self.cap, a))
            adj[c] = a
            final[c] = max(0.0, kev[c]) * math.exp(a)
        s = sum(final.values())
        if s <= 0:
            return dict(kev), adj
        return {c: v / s for c, v in final.items()}, adj


# =====================================================================
# 場面の倍率（PersonaChooser.Situation の写し。features から読める数字だけ）
# =====================================================================

def day_view(f):
    cash = 10 ** (6 * f[1]) if f[1] > 0 else 0
    return {
        "cash": cash,
        "debtRatio": 3 * f[2],
        "debtDays": 10 * f[3],
        "streamDays": 5 * f[4],
        "weak": f[5] >= 0.5,
        "stockShare": f[8],
    }


def situation(q, f, extra=None):
    """選択肢 → 倍率（無ければ 1）。noul の質問は "true" の倍率（true と false の比に掛ける）だけ返す。"""
    extra = extra or {}
    m = collections.defaultdict(lambda: 1.0)
    if f is None:
        return m
    if q in ("budget", "buy_style", "intel", "support", "focus_share", "newspaper"):
        v = day_view(f)
        tight = v["debtRatio"] >= 0.6 and v["debtDays"] <= 3
        comfy = v["debtRatio"] <= 0.2 and v["cash"] >= 50000
        if q == "budget":
            for lv in range(6):
                if tight:
                    m[str(lv)] *= 0.5 if lv >= 3 else 1.4 if lv <= 1 else 1.0
                if comfy:
                    m[str(lv)] *= 1.3 if lv in (2, 3) else 0.6 if lv == 0 else 1.0
                if v["streamDays"] <= 1 and v["weak"] and lv == 3:
                    m[str(lv)] *= 1.2
        elif q == "buy_style":
            if v["weak"] and v["streamDays"] <= 1:
                m["dungeon_focus"] *= 1.4
            if tight:
                m["recommend"] *= 1.2
                m["dividend"] *= 0.8
        elif q == "intel":
            share = extra.get("intelCostShare", -1)
            if 0 <= share <= 0.05:
                m["true"] *= 1.5
            elif share >= 0.25:
                m["true"] *= 0.4
            if tight:
                m["true"] *= 0.5
            if v["streamDays"] >= 3:
                m["true"] *= 0.7
        elif q == "support":
            gain = extra.get("supportGainRatio", -1)
            for lv in range(4):
                if comfy and v["debtDays"] > 2:
                    m[str(lv)] *= 1.4 if lv == 1 else 0.6 if lv >= 2 else 1.0
                if tight:
                    m[str(lv)] *= 2.0 if lv == 0 else 1.0
                if gain >= 1.5 and lv == 1:
                    m[str(lv)] *= 1.5
                if 0 <= gain < 0.8 and lv >= 1:
                    m[str(lv)] *= 0.4
        elif q == "focus_share":
            for lv in range(5):
                if tight:
                    m[str(lv)] *= 1.4 if lv <= 1 else 0.6 if lv >= 3 else 1.0
                if comfy and lv == 2:
                    m[str(lv)] *= 1.2
    elif q in ("stream_side", "intensity"):
        weak = f[7] >= 0.5
        budget_share = f[8]
        support_paid = f[9] >= 0.5
        cash = 10 ** (6 * f[1]) if f[1] > 0 else 0
        if q == "stream_side":
            if weak:
                m["hero"] *= 1.5
            else:
                m["none"] *= 1.4
                m["hero"] *= 0.8
            if support_paid:
                m["demon"] *= 1.8
                m["hero"] *= 0.6
            if budget_share < 0.05:
                m["none"] *= 1.3
        else:
            side = extra.get("side")
            for lv in range(5):
                if side == "none":
                    m[str(lv)] *= 1.6 if lv <= 1 else 0.5
                if budget_share >= 0.3 and lv == 3:
                    m[str(lv)] *= 1.2
                if cash < 50000 and lv >= 3:
                    m[str(lv)] *= 0.6
                if side == "hero" and weak and lv == 3:
                    m[str(lv)] *= 1.2
    elif q == "village_budget":
        plausible = extra.get("plausibleCount", -1)
        for lv in range(5):
            if plausible > 0:
                m[str(lv)] *= 1.4 if lv >= 2 else 0.4 if lv == 0 else 1.0
            elif plausible == 0:
                m[str(lv)] *= 1.5 if lv <= 1 else 1.0
    elif q.startswith("invest_"):
        fac = q[len("invest_"):]
        if extra.get("plausible") is True:
            m["true"] *= 3.0
        elif extra.get("plausible") is False:
            m["true"] *= 0.3
        if fac in PREFERRED_FACILITIES:
            m["true"] *= 1.5
    return m


def village_plausible(f):
    """features から: 費用が村資金の 5% 以下、または（村資金が 100 万G 未満で）前のランの儲けの 4 倍以下。"""
    if f is None or f[5] <= 0:
        return None
    if f[5] <= 0.05:
        return True
    funds = 10 ** (6 * f[1]) if f[1] < 1 else None
    profit = (f[7] - 0.5) * 2_000_000
    if funds is not None and profit > 0:
        return f[5] * funds <= 4 * profit
    return False


# =====================================================================
# 選び方
# =====================================================================

def logit(p):
    p = min(1 - 1e-6, max(1e-6, p))
    return math.log(p / (1 - p))


def sigmoid(x):
    return 1 / (1 + math.exp(-x))


def careful_pick(rng, final, mult, *, levels=False, margin=0.25, temperature=0.5, explore=0.05, kev=None, band=0.2):
    """(key, reason)。C# の PersonaChooser.Careful と同じ手順。

    - 選択肢: 場面の倍率を掛けて正規化し、1 位と 2 位の差が margin 以上なら 1 位（margin）、
      近ければ 1 位から margin 以内の候補を温度 temperature で抽選（close）。
    - 段階（score）: 倍率を掛けた分布の期待値 e を使い（kev の score と同じ考え方）、e が段階の真ん中寄り
      （小数部が 0.5±band の外）なら丸めた段階（margin）、二つの段階の間なら近さで抽選（close）。
    - どちらも確率 explore で、kev が 5% 以上つけた別の選択肢（段階は隣だけ）を試す（explore）。
    """
    keys = sorted(final, key=(lambda k: int(k)) if levels else (lambda k: k))
    q = {k: max(0.0, final[k]) * mult[k] for k in keys}
    s = sum(q.values())
    q = {k: (v / s if s > 0 else 1 / len(keys)) for k, v in q.items()}
    if len(keys) == 1:
        return keys[0], "only"
    src = kev or final
    r = rng.random()
    if levels:
        e = sum(int(k) * v for k, v in q.items())
        lo_i = max(int(keys[0]), min(int(keys[-1]), math.floor(e)))
        hi_i = min(int(keys[-1]), lo_i + 1)
        frac = e - lo_i
        if frac <= 0.5 - band:
            top, why = str(lo_i), "margin"
        elif frac >= 0.5 + band:
            top, why = str(hi_i), "margin"
        else:
            top, why = None, "close"
        if r < explore:
            base = top if top is not None else str(round(e))
            pool = [k for k in keys if abs(int(k) - int(base)) == 1 and src.get(k, 0) >= 0.05]
            if pool:
                return weighted(rng, pool, [q[k] + 1e-6 for k in pool]), "explore"
        if top is not None:
            return top, why
        p_hi = sigmoid((frac - 0.5) / (0.3 * temperature))
        return (str(hi_i) if rng.random() < p_hi else str(lo_i)), "close"
    ranked = sorted(keys, key=lambda k: -q[k])
    top = ranked[0]
    pool = [k for k in keys if k != top and src.get(k, 0) >= 0.05]
    if pool and r < explore:
        return weighted(rng, pool, [q[k] + 1e-6 for k in pool]), "explore"
    if q[top] - q[ranked[1]] >= margin:
        return top, "margin"
    cands = [k for k in ranked if q[k] >= q[top] - margin]
    return weighted(rng, cands, [q[k] ** (1 / temperature) for k in cands]), "close"


def weighted(rng, keys, weights):
    t = sum(weights)
    x = rng.random() * t
    for k, w in zip(keys, weights):
        x -= w
        if x <= 0:
            return k
    return keys[-1]


def noul_from_final(r):
    """ログの行から kev の noul を戻す（adjust と finalProbabilities から）。"""
    fp = r.get("finalProbabilities")
    adj = r.get("adjust")
    if fp and adj and "true" in fp and "false" in fp:
        ft, ff = max(fp["true"], 1e-9), max(fp["false"], 1e-9)
        odds = ft / ff * math.exp(adj.get("false", 0) - adj.get("true", 0))
        return odds / (1 + odds)
    return None


# =====================================================================
# 再生
# =====================================================================

OLD = dict(lam=1.0, ridge=3.0, forget=0.998, cap=2.0, shrink=0.0, optimism=0.0)
NEW = dict(lam=0.8, ridge=5.0, forget=0.99, cap=0.7, shrink=8.0, optimism=0.15,
           margin=0.25, temperature=0.5, explore=0.05)


def load_rows(persona, runs):
    pat = os.path.join(ROOT, "Recordings", "persona_log_*.jsonl")
    rows = []
    for f in sorted(glob.glob(pat)):
        try:
            with open(f, encoding="utf-8-sig") as fh:
                for line in fh:
                    if not line.strip():
                        continue
                    r = json.loads(line)
                    if r.get("persona") != persona or r.get("rules") != 3:
                        continue
                    rows.append((f, r))
        except OSError:
            continue
    if runs:
        rows = [(f, r) for f, r in rows if runs in os.path.basename(f) or (r.get("run") or "").find(runs) >= 0]
    return rows


def replay(persona, runs, seed, new_params=NEW, train_from=None):
    """(before, after, check)。before / after = 質問 → Counter（選んだもの）。"""
    allrows = load_rows(persona, None)
    old_m = Reweighter(OLD["ridge"], OLD["forget"], OLD["cap"])
    new_m = Reweighter(new_params["ridge"], new_params["forget"], new_params["cap"], new_params["shrink"], new_params["optimism"])
    decisions = {}
    before = collections.defaultdict(collections.Counter)
    after = collections.defaultdict(collections.Counter)
    reasons = collections.defaultdict(collections.Counter)
    check = collections.Counter()
    rng = random.Random(seed)
    side_by_run_day = {}
    for f, r in allrows:
        in_scope = not runs or runs in os.path.basename(f)
        q = r.get("question")
        if r.get("decisionId") and r.get("features"):
            decisions[r["decisionId"]] = r
        if q == "resolve":
            d = decisions.get(r.get("ref"))
            if d is None:
                continue
            qq, cc = (r.get("choice") or "=").split("=", 1)
            for m in (old_m, new_m):
                m.update(qq, cc, d["features"], float(r.get("reward") or 0))
            continue
        kev = r.get("probabilities")
        feats = r.get("features")
        if q in ("weapon", "intervene", "intervene_now", "stream_result", "run_result", "village_result", None):
            continue
        if not in_scope:
            continue
        # ---- 前（ログの値をそのまま。式の写しが合うかも数える） ----
        before[q][r.get("choice")] += 1
        if q in ("intel", "newspaper") or q.startswith("invest_"):
            v = noul_from_final(r)
            if v is None:
                v = r.get("confidence") is not None and 0.5 or 0.5
            kev = {"true": v, "false": 1 - v}
        if not kev or feats is None:
            continue
        o_final, o_adj = old_m.adjust(q, kev, feats, OLD["lam"])
        if r.get("adjust"):
            same = all(abs(o_adj.get(k, 0) - r["adjust"].get(k, 0)) < 0.02 for k in r["adjust"])
            check["adjust_match" if same else "adjust_diff"] += 1
        # ---- 後 ----
        n_final, _ = new_m.adjust(q, kev, feats, new_params["lam"])
        extra = {}
        if q == "intensity":
            extra["side"] = side_by_run_day.get((f, r.get("day")))
        if q.startswith("invest_"):
            extra["plausible"] = village_plausible(feats)
        if q == "village_budget":
            extra["plausibleCount"] = -1
        mult = situation(q, feats, extra)
        if q in SCORE_QUESTIONS:
            pick, why = careful_pick(rng, n_final, mult, levels=True, margin=new_params["margin"],
                                     temperature=new_params["temperature"], explore=new_params["explore"], kev=kev)
            if q == "support" and day_view(feats)["cash"] < 40000:
                pick, why = "0", "floor"
        elif q in ("intel", "newspaper") or q.startswith("invest_"):
            thr = NOUL_THRESHOLD.get(q, 0.5)
            p = sigmoid(logit(n_final["true"]) - logit(thr))
            pick, why = careful_pick(rng, {"true": p, "false": 1 - p}, mult, margin=new_params["margin"],
                                     temperature=new_params["temperature"], explore=new_params["explore"], kev={"true": p, "false": 1 - p})
            if q == "newspaper" and persona == "metan":
                pick, why = "true", "always"   # めたんは毎朝読む（NewspaperSeconds）
        else:
            pick, why = careful_pick(rng, n_final, mult, margin=new_params["margin"], temperature=new_params["temperature"],
                                     explore=new_params["explore"], kev=kev)
        if q == "stream_side":
            side_by_run_day[(f, r.get("day"))] = pick
        after[q][pick] += 1
        reasons[q][why] += 1
    return before, after, reasons, check


# =====================================================================
# 特性（PersonaTraits.cs の写し）と、特性から導く定数（P1）
# =====================================================================

TRAITS = {
    "zundamon": dict(O=0.80, C=0.20, E=0.90, A=0.70, N=0.70, H=0.90, risk=0.85, lossAversion=1.0, impulsivity=0.80,
                     recovery=0.85, pride=0.80, receptivity=0.30, skillRate=0.10, memeAffinity=0.85),
    "metan": dict(O=0.55, C=0.90, E=0.40, A=0.30, N=0.50, H=0.45, risk=0.30, lossAversion=2.25, impulsivity=0.15,
                  recovery=0.40, pride=0.65, receptivity=0.50, skillRate=0.60, memeAffinity=0.10),
}

#: 2026-10-28 までの PersonaProfile の定数（P1 で特性から導くようにしたもの）
LEGACY = {
    "zundamon": dict(sample=True, temperature=1.6, scoreBias=1, hardRoute=0.3, cashFloor=0, lam=0.1, flashy=1.6),
    "metan": dict(sample=False, temperature=1.0, scoreBias=0, hardRoute=0.0, cashFloor=10000, lam=0.8, flashy=1.0),
}


def _r2(v):
    return round(v * 100) / 100


def derive(t):
    """PersonaTraits の導出（C# と同じ式）。"""
    bold = min(1.0, max(0.0, (t["risk"] - 0.30) / 0.55))
    sample = t["impulsivity"] >= 0.5
    return dict(
        sample=sample,
        temperature=_r2(0.4 + 1.5 * t["impulsivity"]) if sample else 1.0,
        scoreBias=round(bold),
        hardRoute=_r2(0.3 * bold),
        cashFloor=round(max(0.0, t["lossAversion"] - 1.0) / 1.25) * 10000,
        lam=max(0.0, _r2(1.4 * t["skillRate"] - 0.04)),
        flashy=max(1.0, _r2(1 + 1.2 * (t["E"] - 0.4))),
    )


def persona_pick(prm, rng, q, final, kev, mult, levels):
    """1 判断の選び方（PersonaStrategy.PickQuestion の写し。prm = LEGACY / derive() の形）。"""
    if prm["sample"]:
        if q in ("intel", "newspaper") or q.startswith("invest_"):
            v = final["true"]
            if q.startswith("invest_") and q[len("invest_"):] in ("plaza", "guild", "artisan"):
                v = min(1.0, v * prm["flashy"])
            pick = "true" if rng.random() < v + (0.5 - v) * 0.3 else "false"
            if q == "intel" and prm["hardRoute"] > 0 and rng.random() < prm["hardRoute"]:
                pick = "false"
            return pick
        keys = sorted(final, key=(lambda k: int(k)) if levels else (lambda k: k))
        weights = [max(1e-4, final[k]) ** (1 / prm["temperature"]) for k in keys]
        # 段階の少ない質問（支援 4 段）は +bias ではなく exp(0.8·bias·段) の傾き（PersonaStrategy.ScoreLevel・rules 4.1）
        tilt = bool(levels) and len(keys) <= 4 and abs(prm["scoreBias"]) > 0.01
        if tilt:
            weights = [w * math.exp(0.8 * prm["scoreBias"] * int(k)) for w, k in zip(weights, keys)]
        pick = weighted(rng, keys, weights)
        if levels and not tilt:
            pick = str(max(int(keys[0]), min(int(keys[-1]), int(pick) + int(prm["scoreBias"]))))
        if q == "stream_side" and prm["hardRoute"] > 0 and pick != "demon" and rng.random() < prm["hardRoute"] * 0.5:
            pick = "demon"
        return pick
    if q in ("intel", "newspaper") or q.startswith("invest_"):
        thr = NOUL_THRESHOLD.get(q, 0.5)
        pt = sigmoid(logit(final["true"]) - logit(thr))
        pick, _ = careful_pick(rng, {"true": pt, "false": 1 - pt}, mult, kev={"true": pt, "false": 1 - pt})
        return pick
    pick, _ = careful_pick(rng, final, mult, levels=levels, kev=kev)
    return pick


def check_traits(runs, seeds):
    """P1 の確かめ: 特性から導いた定数が前の定数と同じか、ログの入力で選択が 100% 一致するか。"""
    ok = True
    for persona in ("zundamon", "metan"):
        d = derive(TRAITS[persona])
        same = all(d[k] == LEGACY[persona][k] for k in LEGACY[persona])
        ok &= same
        print(f"{persona}: 導出 {d}")
        print(f"{persona}: 前の定数と{'一致' if same else '不一致'} {LEGACY[persona]}")
        rows = load_rows(persona, None)
        total = match = 0
        for seed in range(seeds):
            m_old = Reweighter(5.0, 0.99, 0.7, 8.0, 0.15) if persona == "metan" else Reweighter(30.0, 0.999, 2.0)
            decisions = {}
            for f, r in rows:
                q = r.get("question")
                if r.get("decisionId") and r.get("features"):
                    decisions[r["decisionId"]] = r
                if q == "resolve":
                    dd = decisions.get(r.get("ref"))
                    if dd is not None:
                        qq, cc = (r.get("choice") or "=").split("=", 1)
                        m_old.update(qq, cc, dd["features"], float(r.get("reward") or 0))
                    continue
                if q in ("weapon", "intervene", "intervene_now", "stream_result", "run_result", "village_result", None):
                    continue
                if runs and runs not in os.path.basename(f):
                    continue
                kev, feats = r.get("probabilities"), r.get("features")
                if q in ("intel", "newspaper") or q.startswith("invest_"):
                    v = noul_from_final(r)
                    kev = {"true": v if v is not None else 0.5, "false": 1 - (v if v is not None else 0.5)}
                if not kev or feats is None:
                    continue
                levels = q in SCORE_QUESTIONS
                picks = []
                for prm in (LEGACY[persona], d):
                    final, _ = m_old.adjust(q, kev, feats, prm["lam"])
                    mult = situation(q, feats, {}) if not prm["sample"] else collections.defaultdict(lambda: 1.0)
                    rng = random.Random(hash((seed, r.get("decisionId") or r.get("time"), q)) & 0xFFFFFFFF)
                    picks.append(persona_pick(prm, rng, q, final, kev, mult, levels))
                total += 1
                match += picks[0] == picks[1]
        ok &= match == total
        print(f"{persona}: ログ {runs or '全部'} の判断 {total} 件（{seeds} 通りの乱数）で前と後の選択が一致 {match}/{total}"
              f"（{match * 100 / max(1, total):.1f}%）")
    return ok


# =====================================================================
# rules 4（人格の形成）: 人格の効用・KL の上限・エントロピーの下限・固まりの安全弁（PersonaStyle.Compose / PersonaReweighter の写し）
# ログに気分は無いので、流儀 S(a) は 0（気分が平常のとき）として再生する
# =====================================================================

UTILITY = {"metan": dict(loss=2.25, power=1.0, lossWeight=1.0, var=0.1),
           "zundamon": dict(loss=1.0, power=1.15, lossWeight=0.7, var=0.0)}
KL_EPS = {"metan": 0.35, "zundamon": 0.15}
H_MIN = {"metan": 0.5, "zundamon": 1.0}
SIGNATURE = {"metan": {"newspaper", "intel"}, "zundamon": set()}


def _mix(kev, adj, style, w, temp):
    d = {}
    for k, pk in kev.items():
        pk = max(0.0, pk)
        d[k] = 0.0 if pk <= 0 else math.exp((math.log(pk) + w * adj.get(k, 0.0) + style.get(k, 0.0)) / temp)
    s = sum(d.values())
    return {k: (v / s if s > 0 else 1 / len(d)) for k, v in d.items()}


def kl(p, q):
    return max(0.0, sum(v * math.log(v / max(1e-9, q.get(k, 1e-9))) for k, v in p.items() if v > 0))


def entropy_bits(p):
    return -sum(v * math.log2(v) for v in p.values() if v > 0)


def compose(kev, adj, style, skill_scale, kl_eps, h_min):
    """(final, prior, w, kl, temp)。C# の PersonaStyle.Compose と同じ。"""
    style = style or {}
    prior = _mix(kev, adj, style, 0.0, 1.0)
    w = max(0.0, skill_scale)
    final = _mix(kev, adj, style, w, 1.0)
    k = kl(final, prior)
    if kl_eps > 0 and k > kl_eps and w > 0:
        lo, hi = 0.0, w
        for _ in range(20):
            mid = (lo + hi) / 2
            if kl(_mix(kev, adj, style, mid, 1.0), prior) > kl_eps:
                hi = mid
            else:
                lo = mid
        w = lo
        final = _mix(kev, adj, style, w, 1.0)
        k = kl(final, prior)
    temp = 1.0
    if h_min > 0 and sum(1 for v in kev.values() if v >= 0.10) >= 2 and entropy_bits(final) < h_min:
        lo, hi = 1.0, 8.0
        if entropy_bits(_mix(kev, adj, style, w, hi)) >= h_min:
            for _ in range(20):
                mid = (lo + hi) / 2
                if entropy_bits(_mix(kev, adj, style, w, mid)) >= h_min:
                    hi = mid
                else:
                    lo = mid
        temp = hi
        final = _mix(kev, adj, style, w, temp)
    return final, prior, w, k, temp


def replay_v4(persona, runs, seed, v4=True):
    """ログの入力を rules 3（いまの規則）/ rules 4 で再生。学習器はログの結果を時刻順に流して作り直す。"""
    rows = load_rows(persona, None)
    util = UTILITY[persona] if v4 else None
    m = Reweighter(NEW["ridge"], NEW["forget"], NEW["cap"], NEW["shrink"], NEW["optimism"], utility=util)
    decisions = {}
    picks = collections.defaultdict(collections.Counter)
    kls = []
    rng = random.Random(seed)
    skill_scale = 1.0
    run_picks = collections.defaultdict(collections.Counter)
    cur_run = None
    valve = []
    side_by_run_day = {}
    for f, r in rows:
        q = r.get("question")
        run = r.get("run") or os.path.basename(f)
        if run != cur_run:
            if cur_run is not None and v4:
                stuck = [(qq, cn.most_common(1)[0]) for qq, cn in run_picks.items()
                         if sum(cn.values()) >= 5 and cn.most_common(1)[0][1] / sum(cn.values()) > 0.85]
                before = skill_scale
                skill_scale = max(0.125, skill_scale * 0.5) if stuck else min(1.0, skill_scale * 1.5)
                if stuck or before != skill_scale:
                    valve.append((cur_run, before, skill_scale, stuck))
            run_picks = collections.defaultdict(collections.Counter)
            cur_run = run
        if r.get("decisionId") and r.get("features"):
            decisions[r["decisionId"]] = r
        if q == "resolve":
            d = decisions.get(r.get("ref"))
            if d is not None:
                qq, cc = (r.get("choice") or "=").split("=", 1)
                m.update(qq, cc, d["features"], float(r.get("reward") or 0))
            continue
        if q in ("weapon", "intervene", "intervene_now", "stream_result", "run_result", "village_result", "safety_valve", None):
            continue
        if runs and runs not in os.path.basename(f):
            continue
        kev, feats = r.get("probabilities"), r.get("features")
        noul = q in ("intel", "newspaper") or q.startswith("invest_")
        if noul:
            v = noul_from_final(r)
            v = 0.5 if v is None else v
            kev = {"true": v, "false": 1 - v}
        if not kev or feats is None:
            continue
        free = q not in SIGNATURE[persona]
        if v4:
            _, adj = m.adjust(q, kev, feats, NEW["lam"])
            final, prior, w, k, temp = compose(kev, adj, {}, skill_scale, KL_EPS[persona], H_MIN[persona] if free else 0.0)
            kls.append(kl(final, prior))
        else:
            final, _ = m.adjust(q, kev, feats, NEW["lam"])
        extra = {}
        if q == "intensity":
            extra["side"] = side_by_run_day.get((f, r.get("day")))
        if q.startswith("invest_"):
            extra["plausible"] = village_plausible(feats)
        mult = situation(q, feats, extra)
        # エントロピーの下限の選び方の側（PersonaChooser.EntropyFloorExplore）
        explore = 0.05
        if v4 and free and not q.startswith("invest_") and sum(1 for v in kev.values() if v >= 0.10) >= 2:
            explore = 0.15
        if noul:
            thr = NOUL_THRESHOLD.get(q, 0.5)
            pt = sigmoid(logit(final["true"]) - logit(thr))
            pick, _ = careful_pick(rng, {"true": pt, "false": 1 - pt}, mult, kev={"true": pt, "false": 1 - pt}, explore=explore)
            if q == "newspaper" and persona == "metan":
                pick = "true"
        else:
            pick, _ = careful_pick(rng, final, mult, levels=q in SCORE_QUESTIONS, kev=kev, explore=explore)
            if q == "support" and day_view(feats)["cash"] < 40000:
                pick = "0"
        if q == "stream_side":
            side_by_run_day[(f, r.get("day"))] = pick
        picks[q][pick] += 1
        if free:
            run_picks[q][pick] += 1
    return picks, kls, valve


def no_collapse_report(persona, runs, seeds):
    order = ["stream_side", "intensity", "support", "budget", "buy_style", "focus_share", "village_budget", "intel", "newspaper"]
    res = {}
    for v4 in (False, True):
        agg = collections.defaultdict(collections.Counter)
        kls, valves = [], []
        for sd in range(seeds):
            pk, kk, vv = replay_v4(persona, runs, sd, v4=v4)
            for q, cnt in pk.items():
                agg[q].update(cnt)
            kls += kk
            valves += vv
        res[v4] = (agg, kls, valves)
    logged = collections.defaultdict(collections.Counter)
    for f, r in load_rows(persona, runs):
        q = r.get("question")
        if q and q not in ("resolve", "weapon", "intervene", "intervene_now", "stream_result", "run_result", "village_result"):
            logged[q][r.get("choice")] += 1
    print(f"# {persona}: ログ {runs} の入力を rules 3（いま）/ rules 4（人格の効用・KL 上限・エントロピー下限・安全弁）で再生（{seeds} 通りの乱数の合計）")
    print()
    print("| 質問 | 署名 | ログ（実際）の最多の割合 | rules 3 の最多の割合 | rules 3 H(bit) | rules 4 の最多の割合 | rules 4 H(bit) | rules 4 の分布 |")
    print("|---|---|---|---|---|---|---|---|")

    def top(cn):
        n = sum(cn.values())
        return cn.most_common(1)[0][1] / n if n else 0

    def h(cn):
        n = sum(cn.values())
        return entropy_bits({k: v / n for k, v in cn.items()}) if n else 0

    worst = 0.0
    qs = order + sorted(q for q in res[True][0] if q not in order)
    for q in qs:
        a3, a4 = res[False][0].get(q, collections.Counter()), res[True][0].get(q)
        if not a4:
            continue
        sig = q in SIGNATURE[persona]
        if not sig and not q.startswith("invest_"):   # 指標 M2 の「自由な質問」（村の施設ごとは数えない）
            worst = max(worst, top(a4))
        lg = logged.get(q, collections.Counter())
        print(f"| {q} | {'署名' if sig else ''} | {top(lg):.2f} | {top(a3):.2f} | {h(a3):.2f} | {top(a4):.2f} | {h(a4):.2f} | {fmt(a4)} |")
    kl4 = sorted(res[True][1])
    if kl4:
        print()
        print(f"rules 4: KL(π‖π_prior) の 95 パーセンタイル {kl4[max(0, int(len(kl4) * 0.95) - 1)]:.3f} nat（上限 {KL_EPS[persona]}）")
    print(f"rules 4: 自由な質問（村の施設ごとを除く）の最多の割合の最大 {worst:.2f}（目標 ≤ 0.85）")
    nv = sum(1 for v in res[True][2] if v[3])
    print(f"rules 4: 安全弁が働いた回数 {nv}（{seeds} 通りの合計）")
    return worst <= 0.85


def fmt(counter):
    n = sum(counter.values())
    if n == 0:
        return "-"
    return " / ".join(f"{k} {v * 100 / n:.0f}%" for k, v in sorted(counter.items(), key=lambda kv: -kv[1]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--persona", default="metan")
    ap.add_argument("--runs", default="0215", help="ログのファイル名に含まれる文字列（既定: 2026-10-28 のマラソン）")
    ap.add_argument("--seeds", type=int, default=10)
    ap.add_argument("--check-traits", action="store_true", help="P1: 特性から導いた定数で選択が前と 100% 一致するか")
    ap.add_argument("--no-collapse", action="store_true", help="P4: rules 4 の保険で固まらないか（めたんのログを再生）")
    args = ap.parse_args()
    if args.no_collapse:
        raise SystemExit(0 if no_collapse_report(args.persona, args.runs, args.seeds) else 1)
    if args.check_traits:
        raise SystemExit(0 if check_traits(args.runs, args.seeds) else 1)
    agg = collections.defaultdict(collections.Counter)
    why = collections.defaultdict(collections.Counter)
    before = check = None
    for s in range(args.seeds):
        b, a, rs, c = replay(args.persona, args.runs, s)
        before, check = b, c
        for q, cnt in a.items():
            agg[q].update(cnt)
        for q, cnt in rs.items():
            why[q].update(cnt)
    print(f"# {args.persona}: ログ {args.runs} の判断を再生（後は {args.seeds} 通りの乱数の合計）")
    print(f"式の写しの確認（ログの adjust と ±0.02 で一致）: {dict(check)}")
    print()
    print("| 質問 | 件数 | 前（ログ） | 後（新しい規則） | 後の決まり方 |")
    print("|---|---|---|---|---|")
    order = ["stream_side", "intensity", "support", "intel", "budget", "buy_style", "focus_share", "newspaper", "village_budget"]
    qs = order + sorted(q for q in before if q not in order)
    for q in qs:
        if q not in before:
            continue
        print(f"| {q} | {sum(before[q].values())} | {fmt(before[q])} | {fmt(agg[q])} | {fmt(why[q])} |")


if __name__ == "__main__":
    main()
