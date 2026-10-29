"""人格マラソンの見張り役の単体テスト（Unity も Ollama も使わない。sim.FakeEnv で一晩を早回しする）。

    py -3 -m unittest tools/persona_play/marathon/test_marathon.py -v
"""
from __future__ import annotations

import contextlib
import datetime as dt
import io
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import marathon as M  # noqa: E402
from sim import FakeEnv  # noqa: E402

DAY = dt.date(2026, 10, 28)


def at(hhmm: str) -> dt.datetime:
    return dt.datetime.combine(DAY, M.parse_hhmm(hhmm))


def make(scenario="ok", start="00:30", lap_minutes=22.0, **over):
    tmp = Path(tempfile.mkdtemp(prefix="pm_test_"))
    project = tmp / "project"
    (project / "Recordings").mkdir(parents=True)
    user = tmp / "user"
    (user / "slot_0").mkdir(parents=True)
    (user / "slot_0" / "save.json").write_text("{}", encoding="utf-8")
    (user / "Player.log").write_text("a", encoding="utf-8")
    (user / "remoteconfig").mkdir()
    (user / "remoteconfig" / "gameconst.json").write_text("{}", encoding="utf-8")
    cfg = dict(M.DEFAULTS)
    cfg.update(user_save_dir=str(user), unity_exe=FakeEnv.EXE)
    cfg.update(over)
    env = FakeEnv(project, at(start), scenario=scenario, user_save=user, lap_minutes=lap_minutes)
    m = M.Marathon(cfg, project, project / "Recordings" / "marathon", env, dry_run=True)
    return m, env, user


def run_quiet(m):
    with contextlib.redirect_stdout(io.StringIO()):
        m.run()


def ts(text):
    return dt.datetime.fromisoformat(text)


class SchedulerTest(unittest.TestCase):
    def test_alternation_and_record_every_two_hours(self):
        s = M.Scheduler(dict(M.DEFAULTS), at("00:30"))
        self.assertEqual(s.stop_at, at("08:00"))
        self.assertEqual(s.no_new_after, at("07:30"))
        p, rec, _ = s.plan(at("00:30"), None, {}, 200)
        self.assertEqual((p, rec), ("zundamon", False))
        p, rec, _ = s.plan(at("01:15"), "zundamon", {}, 200)
        self.assertEqual((p, rec), ("metan", False))
        p, rec, _ = s.plan(at("02:31"), "metan", {}, 200)
        self.assertEqual((p, rec), ("zundamon", True))
        s.mark_recorded(at("02:31"))
        p, rec, _ = s.plan(at("03:30"), "zundamon", {"zundamon": 1}, 200)
        self.assertEqual((p, rec), ("metan", False))
        # 2 本目の録画は録画の少ない人格（めたん）を選ぶ（交互の順番がずんだもんでも）
        p, rec, _ = s.plan(at("04:40"), "metan", {"zundamon": 1}, 200)
        self.assertEqual((p, rec), ("metan", True))

    def test_consult_only_and_three_way(self):
        s = M.Scheduler(dict(M.DEFAULTS, personas=["consult"], first_persona="consult"), at("00:30"))
        self.assertEqual(s.plan(at("00:30"), None, {}, 200)[0], "consult")
        self.assertEqual(s.plan(at("01:00"), "consult", {}, 200)[0], "consult")
        s = M.Scheduler(dict(M.DEFAULTS, personas=["zundamon", "metan", "consult"]), at("00:30"))
        self.assertEqual(s.plan(at("01:00"), "metan", {}, 200)[0], "consult")
        self.assertEqual(s.plan(at("01:30"), "consult", {}, 200)[0], "zundamon")

    def test_record_must_end_before_stop(self):
        cfg = dict(M.DEFAULTS, record_first=True)
        s = M.Scheduler(cfg, at("07:00"))
        self.assertTrue(s.plan(at("07:00"), None, {}, 200)[1])          # 07:00 + 55 分 = 07:55 → 間に合う
        p, rec, note = s.plan(at("07:06"), None, {}, 200)
        self.assertFalse(rec)
        self.assertIn("08:00", note)

    def test_low_disk_disables_recording(self):
        cfg = dict(M.DEFAULTS, record_first=True)
        s = M.Scheduler(cfg, at("01:00"))
        p, rec, note = s.plan(at("01:00"), None, {}, 12.0)
        self.assertFalse(rec)
        self.assertIn("GB", note)

    def test_start_after_eight_means_tomorrow(self):
        s = M.Scheduler(dict(M.DEFAULTS), at("23:10"))
        self.assertEqual(s.stop_at, at("08:00") + dt.timedelta(days=1))
        self.assertEqual(s.no_new_after, at("07:30") + dt.timedelta(days=1))


