# -*- coding: utf-8 -*-
"""
実況動画の BGM（CC0、assets/bgm_cc0）とゲーム音の音量の段取り。jikkyo.py から使う。

台本で「BGM の計画」を書いたときだけ働く（書いていない台本は今まで通り。sc["bgm"] が文字列ならゲームの BGM 1 曲）。

台本の書き方（どれか）:
  "bgm": [ {"at": 0, "track": "shop_rabbitbop"}, {"at": "ch:village", "track": "village_rpgtown", "fade": 2.0}, ... ]
  "bgm": {"plan": [...], "vol": 0.30, "duck": 0.45, "game_vol": 0.10}
  "bgm": "auto"                         # propose_plan() の案をそのまま使う
  "bgm": "E_soudan.bgm.json"            # scripts/ の下の計画ファイル（{"plan": [...]} か [...]）
  "game_vol": 0.10                      # 台本全体のゲーム音（録画の音）の大きさ。計画があるときの既定は 0.10

計画の 1 項目:
  {"at": 秒 | "ch:<章id>[+-秒]" | "line:<章id>/<n>[+-秒]" | "end[-秒]",
   "track": "<曲id>" | null（止める）, "vol": 0.3, "fade": 1.5（前の曲からのクロスフェード秒）, "offset": 0（曲の何秒目から）}
  {"at": ..., "sting": "<曲id>", "vol": 0.9, "drop": 2.5（BGM を下げておく秒）, "drop_to": 0.15}
  "line:<章id>/<n>" は章の中の n 番目（0 から）のセリフの頭。

行（lines の 1 行）に書けるもの:
  "bgm": "<曲id>" か {"track": ..., "vol": ..., "fade": ...}   … この行の頭で曲を替える
  "bgm_stop": true か 秒（フェード秒）                         … この行の頭で BGM を止める
  "bgm_sting": "<曲id>" か {"sting": ..., "vol": ..., "drop": ..., "at": "end" / 秒}  … 一発もの
  "bgm_duck": 0.3（この行の間の BGM の倍率）/ false（この行は下げない）
  "game_vol": 0.4                                               … この行の頭から次の行の頭までゲーム音をこの大きさに
章（chapters の 1 件）にも "bgm" / "game_vol" / "mood" を書ける（章の頭から章の終わりまで）。
"""
import json
import math
import os
import re
import subprocess
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
BGM_DIR = os.path.join(HERE, "assets", "bgm_cc0")
SR = 48000

DEFAULT_VOL = 0.30        # -18 LUFS の曲 × 0.30 ≒ -28.5 LUFS（声は約 -17〜-19 LUFS）
DEFAULT_DUCK = 0.38       # 声の間はさらに ×0.38（≒ -8.4 dB）。声より 10 dB ほど下になる
STING_DUCK = 0.35         # 一発ものも声と重なるときは ×0.35
DEFAULT_STING_VOL = 0.6
DEFAULT_FADE = 1.5
DEFAULT_GAME_VOL = 0.10   # 計画があるとき、ゲーム（録画の音）は 0.10 に下げる（ゲーム側の配信 BGM がくり返しになるので）
DUCK_ATTACK, DUCK_RELEASE = 0.12, 0.45
MAX_RUN = 150.0           # 提案: 1 曲を続けるのはこれまで（秒）
MOOD_VOL = {"village": 0.30, "shop": 0.28, "stream": 0.30, "climax": 0.32, "fail": 0.30, "smug": 0.28, "ending": 0.30}


def is_plan(sc):
    """この台本が BGM の計画を持つか（持たないなら jikkyo.py は今まで通りにする）。"""
    b = sc.get("bgm")
    if isinstance(b, (list, dict)):
        return True
    if isinstance(b, str) and (b == "auto" or b.endswith(".json")):
        return True
    return any("bgm" in ch or any(k in ln for ln in ch["lines"] for k in ("bgm", "bgm_stop", "bgm_sting"))
               for ch in sc["chapters"])


def uses_game_vol(sc):
    return is_plan(sc) or "game_vol" in sc or any("game_vol" in ch or any("game_vol" in ln for ln in ch["lines"]) for ch in sc["chapters"])


