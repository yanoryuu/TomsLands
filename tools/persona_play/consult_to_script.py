"""相談プレイの会話ログ（Recordings/consult_log_<ラン ID>.jsonl）を jikkyo.py の台本（scripts/*.json と同じ形）にする（標準ライブラリだけ）。

レンダーはしない。既定は標準出力に台本の要約と検査結果を出すだけ（dry-run）。--out を付けたときだけ台本の JSON を書く。

    py -3 -X utf8 tools/persona_play/consult_to_script.py Recordings/consult_log_<ラン ID>.jsonl
    py -3 -X utf8 tools/persona_play/consult_to_script.py <log> --video Recordings/MarathonC_<ラン ID>_<日時>.mp4 --out <台本.json>

- 章 = 日 × 局面（営業日の朝 / 配信 / 配信中 / 結果 / 村）。footage は "consult"（footage.json の tags に録画を割り当てる。--video があれば割り当ての例も出す）。
- 行: s（z / m）・e（表情。jikkyo.py の STYLES のキー。めたんは namida を使わない）・t（セリフ）。
  録画の開始時刻（--video のファイル名の日時か --video-start）が分かれば、やり取りの最初の行に at（録画の秒）を付ける。
- ミーム音: snd（assets/sounds_meme/<id>.mp3）と snd_at（timing before → 行の前、after → 言い終わり、over → 行の頭）。
- ミーム: img（ミームの id。assets/memes_user に元ネタを置いたときだけ画面に出る）。
- 大当たり: 結果の行（result の verdict が good）で、いつもとの差（vsUsual）が --jackpot-min 以上のものを、
  そのランの差の大きい順に --jackpot-max 件まで「大勝ち」とし、必ず大当たりの音を付ける（--no-jackpot で付けない）。
  音は index.json の use・verified で meaning が大当たり / 当たり確定 / 大勝ち のもの。結果が出る瞬間（行の頭か前）に鳴らし、
  同じ音を続けて使わない。avoid の「1 本 N 回まで」を守る。ゲームが付けた音が勝ちの音（tags に win）なら、
  その音は同じ章の次の行（音が無く話し手が合うとき）へ移し、移せなければ外す。
- 検査: 禁止語（効いて / 効いた / 回目）、音が index.json で use・verified か・話し手が合うか、めたんの namida。
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOUND_INDEX = os.path.join(ROOT, "tools", "jikkyo_video", "assets", "sounds_meme", "index.json")
EMOTIONS = {"normal", "ama", "tsun", "sexy", "sasayaki", "hiso", "hero", "namida", "surprise"}
BANNED = ("効いて", "効いた", "回目")
JACKPOT_WORDS = ("大当たり", "当たり確定", "大勝ち")
JACKPOT_MIN = 400000   # いつもより何 G 多ければ大勝ちか（vsUsual）
JACKPOT_MAX = 3        # 1 ランで大当たりにする数の上限（差の大きい順）
PHASE_JA = {"day": "朝の相談", "stream": "配信の相談", "in_stream": "配信中", "result": "結果", "village": "村"}


def load_log(path):
    rows = []
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def jackpot_sounds(sounds):
    """大当たりの音（use・verified で meaning が大当たり系）。前に鳴らすもの（timing before）を先に並べる。"""
    out = []
    for x in sounds.values():
        if not (x.get("use") and x.get("verified")):
            continue
        if not any(w in (x.get("meaning") or "") for w in JACKPOT_WORDS):
            continue
        avoid = x.get("usage", {}).get("avoid") or ""
        avoid = " ".join(avoid) if isinstance(avoid, list) else avoid
        m = re.search(r"1\s*本\s*(\d+)\s*回まで", avoid)
        out.append({"id": x["id"], "who": x.get("who") or "either", "timing": x.get("timing") or "after",
                    "duration": float(x.get("duration") or 1.0), "limit": int(m.group(1)) if m else None})
    out.sort(key=lambda j: (j["timing"] != "before", j["id"]))
    return out


def big_wins(rows, min_gain=JACKPOT_MIN, max_n=JACKPOT_MAX):
    """大勝ちの行（結果の行で verdict good、vsUsual >= min_gain、差の大きい順に max_n 件）の index。"""
    cands = []
    for i, r in enumerate(rows):
        res = r.get("result")
        if not r.get("speaker") or not isinstance(res, dict) or res.get("verdict") != "good":
            continue
        gain = res.get("vsUsual")
        if isinstance(gain, (int, float)) and gain >= min_gain:
            cands.append((gain, i))
    cands.sort(reverse=True)
    return {i for _, i in cands[:max_n]}


def is_win_sound(info):
    return info is not None and "win" in (info.get("tags") or [])


def snd_at(timing, dur):
    return round(-(dur + 0.05), 2) if timing == "before" else "end+0.05" if timing == "after" else 0.0


def video_start(args):
    if args.video_start:
        return dt.datetime.fromisoformat(args.video_start)
    if args.video:
        m = re.search(r"_(\d{8}_\d{6})\.mp4$", os.path.basename(args.video))
        if m:
            return dt.datetime.strptime(m.group(1), "%Y%m%d_%H%M%S")
    return None


def build(rows, start=None, run_id="", jackpot=True, jackpot_min=JACKPOT_MIN, jackpot_max=JACKPOT_MAX):
    chapters, issues, jackpots = [], [], []
    sounds = {}
    try:
        with open(SOUND_INDEX, encoding="utf-8") as f:
            sounds = {x["id"]: x for x in json.load(f)}
    except OSError:
        issues.append("index.json を読めない（音の検査をしない）")
    pool = jackpot_sounds(sounds) if jackpot else []
    if jackpot and sounds and not pool:
        issues.append("大当たりの音が index.json に無い（大勝ちに音を付けない）")
    wins = big_wins(rows, jackpot_min, jackpot_max) if pool else set()
    used, last_jp = {}, None
    moved = None   # 大当たりに場所を譲った勝ちの音（次の行へ）
    cur = None
    last_exchange = None
    for idx, r in enumerate(rows):
        if not r.get("speaker"):
            continue   # silent / nod（声に出さなかった判断）
        key = (r.get("day"), r.get("phase"))
        if cur is None or cur["_key"] != key:
            if moved:
                moved["entry"]["moved"] = None   # 移す行が無い → 外す
                moved = None
            cur = {"_key": key, "id": f"d{int(r.get('day') or 0):02d}_{r.get('phase')}_{len(chapters):03d}", "footage": "consult", "lines": []}
            chapters.append(cur)
        s = "z" if r["speaker"] == "z" else "m"
        e = r.get("e") or "normal"
        if e not in EMOTIONS:
            issues.append(f"表情 {e} は jikkyo.py に無い → normal: {r.get('line')}")
            e = "normal"
        if s == "m" and e == "namida":
            issues.append(f"めたんの namida → tsun: {r.get('line')}")
            e = "tsun"
        text = r.get("line") or ""
        for b in BANNED:
            if b in text:
                issues.append(f"禁止語「{b}」: {text}")
        line = {"s": s, "e": e, "t": text}
        if start is not None and r.get("exchange") != last_exchange and r.get("unixMs"):
            at = (dt.datetime.fromtimestamp(r["unixMs"] / 1000) - start).total_seconds()
            if at >= 0:
                line["at"] = round(at, 1)
        last_exchange = r.get("exchange")
        if r.get("meme"):
            line["img"] = r["meme"]
        sid = r.get("snd")
        if sid:
            info = sounds.get(sid)
            if sounds and (info is None or not info.get("use") or not info.get("verified")):
                issues.append(f"音 {sid} は index.json で使える音ではない")
            elif info is not None:
                who = info.get("who")
                speaker = "zundamon" if s == "z" else "metan"
                if who not in ("either", speaker):
                    issues.append(f"音 {sid} は {who} 用（話し手 {speaker}）")
            timing = r.get("sndTiming") or (info or {}).get("timing") or "after"
            dur = float(r.get("sndDuration") or (info or {}).get("duration") or 1.0)
            line["snd"] = sid
            line["snd_at"] = snd_at(timing, dur)
        speaker = "zundamon" if s == "z" else "metan"
        if moved and "snd" not in line and moved["who"] in ("either", speaker):
            line["snd"], line["snd_at"] = moved["snd"], moved["snd_at"]
            moved = None
        if idx in wins:
            jp = pick_jackpot(pool, used, last_jp, speaker)
            old = line.get("snd")
            if jp is None:
                issues.append(f"大勝ちに付けられる大当たりの音が無い: {text}")
            elif old in {j["id"] for j in pool}:
                jp = next(j for j in pool if j["id"] == old)   # ゲームがもう大当たりの音を付けている
            else:
                if old and is_win_sound(sounds.get(old)):
                    moved = {"snd": old, "snd_at": line["snd_at"], "who": (sounds.get(old) or {}).get("who") or "either"}
                line["snd"] = jp["id"]
                # 結果が出る瞬間に鳴らす: before はその通り、after は行の頭に寄せる
                line["snd_at"] = snd_at("before", jp["duration"]) if jp["timing"] == "before" else 0.0
            if jp is not None:
                used[jp["id"]] = used.get(jp["id"], 0) + 1
                last_jp = jp["id"]
                res = r.get("result") or {}
                entry = {"chapter": cur["id"], "t": text, "snd": jp["id"], "replaced": old if old != jp["id"] else None,
                         "moved": moved["snd"] if moved else None, "replaced_win": bool(moved),
                         "reward": res.get("reward"), "vsUsual": res.get("vsUsual"), "kind": r.get("kind")}
                if moved:
                    moved["entry"] = entry
                jackpots.append(entry)
        cur["lines"].append(line)
    if moved:
        moved["entry"]["moved"] = None
    for c in chapters:
        c.pop("_key", None)
    script = {
        "id": f"consult_{run_id}" if run_id else "consult",
        "style": "play",
        "title": "相談プレイ（ずんだもんが遊び、めたんに相談）",
        "bgm": "配信フェーズ",
        "chapters": chapters,
    }
    return script, issues, jackpots


def pick_jackpot(pool, used, last, speaker):
    """前と違う音を優先し、回数の上限と話し手を守る。無ければ同じ音の連続も許す。"""
    ok = [j for j in pool if j["who"] in ("either", speaker) and (j["limit"] is None or used.get(j["id"], 0) < j["limit"])]
    fresh = [j for j in ok if j["id"] != last]
    if not ok:
        return None
    return min(fresh or ok, key=lambda j: used.get(j["id"], 0))


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("log", help="Recordings/consult_log_<ラン ID>.jsonl")
    ap.add_argument("--video", help="録画（MarathonC_<ラン ID>_<yyyyMMdd_HHmmss>.mp4）。ファイル名の日時を録画の開始とする")
    ap.add_argument("--video-start", help="録画の開始時刻（ISO 形式。--video の名前から取れないとき）")
    ap.add_argument("--out", help="台本の JSON を書く先（省くと書かない）")
    ap.add_argument("--no-jackpot", action="store_true", help="大勝ちに大当たりの音を付けない")
    ap.add_argument("--jackpot-min", type=float, default=JACKPOT_MIN, help="大勝ちとする、いつもとの差（G）の下限")
    ap.add_argument("--jackpot-max", type=int, default=JACKPOT_MAX, help="1 ランで大当たりにする数の上限")
    ap.add_argument("--show", type=int, default=40, help="標準出力に出す行の数")
    args = ap.parse_args(argv)
    rows = load_log(args.log)
    m = re.search(r"consult_log_(.+)\.jsonl$", os.path.basename(args.log))
    run_id = m.group(1) if m else ""
    script, issues, jackpots = build(rows, video_start(args), run_id, not args.no_jackpot, args.jackpot_min, args.jackpot_max)
    lines = [l for c in script["chapters"] for l in c["lines"]]
    decisions = [r for r in rows if r.get("decision")]
    rel = {}
    for r in decisions:
        k = r["decision"].get("relation")
        rel[k] = rel.get(k, 0) + 1
    followed = sum(1 for r in decisions if r["decision"].get("followed") is True)
    ignored = sum(1 for r in decisions if r["decision"].get("followed") is False)
    print(f"# {args.log}")
    print(f"章 {len(script['chapters'])} / セリフ {len(lines)}（ずんだもん {sum(1 for l in lines if l['s'] == 'z')}・"
          f"めたん {sum(1 for l in lines if l['s'] == 'm')}）/ 判断 {len(decisions)}（{rel}）/ 聞いた {followed}・聞かなかった {ignored} / "
          f"音 {sum(1 for l in lines if 'snd' in l)} / ミーム {sum(1 for l in lines if 'img' in l)}")
    if args.no_jackpot:
        print("大当たり: 付けない（--no-jackpot）")
    else:
        print(f"大当たり: {len(jackpots)} 件（いつもより {args.jackpot_min / 10000:g}万G 以上、多い順に {args.jackpot_max} 件まで）")
        for j in jackpots:
            extra = (f" / {j['replaced']} を置き換え" if j["replaced"] else "") + (
                f"（{j['moved']} は次の行へ）" if j["moved"] else "（勝ちの音だが移す行が無いので外した）" if j["replaced_win"] else "")
            print(f"  [{j['chapter']}] {j['t']} → {j['snd']}（{j['kind']}、+{(j['vsUsual'] or 0) / 10000:.1f}万G）{extra}")
    print(f"検査: {'問題なし' if not issues else str(len(issues)) + ' 件'}")
    for i in issues:
        print("  - " + i)
    print()
    n = 0
    for c in script["chapters"]:
        if n >= args.show:
            break
        print(f"[{c['id']}]")
        for l in c["lines"]:
            if n >= args.show:
                break
            extra = "".join(f" {k}={l[k]}" for k in ("at", "snd", "snd_at", "img") if k in l)
            print(f"  {'Z' if l['s'] == 'z' else 'M'}({l['e']}) {l['t']}{extra}")
            n += 1
    if args.video:
        print()
        print("footage.json の例: \"sources\": {\"consult\": \"" + args.video.replace("\\", "/") + "\"}, "
              "\"tags\": {\"consult\": {\"src\": \"consult\", \"start\": 0}}")
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump(script, f, ensure_ascii=False, indent=2)
        print(f"\n台本を書いた: {args.out}")
    return 1 if issues else 0


if __name__ == "__main__":
    sys.exit(main())
