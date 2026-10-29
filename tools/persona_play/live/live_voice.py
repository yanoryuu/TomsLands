# -*- coding: utf-8 -*-
"""
人格マラソンの一言を VOICEVOX で読み上げる（YouTube ライブ配信用）。

  py -I -X utf8 tools/persona_play/live/live_voice.py                  # いちばん新しいログを追いかける
  py -I -X utf8 tools/persona_play/live/live_voice.py --start-voicevox # VOICEVOX が落ちていたら裏で起こす
  py -I -X utf8 tools/persona_play/live/live_voice.py --duration 90    # 90 秒で止める（確認用）

- Recordings/persona_log_*.jsonl のいちばん新しいファイルの末尾から読み、"line" が空でない行を声にする。
  新しいファイル（次のラン）ができたら、今のファイルを読み切ってから乗り換える。
  相談ラン（ラン ID が _c）は consult_log_<ラン ID>.jsonl（めたんのセリフ入り）も追い、同じ一言は 1 回にまとめる。
- 古くなった行（ログの時刻から --max-age 秒を過ぎた行）は読まずに捨てる。同じ人の同じ一言が続いたら 1 回だけ読む。
- 音は既定の出力デバイスで鳴らす（winsound。ウィンドウは出さない）。OBS は「デスクトップ音声」で拾う。
- OBS 用のファイルを obs/ に書く（UTF-8）。テキストソースの「ファイルから読み取り」と画像ソースで使う。
    line.txt          いま読んでいる一言（読み終えて --hold 秒たつと空になる）
    speaker.txt       話している人（ずんだもん / 四国めたん）
    caption.txt       「ずんだもん：一言」の形
    zundamon_line.txt / metan_line.txt   その人の一言だけ（立ち絵の横に置く用）
    speaking.txt      zundamon / metan / 空
    day.txt           「13日目」（日が分からない行では前の値のまま）
    credit.txt        VOICEVOX:ずんだもん　VOICEVOX:四国めたん（起動時に 1 回書く）
    zundamon.png / metan.png   立ち絵。話している人は表情つき・口あき、もう一人は暗くする
止める: Ctrl+C（--duration を付けたときはその秒数で止まる）。
"""
import argparse
import collections
import datetime as dt
import glob
import io
import json
import os
import queue
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import wave
import winsound

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
TACHIE = os.path.join(REPO, "tools", "jikkyo_video", "assets", "tachie")
CHARS = os.path.join(REPO, "tools", "jikkyo_video", "assets", "chars")
VOICEVOX_EXE = r"D:\Tools\voicevox\windows-cpu\run.exe"

# 話者 × 感情 → VOICEVOX のスタイル id（tools/jikkyo_video/jikkyo.py の STYLES と同じ）
STYLES = {
    "z": {"normal": 3, "ama": 1, "tsun": 7, "sexy": 5, "sasayaki": 22, "hiso": 38, "hero": 75, "namida": 76, "surprise": 3},
    "m": {"normal": 2, "ama": 0, "tsun": 6, "sexy": 4, "sasayaki": 36, "hiso": 37, "hero": 6, "namida": 36, "surprise": 2},
}
NAME = {"z": "ずんだもん", "m": "四国めたん"}
KEY = {"z": "zundamon", "m": "metan"}
CREDIT = "VOICEVOX:ずんだもん　VOICEVOX:四国めたん"
DUP_WINDOW = 15.0    # 相談ログと persona_log の同じ一言をまとめる窓（秒）
HOLD_PERSONA = 2.0   # 相談ランで persona_log の行を待たせる秒数
EXCHANGE_MARGIN = 6.0  # 相談のやり取りの 1 行目は、古さの上限よりこの秒数だけ早く見切る

_log_fp = None


def log(*a):
    s = time.strftime("%H:%M:%S ") + " ".join(str(x) for x in a)
    if _log_fp:
        _log_fp.write(s + "\n")
        _log_fp.flush()
    else:
        try:
            print(s, flush=True)
        except Exception:
            pass