def load_index():
    p = os.path.join(BGM_DIR, "index.json")
    with open(p, encoding="utf-8") as f:
        return {t["id"]: t for t in json.load(f)}


def plan_settings(sc, scripts_dir):
    b = sc.get("bgm")
    plan, opts = None, {}
    if isinstance(b, list):
        plan = b
    elif isinstance(b, dict):
        plan, opts = b.get("plan"), b
    elif isinstance(b, str) and b.endswith(".json"):
        with open(os.path.join(scripts_dir, b), encoding="utf-8") as f:
            j = json.load(f)
        plan, opts = (j, {}) if isinstance(j, list) else (j.get("plan"), j)
    return plan, opts


# ---------------------------------------------------------------- 時刻
def resolve_at(at, tl):
    if isinstance(at, (int, float)):
        return float(at)
    if isinstance(at, dict):
        ch = at.get("chapter"); off = float(at.get("offset", 0.0))
        if "line" in at:
            return line_start(tl, ch, int(at["line"])) + off
        return chapter_start(tl, ch) + off
    s = str(at).strip()
    m = re.match(r"^(ch|line|end)(?::([^+\-]*?))?(?:/(\d+))?\s*([+\-]\s*[\d.]+)?$", s)
    if not m:
        raise SystemExit(f"bgm の at が読めない: {at}")
    kind, cid, n, off = m.group(1), m.group(2), m.group(3), float((m.group(4) or "0").replace(" ", ""))
    if kind == "end":
        return tl["total"] + off
    if kind == "ch":
        return chapter_start(tl, cid) + off
    return line_start(tl, cid, int(n or 0)) + off


def chapter_start(tl, cid):
    for c in tl["chapters"]:
        if c["id"] == cid:
            return c["start"]
    raise SystemExit(f"bgm: 章 {cid} が無い")


def line_start(tl, cid, n):
    ls = [e for e in tl["events"] if e["kind"] == "line" and e["chapter"] == cid]
    if not ls:
        raise SystemExit(f"bgm: 章 {cid} にセリフが無い")
    return ls[min(n, len(ls) - 1)]["start"]


# ---------------------------------------------------------------- 計画を集める
def collect(sc, tl, scripts_dir, index):
    """台本・章・行の指定を、時刻つきの指示の列にする。"""
    plan, opts = plan_settings(sc, scripts_dir)
    if sc.get("bgm") == "auto":
        plan = propose_plan(sc, tl, index)
    items = []
    for it in plan or []:
        items.append(dict(it, t=resolve_at(it.get("at", 0), tl)))
    for ch in sc["chapters"]:
        if "bgm" in ch:
            v = ch["bgm"]
            it = {"track": v} if isinstance(v, str) or v is None else dict(v)
            items.append(dict(it, t=chapter_start(tl, ch["id"])))
    for e in tl["events"]:
        if e["kind"] != "line":
            continue
        if "bgm" in e:
            v = e["bgm"]
            it = {"track": v} if isinstance(v, str) or v is None else dict(v)
            items.append(dict(it, t=e["start"]))
        if e.get("bgm_stop"):
            fd = e["bgm_stop"]
            items.append({"track": None, "fade": float(fd) if not isinstance(fd, bool) else 1.0, "t": e["start"]})
        if e.get("bgm_sting"):
            v = e["bgm_sting"]
            it = {"sting": v} if isinstance(v, str) else dict(v)
            at = it.get("at", 0.0)
            if isinstance(at, str) and at.startswith("end"):
                t = e["end"] + float(at[3:] or 0.0)
            else:
                t = e["start"] + float(at)
            items.append(dict(it, t=t))
    items.sort(key=lambda x: x["t"])
    return items, opts


# ---------------------------------------------------------------- 音
_cache = {}


def decode(path):
    if path not in _cache:
        r = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-f", "f32le", "-ac", "2", "-ar", str(SR), "-"],
                           stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if r.returncode != 0:
            raise SystemExit("bgm: 読めない " + path)
        _cache[path] = np.frombuffer(r.stdout, np.float32).reshape(-1, 2)
    return _cache[path]


def track_audio(index, tid):
    t = index.get(tid)
    if not t:
        raise SystemExit(f"bgm: 曲 {tid} が assets/bgm_cc0/index.json に無い")
    return decode(os.path.join(BGM_DIR, t["file"])), t