class MixPatternTest(unittest.TestCase):
    MIX = dict(persona_pattern=["consult", "consult", "zundamon", "metan"], record_personas=["consult"])

    def test_pattern_rotation_by_index(self):
        s = M.Scheduler(dict(M.DEFAULTS, **self.MIX), at("00:30"))
        got = [s.plan(at("00:30"), None, {}, 200, i)[0] for i in range(9)]
        self.assertEqual(got, ["consult", "consult", "zundamon", "metan"] * 2 + ["consult"])

    def test_record_deferred_to_next_consult_turn(self):
        s = M.Scheduler(dict(M.DEFAULTS, **self.MIX), at("00:30"))
        # 2 時間たったのが ずんだもん（index 2）の番 → 録画しない・間隔も数え直さない
        p, rec, note = s.plan(at("02:31"), "consult", {}, 200, 2)
        self.assertEqual((p, rec), ("zundamon", False))
        self.assertIn("回す", note)
        p, rec, _ = s.plan(at("02:50"), "zundamon", {}, 200, 3)
        self.assertEqual((p, rec), ("metan", False))
        p, rec, _ = s.plan(at("03:05"), "metan", {}, 200, 4)
        self.assertEqual((p, rec), ("consult", True))

    def test_record_first_skips_solo_first_turn(self):
        cfg = dict(M.DEFAULTS, persona_pattern=["zundamon", "consult"], record_personas=["consult"], record_first=True)
        s = M.Scheduler(cfg, at("00:30"))
        self.assertEqual(s.plan(at("00:30"), None, {}, 200, 0)[:2], ("zundamon", False))
        self.assertEqual(s.plan(at("01:00"), "zundamon", {}, 200, 1)[:2], ("consult", True))

    def test_record_personas_without_pattern(self):
        # 交互の 2 人で ずんだもんだけ録画: めたんの番に録画が来ても録らない
        cfg = dict(M.DEFAULTS, record_personas=["zundamon"], record_first=True)
        s = M.Scheduler(cfg, at("00:30"))
        self.assertEqual(s.plan(at("00:30"), None, {}, 200)[:2], ("zundamon", True))
        s.mark_recorded(at("00:30"))
        p, rec, _ = s.plan(at("02:40"), "zundamon", {"zundamon": 1}, 200)
        self.assertEqual((p, rec), ("metan", False))
        self.assertEqual(s.plan(at("03:20"), "metan", {"zundamon": 1}, 200)[:2], ("zundamon", True))

    def test_last_record_at_carries_cadence(self):
        cfg = dict(M.DEFAULTS, **self.MIX, last_record_at="2026-10-28T00:10:00")
        s = M.Scheduler(cfg, at("00:30"))
        self.assertFalse(s.plan(at("02:05"), None, {}, 200, 0)[1])
        self.assertTrue(s.plan(at("02:11"), None, {}, 200, 0)[1])

    def test_mixed_night(self):
        m, env, _ = make("ok", **self.MIX)
        run_quiet(m)
        runs = m.state["runs"]
        self.assertGreaterEqual(len(runs), 8)
        self.assertEqual([r["persona"] for r in runs[:8]], ["consult", "consult", "zundamon", "metan"] * 2)
        self.assertTrue(all(r["persona"] == "consult" for r in runs if r["record"]))
        self.assertGreaterEqual(sum(1 for r in runs if r["record"]), 2)
        # 録画ランだけ Ollama は CPU（録画しない相談・1 人プレイは GPU）
        self.assertEqual(env.ollama_history[0][1], "gpu")
        # セーブは人格ごとに別（相談 / ずんだもん / めたん）
        roots = {}
        for a in env.attempts:
            r = next(x for x in runs if x["id"] == a[0])
            roots.setdefault(r["persona"], set()).add(a[3])
        self.assertEqual(set(roots), {"consult", "zundamon", "metan"})
        self.assertTrue(all(len(v) == 1 for v in roots.values()))
        self.assertEqual(len(set().union(*roots.values())), 3)
        text = (m.out_dir / "summary.md").read_text(encoding="utf-8")
        for ja in ("### 相談", "### ずんだもん", "### 四国めたん", "回す順番"):
            self.assertIn(ja, text)
        self.assertEqual([r["id"][-1] for r in runs[:4]], ["c", "c", "z", "m"])