def speaker_code(persona):
    p = (persona or "").lower()
    if p.startswith("zunda") or p == "z":
        return "z"
    if p.startswith("metan") or p == "m":
        return "m"
    return None


def norm_line(s):
    return s.strip().rstrip("。．.！!").strip()


def parse_time(s):
    try:
        return dt.datetime.fromisoformat(s).timestamp()
    except Exception:
        return None


# ---------------------------------------------------------------- OBS 用ファイル
def write_atomic(path, data):
    """OBS が読んでいる途中でも壊れないよう、別名で書いてから差し替える。"""
    if isinstance(data, str):
        data = data.encode("utf-8")
    tmp = path + ".tmp"
    for i in range(20):
        try:
            with open(tmp, "wb") as f:
                f.write(data)
            os.replace(tmp, path)
            return
        except PermissionError:
            time.sleep(0.05)
    log("書けなかった:", path)


class Portraits:
    """立ち絵 PNG を作っておき、話し手が変わったら obs/zundamon.png, metan.png を差し替える。"""

    def __init__(self, out_dir, height, flip_metan, dim):
        self.out_dir, self.height, self.flip_metan, self.dim = out_dir, height, flip_metan, dim
        self.cache = {}
        self.boxes = {}
        self.current = {}
        self.ok = True
        try:
            from PIL import Image  # noqa: F401
        except Exception:
            log("Pillow が無いので立ち絵は書かない（テキストだけ）")
            self.ok = False

    def _src(self, code, emo, mouth):
        p = os.path.join(TACHIE, f"{code}_{emo}_{mouth}.png")
        if os.path.exists(p):
            return p
        p = os.path.join(TACHIE, f"{code}_normal_{mouth}.png")
        if os.path.exists(p):
            return p
        return os.path.join(CHARS, f"{code}_portrait.png")

    def _box(self, code):
        """その人の全部の立ち絵が収まる枠（表情で腕が動いても位置がずれないように）。"""
        if code in self.boxes:
            return self.boxes[code]
        from PIL import Image
        files = glob.glob(os.path.join(TACHIE, f"{code}_*.png")) or [os.path.join(CHARS, f"{code}_portrait.png")]
        box = None
        size = None
        for f in files:
            with Image.open(f) as im:
                if size is None:
                    size = im.size
                elif im.size != size:
                    continue
                b = im.getchannel("A").getbbox() if im.mode == "RGBA" else im.getbbox()
            if b:
                box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3]))
        self.boxes[code] = box
        return box

    def render(self, code, emo, mouth, dim):
        k = (code, emo, mouth, dim)
        if k in self.cache:
            return self.cache[k]
        from PIL import Image, ImageEnhance
        with Image.open(self._src(code, emo, mouth)) as im0:
            im = im0.convert("RGBA")
        box = self._box(code)
        if box and im.size[0] >= box[2] and im.size[1] >= box[3]:
            im = im.crop(box)
        if code == "m" and self.flip_metan:
            im = im.transpose(Image.FLIP_LEFT_RIGHT)
        w = max(1, round(im.width * self.height / im.height))
        im = im.resize((w, self.height), Image.LANCZOS)
        if dim:
            a = im.getchannel("A")
            im = ImageEnhance.Brightness(im.convert("RGB")).enhance(self.dim).convert("RGBA")
            im.putalpha(a)
        buf = io.BytesIO()
        im.save(buf, "PNG")
        self.cache[k] = buf.getvalue()
        return self.cache[k]

    def prepare(self):
        if not self.ok:
            return
        t = time.time()
        for code in ("z", "m"):
            for emo in STYLES[code]:
                self.render(code, emo, "open", False)
            self.render(code, "normal", "closed", False)
            self.render(code, "normal", "closed", True)
        log(f"立ち絵を用意した（{time.time() - t:.1f} 秒）")

    def show(self, speaking=None, emo="normal"):
        """speaking: 話している人のコード（None なら二人とも普通の顔）。"""
        if not self.ok:
            return
        for code in ("z", "m"):
            if speaking is None:
                k = (code, "normal", "closed", False)
            elif code == speaking:
                k = (code, emo if emo in STYLES[code] else "normal", "open", False)
            else:
                k = (code, "normal", "closed", True)
            if self.current.get(code) == k:
                continue
            write_atomic(os.path.join(self.out_dir, KEY[code] + ".png"), self.render(*k))
            self.current[code] = k