def looped(x, start_off, n, xf=480):
    """x を繰り返して n サンプル（start_off サンプル目から）。継ぎ目は 10 ms 重ねてクリックを消す
    （ogg の端や limiter で、加工済みのループでも 1 サンプル目がわずかに跳ぶことがある）。"""
    if len(x) == 0:
        return np.zeros((n, 2), np.float32)
    if len(x) <= xf * 4:
        idx = (np.arange(n) + start_off) % len(x)
        return x[idx]
    step = len(x) - xf
    t = np.linspace(0, np.pi / 2, xf, dtype=np.float32)[:, None]
    body = x.copy()
    body[:xf] = x[:xf] * np.sin(t) + x[len(x) - xf:] * np.cos(t)   # 2 回目以降の頭（前の尻尾と重ねた形）
    body = body[: step]
    reps = (start_off + n) // step + 2
    out = np.concatenate([x[:step]] + [body] * reps)
    return out[start_off: start_off + n]


def smooth_env(target, rate, attack, release):
    """制御レートの目標値を、下げるときは attack、上げるときは release でならす。"""
    out = np.empty_like(target)
    a = 1 - math.exp(-1 / (attack * rate)); r = 1 - math.exp(-1 / (release * rate))
    v = target[0]
    for i, x in enumerate(target):
        v += (x - v) * (a if x < v else r)
        out[i] = v
    return out


def to_samples(env, rate, n):
    t = np.arange(n) / SR
    return np.interp(t, np.arange(len(env)) / rate, env).astype(np.float32)


