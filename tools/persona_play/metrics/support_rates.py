"""魔王軍支援の負け率の表（ダンジョン×レベルごとの勇者の勝ち負け）を persona_log から数える。標準ライブラリだけ・読むのはログだけ。

使い方（プロジェクト直下から）:
  py -I -X utf8 tools/persona_play/metrics/support_rates.py                # Recordings/ を数えて Library/PersonaMarathon/support_rates.json へ
  py -I -X utf8 tools/persona_play/metrics/support_rates.py --print       # 表を出すだけ

入力: Recordings/persona_log_*.jsonl の question=stream_result の行（2026-10-30 から dungeon / dungeonLv が付く）。
      choice の頭が "win"（勇者の勝ち）/ "lose"（勇者の負け）の行だけ数える（aborted・unknown は数えない）。
出力: {"updated": ..., "files": n, "streams": n, "rates": {ダンジョン: {レベル: {"n": 件数, "losses": 勇者の負け}}}}
C# の PersonaSupportRates（Assets/Scripts/Debug/AutoPlay/Jev/PersonaSupportEv.cs）が読み、
UserSettings/PersonaPlay.json の supportByEv が true のとき魔王軍支援の段数を期待値で決める。marathon.py がランの後に書き直す。
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import sys
from pathlib import Path

DEFAULT_OUT = Path("Library") / "PersonaMarathon" / "support_rates.json"


def stream_rows(recordings: Path):
    """persona_log の stream_result の行（dungeon と dungeonLv が付いたもの）を返す。(ダンジョン, Lv, 勇者の負けか)。"""
    for f in sorted(Path(recordings).glob("persona_log_*.jsonl")):
        try:
            text = f.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        for line in text.splitlines():
            if '"stream_result"' not in line or '"dungeonLv"' not in line:
                continue
            try:
                e = json.loads(line)
            except ValueError:
                continue
            if e.get("question") != "stream_result":
                continue
            d, lv = e.get("dungeon"), e.get("dungeonLv")
            if not isinstance(d, str) or not d or not isinstance(lv, int) or lv < 0:
                continue
            head = str(e.get("choice") or "").split(" ", 1)[0]
            if head not in ("win", "lose"):
                continue
            yield d, lv, head == "lose"


def collect(recordings: Path) -> dict:
    rates: dict[str, dict[str, dict[str, int]]] = {}
    files = len(list(Path(recordings).glob("persona_log_*.jsonl"))) if Path(recordings).exists() else 0
    n = 0
    for d, lv, lost in stream_rows(recordings):
        cell = rates.setdefault(d, {}).setdefault(str(lv), {"n": 0, "losses": 0})
        cell["n"] += 1
        cell["losses"] += 1 if lost else 0
        n += 1
    for d in rates:
        rates[d] = dict(sorted(rates[d].items(), key=lambda kv: int(kv[0])))
    return {"updated": dt.datetime.now().isoformat(timespec="seconds"), "files": files, "streams": n,
            "rates": dict(sorted(rates.items()))}


def write(project: Path, out: Path | None = None) -> tuple[Path, dict]:
    """プロジェクトの Recordings を数えて表を書く（一時ファイル → 置き換え）。書いた先と中身を返す。"""
    project = Path(project)
    data = collect(project / "Recordings")
    path = Path(out) if out else project / DEFAULT_OUT
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
    os.replace(tmp, path)
    return path, data


def table(data: dict) -> str:
    lines = []
    for d, by in data.get("rates", {}).items():
        cells = ", ".join(f"Lv{lv} {c['losses']}/{c['n']}" for lv, c in by.items())
        lines.append(f"{d}: {cells}")
    return "\n".join(lines) or "（まだ dungeonLv の付いた配信が無い）"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="魔王軍支援の負け率の表を persona_log から数える")
    ap.add_argument("--project", default=str(Path(__file__).resolve().parents[3]))
    ap.add_argument("--out", default=None)
    ap.add_argument("--print", action="store_true", help="書かずに表を出すだけ")
    a = ap.parse_args(argv)
    if a.print:
        data = collect(Path(a.project) / "Recordings")
    else:
        path, data = write(Path(a.project), Path(a.out) if a.out else None)
        print("書いた:", path)
    print(f"配信 {data['streams']} 件（ファイル {data['files']}）")
    print(table(data))
    return 0


if __name__ == "__main__":
    sys.exit(main())