class ObsFiles:
    def __init__(self, out_dir, portraits):
        self.dir = out_dir
        self.portraits = portraits
        os.makedirs(out_dir, exist_ok=True)
        self.state = {}
        write_atomic(os.path.join(out_dir, "credit.txt"), CREDIT)
        self.clear()

    def _put(self, name, text):
        if self.state.get(name) == text:
            return
        write_atomic(os.path.join(self.dir, name), text)
        self.state[name] = text

    def speak(self, item):
        c = item["code"]
        self._put("line.txt", item["line"])
        self._put("speaker.txt", NAME[c])
        self._put("caption.txt", f"{NAME[c]}：{item['line']}")
        self._put("speaking.txt", KEY[c])
        self._put(KEY[c] + "_line.txt", item["line"])
        other = "m" if c == "z" else "z"
        self._put(KEY[other] + "_line.txt", "")
        d = item.get("day")
        if isinstance(d, int) and d >= 0:
            self._put("day.txt", f"{d}日目")
        self.portraits.show(c, item.get("e") or "normal")

    def silent(self):
        """読み終えた: 字幕は残し、立ち絵だけ普通の顔に戻す。"""
        self._put("speaking.txt", "")
        self.portraits.show(None)

    def clear(self):
        for n in ("line.txt", "speaker.txt", "caption.txt", "speaking.txt", "zundamon_line.txt", "metan_line.txt"):
            self._put(n, "")
        if "day.txt" not in self.state:
            self._put("day.txt", "")
        self.portraits.show(None)


# ---------------------------------------------------------------- VOICEVOX
class Voicevox:
    def __init__(self, url, speed, intonation, volume):
        self.url, self.speed, self.intonation, self.volume = url.rstrip("/"), speed, intonation, volume

    def alive(self):
        try:
            with urllib.request.urlopen(self.url + "/version", timeout=2) as r:
                return r.read().decode("utf-8").strip('"')
        except Exception:
            return None

    def init_speakers(self):
        for sid in sorted({s for st in STYLES.values() for s in st.values()}):
            try:
                urllib.request.urlopen(urllib.request.Request(
                    f"{self.url}/initialize_speaker?speaker={sid}&skip_reinit=true", method="POST"), timeout=60).read()
            except Exception as e:
                log("話者の準備に失敗:", sid, e)

    def synth(self, text, sid):
        q = urllib.request.urlopen(urllib.request.Request(
            f"{self.url}/audio_query?text={urllib.parse.quote(text)}&speaker={sid}", method="POST"), timeout=30).read()
        qj = json.loads(q)
        qj["speedScale"] = self.speed
        qj["intonationScale"] = self.intonation
        qj["volumeScale"] = self.volume
        qj["prePhonemeLength"] = 0.05
        qj["postPhonemeLength"] = 0.08
        qj["outputSamplingRate"] = 24000
        qj["outputStereo"] = False
        return urllib.request.urlopen(urllib.request.Request(
            f"{self.url}/synthesis?speaker={sid}", data=json.dumps(qj).encode("utf-8"),
            headers={"Content-Type": "application/json"}, method="POST"), timeout=60).read()


def start_voicevox(vv, exe, wait):
    if vv.alive():
        return True
    if not os.path.exists(exe):
        log("VOICEVOX が見つからない:", exe)
        return False
    log("VOICEVOX を裏で起動:", exe)
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 0  # SW_HIDE
    flags = subprocess.CREATE_NO_WINDOW | subprocess.CREATE_NEW_PROCESS_GROUP
    subprocess.Popen([exe, "--host", "127.0.0.1", "--port", "50021"], cwd=os.path.dirname(exe),
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     startupinfo=si, creationflags=flags, close_fds=True)
    t = time.time()
    while time.time() - t < wait:
        v = vv.alive()
        if v:
            log(f"VOICEVOX {v} が起きた（{time.time() - t:.0f} 秒）")
            return True
        time.sleep(1)
    log("VOICEVOX が起きない")
    return False