def render_bgm(sc, tl, scripts_dir, out_path):
    """BGM の計画を 48 kHz ステレオの wav にする（声の下で自動で下げる・一発ものの前後で落とす）。返り値は区間の一覧。"""
    index = load_index()
    items, opts = collect(sc, tl, scripts_dir, index)
    total = tl["total"]
    n = int(total * SR) + SR
    base_vol = float(opts.get("vol", DEFAULT_VOL))
    buf = np.zeros((n, 2), np.float32)
    sting_buf = np.zeros((n, 2), np.float32)
    # 区間: (t0, t1, track, vol, fade_in, offset)
    secs = []
    cur = None
    for it in items:
        if "sting" in it:
            continue
        if cur:
            cur["t1"] = it["t"]
            secs.append(cur)
        tid = it.get("track")
        cur = None if not tid else {"t0": it["t"], "t1": total, "track": tid,
                                    "vol": float(it.get("vol", base_vol)), "fade": float(it.get("fade", DEFAULT_FADE)),
                                    "offset": float(it.get("offset", 0.0))}
        if not tid and secs:
            secs[-1]["fade_out"] = float(it.get("fade", DEFAULT_FADE))
    if cur:
        secs.append(cur)
    for i, s in enumerate(secs):
        x, _meta = track_audio(index, s["track"])
        nxt_fade = secs[i + 1]["fade"] if i + 1 < len(secs) and abs(secs[i + 1]["t0"] - s["t1"]) < 0.01 else s.get("fade_out", DEFAULT_FADE)
        if i + 1 == len(secs) and s["t1"] >= total - 0.01:
            nxt_fade = 0.0
        a = int(s["t0"] * SR)
        b = min(n, int((s["t1"] + nxt_fade) * SR))
        if b <= a:
            continue
        seg = looped(x, int(s["offset"] * SR), b - a) * s["vol"]
        g = np.ones(b - a, np.float32)
        fi = int(s["fade"] * SR) if i > 0 or s["t0"] > 0.05 else int(0.5 * SR)
        fi = min(fi, b - a)
        if fi > 1:
            g[:fi] = np.sin(np.linspace(0, np.pi / 2, fi)) ** 2
        fo = int(nxt_fade * SR)
        if fo > 1:
            k = min(fo, b - a)
            g[-k:] *= np.cos(np.linspace(0, np.pi / 2, k)) ** 2
        buf[a:b] += seg * g[:, None]
    # 一発もの（勝ち・失敗のジングル）。鳴っているあいだ BGM を落とす
    rate = 100
    m = int(total * rate) + rate
    drop = np.ones(m, np.float32)
    stings = []
    for it in items:
        if "sting" not in it:
            continue
        x, meta = track_audio(index, it["sting"])
        a = int(it["t"] * SR)
        v = float(it.get("vol", DEFAULT_STING_VOL))
        k = min(len(x), n - a)
        if k > 0:
            sting_buf[a:a + k] += x[:k] * v
        dd = float(it.get("drop", len(x) / SR))
        lo = float(it.get("drop_to", 0.15))
        s0, s1 = int(max(0, it["t"] - 0.08) * rate), int((it["t"] + dd) * rate)
        drop[s0:s1] = np.minimum(drop[s0:s1], lo)
        stings.append((it["t"], it["sting"]))
    # 声の下で下げる
    duck_lvl = float(opts.get("duck", DEFAULT_DUCK))
    duck = np.ones(m, np.float32)
    for e in tl["events"]:
        if e["kind"] != "line":
            continue
        d = e.get("bgm_duck", duck_lvl)
        if d is False:
            continue
        d = float(d)
        s0, s1 = int(max(0, e["start"] - 0.05) * rate), int((e["end"] + 0.05) * rate)
        duck[s0:s1] = np.minimum(duck[s0:s1], d)
    env = smooth_env(duck * drop, rate, DUCK_ATTACK, DUCK_RELEASE)
    buf *= to_samples(env, rate, n)[:, None]
    s_env = smooth_env(np.maximum(duck, STING_DUCK), rate, DUCK_ATTACK, DUCK_RELEASE)
    sting_buf *= to_samples(s_env, rate, n)[:, None]
    # 終わり 4 秒でフェードアウト（今までのゲーム BGM と同じ）
    fo = int(4 * SR); e_ = int(total * SR)
    buf[max(0, e_ - fo):e_] *= np.linspace(1, 0, min(fo, e_), dtype=np.float32)[:, None]
    buf[e_:] = 0
    out = np.clip(buf + sting_buf, -1, 1)
    with wave.open(out_path, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((out * 32767).astype(np.int16).tobytes())
    return secs, stings


# ---------------------------------------------------------------- ゲーム音の大きさ
def game_gain_env(sc, tl, preset_game_vol):
    """ゲーム音（録画の音）に掛ける倍率を 100 Hz の列で返す。"""
    rate = 100
    m = int(tl["total"] * rate) + rate
    base = sc.get("game_vol")
    if base is None and isinstance(sc.get("bgm"), dict):
        base = sc["bgm"].get("game_vol")
    if base is None:
        base = DEFAULT_GAME_VOL if is_plan(sc) else preset_game_vol
    g = np.full(m, float(base), np.float32)
    for ch, c in zip(sc["chapters"], tl["chapters"]):
        if "game_vol" in ch:
            g[int(c["start"] * rate):int(c["end"] * rate)] = float(ch["game_vol"])
    lines = [e for e in tl["events"] if e["kind"] == "line"]
    for i, e in enumerate(lines):
        if "game_vol" not in e:
            continue
        end = lines[i + 1]["start"] if i + 1 < len(lines) else tl["total"]
        g[int(e["start"] * rate):int(end * rate)] = float(e["game_vol"])
    return smooth_env(g, rate, 0.25, 0.25), rate


def apply_game_vol(sc, tl, preset_game_vol, base_mp4, out_wav):
    """base.mp4 の音を取り出し、倍率を掛けて wav にする。"""
    r = subprocess.run(["ffmpeg", "-v", "error", "-i", base_mp4, "-vn", "-af", "aresample=48000:async=1:first_pts=0",
                        "-f", "f32le", "-ac", "2", "-ar", str(SR), "-"], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if r.returncode != 0:
        raise SystemExit("game audio の取り出しに失敗")
    x = np.frombuffer(r.stdout, np.float32).reshape(-1, 2)
    env, rate = game_gain_env(sc, tl, preset_game_vol)
    y = x * to_samples(env, rate, len(x))[:, None]
    with wave.open(out_wav, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(y, -1, 1) * 32767).astype(np.int16).tobytes())


# ---------------------------------------------------------------- 計画の提案
MOOD_RULES = [  # (mood, 章id・カードに含まれる語)
    ("ending", ("final", "last", "ending", "outro", "まとめ", "結論")),
    ("village", ("village", "村")),
    ("climax", ("boss", "climax", "大一番", "ボス", "決戦")),
    ("stream", ("stream", "配信")),
    ("result", ("result", "after", "_end", "結果", "リザルト")),
    ("smug", ("hook", "intro", "title", "op")),
]
STREAM_WORDS = ("配信", "スパチャ", "勇者", "魔王", "ボス", "増援", "介入", "張る", "コメント")
RESULT_WORDS = ("結果", "純利益", "儲け", "赤字", "黒字", "翌朝", "最終金額", "損")
WIN_RE = re.compile(r"(プラス|純利益|儲け|大勝ち|大儲け|黒字|倍)[^。！]*?[\d,０-９]+(万|億)?G|(大勝ち|大儲け|ジャックポット)")
FAIL_RE = re.compile(r"(マイナス|赤字|大損|ごめん|負けた|溶け|すっからかん|破産|ゲームオーバー)")
AMOUNT_RE = re.compile(r"(?:(\d[\d,]*)億)?(?:(\d[\d,]*)万)?(\d[\d,]*)?G")
BIG_WIN_G = 100_000       # これ以上の儲けを言う行で「大勝ち」の一発を鳴らす


def max_amount(text):
    """行の中のいちばん大きい金額（G）。「78万6,739G」「5,556,635G」「1.2億G」は読まない（小数は無視）。"""
    best = 0
    for m in AMOUNT_RE.finditer(text):
        oku, man, rest = (int(g.replace(",", "")) if g else 0 for g in m.groups())
        best = max(best, oku * 100_000_000 + man * 10_000 + rest)
    return best


ALT_MOOD = {"village": ["shop"], "shop": ["smug", "village"], "stream": ["climax"], "climax": ["stream"],
            "fail": ["smug"], "smug": ["shop"], "ending": ["village"], "win": ["smug"]}


def chapter_mood(ch):
    if ch.get("mood"):
        return ch["mood"]
    key = (ch["id"] + " " + (ch.get("card") or "")).lower()
    for mood, words in MOOD_RULES:
        if any(w.lower() in key for w in words):
            return mood
    return "shop"


def line_moods(sc, tl):
    """行ごとの気分（章の気分＋行の中身で配信・結果・失敗を見分ける）。返り値 [(t, mood, event)]。"""
    by_ch = {ch["id"]: ch for ch in sc["chapters"]}
    out = []
    lines = [e for e in tl["events"] if e["kind"] == "line"]
    state = {}
    for e in lines:
        ch = by_ch[e["chapter"]]
        cm = chapter_mood(ch)
        mood = e.get("mood")
        if not mood:
            if cm in ("village", "ending", "climax", "smug"):
                mood = cm
            else:
                st = state.get(e["chapter"], "stream" if cm == "stream" else "shop")
                txt = e["text"]
                if any(w in txt for w in STREAM_WORDS) and st == "shop":
                    st = "stream"
                elif any(w in txt for w in RESULT_WORDS) and st == "stream":
                    st = "result"
                state[e["chapter"]] = st
                mood = st
            if mood == "result":
                mood = "fail" if (e["e"] == "namida" or FAIL_RE.search(e["text"])) else "smug"
        out.append((e["start"], mood, e))
    return out


def propose_plan(sc, tl, index=None, max_run=MAX_RUN, min_sec=18.0):
    """章・行の中身から BGM の計画を作る（1 曲を max_run 秒より長く続けない・同じ曲を続けない・大勝ちで一発＋BGM を落とす）。"""
    index = index or load_index()
    loops = {}
    for t in index.values():
        if t["kind"] == "loop":
            loops.setdefault(t["mood"], []).append(t["id"])
    stings = {}
    for t in index.values():
        if t["kind"] == "sting":
            stings.setdefault(t["mood"], []).append(t["id"])
    lm = line_moods(sc, tl)
    # 気分の区間（短すぎる区間は前に吸わせる）
    secs = []
    for t, mood, e in lm:
        if mood == "win":
            mood = "smug"
        if not secs or secs[-1]["mood"] != mood:
            secs.append({"t0": t if secs else 0.0, "mood": mood, "first": e})
        # 失敗は短くてもよい（オチの曲）
    merged = []
    for i, s in enumerate(secs):
        t1 = secs[i + 1]["t0"] if i + 1 < len(secs) else tl["total"]
        s["t1"] = t1
        if merged and (t1 - s["t0"] < min_sec and s["mood"] not in ("fail", "ending")):
            merged[-1]["t1"] = t1
            continue
        if merged and merged[-1]["mood"] == s["mood"]:
            merged[-1]["t1"] = t1
            continue
        merged.append(s)
    # 曲を割り当てる（長い区間は max_run ごとに同じ気分の別の曲へ）
    plan, last, used = [], None, {}
    def pick(mood):
        nonlocal last
        cands = list(loops.get(mood, []))
        for alt in ALT_MOOD.get(mood, []):
            cands += [c for c in loops.get(alt, []) if c not in cands]
        cands = [c for c in cands if c != last] or cands
        own = [c for c in cands if index[c]["mood"] == mood]
        pool = own or cands
        c = min(pool, key=lambda c: used.get(c, 0))
        used[c] = used.get(c, 0) + 1
        last = c
        return c
    def anchor(t, e):
        if e is None:
            return round(t, 2)
        ls = [x for x in tl["events"] if x["kind"] == "line" and x["chapter"] == e["chapter"]]
        k = ls.index(e)
        return f"ch:{e['chapter']}" if k == 0 and abs(chapter_start(tl, e["chapter"]) - t) < 1.5 else f"line:{e['chapter']}/{k}"
    for s in merged:
        t0, t1 = s["t0"], s["t1"]
        n_parts = max(1, math.ceil((t1 - t0) / max_run))
        step = (t1 - t0) / n_parts
        for p in range(n_parts):
            ts = t0 + p * step
            e = s["first"] if p == 0 else next((x for x in tl["events"] if x["kind"] == "line" and x["start"] >= ts), None)
            if e is not None and p > 0:
                ts = e["start"]
            tid = pick(s["mood"])
            item = {"at": anchor(ts, e) if (p > 0 or t0 > 0) else 0, "track": tid,
                    "vol": MOOD_VOL.get(s["mood"], DEFAULT_VOL), "fade": 0.8 if s["mood"] == "fail" else DEFAULT_FADE,
                    "_mood": s["mood"], "_t": round(ts, 1)}
            plan.append(item)
    # 大勝ち・大失敗の一発（その行の頭。BGM を少し落とす）
    for t, mood, e in lm:
        if (e["e"] in ("surprise", "ama", "hero") and WIN_RE.search(e["text"]) and not FAIL_RE.search(e["text"])
                and max_amount(e["text"]) >= BIG_WIN_G):
            sid = (stings.get("win") or [None])[len([p for p in plan if "sting" in p]) % max(1, len(stings.get("win", [])))]
            if sid:
                plan.append({"at": anchor(t, e), "sting": sid, "vol": DEFAULT_STING_VOL, "drop": 3.0, "drop_to": 0.12, "_t": round(t, 1),
                             "_why": e["text"][:30]})
        elif e["e"] == "namida" and FAIL_RE.search(e["text"]):
            sid = (stings.get("fail") or [None])[0]
            if sid and not any(p.get("sting") == sid and abs(p["_t"] - t) < 60 for p in plan if "sting" in p):
                plan.append({"at": anchor(t, e), "sting": sid, "vol": 0.5, "drop": 2.5, "drop_to": 0.2, "_t": round(t, 1),
                             "_why": e["text"][:30]})
    plan.sort(key=lambda p: p["_t"])
    return plan


def check_plan(plan_secs, total, max_run=MAX_RUN):
    """長すぎる区間・同じ曲の連続を知らせる（止めはしない）。"""
    warns = []
    for i, s in enumerate(plan_secs):
        if s["t1"] - s["t0"] > max_run + 30:
            warns.append(f"{s['track']} が {s['t1'] - s['t0']:.0f} 秒続く（{s['t0']:.0f}〜）")
        if i and plan_secs[i - 1]["track"] == s["track"] and abs(plan_secs[i - 1]["t1"] - s["t0"]) < 0.01:
            warns.append(f"{s['track']} が続けて 2 回（{s['t0']:.0f} 秒）")
    return warns