class SaveCheckTest(unittest.TestCase):
    def test_manifest_ignores_logs_and_soft_cache(self):
        tmp = Path(tempfile.mkdtemp(prefix="pm_md5_"))
        (tmp / "slot_0").mkdir()
        (tmp / "slot_0" / "save.json").write_text("{}", encoding="utf-8")
        (tmp / "Player.log").write_text("1", encoding="utf-8")
        (tmp / "remoteconfig").mkdir()
        (tmp / "remoteconfig" / "items.json").write_text("{}", encoding="utf-8")
        ignore = M.DEFAULTS["user_save_ignore"]
        base = M.save_manifest(tmp, ignore)
        self.assertNotIn("Player.log", base)
        (tmp / "Player.log").write_text("2", encoding="utf-8")
        (tmp / "Player-prev.log").write_text("x", encoding="utf-8")
        (tmp / "remoteconfig" / "items.json").write_text('{"v":2}', encoding="utf-8")
        strict, soft = M.compare_manifests(base, M.save_manifest(tmp, ignore), M.DEFAULTS["user_save_soft"])
        self.assertEqual(strict, [])
        self.assertEqual(soft, ["changed: remoteconfig/items.json"])
        (tmp / "slot_0" / "save.json").write_text('{"x":1}', encoding="utf-8")
        (tmp / "slot_1").mkdir()
        (tmp / "slot_1" / "metaData.json").write_text("{}", encoding="utf-8")
        strict, _ = M.compare_manifests(base, M.save_manifest(tmp, ignore), M.DEFAULTS["user_save_soft"])
        self.assertEqual(strict, ["changed: slot_0/save.json", "added: slot_1/metaData.json"])

    def test_project_arg(self):
        self.assertEqual(M.project_arg(r'"C:\Program Files\Unity\Hub\Editor\6000.0.32f1\Editor\Unity.exe" -projectPath D:\WorkTree\TomsLands\wt-04'),
                         M.norm_path(r"d:\worktree\tomslands\wt-04"))
        self.assertEqual(M.project_arg(r'Unity.exe -projectpath "D:\Work Tree\x" -useHub'), M.norm_path(r"D:\Work Tree\x"))
        self.assertIsNone(M.project_arg("unity mcp"))