def check_audio():
    """0.1 秒の無音を鳴らして、既定の出力デバイスが使えるかを確かめる（使えなくても止めない）。"""
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(24000)
        w.writeframes(b"\0\0" * 2400)
    try:
        winsound.PlaySound(buf.getvalue(), winsound.SND_MEMORY | winsound.SND_NODEFAULT)
        log("音の出力: OK（Windows の既定の出力デバイス）")
        return True
    except RuntimeError as e:
        log(f"!! 音を鳴らせない（{e}）。Windows の既定の出力デバイスを確認。字幕と立ち絵だけ進める")
        return False


# ---------------------------------------------------------------- ログの追いかけ
class Follower:
    """1 つの jsonl の書き足された分を読む。"""

    def __init__(self, path, from_start):
        self.path = path
        self.buf = b""
        try:
            self.pos = 0 if from_start else os.path.getsize(path)
        except OSError:
            self.pos = 0

    def read(self):
        try:
            size = os.path.getsize(self.path)
        except OSError:
            return []
        if size < self.pos:  # 書き直された
            self.pos, self.buf = 0, b""
        if size == self.pos:
            return []
        with open(self.path, "rb") as f:
            f.seek(self.pos)
            data = f.read(size - self.pos)
        self.pos += len(data)
        self.buf += data
        *lines, self.buf = self.buf.split(b"\n")
        rows = []
        for raw in lines:
            raw = raw.strip()
            if raw:
                try:
                    rows.append(json.loads(raw.decode("utf-8")))
                except Exception as e:
                    log("読めない行:", e)
        return rows


def run_id(persona_path):
    """persona_log_<yyyyMMdd_HHmmss>_<ラン ID>.jsonl → ラン ID（無ければ None）。"""
    parts = os.path.basename(persona_path)[:-len(".jsonl")].split("_")
    return "_".join(parts[4:]) if len(parts) > 4 else None


class Tailer(threading.Thread):
    """いちばん新しい persona_log を追う。相談ラン（ラン ID が _c で終わる）は、
    めたんのセリフが入っている consult_log_<ラン ID>.jsonl も一緒に追う。"""

    def __init__(self, logs_glob, from_start, on_row, on_tick, stop):
        super().__init__(daemon=True)
        self.glob, self.from_start, self.on_row, self.on_tick, self.stop = logs_glob, from_start, on_row, on_tick, stop
        self.persona = None
        self.consult = None
        self.consult_path = None
        self.consult_start = from_start

    def newest(self):
        files = glob.glob(self.glob)
        if not files:
            return None
        return max(files, key=lambda p: (os.path.getmtime(p), p))

    def pump(self):
        for src, fol in (("consult", self.consult), ("persona", self.persona)):
            if fol:
                for r in fol.read():
                    self.on_row(r, src)

    def run(self):
        first = True
        while not self.stop.is_set():
            n = self.newest()
            if n and (self.persona is None or n != self.persona.path):
                if self.persona:
                    self.pump()  # 前のファイルを読み切ってから乗り換える
                    log("次のログに乗り換え:", os.path.basename(n))
                else:
                    log("ログ:", os.path.basename(n))
                start = self.from_start or not first
                self.persona = Follower(n, start)
                rid = run_id(n)
                self.consult = None
                self.consult_path = None
                if rid and rid.endswith("_c"):
                    self.consult_path = os.path.join(os.path.dirname(n), f"consult_log_{rid}.jsonl")
                    self.consult_start = start
                first = False
            if self.consult_path and self.consult is None and os.path.exists(self.consult_path):
                self.consult = Follower(self.consult_path, self.consult_start)
                log("相談ログも追う:", os.path.basename(self.consult_path))
            self.pump()
            self.on_tick()
            self.stop.wait(0.3)


