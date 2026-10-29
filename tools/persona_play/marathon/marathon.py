#!/usr/bin/env python3
"""人格マラソンの見張り役（標準ライブラリだけ）。

エディタ（Assets/Scripts/Editor/Recording/PersonaMarathon.cs）と Library/PersonaMarathon/ のファイルでやり取りして、
ずんだもん / 四国めたんのランを朝まで交互に回す。

- ランごとに人格と録画の有無を決める（録画はおよそ 2 時間に 1 回。録画しないランは Ollama を GPU、録画するランは CPU だけ）。
- Ollama を切り替える（E:\\Ollama\\app\\ollama.exe serve を隠しウィンドウで立て直す。CPU のときだけ CUDA_VISIBLE_DEVICES=-1）。
- セーブは人格ごとに 1 つ（Library/PersonaMarathonSaves/<persona>/main）を持ち続け、毎ラン「続きから」遊ぶ（村・村資金・ラン数が引き継がれる）。
- 心拍（heartbeat.json）・進み具合・応答なしを見て、止まったら Unity を強制終了して開き直し、同じ隔離セーブから続きを遊ぶ。
  3 回続けて失敗したらやめる。
- 07:30 を過ぎたら新しいランを始めず、08:00 までに全部止める。
- 毎ラン後にユーザーのセーブフォルダ（Player.log / Player-prev.log を除く）が開始時の控えと同じ md5 か確かめ、違えば全部止める。
- 進み具合のログ（progress.log）と朝のまとめ（summary.md）を Recordings/marathon_<日付>/ に書く。

使い方は tools/persona_play/README.md（start.ps1 / stop.ps1）。試すだけなら:
    py -3 tools/persona_play/marathon/marathon.py --dry-run          （Unity も Ollama も使わない模擬の一晩）
    py -3 -m unittest tools/persona_play/marathon/test_marathon.py   （判定の単体テスト）
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.request
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_PROJECT = HERE.parents[2]

PERSONAS = ("zundamon", "metan")   # 既定で交互に回す人格
# consult = 相談プレイ（ずんだもんが遊び、判断ごとに四国めたんに相談する。セーブは Library/PersonaMarathonSaves/consult/main）
ALL_PERSONAS = ("zundamon", "metan", "consult")
SAVE_NAME = "main"   # 人格ごとに持ち続けるセーブのフォルダ名
FACILITIES = ("hall", "guild", "tavern", "bank", "artisan", "plaza")
FACILITY_JA = {"hall": "領主館", "guild": "ギルド", "tavern": "酒場", "bank": "銀行", "artisan": "職人", "plaza": "広場"}
PERSONA_JA = {"zundamon": "ずんだもん", "metan": "四国めたん", "consult": "相談（ずんだもん＋めたん）"}

DEFAULTS = {
    # 動かし方をまとめて決める: "learn" = 学習（録画しない・GPU・記憶を育てる）。"video" = 撮影（録画・記憶は読むだけ・撮影用セーブ）。
    # None なら下の個別の設定どおり。video の読み取り専用はまだ C# 側に無いので、いまは始めずに止める（学習を壊さないため）
    "mode": None,
    "start_note": "",                   # 開始時に progress.log へ書く印（例: 人格の規則の版が変わった。前後で村の育ちを分けて数えるため）
    "stop_at": "08:00",                 # これまでに全部止める（"hh:mm" = 次のその時刻、"2026-10-30T23:00" = その日時）
    "no_new_run_after": "07:30",        # これ以降は新しいランを始めない（同上）
    "restart_every_hours": 0,           # 長く回すとき、ランの合間にこの時間ごとに Unity と Ollama を開き直す（0 = しない）
    "record_every_minutes": 120,        # 録画するランの間隔（前の録画ランの開始から）
    "record_first": False,              # true なら最初のランを録画する
    "first_persona": "zundamon",
    "personas": ["zundamon", "metan"],  # 回す人格（順に交互）。相談プレイだけなら ["consult"]
    # 回す順番の型（あれば personas / first_persona より優先。ランの番号で繰り返す）。例 ["consult", "consult", "zundamon", "metan"]
    "persona_pattern": None,
    # 録画してよい人格（無ければどれでも）。録画の番が録画しない人格に来たら、次にこの人格が来るランへ回す。例 ["consult"]
    "record_personas": None,
    # 前の録画ランの開始（ISO の日時。前のマラソンから 2 時間の間隔を引き継ぐ）。record_first より後ろに置く
    "last_record_at": None,
    "max_runs": 0,                      # これだけランを終えたらやめる（0 = 時刻まで）
    # 村の総合Lv がこれに達したセーブ（全施設 Lv5 = 30。ほぼクリア）は、次のランの前に
    # Library/PersonaMarathonSaves_cleared/<人格>_<日時>/ へ移して新しいセーブで始める（記憶・学習器・想起はそのまま）。0 = しない
    "reset_village_level": 30,
    "laps_growth": 2,                   # 1 ランで何周するか（録画なし）
    "laps_record": 2,                   # 同（録画あり。撮影プリセットと同じ 1 周目 → 村 → 2 周目）
    "time_scale_growth": 2.0,           # 録画しないランの速度倍率
    "scenario_fast_growth": True,       # 録画しないランは会話を飛ばす
    "max_run_minutes_growth": 100,      # 1 ラン（の 1 試行）がこれを超えたら止まったとみなす
    "max_run_minutes_record": 85,
    "record_max_minutes": 80,           # 録画の上限（エディタの自動録画の保険）
    "est_record_minutes": 55,           # 録画ランの見込み時間（終わりが stop_at に間に合わないなら録画しない）
    "min_free_gb_record": 30,           # 録画するランの前に要る空き（D:）
    "min_free_gb_any": 5,
    "space_drive": "D:\\",
    "heartbeat_stale_minutes": 4,       # 心拍がこれだけ来なければ止まった
    "progress_stale_minutes": 12,       # ターン・フェーズ・ログが動かなければ止まった（考え中は別）
    "thinking_stale_minutes": 6,        # 1 回の考え中がこれを超えたら止まった
    "not_responding_minutes": 3,        # Windows の「応答なし」がこれだけ続いたら止まった
    "start_timeout_minutes": 6,         # command を書いてから Play に入るまで
    "unity_ready_timeout_minutes": 25,  # 開き直してから心拍が来るまで（インポート込み）
    "max_consecutive_failures": 3,
    "soft_retries_per_run": 1,          # Unity は元気なのにランが途中で止まったとき、落とさずに続きから試す回数
    "poll_seconds": 15,
    "stop_grace_minutes": 6,            # stop_at のこれだけ前に、走っているランへ止める指示
    "user_save_dir": r"C:\Users\yanor\AppData\LocalLow\MozaicWorks\TomSLand",
    "user_save_ignore": ["Player.log", "Player-prev.log"],
    "user_save_soft": ["remoteconfig/"],  # 起動のたびに書き直されるダウンロードのキャッシュ（記録だけして止めない）
    # 会話エンジン（宴）は隔離セーブを使わず、いつも %USERPROFILE%\AppData\LocalLow\...\Utage\ に既読・自動セーブを書く。
    # ここが変わったときは開始時の控えから戻して（足されたファイルは消して）からもう一度確かめる
    "user_save_restore": ["Utage/"],
    "ollama_exe": r"E:\Ollama\app\ollama.exe",
    "ollama_models": r"E:\Ollama\models",
    "ollama_url": "http://localhost:11434",
    "ollama_keep_alive_gpu": "30m",
    "ollama_keep_alive_cpu": "2h",
    "ollama_final_mode": "gpu",         # 終わったら戻すモード（"gpu" / "cpu" / "leave"）
    "unity_exe": "",                    # 空なら起動中のエディタか ProjectVersion.txt から探す
}


# =====================================================================
# 小物
# =====================================================================

def parse_hhmm(text: str) -> dt.time:
    h, m = text.split(":")
    return dt.time(int(h), int(m))


def next_at(now: dt.datetime, hhmm: str) -> dt.datetime:
    """now 以降で最初の hh:mm。"2026-10-30T23:00" のように日付つきならその日時（過ぎていてもそのまま）。"""
    if "-" in hhmm or "T" in hhmm:
        return dt.datetime.fromisoformat(hhmm)
    t = parse_hhmm(hhmm)
    cand = now.replace(hour=t.hour, minute=t.minute, second=0, microsecond=0)
    if cand <= now:
        cand += dt.timedelta(days=1)
    return cand


def read_json(path: Path):
    try:
        with open(path, encoding="utf-8-sig") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def write_json_atomic(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    os.replace(tmp, path)


def md5_file(path: Path) -> str:
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def save_manifest(root: Path, ignore: list[str]) -> dict[str, str]:
    """フォルダの中身（相対パス → md5）。ignore はファイル名（どの階層でも）で除く。"""
    out = {}
    if not root.exists():
        return out
    for p in sorted(root.rglob("*")):
        if not p.is_file() or p.name in ignore:
            continue
        rel = p.relative_to(root).as_posix()
        try:
            out[rel] = md5_file(p)
        except OSError as e:  # 書き込み中などで読めない
            out[rel] = f"unreadable:{e.__class__.__name__}"
    return out


def compare_manifests(base: dict, now: dict, soft_prefixes: list[str]):
    """(厳密な違い, 記録だけする違い)。各要素は "changed: x" / "added: x" / "removed: x"。"""
    strict, soft = [], []
    for rel in sorted(set(base) | set(now)):
        if rel not in now:
            kind = "removed"
        elif rel not in base:
            kind = "added"
        elif base[rel] != now[rel]:
            kind = "changed"
        else:
            continue
        (soft if any(rel.startswith(pref) for pref in soft_prefixes) else strict).append(f"{kind}: {rel}")
    return strict, soft


# =====================================================================
# 予定（純粋なロジック）
# =====================================================================

class Scheduler:
    """次のランの人格と録画の有無を決める。"""

    def __init__(self, cfg: dict, start: dt.datetime):
        self.cfg = cfg
        self.stop_at = next_at(start, cfg["stop_at"])
        nna = next_at(start, cfg["no_new_run_after"])
        self.no_new_after = min(nna, self.stop_at)
        self.last_record_start = None if cfg["record_first"] else start
        if not cfg["record_first"] and cfg.get("last_record_at"):
            self.last_record_start = dt.datetime.fromisoformat(cfg["last_record_at"])

    def can_start(self, now: dt.datetime) -> bool:
        return now < self.no_new_after

    def record_due(self, now: dt.datetime) -> bool:
        if self.last_record_start is None:
            return True
        return now - self.last_record_start >= dt.timedelta(minutes=self.cfg["record_every_minutes"])

    def record_fits(self, now: dt.datetime) -> bool:
        return now + dt.timedelta(minutes=self.cfg["est_record_minutes"]) <= self.stop_at - dt.timedelta(minutes=5)

    def pattern(self) -> list[str]:
        return [p for p in self.cfg.get("persona_pattern") or [] if p in ALL_PERSONAS]

    def plan(self, now: dt.datetime, last_persona: str | None, record_counts: dict, free_gb: float | None, index: int = 0):
        """(persona, record, note)。index = これから始めるランの番号（0 から。persona_pattern の位置）。"""
        note = ""
        record = self.record_due(now)
        if record and not self.record_fits(now):
            record, note = False, f"録画ランは {self.stop_at:%H:%M} までに終わらないので録画しない"
        if record and free_gb is not None and free_gb < self.cfg["min_free_gb_record"]:
            record, note = False, f"空きが {free_gb:.0f} GB しかないので録画しない"
        pattern = self.pattern()
        if pattern:
            persona = pattern[index % len(pattern)]
        else:
            order = [p for p in self.cfg.get("personas") or PERSONAS if p in ALL_PERSONAS] or list(PERSONAS)
            if last_persona is None:
                persona = self.cfg["first_persona"] if self.cfg["first_persona"] in order else order[0]
            else:
                persona = order[(order.index(last_persona) + 1) % len(order)] if last_persona in order else order[0]
            if record and len(order) > 1:
                allowed = [p for p in order if self.may_record(p)]
                counts = [record_counts.get(p, 0) for p in allowed]
                if len(set(counts)) > 1:
                    persona = allowed[counts.index(min(counts))]
        if record and not self.may_record(persona):
            # 録画の番だが、この人格は録画しない → 間隔は数え直さず、次に録画してよい人格が来たランで録る
            record, note = False, f"録画の番だが {PERSONA_JA[persona]} は録画しないので、次の {'・'.join(PERSONA_JA[p] for p in self.cfg['record_personas'])} のランへ回す"
        return persona, record, note

    def may_record(self, persona: str) -> bool:
        allowed = self.cfg.get("record_personas")
        return not allowed or persona in allowed

    def mark_recorded(self, now: dt.datetime):
        self.last_record_start = now


# =====================================================================
# 実機の操作（テストでは sim.FakeEnv に差し替える）
# =====================================================================

NO_WINDOW = 0x08000000
DETACHED = 0x00000008
NEW_GROUP = 0x00000200


class RealEnv:
    def __init__(self, cfg: dict, project: Path, out_dir: Path):
        self.cfg = cfg
        self.project = project
        self.out_dir = out_dir

    # ---- 時間 ----
    def now(self) -> dt.datetime:
        return dt.datetime.now()

    def sleep(self, seconds: float) -> None:
        time.sleep(seconds)

    # ---- PowerShell ----
    def _ps(self, script: str, timeout=60) -> str:
        r = subprocess.run(
            ["powershell", "-NoProfile", "-NonInteractive", "-Command", script],
            capture_output=True, text=True, timeout=timeout, creationflags=NO_WINDOW,
        )
        return r.stdout.strip()

    # ---- Unity ----
    def unity_processes(self) -> list[dict]:
        """このプロジェクトを開いているエディタ（Unity.exe）。unity mcp（AppData の CLI）は除く。"""
        out = self._ps(
            "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | "
            "Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json -Compress"
        )
        if not out:
            return []
        try:
            data = json.loads(out)
        except ValueError:
            return []
        if isinstance(data, dict):
            data = [data]
        proj = norm_path(str(self.project))
        found = []
        for p in data:
            exe = p.get("ExecutablePath") or ""
            cmd = p.get("CommandLine") or ""
            if "\\hub\\editor\\" not in exe.lower() and "\\editor\\unity.exe" not in exe.lower():
                continue
            if project_arg(cmd) == proj:
                found.append({"pid": int(p["ProcessId"]), "exe": exe, "cmd": cmd})
        return found

    def unity_responding(self, pid: int):
        out = self._ps(f"$p = Get-Process -Id {pid} -ErrorAction SilentlyContinue; if ($p) {{ $p.Responding }}")
        if out.lower() == "true":
            return True
        if out.lower() == "false":
            return False
        return None

    def unity_kill(self, pid: int) -> None:
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True, creationflags=NO_WINDOW)
        # クラッシュ時の報告ウィンドウが残っていると開き直しの邪魔になる
        subprocess.run(["taskkill", "/IM", "UnityBugReporter.exe", "/F"], capture_output=True, creationflags=NO_WINDOW)
        for _ in range(60):
            if not any(p["pid"] == pid for p in self.unity_processes()):
                return
            time.sleep(1)

    def unity_quit(self, pid: int) -> None:
        """ふつうに閉じる（ウィンドウを閉じるのと同じ）。90 秒で閉じなければ（保存の確認などで止まったら）強制終了。"""
        self._ps(f"$p = Get-Process -Id {pid} -ErrorAction SilentlyContinue; if ($p) {{ [void]$p.CloseMainWindow() }}")
        for _ in range(90):
            if not any(p["pid"] == pid for p in self.unity_processes()):
                return
            time.sleep(1)
        self.unity_kill(pid)

    def unity_launch(self, exe: str) -> None:
        # Unity Hub の「開く」と同じ: Unity.exe -projectPath <プロジェクト>（今のエディタもこの引数で起動されていた）
        # 窓をできるだけ前に出さない: 最小化・アクティブにしない（SW_SHOWMINNOACTIVE = 7）で起動を頼む。
        # Unity がこの指定をどこまで守るか（スプラッシュ・後から出る窓）は確かめていない（README の「注意」）
        si = None
        if os.name == "nt":
            si = subprocess.STARTUPINFO()
            si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            si.wShowWindow = 7
        subprocess.Popen(
            [exe, "-projectPath", str(self.project)],
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            creationflags=DETACHED | NEW_GROUP, close_fds=True, startupinfo=si,
        )

    # ---- Ollama ----
    def ollama_alive(self) -> bool:
        try:
            with urllib.request.urlopen(self.cfg["ollama_url"].rstrip("/") + "/api/version", timeout=5) as r:
                return r.status == 200
        except Exception:
            return False

    def ollama_restart(self, mode: str) -> bool:
        """mode: "gpu" / "cpu"。トレイの Ollama も含めて落としてから serve を立て直す。"""
        for image in ("ollama app.exe", "ollama.exe"):
            subprocess.run(["taskkill", "/IM", image, "/F", "/T"], capture_output=True, creationflags=NO_WINDOW)
        time.sleep(3)
        env = os.environ.copy()
        env["OLLAMA_MODELS"] = self.cfg["ollama_models"]
        env["OLLAMA_KEEP_ALIVE"] = self.cfg["ollama_keep_alive_cpu" if mode == "cpu" else "ollama_keep_alive_gpu"]
        # Ollama 0.40 は CUDA を切っても Vulkan で GPU に載せる（2026-10-28 の試走で確認）。CPU のときは Vulkan も切る
        cpu_only = {"CUDA_VISIBLE_DEVICES": "-1", "OLLAMA_VULKAN": "0", "GGML_VK_VISIBLE_DEVICES": "-1"}
        for k, v in cpu_only.items():
            if mode == "cpu":
                env[k] = v
            else:
                env.pop(k, None)
        log = open(self.out_dir / "ollama.log", "a", encoding="utf-8")
        log.write(f"\n===== {dt.datetime.now():%Y-%m-%d %H:%M:%S} serve ({mode}) =====\n")
        log.flush()
        subprocess.Popen(
            [self.cfg["ollama_exe"], "serve"], env=env, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT,
            creationflags=NO_WINDOW | NEW_GROUP, close_fds=True,
        )
        for _ in range(60):
            if self.ollama_alive():
                return True
            time.sleep(1)
        return False

    # ---- ディスク ----
    def free_gb(self, drive: str) -> float | None:
        try:
            return shutil.disk_usage(drive).free / 1e9
        except OSError:
            return None


def norm_path(p: str) -> str:
    return os.path.normcase(os.path.normpath(p.strip().strip('"')))


def project_arg(cmdline: str) -> str | None:
    """コマンドラインの -projectPath の値（正規化）。"""
    import shlex
    try:
        parts = shlex.split(cmdline, posix=False)
    except ValueError:
        parts = cmdline.split()
    for i, a in enumerate(parts):
        if a.strip('"').lower() == "-projectpath" and i + 1 < len(parts):
            return norm_path(parts[i + 1])
    return None


def find_unity_exe(project: Path, running: list[dict]) -> str | None:
    for p in running:
        if p.get("exe") and Path(p["exe"]).exists():
            return p["exe"]
    ver = None
    try:
        for line in (project / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8").splitlines():
            if line.startswith("m_EditorVersion:"):
                ver = line.split(":", 1)[1].strip()
                break
    except OSError:
        pass
    if not ver:
        return None
    roots = [Path(r"C:\Program Files\Unity\Hub\Editor")]
    sec = Path(os.environ.get("APPDATA", "")) / "UnityHub" / "secondaryInstallPath.json"
    try:
        extra = json.loads(sec.read_text(encoding="utf-8") or '""')
        if extra:
            roots.insert(0, Path(extra))
    except (OSError, ValueError):
        pass
    for r in roots:
        exe = r / ver / "Editor" / "Unity.exe"
        if exe.exists():
            return str(exe)
    return None


# =====================================================================
# 見張り役
# =====================================================================

class GiveUp(Exception):
    pass


class Marathon:
    def __init__(self, cfg: dict, project: Path, out_dir: Path, env, dry_run=False):
        self.cfg = cfg
        self.project = project
        self.out_dir = out_dir
        self.env = env
        self.dry_run = dry_run
        self.ipc = project / "Library" / "PersonaMarathon"
        self.saves = project / "Library" / "PersonaMarathonSaves"
        self.cmd_path = self.ipc / "command.json"
        self.status_path = self.ipc / "status.json"
        self.hb_path = self.ipc / "heartbeat.json"
        self.stop_path = self.ipc / "stop.flag"
        self.runner_stop = self.ipc / "runner_stop.flag"
        self.pause_path = self.ipc / "pause.flag"      # pause.ps1: 走っているランが終わったら止める
        self.state_path = out_dir / "state.json"
        self.log_path = out_dir / "progress.log"
        self.start = env.now()
        self.sched = Scheduler(cfg, self.start)
        self.state = {
            "started": self.start.isoformat(timespec="seconds"),
            "stopAt": self.sched.stop_at.isoformat(timespec="seconds"),
            "noNewRunAfter": self.sched.no_new_after.isoformat(timespec="seconds"),
            "runs": [], "events": [], "ollamaMode": None, "stopReason": None, "personas": list(dict.fromkeys(cfg.get("persona_pattern") or cfg.get("personas") or PERSONAS)),
            "personaPattern": list(cfg.get("persona_pattern") or []), "recordPersonas": list(cfg.get("record_personas") or []),
            "saveChecks": [], "dryRun": dry_run,
        }
        self.ollama_mode = None
        self.consecutive_failures = 0
        self.unity_exe = cfg.get("unity_exe") or None
        self.baseline = None
        self.stopping = False
        self.last_restart = self.start

    # ---------------- ログ ----------------

    def day_log_path(self) -> Path:
        """1 日目は progress.log、2 日目からは progress_<日付>.log（長く回すときに 1 ファイルが大きくならないように）。"""
        today = self.env.now().date()
        return self.log_path if today == self.start.date() else self.out_dir / f"progress_{today:%Y-%m-%d}.log"

    def log(self, msg: str) -> None:
        line = f"{self.env.now():%Y-%m-%d %H:%M:%S} {msg}"
        self.out_dir.mkdir(parents=True, exist_ok=True)
        with open(self.day_log_path(), "a", encoding="utf-8") as f:
            f.write(line + "\n")
        try:
            print(line, flush=True)
        except (OSError, ValueError):  # pythonw では stdout が無い
            pass

    def event(self, kind: str, detail: str) -> None:
        self.state["events"].append({"time": self.env.now().isoformat(timespec="seconds"), "kind": kind, "detail": detail})
        self.save_state()

    def save_state(self) -> None:
        write_json_atomic(self.state_path, self.state)

    # ---------------- ユーザーのセーブの見張り ----------------

    def backup_user_saves(self) -> None:
        src = Path(self.cfg["user_save_dir"])
        ignore = self.cfg["user_save_ignore"]
        self.baseline = save_manifest(src, ignore)
        write_json_atomic(self.out_dir / "user_save_manifest.json", self.baseline)
        dst = self.out_dir / "user_save_backup"
        if src.exists() and not dst.exists():
            shutil.copytree(src, dst, ignore=shutil.ignore_patterns(*ignore))
        self.log(f"ユーザーのセーブを控えた: {len(self.baseline)} ファイル → {dst}")

    def restore_user_files(self, diffs: list[str]) -> list[str]:
        """控え（user_save_backup）から戻す。diffs は compare_manifests の "changed: x" など。戻せなかったものを返す。"""
        src = Path(self.cfg["user_save_dir"])
        backup = self.out_dir / "user_save_backup"
        failed = []
        for d in diffs:
            kind, rel = d.split(": ", 1)
            target = src / rel
            try:
                if kind == "added":
                    target.unlink()
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(backup / rel, target)
            except OSError as e:
                failed.append(f"{d}（{e.__class__.__name__}）")
        return failed

    def verify_user_saves(self, when: str) -> bool:
        now = save_manifest(Path(self.cfg["user_save_dir"]), self.cfg["user_save_ignore"])
        strict, soft = compare_manifests(self.baseline or {}, now, self.cfg["user_save_soft"])
        prefixes = self.cfg.get("user_save_restore") or []
        restorable = [d for d in strict if any(d.split(": ", 1)[1].startswith(p) for p in prefixes)]
        if restorable:
            failed = self.restore_user_files(restorable)
            self.log(f"セーブ確認（{when}）: 会話エンジンの保存を開始時の控えに戻した: {', '.join(restorable)}"
                     + (f"（戻せなかった: {', '.join(failed)}）" if failed else ""))
            self.state.setdefault("restored", []).append({"time": self.env.now().isoformat(timespec="seconds"), "when": when,
                                                         "files": restorable, "failed": failed})
            now = save_manifest(Path(self.cfg["user_save_dir"]), self.cfg["user_save_ignore"])
            strict, soft = compare_manifests(self.baseline or {}, now, self.cfg["user_save_soft"])
        self.state["saveChecks"].append({"time": self.env.now().isoformat(timespec="seconds"), "when": when,
                                         "ok": not strict, "strict": strict, "soft": soft})
        self.save_state()
        if soft:
            self.log(f"セーブ確認（{when}）: キャッシュだけ変わった（止めない）: {', '.join(soft)}")
        if strict:
            self.log(f"!!! セーブ確認（{when}）: ユーザーのセーブが開始時と違う: {', '.join(strict)}")
            self.log(f"!!! 全部止めます。控えは {self.out_dir / 'user_save_backup'}")
            return False
        self.log(f"セーブ確認（{when}）: 開始時と同じ（{len(now)} ファイル）")
        return True

    # ---------------- Ollama ----------------

    def ensure_ollama(self, mode: str) -> None:
        if self.ollama_mode == mode and self.env.ollama_alive():
            return
        self.log(f"Ollama を {mode.upper()} で立て直す")
        ok = self.env.ollama_restart(mode)
        self.ollama_mode = mode if ok else None
        self.state["ollamaMode"] = self.ollama_mode
        self.event("ollama", f"{mode} {'ok' if ok else 'failed'}")
        if not ok:
            self.log("!!! Ollama が立ち上がらない（このランの判断は標準戦略になる）")

    # ---------------- Unity ----------------

    def unity_pid(self) -> int | None:
        procs = self.env.unity_processes()
        if not self.unity_exe:
            self.unity_exe = find_unity_exe(self.project, procs)
        hb = read_json(self.hb_path) or {}
        for p in procs:
            if p["pid"] == hb.get("pid"):
                return p["pid"]
        return procs[0]["pid"] if procs else None

    def heartbeat(self):
        hb = read_json(self.hb_path)
        if not hb:
            return None, None
        age = self.env.now().timestamp() - float(hb.get("unix", 0))
        return hb, age

    def wait_unity_ready(self, since: float) -> None:
        """心拍が since より新しく、エディタが待機中になるまで待つ。"""
        deadline = self.env.now() + dt.timedelta(minutes=self.cfg["unity_ready_timeout_minutes"])
        while self.env.now() < deadline:
            hb, age = self.heartbeat()
            if hb and float(hb.get("unix", 0)) >= since and age is not None and age < 60:
                if hb.get("compileFailed"):
                    raise GiveUp("コンパイルエラーでエディタが Play に入れない")
                if hb.get("editor") == "idle":
                    return
            if self.unity_pid() is None and self.env.now().timestamp() - since > 120:
                raise RuntimeError("Unity が起動していない")
            self.env.sleep(10)
        raise RuntimeError("開き直した Unity の心拍が来ない")

    def ensure_unity(self) -> None:
        pid = self.unity_pid()
        hb, age = self.heartbeat()
        if pid is not None and hb and age is not None and age < 60 and hb.get("editor") != "playing":
            if hb.get("compileFailed"):
                raise GiveUp("コンパイルエラーでエディタが Play に入れない")
            return
        if pid is None:
            self.launch_unity()
            return
        # 起動中（コンパイル・インポート中）か、前のランの Play が残っている
        self.wait_unity_ready(since=0)

    def launch_unity(self) -> None:
        if not self.unity_exe:
            self.unity_exe = find_unity_exe(self.project, [])
        if not self.unity_exe:
            raise GiveUp("Unity.exe が見つからない（ProjectSettings/ProjectVersion.txt と Unity Hub の場所を確認）")
        since = self.env.now().timestamp()
        self.log(f"Unity を起動: {self.unity_exe} -projectPath {self.project}")
        self.env.unity_launch(self.unity_exe)
        self.wait_unity_ready(since)
        self.log("Unity の準備ができた")

    def restart_unity(self, why: str) -> None:
        pid = self.unity_pid()
        self.log(f"!!! Unity を強制終了して開き直す（{why}）pid={pid}")
        self.event("restart", why)
        if pid is not None:
            self.env.unity_kill(pid)
        for p in (self.cmd_path, self.stop_path):
            try:
                p.unlink()
            except FileNotFoundError:
                pass
        self.launch_unity()

    def periodic_restart(self) -> None:
        """長く回すとき: ランの合間に Unity をふつうに閉じて開き直し、Ollama も立て直す（メモリの溜まり・固まり対策）。"""
        hours = float(self.cfg.get("restart_every_hours") or 0)
        if hours <= 0 or self.env.now() - self.last_restart < dt.timedelta(hours=hours):
            return
        self.last_restart = self.env.now()
        pid = self.unity_pid()
        self.log(f"定期の開き直し（{hours:g} 時間ごと）: Unity（pid={pid}）を閉じて開き直し、Ollama も立て直す")
        self.event("periodic_restart", f"every {hours:g}h")
        if pid is not None:
            quit_ = getattr(self.env, "unity_quit", None)
            (quit_ or self.env.unity_kill)(pid)
        for p in (self.cmd_path, self.stop_path):
            try:
                p.unlink()
            except FileNotFoundError:
                pass
        self.ollama_mode = None
        self.launch_unity()

    # ---------------- 1 ラン ----------------

    def run_once(self, run: dict) -> str:
        """1 ランを終わりまで（落ちたら続きから）。戻り値: finished / stopped / gave_up / save_alert。"""
        resume = False
        soft_left = self.cfg["soft_retries_per_run"]
        while True:
            if self.stopping:
                return "stopped"
            try:
                self.ensure_unity()
            except GiveUp:
                raise
            except Exception as e:  # 起動できない → 失敗として数えて開き直す
                if not self.count_failure(run, f"Unity の準備に失敗: {e}"):
                    return "gave_up"
                self.restart_unity_safe(run, str(e))
                resume = True
                continue

            outcome, detail, laps_done = self.attempt(run, resume)
            run["lapsDone"] = max(run.get("lapsDone", 0), laps_done)
            if outcome == "finished":
                self.consecutive_failures = 0
                return "finished"
            if outcome == "stopped" or self.stopping:
                return "stopped"
            if run["lapsDone"] >= run["laps"]:
                self.log(f"ラン {run['id']}: 周回は終わっていた（{detail}）→ 完了扱い")
                self.consecutive_failures = 0
                return "finished"
            if not self.count_failure(run, detail):
                return "gave_up"
            resume = True
            if outcome == "soft" and soft_left > 0:
                soft_left -= 1
                run["softRetries"] = run.get("softRetries", 0) + 1
                self.log(f"ラン {run['id']}: Unity は元気なので落とさずに続きから試す（{detail}）")
                continue
            self.restart_unity_safe(run, detail)

    def restart_unity_safe(self, run: dict, why: str) -> None:
        run["restarts"] = run.get("restarts", 0) + 1
        self.save_state()
        try:
            self.restart_unity(why)
        except GiveUp:
            raise
        except Exception as e:
            self.log(f"!!! 開き直しに失敗: {e}")
            if not self.count_failure(run, f"開き直しに失敗: {e}"):
                raise GiveUp(f"Unity を開き直せない: {e}")

    def count_failure(self, run: dict, detail: str) -> bool:
        self.consecutive_failures += 1
        run.setdefault("failures", []).append({"time": self.env.now().isoformat(timespec="seconds"), "detail": detail})
        self.save_state()
        self.log(f"!!! ラン {run['id']} が失敗（連続 {self.consecutive_failures}/{self.cfg['max_consecutive_failures']}）: {detail}")
        if self.consecutive_failures >= self.cfg["max_consecutive_failures"]:
            self.log("!!! 3 回続けて失敗したのでマラソンをやめます")
            return False
        return True

    def attempt(self, run: dict, resume: bool):
        """command を書いて終わるまで見張る。(outcome, detail, lapsDone)。outcome: finished / stopped / soft / hang。"""
        nonce = uuid.uuid4().hex[:12]
        cmd = {
            "id": run["id"], "nonce": nonce, "persona": run["persona"], "record": run["record"], "resume": resume,
            "saveRoot": run["saveRoot"], "laps": run["laps"], "lapsDone": run.get("lapsDone", 0),
            "timeScale": self.cfg["time_scale_growth"], "scenarioFast": self.cfg["scenario_fast_growth"],
            "maxMinutes": self.cfg["record_max_minutes"],
        }
        for p in (self.stop_path,):
            try:
                p.unlink()
            except FileNotFoundError:
                pass
        write_json_atomic(self.cmd_path, cmd)
        run["attempts"] = run.get("attempts", 0) + 1
        self.save_state()
        self.log(f"ラン {run['id']}（{PERSONA_JA[run['persona']]}・録画{'あり' if run['record'] else 'なし'}）"
                 f"{'続きから' if resume else '開始'}: {run.get('lapsDone', 0)}/{run['laps']} 周済み")

        t0 = self.env.now()
        max_run = dt.timedelta(minutes=self.cfg["max_run_minutes_record" if run["record"] else "max_run_minutes_growth"])
        last_key, last_progress = None, t0
        thinking_since = None
        not_resp_since = None
        hb_missing_since = None  # 心拍の読み取りに一度失敗しただけでは止まったとみなさない
        paused_logged = False
        laps_done = run.get("lapsDone", 0)
        stop_sent_at = None
        poll = self.cfg["poll_seconds"]

        while True:
            self.env.sleep(poll)
            now = self.env.now()
            st = read_json(self.status_path)
            if st and st.get("nonce") == nonce:
                laps_done = max(laps_done, int(st.get("lapsDone", 0)))
                # 続きからの試行の status には、その試行で終わった周しか無いので周の番号で足し合わせる
                merged = {int(l.get("lap", 0)): l for l in run.get("laps_result", [])}
                for l in st.get("laps", []) or []:
                    merged[int(l.get("lap", 0))] = l
                run["laps_result"] = [merged[k] for k in sorted(merged)]
                if st.get("recording"):
                    run.setdefault("recordings", [])
                    if st["recording"] not in run["recordings"]:
                        run["recordings"].append(st["recording"])
                state = st.get("state")
                if state == "finished":
                    self.log(f"ラン {run['id']}: 完了（{laps_done}/{run['laps']} 周）")
                    return "finished", "", laps_done
                if state == "stopped":
                    self.log(f"ラン {run['id']}: 止めた（{st.get('reason', '')}）")
                    return "stopped", st.get("reason", ""), laps_done
                if state in ("aborted", "failed"):
                    return "soft", f"{state}: {st.get('reason', '')}", laps_done
            elif self.cmd_path.exists() and now - t0 > dt.timedelta(minutes=self.cfg["start_timeout_minutes"]):
                return "hang", "エディタが command を読まない", laps_done

            # ---- 08:00 に向けて止める ----
            if self.should_stop_now(now) and stop_sent_at is None:
                self.log("止める時刻なので、走っているランに止める指示を出す")
                self.stop_path.parent.mkdir(parents=True, exist_ok=True)
                self.stop_path.write_text("stop", encoding="utf-8")
                stop_sent_at = now
                self.stopping = True
            if stop_sent_at is not None and now >= self.sched.stop_at:
                pid = self.unity_pid()
                hb, _ = self.heartbeat()
                if pid is not None and hb and hb.get("editor") == "playing":
                    self.log("!!! 08:00 を過ぎても Play が終わらないので Unity を強制終了する")
                    self.env.unity_kill(pid)
                return "stopped", "08:00", laps_done

            # ---- 止まっていないか ----
            pid = self.unity_pid()
            if pid is None:
                return "hang", "Unity のプロセスが無い（落ちた）", laps_done
            hb, age = self.heartbeat()
            if hb is None:
                # Unity の書き込み中に読むと壊れた JSON に見えることがある。続けて読めないときだけ止まったとみなす
                hb_missing_since = hb_missing_since or now
                if now - hb_missing_since > dt.timedelta(minutes=self.cfg["heartbeat_stale_minutes"]):
                    return "hang", "心拍のファイルが読めない", laps_done
                continue
            hb_missing_since = None
            if age is None or age > self.cfg["heartbeat_stale_minutes"] * 60:
                if now - t0 > dt.timedelta(minutes=2):
                    return "hang", f"心拍が {0 if age is None else age / 60:.1f} 分来ない", laps_done
                continue
            responding = self.env.unity_responding(pid)
            if responding is False:
                not_resp_since = not_resp_since or now
                if now - not_resp_since > dt.timedelta(minutes=self.cfg["not_responding_minutes"]):
                    return "hang", "Windows が「応答なし」と言っている", laps_done
            else:
                not_resp_since = None
            key = (hb.get("turn"), hb.get("phase"), hb.get("logEvents"), hb.get("lapsDone"), hb.get("state"), hb.get("editor"))
            if key != last_key:
                last_key, last_progress = key, now
            if hb.get("pausedNoFocus"):
                # フォーカスが無くて Play が止まっている（runInBackground が入っていない）だけ。固まりではないので落とさずに待つ
                # （開き直すと Unity の窓が前に出てユーザーの操作を奪う）。待った分は「進んでいない」「1 ランの長さ」に数えない
                if not paused_logged:
                    self.log(f"ラン {run['id']}: Unity にフォーカスが無くて Play が止まっている → 固まりとは見なさず待つ")
                    paused_logged = True
                last_progress = now
                t0 += dt.timedelta(seconds=poll)
                thinking_since = None
                continue
            paused_logged = False
            if hb.get("thinking"):
                thinking_since = thinking_since or now
                if now - thinking_since > dt.timedelta(minutes=self.cfg["thinking_stale_minutes"]):
                    return "hang", "モデルの答えを待ったまま進まない", laps_done
            else:
                thinking_since = None
                if now - last_progress > dt.timedelta(minutes=self.cfg["progress_stale_minutes"]):
                    return "hang", f"{self.cfg['progress_stale_minutes']} 分進んでいない（{hb.get('phase')} / {hb.get('autoplayStatus')}）", laps_done
            if now - t0 > max_run:
                return "hang", f"1 ランが {max_run.total_seconds() / 60:.0f} 分を超えた", laps_done

    def should_stop_now(self, now: dt.datetime) -> bool:
        if self.stopping:
            return True
        if self.runner_stop.exists():
            self.log("止める指示（runner_stop.flag）を受けた")
            return True
        return now >= self.sched.stop_at - dt.timedelta(minutes=self.cfg["stop_grace_minutes"])

    # ---------------- 一晩 ----------------

    def run(self) -> None:
        self.out_dir.mkdir(parents=True, exist_ok=True)
        self.log(f"===== 人格マラソン開始（{'模擬' if self.dry_run else '実機'}）: 新しいランは {self.sched.no_new_after:%m/%d %H:%M} まで、"
                 f"{self.sched.stop_at:%m/%d %H:%M} に全部止める =====")
        try:
            self.runner_stop.unlink()
        except FileNotFoundError:
            pass
        if self.cfg.get("start_note"):
            self.log(f"*** {self.cfg['start_note']}")
            self.state["startNote"] = self.cfg["start_note"]
        if self.sched.pattern():
            self.log(f"回す順番: {' → '.join(PERSONA_JA[p] for p in self.sched.pattern())}（繰り返し）")
        if self.cfg.get("record_personas"):
            self.log(f"録画するのは {'・'.join(PERSONA_JA[p] for p in self.cfg['record_personas'])} のランだけ"
                     + (f"（前の録画 {self.sched.last_record_start:%H:%M} から {self.cfg['record_every_minutes']} 分ごと）"
                        if self.sched.last_record_start and self.sched.last_record_start != self.start else ""))
        self.backup_user_saves()
        stop_reason = "予定の時刻"
        index = 0
        last_persona = None
        try:
            while True:
                now = self.env.now()
                if self.runner_stop.exists():
                    stop_reason = "stop.ps1 で止めた"
                    break
                if self.pause_path.exists():
                    stop_reason = "pause.ps1 で一時停止（resume.ps1 で同じ設定・同じセーブの続きから）"
                    try:
                        self.pause_path.unlink()
                    except FileNotFoundError:
                        pass
                    break
                if self.cfg.get("max_runs") and index >= int(self.cfg["max_runs"]):
                    stop_reason = f"決めたラン数（{self.cfg['max_runs']} 本）を終えた"
                    break
                if not self.sched.can_start(now):
                    stop_reason = f"{self.cfg['no_new_run_after']} を過ぎたので新しいランは始めない"
                    break
                free = self.env.free_gb(self.cfg["space_drive"])
                if free is not None and free < self.cfg["min_free_gb_any"]:
                    stop_reason = f"D: の空きが {free:.1f} GB しかない"
                    break
                counts = {p: sum(1 for r in self.state["runs"] if r["persona"] == p and r["record"]) for p in ALL_PERSONAS}
                persona, record, note = self.sched.plan(now, last_persona, counts, free, index)
                if note:
                    self.log(note)
                if self.cfg.get("never_record"):
                    record = False
                try:
                    self.periodic_restart()
                except GiveUp as e:
                    stop_reason = f"あきらめた: {e}"
                    self.log(f"!!! {stop_reason}")
                    break
                except RuntimeError as e:
                    self.log(f"!!! 定期の開き直しで Unity が戻らない: {e}（次のランの前にもう一度確かめる）")
                if record:
                    self.sched.mark_recorded(now)
                index += 1
                run_id = f"{self.start:%Y%m%d_%H%M}_{index:02d}_{persona[0]}"   # 同じ日に何度始めても ID がかぶらないように時刻も入れる
                save_root = self.saves / persona / SAVE_NAME
                before = self.reset_if_cleared(persona, save_root, read_village(save_root))
                run = {
                    "id": run_id, "persona": persona, "record": record,
                    "laps": self.cfg["laps_record" if record else "laps_growth"], "lapsDone": 0,
                    "saveRoot": str(save_root),
                    "started": now.isoformat(timespec="seconds"), "ended": None, "result": None,
                    "attempts": 0, "restarts": 0, "softRetries": 0,
                    "freshSave": before is None, "villageBefore": before,
                }
                self.state["runs"].append(run)
                self.save_state()
                if before is None:
                    self.log(f"{PERSONA_JA[persona]} のセーブ（{save_root}）が無いので新しく始める")
                else:
                    self.log(f"{PERSONA_JA[persona]} のセーブの続きから: ラン {before['totalRuns']} 回済み・村資金 {before['funds']:,}G・"
                             f"総合Lv {before['villageLevel']}（{fmt_facilities(before)}）")
                self.ensure_ollama("cpu" if record else "gpu")
                try:
                    result = self.run_once(run)
                except GiveUp as e:
                    run["result"] = "gave_up"
                    run["ended"] = self.env.now().isoformat(timespec="seconds")
                    stop_reason = f"あきらめた: {e}"
                    self.log(f"!!! {stop_reason}")
                    break
                run["result"] = result
                run["ended"] = self.env.now().isoformat(timespec="seconds")
                run["villageAfter"] = read_village(Path(run["saveRoot"]))
                run["villageInvested"] = village_invested(self.project, run_id)
                self.save_state()
                va = run["villageAfter"]
                if va:
                    self.log(f"ラン {run_id} の後の村: ラン {va['totalRuns']} 回済み・村資金 {va['funds']:,}G・総合Lv {va['villageLevel']}"
                             f"（{fmt_facilities(va)}）・このランの投資 {run['villageInvested']:,}G")
                    reset_lv = int(self.cfg.get("reset_village_level") or 0)
                    if reset_lv > 0 and va["villageLevel"] >= reset_lv:
                        self.log(f"{PERSONA_JA[persona]} のセーブが総合Lv {va['villageLevel']} に到達（{va['totalRuns']} 周）→ "
                                 f"次の{PERSONA_JA[persona]}のランは初めから")
                last_persona = persona
                laps_txt = ", ".join(
                    f"{l.get('lap')}周目 {'破産' if l.get('bankrupt') else ''}{l.get('money', 0):,}G" for l in run.get("laps_result", []))
                self.log(f"ラン {run_id} の結果: {result}（{laps_txt or '周回なし'}）")
                if not self.verify_user_saves(f"ラン {run_id} の後"):
                    stop_reason = "ユーザーのセーブが変わった"
                    self.state["saveAlert"] = True
                    break
                if result == "gave_up":
                    stop_reason = "3 回続けて失敗"
                    break
                if result == "stopped":
                    stop_reason = "止める時刻 / 止める指示"
                    break
                write_summary(self)
                if len({r["persona"] for r in self.state["runs"]}) > 1:
                    # 混ぜて回すときは途中のまとめにも人格ごとの指標を載せる（読み取りだけ。失敗しても続ける）
                    try:
                        append_metrics(self.out_dir)
                        copy_day_summary(self)
                    except Exception as e:  # noqa: BLE001
                        self.log(f"人格の指標を数えられなかった: {e!r}")
        finally:
            self.finish(stop_reason)

    def reset_if_cleared(self, persona: str, save_root: Path, village):
        """村の総合Lv が reset_village_level 以上のセーブを控えへ移す（クリア→初めから）。移したら None（新しいセーブ）を返す。"""
        lv = int(self.cfg.get("reset_village_level") or 0)
        if not village or lv <= 0 or int(village.get("villageLevel", 0)) < lv:
            return village
        now = self.env.now()
        cleared_root = self.project / "Library" / "PersonaMarathonSaves_cleared"
        dest = cleared_root / f"{persona}_{now:%Y%m%d_%H%M%S}"
        n = 1
        while dest.exists():
            n += 1
            dest = cleared_root / f"{persona}_{now:%Y%m%d_%H%M%S}_{n}"
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(save_root), str(dest))
        clear = {
            "time": now.isoformat(timespec="seconds"), "persona": persona, "level": int(village["villageLevel"]),
            "lapsToClear": int(village.get("totalRuns", 0)), "clearedLaps": int(village.get("clearedRuns", 0)),
            "funds": int(village.get("funds", 0)), "levels": village.get("levels", {}), "archive": str(dest),
            "marathon": str(self.out_dir),
        }
        self.state.setdefault("clears", []).append(clear)
        try:   # マラソンをまたいだ履歴（何周で総合Lv 30 に届いたか）
            with open(cleared_root / "clears.jsonl", "a", encoding="utf-8") as f:
                f.write(json.dumps(clear, ensure_ascii=False) + "\n")
        except OSError:
            pass
        self.event("clear_reset", f"{persona} Lv{village['villageLevel']} {clear['lapsToClear']} laps -> {dest}")
        self.log(f"クリア→初めから: {PERSONA_JA[persona]} のセーブが総合Lv {village['villageLevel']}（{fmt_facilities(village)}）に達していたので "
                 f"{dest} へ移し、新しいセーブで始める（{clear['lapsToClear']} 周で到達・村資金 {clear['funds']:,}G）。記憶・学習器・想起はそのまま")
        self.save_state()
        return None

    def finish(self, reason: str) -> None:
        self.state["stopReason"] = reason
        self.log(f"マラソンを終える: {reason}")
        # 走っているランがあれば止める
        hb, age = self.heartbeat()
        if hb and age is not None and age < 120 and hb.get("editor") == "playing":
            self.stop_path.parent.mkdir(parents=True, exist_ok=True)
            self.stop_path.write_text("stop", encoding="utf-8")
            self.log("エディタに止める指示を出した")
            for _ in range(24):
                self.env.sleep(5)
                hb, _ = self.heartbeat()
                if not hb or hb.get("editor") != "playing":
                    break
        try:
            self.cmd_path.unlink()
        except FileNotFoundError:
            pass
        if self.baseline is not None:
            self.verify_user_saves("終了時")
        final = self.cfg["ollama_final_mode"]
        if final in ("gpu", "cpu") and self.ollama_mode != final:
            self.ensure_ollama(final)
        self.save_state()
        path = write_summary(self)
        self.log(f"朝のまとめ: {path}")
        # 人格の指標 M1〜M14（tools/persona_play/metrics/persona_metrics.py）を summary.md に足す。読み取りだけなので失敗しても続ける
        try:
            append_metrics(self.out_dir)
            copy_day_summary(self)
            self.log("人格の指標を summary.md に足した（metrics.md / metrics.json も）")
        except Exception as e:  # noqa: BLE001
            self.log(f"人格の指標を数えられなかった: {e!r}")


def append_metrics(marathon_dir: Path) -> Path:
    """persona_metrics.append_to_summary を呼ぶ（同じ tools/persona_play の metrics フォルダから読み込む）。"""
    metrics_dir = str(HERE.parent / "metrics")
    if metrics_dir not in sys.path:
        sys.path.insert(0, metrics_dir)
    import persona_metrics  # noqa: E402（metrics フォルダ）
    return persona_metrics.append_to_summary(marathon_dir)


# =====================================================================
# 朝のまとめ
# =====================================================================

def consult_stats(project: Path, run_ids: list[str]) -> dict:
    """相談ログ（Recordings/consult_log_<ラン ID>.jsonl）の数え上げ。"""
    out = {k: 0 for k in ("voiced", "agree", "condition", "disagree", "followed", "ignored", "told", "mount", "thanks", "blame", "snd")}
    for rid in run_ids:
        f = project / "Recordings" / f"consult_log_{rid}.jsonl"
        if not f.exists():
            continue
        for line in f.read_text(encoding="utf-8-sig").splitlines():
            try:
                e = json.loads(line)
            except ValueError:
                continue
            d = e.get("decision")
            if d and e.get("kind") != "silent":
                out["voiced"] += 1
                if d.get("relation") in ("agree", "condition", "disagree"):
                    out[d["relation"]] += 1
                if d.get("followed") is True:
                    out["followed"] += 1
                elif d.get("followed") is False:
                    out["ignored"] += 1
            if e.get("kind") in ("told", "mount", "thanks", "blame"):
                out[e["kind"]] += 1
            if e.get("snd"):
                out["snd"] += 1
    return out


def load_persona_entries(project: Path, run_id: str) -> list[dict]:
    rows = []
    for f in sorted((project / "Recordings").glob(f"persona_log_*_{run_id}.jsonl")):
        try:
            for line in f.read_text(encoding="utf-8-sig").splitlines():
                if line.strip():
                    try:
                        rows.append(json.loads(line))
                    except ValueError:
                        pass
        except OSError:
            pass
    return rows


def read_village(save_root: Path):
    """隔離セーブの metaData.json（村の進行）。無ければ None。"""
    meta = read_json(save_root / "metaData.json")
    if not isinstance(meta, dict):
        return None
    levels = {f.get("facilityId"): int(f.get("level", 0)) for f in meta.get("facilities", []) or [] if isinstance(f, dict)}
    records = meta.get("runRecords", []) or []
    last = records[-1] if records and isinstance(records[-1], dict) else {}
    return {
        "funds": int(meta.get("villageFunds", 0)),
        "totalRuns": int(meta.get("totalRuns", 0)),
        "clearedRuns": int(meta.get("clearedRuns", 0)),
        "levels": {k: levels.get(k, 0) for k in FACILITIES},
        "villageLevel": sum(levels.get(k, 0) for k in FACILITIES),
        "lastProfit": int(last.get("profit", 0)) if last else 0,
        "lastCleared": bool(last.get("cleared", True)) if last else None,
    }


def fmt_facilities(v: dict) -> str:
    return " ".join(f"{FACILITY_JA[k]}{v['levels'].get(k, 0)}" for k in FACILITIES)


def village_invested(project: Path, run_id: str) -> int:
    """判断ログの village_result の行（"invested 500000G: ..."）から、このランで村に投資した額。"""
    total = 0
    for r in load_persona_entries(project, run_id):
        c = r.get("choice") or ""
        if r.get("question") == "village_result" and c.startswith("invested "):
            try:
                total += int(c.split(" ", 2)[1].rstrip("G:"))
            except ValueError:
                pass
    return total


def run_stats(rows: list[dict]) -> dict:
    lat = [r["latencyMs"] for r in rows if isinstance(r.get("latencyMs"), (int, float))]
    # latencyMs は 1 リクエストの全質問に同じ値が入るので、同じ (day, latency) は 1 回と数える
    seen, lat_unique = set(), []
    for r in rows:
        if isinstance(r.get("latencyMs"), (int, float)):
            k = (r.get("day"), r.get("latencyMs"), r.get("question") in ("stream_side", "intervene", "intensity"))
            if k not in seen:
                seen.add(k)
                lat_unique.append(r["latencyMs"])
    streams = [r for r in rows if r.get("question") == "stream_result"]
    won = lost = 0
    for r in streams:
        c = r.get("choice", "")
        side = c.split("side=")[-1] if "side=" in c else "none"
        result = c.split(" ")[0]
        if side == "hero":
            won += result == "win"
            lost += result != "win"
        elif side == "demon":
            won += result == "lose"
            lost += result != "lose"
    def dist(q):
        d = {}
        for r in rows:
            if r.get("question") == q and r.get("decisionId"):
                d[r["choice"]] = d.get(r["choice"], 0) + 1
        return d
    adj = [max((abs(v) for v in r["adjust"].values()), default=0) for r in rows if isinstance(r.get("adjust"), dict)]
    flips = 0
    for r in rows:
        kp, fp = r.get("probabilities"), r.get("finalProbabilities")
        if isinstance(kp, dict) and isinstance(fp, dict) and kp and fp:
            if max(kp, key=kp.get) != max(fp, key=fp.get):
                flips += 1
    return {
        "decisions": sum(1 for r in rows if r.get("decisionId")),
        "requests": len(lat_unique),
        "latencyAvgS": (sum(lat_unique) / len(lat_unique) / 1000) if lat_unique else None,
        "latencyMaxS": (max(lat) / 1000) if lat else None,
        "fallbacks": sum(1 for r in rows if r.get("fallback")),
        "streams": len(streams), "betsWon": won, "betsLost": lost,
        "side": dist("stream_side"), "budget": dist("budget"), "intel": dist("intel"), "support": dist("support"),
        "intensity": dist("intensity"), "intervene": dist("intervene"),
        "adjustAvg": (sum(adj) / len(adj)) if adj else 0.0, "flips": flips,
        "recalledUsed": sum(1 for r in rows if r.get("recalled")),
        "resolved": sum(1 for r in rows if r.get("question") == "resolve"),
    }


def merge_dist(a: dict, b: dict) -> dict:
    out = dict(a)
    for k, v in b.items():
        out[k] = out.get(k, 0) + v
    return out


def fmt_dist(d: dict) -> str:
    total = sum(d.values())
    if not total:
        return "-"
    return " / ".join(f"{k} {v * 100 / total:.0f}%" for k, v in sorted(d.items(), key=lambda kv: -kv[1]))


def mean_level(d: dict) -> str:
    total = sum(d.values())
    if not total:
        return "-"
    try:
        return f"{sum(int(k) * v for k, v in d.items()) / total:.2f}"
    except ValueError:
        return "-"


def write_summary(m: Marathon) -> Path:
    s = m.state
    lines = [f"# 人格マラソンのまとめ（{s['started'][:10]}）", ""]
    lines.append(f"- 開始 {s['started']} / 予定の終わり {s['stopAt']} / 終わった理由: {s.get('stopReason') or '（実行中）'}")
    if s.get("dryRun"):
        lines.append("- **模擬（dry-run）です。Unity も Ollama も使っていません。**")
    if s.get("saveAlert"):
        lines.append("- **!!! ユーザーのセーブが開始時と違いました。マラソンを止めています。控え: "
                     f"`{m.out_dir / 'user_save_backup'}`**")
    if s.get("personaPattern"):
        lines.append(f"- 回す順番: {' → '.join(PERSONA_JA.get(p, p) for p in s['personaPattern'])}（繰り返し）"
                     + (f" / 録画は {'・'.join(PERSONA_JA.get(p, p) for p in s['recordPersonas'])} だけ" if s.get("recordPersonas") else ""))
    if s.get("startNote"):
        lines.append(f"- 印: {s['startNote']}")
    restarts = sum(r.get("restarts", 0) for r in s["runs"])
    lines.append(f"- ラン {len(s['runs'])} 本（録画 {sum(1 for r in s['runs'] if r['record'])} 本）/ Unity の開き直し {restarts} 回 / "
                 f"落とさずに続きから {sum(r.get('softRetries', 0) for r in s['runs'])} 回")
    checks = s.get("saveChecks", [])
    if checks:
        bad = [c for c in checks if not c["ok"]]
        soft = sorted({x for c in checks for x in c.get("soft", [])})
        lines.append(f"- ユーザーのセーブ確認 {len(checks)} 回: {'すべて一致' if not bad else '不一致あり！'}"
                     + (f"（キャッシュだけ変化: {', '.join(soft)}）" if soft else ""))
    lines += ["", "## ラン", "",
              "| # | 人格 | 録画 | 開始 | 終了 | 結果 | 周ごとの所持金 | 配信の賭け 勝/負 | 判断 平均秒 | 予備の判断 | 開き直し |",
              "|---|---|---|---|---|---|---|---|---|---|---|"]
    per_persona = {p: [] for p in ALL_PERSONAS}
    for r in s["runs"]:
        rows = load_persona_entries(m.project, r["id"])
        st = run_stats(rows)
        per_persona[r["persona"]].append((r, st))
        laps = ", ".join(f"{'破産 ' if l.get('bankrupt') else ''}{l.get('money', 0):,}G" for l in r.get("laps_result", [])) or "-"
        lat = f"{st['latencyAvgS']:.1f}" if st["latencyAvgS"] is not None else "-"
        lines.append(f"| {r['id']} | {PERSONA_JA[r['persona']]} | {'○' if r['record'] else ''} | {r['started'][11:16]} | "
                     f"{(r.get('ended') or '')[11:16]} | {r.get('result') or '実行中'} | {laps} | {st['betsWon']}/{st['betsLost']} | "
                     f"{lat} | {st['fallbacks']} | {r.get('restarts', 0)} |")
    recs = [x for r in s["runs"] for x in r.get("recordings", [])]
    if recs:
        lines += ["", "## 録画", ""]
        for x in recs:
            try:
                size = Path(x).stat().st_size / 1e9
                lines.append(f"- `{x}`（{size:.2f} GB）")
            except OSError:
                lines.append(f"- `{x}`")
    lines += ["", "## 選び方と学習の変化（前半 → 後半）", ""]
    for p in ALL_PERSONAS:
        items = per_persona[p]
        if not items:
            continue
        half = max(1, len(items) // 2)
        first, second = items[:half], items[half:] or items[-1:]
        def agg(group):
            out = {"side": {}, "budget": {}, "intel": {}, "support": {}, "intensity": {}, "intervene": {}}
            adj, flips, dec, rec = [], 0, 0, 0
            for _, st in group:
                for k in out:
                    out[k] = merge_dist(out[k], st[k])
                adj.append(st["adjustAvg"])
                flips += st["flips"]
                dec += st["decisions"]
                rec += st["recalledUsed"]
            out["adj"] = sum(adj) / len(adj) if adj else 0.0
            out["flips"], out["dec"], out["rec"] = flips, dec, rec
            return out
        a, b = agg(first), agg(second)
        golds = [l.get("money", 0) for r, _ in items for l in r.get("laps_result", [])]
        lines.append(f"### {PERSONA_JA[p]}（ラン {len(items)} 本・周 {len(golds)} 回・周の平均 {sum(golds) / len(golds):,.0f}G）" if golds
                     else f"### {PERSONA_JA[p]}（ラン {len(items)} 本）")
        lines.append("")
        lines.append("| | 前半 | 後半 |")
        lines.append("|---|---|---|")
        lines.append(f"| 配信で張った側 | {fmt_dist(a['side'])} | {fmt_dist(b['side'])} |")
        lines.append(f"| 予算の段階（0〜5 の平均） | {mean_level(a['budget'])} | {mean_level(b['budget'])} |")
        lines.append(f"| 情報を買った | {fmt_dist(a['intel'])} | {fmt_dist(b['intel'])} |")
        lines.append(f"| 魔王軍支援（段階） | {fmt_dist(a['support'])} | {fmt_dist(b['support'])} |")
        lines.append(f"| スパチャの勢い（0〜4 の平均） | {mean_level(a['intensity'])} | {mean_level(b['intensity'])} |")
        lines.append(f"| 実際に一番払ったスパチャ | {fmt_dist(a['intervene'])} | {fmt_dist(b['intervene'])} |")
        lines.append(f"| 学習器の調整（最大 log 倍率の平均） | {a['adj']:.3f} | {b['adj']:.3f} |")
        lines.append(f"| 学習器で一番が入れ替わった判断 | {a['flips']} / {a['dec']} | {b['flips']} / {b['dec']} |")
        lines.append(f"| 似た場面を載せた判断 | {a['rec']} | {b['rec']} |")
        if p == "consult":
            cs = consult_stats(m.project, [r["id"] for r, _ in items])
            lines.append(f"| 相談（声に出した判断） | {cs['voiced']} 件（賛成 {cs['agree']}・条件 {cs['condition']}・反対 {cs['disagree']}）| |")
            lines.append(f"| ずんだもんが聞いた / 聞かなかった | {cs['followed']} / {cs['ignored']} | |")
            lines.append(f"| 後日談（だから言ったでしょ / マウント / お礼 / 外れ） | {cs['told']} / {cs['mount']} / {cs['thanks']} / {cs['blame']} | |")
            lines.append(f"| ミーム音 | {cs['snd']} 回 | |")
        model = read_json(m.project / "UserSettings" / "PersonaMemory" / f"{p if p != 'consult' else 'zundamon'}_model.json")
        if model:
            pairs = model.get("pairs", {})
            top = sorted(pairs.items(), key=lambda kv: -kv[1].get("count", 0))[:6]
            lines.append("")
            lines.append(f"学習器: {model.get('updates', 0)} 回学習 / 組 {len(pairs)}（λ は人格の LearningRate）。件数の多い組: "
                         + ", ".join(f"{k} ×{v.get('count', 0)}（平均 {v.get('sumR', 0) / max(1e-9, v.get('n', 0)) * 10000:+,.0f}G）" for k, v in top))
        lines.append("")
    # 村の育ち方（人格ごとのセーブを続けて遊ぶので、ランをまたいで施設が増える）
    vrows = [r for r in s["runs"] if r.get("villageAfter") or r.get("villageBefore")]
    if vrows:
        lines += ["## 村の育ち方（人格ごとのセーブ）", ""]
        for p in ALL_PERSONAS:
            rs = [r for r in vrows if r["persona"] == p]
            if not rs:
                continue
            lines += [f"### {PERSONA_JA[p]}", "",
                      "| ラン | 始め | 終わったラン数 | " + " | ".join(FACILITY_JA[k] for k in FACILITIES) + " | 総合Lv | 村資金 | このランの投資 |",
                      "|---|---|---|" + "---|" * len(FACILITIES) + "---|---|---|"]
            for r in rs:
                v = r.get("villageAfter") or r.get("villageBefore")
                start = "新規" if r.get("freshSave") else "続き"
                lines.append(f"| {r['id']} | {start} | {v['totalRuns']} | " + " | ".join(str(v["levels"].get(k, 0)) for k in FACILITIES)
                             + f" | {v['villageLevel']} | {v['funds']:,}G | {r.get('villageInvested', 0):,}G |")
            lines.append("")
    clears = s.get("clears", [])
    if clears:
        lines += ["## クリア→初めから（村の総合Lv 30）", "",
                  "| 時刻 | 人格 | 総合Lv | 到達までの周 | うちクリア | 村資金 | 控え |", "|---|---|---|---|---|---|---|"]
        for c in clears:
            lines.append(f"| {c['time'][5:16].replace('T', ' ')} | {PERSONA_JA.get(c['persona'], c['persona'])} | {c['level']} | "
                         f"{c['lapsToClear']} | {c['clearedLaps']} | {c['funds']:,}G | `{c['archive']}` |")
        lines.append("")
    ev = [e for e in s.get("events", []) if e["kind"] == "restart"]
    if ev:
        lines += ["## 開き直し", ""] + [f"- {e['time'][11:16]} {e['detail']}" for e in ev] + [""]
    fails = [(r["id"], f) for r in s["runs"] for f in r.get("failures", [])]
    if fails:
        lines += ["## 失敗", ""] + [f"- {f['time'][11:16]} {rid}: {f['detail']}" for rid, f in fails] + [""]
    lines += ["", f"進み具合のログ: `{m.log_path}`", ""]
    path = m.out_dir / "summary.md"
    path.write_text("\n".join(lines), encoding="utf-8")
    copy_day_summary(m)
    return path


def copy_day_summary(m) -> None:
    """日ごとの控え summary_<日付>.md（その日の最後のまとめ。長く回すときに日ごとに読めるように）。"""
    src = m.out_dir / "summary.md"
    if src.exists():
        shutil.copyfile(src, m.out_dir / f"summary_{m.env.now():%Y-%m-%d}.md")


# =====================================================================
# 入口
# =====================================================================

def apply_mode(cfg: dict) -> str | None:
    """mode をまとめて設定に展開する。始められないときはその理由を返す。"""
    mode = cfg.get("mode")
    if not mode:
        return None
    if mode == "learn":
        # 学習: 録画しない（録画しない人格のランだけ）・Ollama は GPU（録画しないランは GPU）・記憶は育てる
        cfg["never_record"] = True
        cfg["record_first"] = False
        cfg["record_personas"] = None
        return None
    if mode == "video":
        # 撮影: 記憶・学習器・想起・信頼を書き換えない読み取り専用が要るが、C# 側（PersonaStrategy など）にまだ無い。
        # このまま撮ると学習のセーブと記憶が進むので、始めない
        return "mode \"video\" はまだ使えない（記憶を読むだけにする仕組みが C# 側に無い）。撮影は今までどおりの設定で行う"
    return f"mode は \"learn\" か \"video\"（{mode!r} は分からない）"


def load_config(path: Path | None) -> dict:
    cfg = dict(DEFAULTS)
    p = path or (HERE / "config.json")
    if p.exists():
        cfg.update(json.loads(p.read_text(encoding="utf-8-sig")))
    return cfg


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="人格マラソンの見張り役")
    ap.add_argument("--config", type=Path, help="設定の上書き（JSON）。無ければ marathon/config.json があれば使う")
    ap.add_argument("--project", type=Path, default=DEFAULT_PROJECT)
    ap.add_argument("--dry-run", action="store_true", help="Unity も Ollama も使わない模擬の一晩（sim.py）")
    ap.add_argument("--sim-start", default="00:30", help="--dry-run の開始時刻")
    ap.add_argument("--sim-scenario", default="hang", help="--dry-run の筋書き: ok / hang / crash / abort / save / fail3")
    ap.add_argument("--personas", help="回す人格をカンマ区切りで（zundamon / metan / consult）。例: --personas consult")
    ap.add_argument("--max-runs", type=int, help="これだけランを終えたらやめる（試走用）")
    ap.add_argument("--laps", type=int, help="1 ランの周回数（録画なし・ありの両方を上書き）")
    ap.add_argument("--no-record", action="store_true", help="録画しない（試走用）")
    args = ap.parse_args(argv)
    cfg = load_config(args.config)
    if args.personas:
        cfg["personas"] = [p.strip() for p in args.personas.split(",") if p.strip() in ALL_PERSONAS]
        cfg["first_persona"] = cfg["personas"][0] if cfg["personas"] else cfg["first_persona"]
    if args.max_runs:
        cfg["max_runs"] = args.max_runs
    if args.laps:
        cfg["laps_growth"] = cfg["laps_record"] = args.laps
    if args.no_record:
        cfg["never_record"] = True
    err = apply_mode(cfg)
    if err:
        print(err, file=sys.stderr)
        return 2

    if args.dry_run:
        import tempfile
        sys.path.insert(0, str(HERE))
        from sim import FakeEnv  # noqa: E402（同じフォルダ）
        tmp = Path(tempfile.mkdtemp(prefix="persona_marathon_sim_"))
        project = tmp / "project"
        (project / "Recordings").mkdir(parents=True)
        user = tmp / "user_save"
        (user / "slot_0").mkdir(parents=True)
        (user / "slot_0" / "save.json").write_text("{}", encoding="utf-8")
        (user / "Player.log").write_text("log", encoding="utf-8")
        cfg["user_save_dir"] = str(user)
        start = dt.datetime.combine(dt.date(2026, 10, 28), parse_hhmm(args.sim_start))
        cfg["unity_exe"] = FakeEnv.EXE
        env = FakeEnv(project, start, scenario=args.sim_scenario, user_save=user)
        out = project / "Recordings" / f"marathon_{start:%Y%m%d}"
        Marathon(cfg, project, out, env, dry_run=True).run()
        print(f"\n模擬の出力: {out}")
        print((out / "summary.md").read_text(encoding="utf-8"))
        return 0

    project = args.project.resolve()
    start = dt.datetime.now()
    out = project / "Recordings" / f"marathon_{start:%Y%m%d_%H%M}"
    out.mkdir(parents=True, exist_ok=True)
    pid_file = project / "Library" / "PersonaMarathon" / "runner.pid"
    pid_file.parent.mkdir(parents=True, exist_ok=True)
    pid_file.write_text(str(os.getpid()), encoding="utf-8")
    try:
        Marathon(cfg, project, out, RealEnv(cfg, project, out)).run()
    except Exception as e:  # 想定外でも理由を残す
        with open(out / "progress.log", "a", encoding="utf-8") as f:
            f.write(f"{dt.datetime.now():%Y-%m-%d %H:%M:%S} !!! 見張り役が止まった: {e!r}\n")
        raise
    finally:
        try:
            pid_file.unlink()
        except OSError:
            pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