class NightTest(unittest.TestCase):
    def assert_night_bounds(self, m):
        for r in m.state["runs"]:
            self.assertLess(ts(r["started"]), at("07:30"), r["id"])
            self.assertLessEqual(ts(r["ended"]), at("08:00"), r["id"])

    def test_ok_night(self):
        m, env, _ = make("ok")
        run_quiet(m)
        runs = m.state["runs"]
        self.assertGreaterEqual(len(runs), 8)
        self.assert_night_bounds(m)
        self.assertTrue(all(r["result"] == "finished" for r in runs[:-1]))
        self.assertIn(runs[-1]["result"], ("finished", "stopped"))   # 07:30 直前に始めたランは 07:54 に止める
        personas = [r["persona"] for r in runs]
        self.assertEqual(personas[:4], ["zundamon", "metan", "zundamon", "metan"])
        recorded = [r for r in runs if r["record"]]
        self.assertGreaterEqual(len(recorded), 2)
        # 録画の間隔はおよそ 2 時間（前の録画ランの開始から 120 分以上）
        starts = [ts(r["started"]) for r in recorded]
        for a, b in zip(starts, starts[1:]):
            self.assertGreaterEqual(b - a, dt.timedelta(minutes=120))
        self.assertGreaterEqual(starts[0] - at("00:30"), dt.timedelta(minutes=120))
        # Ollama: 録画ランの前は CPU、それ以外は GPU。終わったら GPU
        modes = [mode for _, mode in env.ollama_history]
        self.assertEqual(modes[0], "gpu")
        self.assertIn("cpu", modes)
        self.assertEqual(modes[-1], "gpu")
        self.assertEqual(env.kills, 0)
        self.assertTrue((m.out_dir / "summary.md").exists())
        # 隔離セーブは人格ごとに 1 つ（毎ラン同じフォルダの続きから。人格どうしは混ぜない）
        by_persona = {}
        for a, r in zip(env.attempts, [next(x for x in runs if x["id"] == a[0]) for a in env.attempts]):
            by_persona.setdefault(r["persona"], set()).add(a[3])
        self.assertEqual(set(by_persona), {"zundamon", "metan"})
        self.assertTrue(all(len(v) == 1 for v in by_persona.values()))
        self.assertNotEqual(by_persona["zundamon"], by_persona["metan"])
        self.assertTrue(all(r.replace("\\", "/").endswith(p + "/main") for p, v in by_persona.items() for r in v))

    def test_hang_kills_relaunches_and_resumes(self):
        m, env, _ = make("hang")
        run_quiet(m)
        self.assertEqual(env.kills, 1)
        self.assertEqual(env.launches, 1)
        second = m.state["runs"][1]
        tries = [a for a in env.attempts if a[0] == second["id"]]
        self.assertEqual(len(tries), 2)
        self.assertFalse(tries[0][1])
        self.assertTrue(tries[1][1])                 # 続きから
        self.assertEqual(tries[1][2], 1)             # 1 周終わったところから
        self.assertEqual(tries[0][3], tries[1][3])   # 同じ隔離セーブ
        self.assertEqual(second["result"], "finished")
        self.assertEqual([l["lap"] for l in second["laps_result"]], [1, 2])
        self.assertEqual(second["restarts"], 1)
        self.assert_night_bounds(m)
        # 失敗の後に成功したので、連続失敗は数え直し（マラソンは続く）
        self.assertGreater(len(m.state["runs"]), 3)

    def test_crash_relaunches_without_kill(self):
        m, env, _ = make("crash")
        run_quiet(m)
        self.assertEqual(env.kills, 0)
        self.assertEqual(env.launches, 1)
        third = m.state["runs"][2]
        tries = [a for a in env.attempts if a[0] == third["id"]]
        self.assertTrue(tries[-1][1])
        self.assertEqual(third["result"], "finished")

    def test_soft_abort_retries_without_restart(self):
        m, env, _ = make("abort")
        run_quiet(m)
        self.assertEqual(env.kills, 0)
        self.assertEqual(env.launches, 0)
        second = m.state["runs"][1]
        self.assertEqual(second["softRetries"], 1)
        self.assertEqual(second["result"], "finished")

    def test_gives_up_after_three_failures(self):
        m, env, _ = make("fail3")
        run_quiet(m)
        self.assertEqual(len(m.state["runs"]), 1)
        run = m.state["runs"][0]
        self.assertEqual(run["result"], "gave_up")
        self.assertEqual(len(run["failures"]), 3)
        self.assertEqual(env.kills, 1)              # 1 度目は落とさずに続きから、2 度目で開き直し、3 度目であきらめる
        self.assertIn("3", m.state["stopReason"])
        self.assertEqual(env.ollama_history[-1][1], "gpu")

    def test_stuck_thinking_is_a_hang(self):
        m, env, _ = make("thinking")
        run_quiet(m)
        self.assertEqual(env.kills, 1)
        self.assertIn("答え", m.state["runs"][1]["failures"][0]["detail"])

    def test_not_responding_is_a_hang(self):
        m, env, _ = make("notresp")
        run_quiet(m)
        self.assertEqual(env.kills, 1)
        self.assertIn("応答なし", m.state["runs"][1]["failures"][0]["detail"])

    def test_user_save_change_stops_everything(self):
        m, env, _ = make("save")
        run_quiet(m)
        self.assertEqual(len(m.state["runs"]), 1)
        self.assertTrue(m.state.get("saveAlert"))
        bad = [c for c in m.state["saveChecks"] if not c["ok"]]
        self.assertIn("changed: slot_0/save.json", bad[0]["strict"])
        self.assertTrue((m.out_dir / "user_save_backup" / "slot_0" / "save.json").exists())
        self.assertFalse((m.out_dir / "user_save_backup" / "Player.log").exists())

    def test_utage_save_is_restored_not_fatal(self):
        m, env, user = make("ok")
        (user / "Utage" / "SaveTomsScenario").mkdir(parents=True)
        (user / "Utage" / "SaveTomsScenario" / "system").write_text("orig", encoding="utf-8")
        m.out_dir.mkdir(parents=True, exist_ok=True)
        with contextlib.redirect_stdout(io.StringIO()):
            m.backup_user_saves()
            # Play 中に会話エンジンが既読・自動セーブを書いた
            (user / "Utage" / "SaveTomsScenario" / "system").write_text("changed", encoding="utf-8")
            (user / "Utage" / "SaveTomsScenario" / "saveAuto").write_text("new", encoding="utf-8")
            self.assertTrue(m.verify_user_saves("test"))
        self.assertEqual((user / "Utage" / "SaveTomsScenario" / "system").read_text(encoding="utf-8"), "orig")
        self.assertFalse((user / "Utage" / "SaveTomsScenario" / "saveAuto").exists())
        self.assertEqual(len(m.state["restored"]), 1)
        # スロットのセーブが変わったら、これまでどおり止める
        with contextlib.redirect_stdout(io.StringIO()):
            (user / "slot_0" / "save.json").write_text("x", encoding="utf-8")
            self.assertFalse(m.verify_user_saves("test2"))

    def test_deadline_stops_running_run(self):
        m, env, _ = make("ok", start="07:20", lap_minutes=30)
        run_quiet(m)
        self.assertEqual(len(m.state["runs"]), 1)
        run = m.state["runs"][0]
        self.assertEqual(run["result"], "stopped")
        self.assertGreaterEqual(env.stop_seen, 1)
        self.assertLessEqual(ts(run["ended"]), at("08:00"))
        self.assertGreaterEqual(ts(run["ended"]), at("07:54"))

    def test_runner_stop_flag(self):
        m, env, _ = make("ok")
        m.runner_stop.parent.mkdir(parents=True, exist_ok=True)

        orig = env.sleep
        def sleep_and_stop(sec):
            orig(sec)
            if env.now() >= at("01:40") and not m.runner_stop.exists():
                m.runner_stop.write_text("stop", encoding="utf-8")
        env.sleep = sleep_and_stop
        run_quiet(m)
        self.assertEqual(len(m.state["runs"]), 2)
        self.assertEqual(m.state["runs"][-1]["result"], "stopped")