# ---------------------------------------------------------------- 本体
class Live:
    def __init__(self, a):
        self.a = a
        self.stop = threading.Event()
        self.vv = Voicevox(a.voicevox, a.speed, a.intonation, a.volume)
        self.portraits = Portraits(a.out, a.portrait_height, not a.no_flip_metan, a.dim)
        self.obs = ObsFiles(a.out, self.portraits)
        self.pending = collections.deque()
        self.pending_cv = threading.Condition()
        self.ready = queue.Queue(maxsize=1)
        self.busy_until = 0.0
        self.last_key = None
        self.consult_seen = collections.deque()  # (見た時刻, キー)
        self.held = []                           # 相談ランの persona_log の行（少し待たせる）
        self.dropped_ex = collections.deque(maxlen=200)  # 古くて捨てた相談のやり取り（ラン, exchange）
        self.st = collections.Counter()
        self.lat = []   # ログの時刻 → 鳴り始め（秒）
        self.syn = []   # 合成にかかった秒

    def age(self, item, at=None):
        return (at or time.time()) - item["t"]

    def on_row(self, r, src="persona"):
        line = (r.get("line") or "").strip()
        if not line:
            return
        code = speaker_code(r.get("persona") or r.get("speaker"))
        if not code:
            return
        self.st["rows_" + src] += 1
        t = parse_time(r.get("time")) or time.time()
        if r.get("unixMs"):
            t = r["unixMs"] / 1000.0
        item = {"code": code, "line": line, "e": r.get("e") or "normal", "day": r.get("day"),
                "q": r.get("question") or r.get("kind"), "t": t, "key": (code, norm_line(line)),
                "ex": (r.get("run"), r["exchange"]) if src == "consult" and r.get("exchange") is not None else None,
                "seq": r.get("seq")}
        now = time.time()
        while self.consult_seen and now - self.consult_seen[0][0] > DUP_WINDOW:
            self.consult_seen.popleft()
        if src == "consult":
            # 相談ログが本筋。同じ一言を persona_log から持ってきて待たせていたら捨てる
            self.consult_seen.append((now, item["key"]))
            before = len(self.held)
            self.held = [h for h in self.held if h[1]["key"] != item["key"]]
            self.st["dup"] += before - len(self.held)
            self.emit(item)
        elif str(r.get("run") or "").endswith("_c"):
            # 相談ランの persona_log は相談ログとほぼ同じ一言が並ぶ。少し待って、相談ログに無かった分だけ読む
            if any(k == item["key"] for _, k in self.consult_seen):
                self.st["dup"] += 1
                return
            self.held.append((now, item))
        else:
            self.emit(item)

    def on_tick(self):
        now = time.time()
        while self.held and now - self.held[0][0] >= HOLD_PERSONA:
            self.emit(self.held.pop(0)[1])

    def emit(self, item):
        if item["key"] == self.last_key:
            self.st["dup"] += 1
            return
        self.last_key = item["key"]
        with self.pending_cv:
            self.pending.append(item)
            self.pending_cv.notify()

    def drop_if_stale(self, item, start_at):
        """古い行を捨てる。相談のやり取りは、途中が捨てられたら残りも捨てる（返事だけ聞こえないように）。"""
        ex = item.get("ex")
        if ex is not None and ex in self.dropped_ex:
            self.st["stale"] += 1
            log(f"  捨てた（やり取りの前が無い）{NAME[item['code']]}：{item['line']}")
            return True
        age = self.age(item, start_at)
        limit = self.a.max_age
        if ex is not None and item.get("seq") == 0:
            limit -= EXCHANGE_MARGIN  # 返事まで読み切れそうにないやり取りは始めない
        if self.a.max_age > 0 and age > limit:
            self.st["stale"] += 1
            if ex is not None:
                self.dropped_ex.append(ex)
            log(f"  捨てた（古い {age:.0f}s）{NAME[item['code']]}：{item['line']}")
            return True
        return False

    def synth_loop(self):
        while not self.stop.is_set():
            with self.pending_cv:
                while not self.pending and not self.stop.is_set():
                    self.pending_cv.wait(0.5)
                if self.stop.is_set():
                    return
                item = self.pending.popleft()
            # 読み始めるころには古くなっている行は合成しない
            if self.drop_if_stale(item, max(time.time(), self.busy_until)):
                continue
            sid = STYLES[item["code"]].get(item["e"], STYLES[item["code"]]["normal"])
            t0 = time.time()
            try:
                wav = self.vv.synth(item["line"], sid)
            except Exception as e:
                self.st["error"] += 1
                log("  合成に失敗:", e, item["line"])
                if not self.vv.alive():
                    self.stop.wait(3)
                continue
            item["synth"] = time.time() - t0
            item["wav"] = wav
            with wave.open(io.BytesIO(wav), "rb") as w:
                item["dur"] = w.getnframes() / w.getframerate()
            while not self.stop.is_set():
                try:
                    self.ready.put(item, timeout=0.5)
                    break
                except queue.Full:
                    pass

    def play(self, item):
        """既定の出力デバイスで鳴らす（終わるまで待つ）。鳴らせないときも、同じ長さだけ待って字幕の進みを保つ。"""
        if not self.a.no_audio:
            t0 = time.time()
            try:
                winsound.PlaySound(item["wav"], winsound.SND_MEMORY | winsound.SND_NODEFAULT)
                self.st["played"] += 1
                return
            except RuntimeError as e:
                self.st["audio_error"] += 1
                if self.st["audio_error"] in (1, 10) or self.st["audio_error"] % 100 == 0:
                    log(f"  音を鳴らせない（{e}。出力デバイスを確認。{self.st['audio_error']} 件目）")
            rest = item["dur"] - (time.time() - t0)
        else:
            rest = item["dur"]
        if rest > 0:
            self.stop.wait(rest)

    def play_loop(self):
        a = self.a
        deadline = time.time() + a.duration if a.duration > 0 else None
        last_end = None
        cleared = True
        talking = False
        while not self.stop.is_set():
            if deadline and time.time() >= deadline:
                break
            try:
                item = self.ready.get(timeout=0.25)
            except queue.Empty:
                if talking and last_end and time.time() - last_end > 0.6:
                    self.obs.silent()
                    talking = False
                if not cleared and last_end and a.hold >= 0 and time.time() - last_end > a.hold:
                    self.obs.clear()
                    cleared = True
                continue
            now = time.time()
            if self.drop_if_stale(item, now):
                continue
            self.busy_until = now + item["dur"]
            self.obs.speak(item)
            cleared = False
            talking = True
            self.lat.append(self.age(item, now))
            self.syn.append(item["synth"])
            self.st["spoken"] += 1
            log(f"{NAME[item['code']]}（{item['q']} / {item['e']}）遅れ {self.age(item, now):.1f}s "
                f"合成 {item['synth']:.2f}s 長さ {item['dur']:.1f}s：{item['line']}")
            if a.save_wav:
                write_atomic(os.path.join(a.save_wav, f"{self.st['spoken']:04d}_{KEY[item['code']]}.wav"), item["wav"])
            self.play(item)
            last_end = time.time()
            if a.gap > 0:
                time.sleep(a.gap)
        self.stop.set()

    def run(self):
        a = self.a
        if a.start_voicevox:
            if not start_voicevox(self.vv, a.voicevox_exe, a.voicevox_wait):
                return 2
        v = self.vv.alive()
        if not v:
            log("VOICEVOX に繋がらない:", a.voicevox, "（--start-voicevox で裏で起こせる）")
            return 2
        log("VOICEVOX", v)
        self.vv.init_speakers()
        self.portraits.prepare()
        if not a.no_audio:
            check_audio()
        self.obs.clear()
        th_s = threading.Thread(target=self.synth_loop, daemon=True)
        th_s.start()
        Tailer(a.logs, a.from_start, self.on_row, self.on_tick, self.stop).start()
        log(f"読み上げ開始（古さの上限 {a.max_age:.0f}s / 出力 {a.out}）")
        try:
            self.play_loop()
        except KeyboardInterrupt:
            pass
        finally:
            self.stop.set()
            with self.pending_cv:
                self.pending_cv.notify_all()
            self.obs.clear()
            self.summary()
        return 0

    def summary(self):
        st = self.st

        def avg(x):
            return sum(x) / len(x) if x else 0.0
        log(f"まとめ: 一言のある行 {st['rows_persona'] + st['rows_consult']}（persona_log {st['rows_persona']}・相談ログ {st['rows_consult']}）/ 読んだ {st['spoken']}（音が出た {st['played']}・鳴らせない {st['audio_error']}）/ "
            f"古くて捨てた {st['stale']} / 続けて同じで省いた {st['dup']} / 合成失敗 {st['error']} / "
            f"読み残し {len(self.pending) + self.ready.qsize()}")
        if self.lat:
            log(f"遅れ（ログの時刻→鳴り始め）平均 {avg(self.lat):.1f}s 最小 {min(self.lat):.1f}s 最大 {max(self.lat):.1f}s / "
                f"合成 平均 {avg(self.syn):.2f}s 最大 {max(self.syn):.2f}s")


