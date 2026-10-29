"""人格マラソンの模擬（Unity も Ollama も使わない）。marathon.py --dry-run と test_marathon.py が使う。

FakeEnv は時計を進めるたびに「エディタ」（PersonaMarathon.cs と同じファイルのやり取り）を真似る:
command.json を読んで status.json / heartbeat.json を書き、周回を進め、筋書き（scenario）どおりに固まる・落ちる・途中で止まる。
"""
from __future__ import annotations

import datetime as dt
import json
import os
from pathlib import Path


def _write(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
    os.replace(tmp, path)


class FakeEnv:
    EXE = "C:/fake/Unity.exe"

    def __init__(self, project: Path, start: dt.datetime, scenario: str = "ok", user_save: Path | None = None,
                 lap_minutes: float = 22.0, record_lap_minutes: float = 26.0):
        self.project = project
        self.ipc = project / "Library" / "PersonaMarathon"
        self.t = start
        self.scenario = scenario
        self.user_save = user_save
        self.lap_minutes = lap_minutes
        self.record_lap_minutes = record_lap_minutes
        self.alive = True
        self.pid = 1000
        self.boot_until = None
        self.hung = False
        self.responding = True
        self.editor = "idle"
        self.run = None
        self.runs_started = 0          # 新規に始めたランの数（続きからは数えない）
        self.attempts = []             # (id, resume, lapsDone, saveRoot, record)
        self.kills = 0
        self.launches = 0
        self.ollama_mode = None
        self.ollama_history = []
        self.next_beat = start
        self.stop_seen = 0

    # ---------------- 時計 ----------------

    def now(self) -> dt.datetime:
        return self.t

    def sleep(self, seconds: float) -> None:
        remaining = seconds
        while remaining > 0:
            step = min(5.0, remaining)
            self.t += dt.timedelta(seconds=step)
            remaining -= step
            self._tick()

    # ---------------- プロセス ----------------

    def unity_processes(self):
        return [{"pid": self.pid, "exe": self.EXE, "cmd": f'"{self.EXE}" -projectPath {self.project}'}] if self.alive else []

    def unity_responding(self, pid):
        return self.responding if self.alive else None

    def unity_kill(self, pid):
        self.kills += 1
        self.alive = False
        self.hung = False
        self.responding = True
        self.run = None
        self.editor = "idle"

    def unity_quit(self, pid):
        self.quits = getattr(self, "quits", 0) + 1
        self.alive = False
        self.run = None
        self.editor = "idle"

    def unity_launch(self, exe):
        self.launches += 1
        self.alive = True
        self.pid += 1
        self.boot_until = self.t + dt.timedelta(seconds=90)
        self.editor = "idle"
        self.run = None

    def ollama_alive(self):
        return self.ollama_mode is not None

    def ollama_restart(self, mode):
        self.ollama_mode = mode
        self.ollama_history.append((self.t, mode))
        return True

    def free_gb(self, drive):
        return 250.0

    # ---------------- エディタの真似 ----------------

    def _status(self, **kw):
        r = self.run
        st = {
            "id": r["cmd"]["id"], "nonce": r["cmd"]["nonce"], "state": r["state"], "reason": r.get("reason", ""),
            "persona": r["cmd"]["persona"], "record": r["cmd"]["record"], "lapsTarget": r["cmd"]["laps"],
            "lapsDone": r["lapsDone"], "laps": r["laps"], "startedUnix": int(r["started"].timestamp()),
        }
        if r["cmd"]["record"] and r["state"] == "finished":
            st["recording"] = str(self.project / "Recordings" / f"Marathon_{r['cmd']['id']}.mp4")
        st.update(kw)
        _write(self.ipc / "status.json", st)

    def _beat(self):
        r = self.run
        _write(self.ipc / "heartbeat.json", {
            "unix": self.t.timestamp(), "pid": self.pid, "editor": self.editor, "compileFailed": False,
            "runId": r["cmd"]["id"] if r else None, "state": r["state"] if r else None,
            "lapsDone": r["lapsDone"] if r else 0, "turn": r["turn"] if r else -1, "phase": "TomsShop" if r else "",
            "autoplay": bool(r and r["state"] == "playing"), "autoplayStatus": "", "thinking": bool(r and r.get("thinking")),
            "logEvents": r["events"] if r else 0, "recording": bool(r and r["cmd"]["record"]),
            "pausedNoFocus": bool(r and r.get("nofocus")),
        })

    def _log_line(self, row: dict):
        r = self.run
        path = self.project / "Recordings" / f"persona_log_{r['started']:%Y%m%d_%H%M%S}_{r['cmd']['id']}.jsonl"
        path.parent.mkdir(parents=True, exist_ok=True)
        row = dict(row, persona=r["cmd"]["persona"], run=r["cmd"]["id"], rules=2)
        with open(path, "a", encoding="utf-8") as f:
            f.write(json.dumps(row) + "\n")

    def _tick(self):
        if not self.alive or self.hung:
            return
        if self.boot_until is not None:
            if self.t < self.boot_until:
                return
            self.boot_until = None
        if self.t >= self.next_beat:
            self.next_beat = self.t + dt.timedelta(seconds=5)
            self._beat()

        cmd_path = self.ipc / "command.json"
        stop_path = self.ipc / "stop.flag"
        if stop_path.exists():
            stop_path.unlink()
            self.stop_seen += 1
            if self.run and self.run["state"] in ("starting", "playing"):
                self.run["state"] = "stopped"
                self.run["reason"] = "マラソンの停止"
                self._status()
                self.editor = "idle"
                self.run = None
                if cmd_path.exists():
                    cmd_path.unlink()
            return

        if self.run is None:
            if cmd_path.exists() and self.editor == "idle":
                cmd = json.loads(cmd_path.read_text(encoding="utf-8"))
                cmd_path.unlink()
                if not cmd["resume"]:
                    self.runs_started += 1
                self.attempts.append((cmd["id"], cmd["resume"], cmd["lapsDone"], cmd["saveRoot"], cmd["record"]))
                self.run = {"cmd": cmd, "state": "starting", "started": self.t, "lapsDone": cmd["lapsDone"], "laps": [],
                            "turn": 1, "events": 0, "lapStart": self.t, "attempt": len(self.attempts)}
                self._status()
            return

        r = self.run
        if r["state"] == "starting" and self.t - r["started"] >= dt.timedelta(seconds=20):
            r["state"] = "playing"
            self.editor = "playing"
            self._status()
        if r["state"] != "playing":
            return

        # 筋書き
        nth = self.runs_started
        if self._scenario_event(r, nth):
            return

        # 進行: 30 秒ごとに 1 日、1 日ごとに判断のログ
        r["events"] += 1
        if int((self.t - r["started"]).total_seconds()) % 30 == 0:
            r["turn"] += 1
            self._log_line({"day": r["turn"], "question": "budget", "choice": str(r["turn"] % 6), "decisionId": f"d{r['turn']}",
                            "probabilities": {"0": 0.2, "1": 0.3, "2": 0.5}, "finalProbabilities": {"0": 0.2, "1": 0.5, "2": 0.3},
                            "adjust": {"0": 0.0, "1": 0.4, "2": -0.3}, "latencyMs": 2400 if not r["cmd"]["record"] else 26000,
                            "recalled": ["similar past: day 3, cash ~20k G → chose to spend about 40% of spendable cash → result +1.2k G net worth by the next day"]})
            if r["turn"] % 7 == 0:
                self._log_line({"day": r["turn"], "question": "stream_result", "choice": f"{'win' if r['turn'] % 2 else 'lose'} +1000G side=hero"})
        lap_len = self.record_lap_minutes if r["cmd"]["record"] else self.lap_minutes
        if self.t - r["lapStart"] >= dt.timedelta(minutes=lap_len):
            r["lapsDone"] += 1
            r["lapStart"] = self.t
            r["laps"].append({"lap": r["lapsDone"], "bankrupt": False, "money": 100000 + 1000 * r["lapsDone"], "delta": 5000})
            if r["lapsDone"] >= r["cmd"]["laps"]:
                r["state"] = "finished"
                self._status()
                self.editor = "idle"
                self.run = None
                if self.scenario == "save" and self.user_save is not None:
                    (self.user_save / "slot_0" / "save.json").write_text('{"changed": true}', encoding="utf-8")
                return
            self._status()

    def _scenario_event(self, r, nth) -> bool:
        """筋書きのできごと。True ならこの刻みはここまで。"""
        since_lap = self.t - r["lapStart"]
        s = self.scenario
        if s == "hang" and nth == 2 and r["lapsDone"] == 1 and since_lap >= dt.timedelta(minutes=5) and not getattr(self, "_hung_once", False):
            self._hung_once = True
            self.hung = True              # 心拍が止まる（モーダルのダイアログ・無限ループ）
            return True
        if s == "crash" and nth == 3 and since_lap >= dt.timedelta(minutes=8) and r["attempt"] == len(self.attempts) and not getattr(self, "_crashed", False):
            self._crashed = True
            self.alive = False            # プロセスが消える
            self.run = None
            return True
        if s == "abort" and nth == 2 and since_lap >= dt.timedelta(minutes=3) and not getattr(self, "_aborted", False):
            self._aborted = True
            r["state"] = "aborted"
            r["reason"] = "詰まりから復帰できないため停止"
            self._status()
            self.editor = "idle"
            self.run = None
            return True
        if s == "fail3" and since_lap >= dt.timedelta(minutes=2):
            r["state"] = "aborted"
            r["reason"] = "詰まりから復帰できないため停止"
            self._status()
            self.editor = "idle"
            self.run = None
            return True
        if s == "thinking" and nth == 2 and since_lap >= dt.timedelta(minutes=4) and not getattr(self, "_thought", False):
            self._thought = True
            r["thinking"] = True          # 心拍は来るが、考え中のまま進まない
            r["frozen"] = True
        if s == "nofocus" and nth == 1 and since_lap >= dt.timedelta(minutes=3) and not getattr(self, "_nf_start", None):
            self._nf_start = self.t       # Unity にフォーカスが無くて Play が止まる（心拍は来る・進まない）。30 分後に戻る
            r["nofocus"] = True
        if r.get("nofocus"):
            if self.t - self._nf_start < dt.timedelta(minutes=30):
                return True
            r["nofocus"] = False
        if r.get("frozen"):
            return True
        if s == "notresp" and nth == 2 and since_lap >= dt.timedelta(minutes=4) and not getattr(self, "_nr", False):
            self._nr = True
            self.responding = False       # 心拍は（たまたま）来るが Windows が応答なしと言う
            r["frozen"] = True
            return True
        return False