class LongRunTest(unittest.TestCase):
    """学習を何日も回すための設定（日付つきの stop_at・mode・pause・定期の開き直し・日ごとのログ）。"""

    def test_dated_stop_at(self):
        cfg = dict(M.DEFAULTS, stop_at="2026-10-30T23:00", no_new_run_after="2026-10-30T22:30")
        s = M.Scheduler(cfg, at("21:00"))
        self.assertEqual(s.stop_at, dt.datetime(2026, 10, 30, 23, 0))
        self.assertEqual(s.no_new_after, dt.datetime(2026, 10, 30, 22, 30))
        self.assertTrue(s.can_start(dt.datetime(2026, 10, 29, 12, 0)))
        self.assertFalse(s.can_start(dt.datetime(2026, 10, 30, 22, 31)))

    def test_mode_learn_never_records(self):
        cfg = dict(M.DEFAULTS, mode="learn", record_first=True, record_personas=["consult"])
        self.assertIsNone(M.apply_mode(cfg))
        self.assertTrue(cfg["never_record"])
        self.assertFalse(cfg["record_first"])
        m, env, _ = make("ok", start="00:30", **{k: cfg[k] for k in ("never_record", "record_first", "record_personas")},
                         persona_pattern=["consult", "consult", "zundamon", "metan"], max_runs=4)
        run_quiet(m)
        self.assertEqual([r["persona"] for r in m.state["runs"]], ["consult", "consult", "zundamon", "metan"])
        self.assertFalse(any(r["record"] for r in m.state["runs"]))
        self.assertTrue(all(mode == "gpu" for _, mode in env.ollama_history))

    def test_mode_video_refuses_until_read_only_exists(self):
        self.assertIn("video", M.apply_mode(dict(M.DEFAULTS, mode="video")))
        self.assertIsNotNone(M.apply_mode(dict(M.DEFAULTS, mode="other")))

    def test_pause_flag_stops_after_current_run(self):
        m, env, _ = make("ok")
        m.pause_path.parent.mkdir(parents=True, exist_ok=True)
        orig = env.sleep
        def sleep_and_pause(sec):
            orig(sec)
            if env.now() >= at("00:40") and not m.state.get("_paused"):
                m.state["_paused"] = True
                m.pause_path.write_text("pause", encoding="utf-8")
        env.sleep = sleep_and_pause
        run_quiet(m)
        self.assertEqual(len(m.state["runs"]), 1)
        self.assertEqual(m.state["runs"][0]["result"], "finished")   # 走っているランは止めずに終わらせる
        self.assertIn("pause", m.state["stopReason"])
        self.assertFalse(m.pause_path.exists())

    @staticmethod
    def write_meta(save_root: Path, level: int, runs: int = 40, funds: int = 1234567):
        save_root.mkdir(parents=True, exist_ok=True)
        ids = ["hall", "guild", "tavern", "bank", "artisan", "plaza"]
        # 先頭から Lv5 で埋めて合計を level にする（29 なら 5,5,5,5,5,4）
        lv =[5] * (level // 5) + ([level % 5] if level % 5 else [])
        lv += [0] * (6 - len(lv))
        meta = {"villageFunds": funds, "totalRuns": runs, "clearedRuns": runs - 1,
                "facilities": [{"facilityId": i, "level": l} for i, l in zip(ids, lv)], "runRecords": [{"profit": 1, "cleared": True}]}
        (save_root / "metaData.json").write_text(M.json.dumps(meta), encoding="utf-8")

    def test_cleared_save_is_archived_and_restarted(self):
        m, env, _ = make("ok", personas=["consult"], first_persona="consult", max_runs=2, never_record=True)
        save = m.saves / "consult" / M.SAVE_NAME
        self.write_meta(save, 30, runs=57)
        (save / "slot_0.json").write_text("{}", encoding="utf-8")
        zsave = m.saves / "zundamon" / M.SAVE_NAME
        self.write_meta(zsave, 29)
        run_quiet(m)
        runs = m.state["runs"]
        self.assertTrue(runs[0]["freshSave"])               # Lv30 のセーブは初めから
        self.assertIsNone(runs[0]["villageBefore"])
        clears = m.state.get("clears", [])
        self.assertEqual(len(clears), 1)
        self.assertEqual((clears[0]["persona"], clears[0]["level"], clears[0]["lapsToClear"]), ("consult", 30, 57))
        archive = Path(clears[0]["archive"])
        self.assertTrue((archive / "metaData.json").exists())
        self.assertTrue((archive / "slot_0.json").exists())
        self.assertTrue(archive.parent.name == "PersonaMarathonSaves_cleared" and archive.name.startswith("consult_"))
        self.assertTrue((archive.parent / "clears.jsonl").exists())
        self.assertTrue(zsave.exists())                     # ほかの人格のセーブ（Lv29）は触らない
        log = m.log_path.read_text(encoding="utf-8")
        self.assertIn("クリア→初めから", log)
        self.assertIn("クリア→初めから", (m.out_dir / "summary.md").read_text(encoding="utf-8"))

    def test_below_reset_level_continues_and_zero_disables(self):
        m, env, _ = make("ok", personas=["consult"], first_persona="consult", max_runs=1, never_record=True)
        self.write_meta(m.saves / "consult" / M.SAVE_NAME, 29)
        run_quiet(m)
        self.assertFalse(m.state["runs"][0]["freshSave"])
        self.assertEqual(m.state["runs"][0]["villageBefore"]["villageLevel"], 29)
        self.assertFalse(m.state.get("clears"))
        m, env, _ = make("ok", personas=["consult"], first_persona="consult", max_runs=1, never_record=True, reset_village_level=0)
        self.write_meta(m.saves / "consult" / M.SAVE_NAME, 30)
        run_quiet(m)
        self.assertFalse(m.state["runs"][0]["freshSave"])
        self.assertFalse(m.state.get("clears"))

    def test_unfocused_pause_is_not_a_hang(self):
        # フォーカスが無くて Play が止まっている（心拍の pausedNoFocus）間は、固まりと見なして Unity を落とさない
        m, env, _ = make("nofocus", personas=["consult"], first_persona="consult", max_runs=1, never_record=True)
        run_quiet(m)
        self.assertEqual(env.kills, 0)
        self.assertEqual(env.launches, 0)
        self.assertEqual(m.state["runs"][0]["result"], "finished")
        self.assertIn("フォーカスが無くて Play が止まっている", m.log_path.read_text(encoding="utf-8"))

    def test_periodic_restart_between_runs_and_daily_log(self):
        m, env, _ = make("ok", start="20:00", stop_at="2026-10-29T08:00", no_new_run_after="2026-10-29T07:30",
                         restart_every_hours=3, never_record=True)
        run_quiet(m)
        restarts = [e for e in m.state["events"] if e["kind"] == "periodic_restart"]
        self.assertGreaterEqual(len(restarts), 3)
        self.assertGreaterEqual(getattr(env, "quits", 0), len(restarts))
        self.assertEqual(env.kills, 0)
        self.assertTrue((m.out_dir / "progress.log").exists())
        self.assertTrue((m.out_dir / "progress_2026-10-29.log").exists())
        self.assertTrue((m.out_dir / "summary_2026-10-29.md").exists())


if __name__ == "__main__":
    unittest.main()