def main():
    global _log_fp
    ap = argparse.ArgumentParser(description="人格マラソンの一言を VOICEVOX で読み上げ、OBS 用のファイルを書く")
    ap.add_argument("--logs", default=os.path.join(REPO, "Recordings", "persona_log_*.jsonl"), help="追いかけるログ（glob）")
    ap.add_argument("--out", default=os.path.join(HERE, "obs"), help="OBS 用ファイルの出力先")
    ap.add_argument("--voicevox", default="http://127.0.0.1:50021")
    ap.add_argument("--start-voicevox", action="store_true", help="VOICEVOX が落ちていたら run.exe を隠して起動する")
    ap.add_argument("--voicevox-exe", default=VOICEVOX_EXE)
    ap.add_argument("--voicevox-wait", type=float, default=90)
    ap.add_argument("--max-age", type=float, default=20, help="ログの時刻からこの秒数を過ぎた行は読まない（0 で無効）")
    ap.add_argument("--hold", type=float, default=4, help="読み終えて次が来ないとき、字幕を残す秒数（-1 で消さない）")
    ap.add_argument("--gap", type=float, default=0.15, help="一言と一言の間（秒）")
    ap.add_argument("--speed", type=float, default=1.2)
    ap.add_argument("--intonation", type=float, default=1.1)
    ap.add_argument("--volume", type=float, default=1.0)
    ap.add_argument("--portrait-height", type=int, default=720, help="立ち絵 PNG の高さ（px）")
    ap.add_argument("--dim", type=float, default=0.5, help="話していない方の明るさ（0〜1）")
    ap.add_argument("--no-flip-metan", action="store_true", help="四国めたんを左右反転しない（既定は反転して右向き）")
    ap.add_argument("--from-start", action="store_true", help="最初のログを頭から読む（確認用。--max-age 0 と一緒に）")
    ap.add_argument("--duration", type=float, default=0, help="この秒数で止める（0 で止めない）")
    ap.add_argument("--log", default="", help="進み具合をこのファイルに書く（pyw で隠して動かすとき）")
    ap.add_argument("--no-audio", action="store_true", help="鳴らさない（字幕と立ち絵だけ。確認用）")
    ap.add_argument("--save-wav", default="", help="読んだ声の wav をこのフォルダにも書く（確認用）")
    a = ap.parse_args()
    if a.save_wav:
        os.makedirs(a.save_wav, exist_ok=True)
    if a.log:
        _log_fp = open(a.log, "a", encoding="utf-8")
    sys.exit(Live(a).run())


if __name__ == "__main__":
    main()
