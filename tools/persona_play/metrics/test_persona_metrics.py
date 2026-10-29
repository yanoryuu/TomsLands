"""persona_metrics の単体テスト（小さな合成ログ）。

  py -3 -m unittest tools/persona_play/metrics/test_persona_metrics.py -v

一時ファイルはこのフォルダの下に作って消す（C: の一時フォルダは使わない）。
"""
import contextlib
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import persona_metrics as pm  # noqa: E402

TAG = "20990101_0000"


def by_id(result, mid, who=None):
    out = [m for g in result["groups"] for m in g["metrics"] if m["id"] == mid and (who is None or m["who"] == who)]
    assert out, f"{mid} {who} が無い"
    return out[0]


class LogWriter:
    """persona_log / consult_log を合成する。"""

    def __init__(self, root: Path):
        self.rec = root / "Recordings"
        self.rec.mkdir(parents=True)
        (self.rec / f"marathon_{TAG}").mkdir()
        self.n = 0

    def persona(self, run_no: int, suffix: str, rows: list[dict], stamp="000000") -> Path:
        run = f"{TAG}_{run_no:02d}_{suffix}"
        p = self.rec / f"persona_log_20990101_{stamp}_{run}.jsonl"
        with p.open("w", encoding="utf-8") as f:
            for r in rows:
                r.setdefault("run", run)
                f.write(json.dumps(r, ensure_ascii=False) + "\n")
        return p

    def consult(self, run_no: int, rows: list[dict]) -> Path:
        run = f"{TAG}_{run_no:02d}_c"
        p = self.rec / f"consult_log_{run}.jsonl"
        with p.open("w", encoding="utf-8") as f:
            for r in rows:
                r.setdefault("run", run)
                f.write(json.dumps(r, ensure_ascii=False) + "\n")
        return p

    def dec(self, persona, q, choice, probs=None, final=None, line=None, reward_next=None, minute=0, **kw):
        self.n += 1
        did = f"{persona}-{self.n}"
        r = {"time": f"2099-01-01T00:{minute:02d}:00", "day": 1, "persona": persona, "question": q, "choice": str(choice),
             "fallback": False, "decisionId": did}
        if probs:
            r["probabilities"] = probs
        if final:
            r["finalProbabilities"] = final
        if line:
            r["line"] = line
        r.update(kw)
        out = [r]
        if reward_next is not None:
            out.append({"time": r["time"], "persona": persona, "question": "resolve", "ref": did, "reward": reward_next})
        return out


class TempCase(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="_tmp_", dir=HERE))
        self.w = LogWriter(self.tmp)

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def marathon(self):
        return self.w.rec / f"marathon_{TAG}"


class TestParts(unittest.TestCase):
    def test_entropy_and_js(self):
        from collections import Counter
        self.assertAlmostEqual(pm.entropy_bits(Counter({"a": 1, "b": 1})), 1.0)
        self.assertEqual(pm.entropy_bits(Counter({"a": 5})), 0.0)
        self.assertAlmostEqual(pm.js_distance(Counter({"a": 3, "b": 1}), Counter({"a": 3, "b": 1})), 0.0)
        self.assertAlmostEqual(pm.js_distance(Counter({"a": 1}), Counter({"b": 1})), 1.0)

    def test_kl(self):
        p = {"x": 0.5, "y": 0.5}
        self.assertAlmostEqual(pm.kl_nat(p, p), 0.0)
        self.assertGreater(pm.kl_nat({"x": 0.9, "y": 0.1}, p), 0.3)

    def test_step_norm_and_hard(self):
        self.assertAlmostEqual(pm.step_norm("budget", "5"), 1.0)
        self.assertAlmostEqual(pm.step_norm("support", "1"), 1 / 3)
        self.assertIsNone(pm.step_norm("buy_style", "bargain"))
        d = pm.Decision("zundamon", "zundamon", "r", 1, "intel", "false")
        self.assertTrue(pm.is_hard(d))
        d.question, d.choice = "stream_side", "none"
        self.assertFalse(pm.is_hard(d))
        d.question = "budget"
        self.assertIsNone(pm.is_hard(d))

    def test_merge_summary_idempotent(self):
        res = {"groups": [], "redFlags": ["M2 めたん（1.00）"], "counts": {"decisions": 0, "lines": 0, "consultDecisions": 0},
               "files": [], "generatedAt": "x", "notComputable": []}
        text = "# 人格マラソンのまとめ（2099-01-01）\n\n- 開始\n"
        once = pm.merge_summary(text, res)
        twice = pm.merge_summary(once, res)
        self.assertEqual(once, twice)
        self.assertEqual(twice.count(pm.BLOCK_BEGIN), 1)
        self.assertEqual(twice.count("人格の回帰"), 2)          # 先頭の赤の行と表の中の行
        lines = twice.split("\n")
        self.assertTrue(lines[0].startswith("# 人格マラソン"))
        self.assertIn("人格の回帰", lines[2])
        res["redFlags"] = []
        clean = pm.merge_summary(twice, res)
        self.assertNotIn("人格の回帰", clean)
        self.assertIn("- 開始", clean)


class TestSolo(TempCase):
    def build(self):
        w = self.w
        # めたん: 張る側・勢いが固まり、予算は負けても勝っても同じ
        m_rows = []
        for i in range(12):
            m_rows += w.dec("metan", "stream_side", "demon", {"hero": 0.1, "demon": 0.2, "none": 0.7},
                            {"hero": 0.05, "demon": 0.9, "none": 0.05}, line="魔王軍。迷う理由がないわ", minute=i)
            m_rows += w.dec("metan", "budget", 2 if i % 3 else 1, {str(k): 1 / 6 for k in range(6)},
                            reward_next=(-100 if i % 2 else 100), line=f"予算{i}", minute=i)
        w.persona(2, "m", m_rows)
        # ずんだもん: 負けたら 2・勝ったら 5、負けた後は情報を買わない（hard）
        z_rows = []
        for i in range(20):   # 2 ランで勝ち後 20 件（M4_Z_MIN_WIN）
            loss = i % 2 == 0
            z_rows += w.dec("zundamon", "budget", 3, {str(k): 1 / 6 for k in range(6)}, reward_next=(-50 if loss else 50),
                            line=f"z{i}", minute=i, meme=("m1" if i < 4 else None))
            z_rows += w.dec("zundamon", "budget", 2 if loss else 5, {str(k): 1 / 6 for k in range(6)}, minute=i)
            z_rows += w.dec("zundamon", "intel", "false" if loss else "true", minute=i)
            z_rows += w.dec("zundamon", "stream_side", ["hero", "demon", "none"][i % 3], {"hero": 0.6, "demon": 0.2, "none": 0.2},
                            {"hero": 0.6, "demon": 0.2, "none": 0.2}, minute=i)
        w.persona(1, "z", z_rows)
        w.persona(3, "z", z_rows, stamp="000100")

    def test_metrics(self):
        self.build()
        res = pm.run_marathon(self.marathon())
        m2m = by_id(res, "M2", "めたん")
        self.assertFalse(m2m["ok"])
        self.assertAlmostEqual(m2m["value"], 1.0)
        self.assertFalse(by_id(res, "M3", "めたん")["ok"])
        self.assertTrue(by_id(res, "M2", "ずんだもん")["ok"])
        # M4 ずんだもん: 勝ち後 − 負け後 = 5 − 2 = +3 段、hard 負け後 100% / 勝ち後 0%
        m4z = by_id(res, "M4", "ずんだもん")
        self.assertAlmostEqual(m4z["value"], 3.0)
        self.assertTrue(m4z["ok"])
        self.assertGreater(m4z["detail"]["hardAfterLoss"], m4z["detail"]["hardAfterWin"])
        # M4 めたん: 予算は結果に関係なく決めている → NG
        self.assertFalse(by_id(res, "M4", "めたん")["ok"])
        # M5: ずんだもんの stream_side は hero が 1 位、hero を選んだのは 1/3
        m5z = by_id(res, "M5", "ずんだもん")
        self.assertIsNotNone(m5z["value"])
        # M6: ずんだもんは final = kev → 0、めたんは大きく寄せている
        self.assertAlmostEqual(by_id(res, "M6", "ずんだもん")["value"], 0.0, places=6)
        self.assertFalse(by_id(res, "M6", "めたん")["ok"])
        # M1: ずんだもんの方が大胆
        self.assertGreater(by_id(res, "M1")["value"], 0)
        # M7: M2（めたん）が NG なので OK にしない
        m7 = by_id(res, "M7")
        self.assertFalse(m7["ok"])
        # M12: めたんは同じ行を 12 回
        self.assertEqual(by_id(res, "M12", "めたん")["value"], 12)
        self.assertIsNone(by_id(res, "M11", "ずんだもん")["ok"])
        # 赤: M2 と M4（めたん）
        self.assertTrue(any(r.startswith("M2 めたん") for r in res["redFlags"]))
        self.assertTrue(any(r.startswith("M4 めたん") for r in res["redFlags"]))
        self.assertFalse(any(r.startswith("M4 ずんだもん") for r in res["redFlags"]))

    def test_persona_filter_and_outputs(self):
        self.build()
        res = pm.compute(pm.load(pm.files_for_marathon(self.marathon())), "zundamon")
        whos = {m["who"] for g in res["groups"] for m in g["metrics"]}
        self.assertNotIn("めたん", whos)
        self.assertIn("2 人", whos)        # 2 人の指標はログにあれば出す
        path = pm.append_to_summary(self.marathon())
        text = path.read_text(encoding="utf-8")
        self.assertIn("人格の回帰", text)
        self.assertTrue((self.marathon() / "metrics.json").exists())
        data = json.loads((self.marathon() / "metrics.json").read_text(encoding="utf-8"))
        self.assertIn("groups", data)

    def test_broken_last_line_and_cli(self):
        self.build()
        p = self.w.persona(4, "m", self.w.dec("metan", "budget", 1))
        with p.open("a", encoding="utf-8") as f:
            f.write('{"time": "2099-01-01T00:00:00", "persona": "met')    # 書きかけ
        out = self.tmp / "out"
        with contextlib.redirect_stdout(io.StringIO()) as buf:
            rc = pm.main(["--logs", str(self.w.rec / "persona_log_*.jsonl"), "--out", str(out), "--fail-on-red"])
        self.assertIn("人格の回帰", buf.getvalue())
        self.assertEqual(rc, 2)
        self.assertTrue((out / "metrics.md").exists())


class TestConsult(TempCase):
    def test_consult(self):
        w = self.w
        prow, crow = [], []
        for i in range(20):
            rel = "disagree" if i < 10 else "condition"
            followed = i % 2 == 0          # 反対 5/10 = 0.5（範囲外）、条件つき 5/10 = 0.5（範囲内）
            prow += w.dec("zundamon", "budget", 5, {str(k): 1 / 6 for k in range(6)}, {str(k): 1 / 6 for k in range(6)},
                          proposal="5", advice="2", relation=rel, followed=followed, minute=i)
            crow.append({"time": f"2099-01-01T00:{i:02d}:00", "kind": "ignore", "speaker": "z", "line": f"行{i % 4}",
                         "e": "hiso" if i < 12 else "ama", "decision": {
                             "question": "budget", "z": {"key": "5", "probs": {"5": 0.5, "2": 0.5}},
                             "m": {"key": "2", "probs": {"2": 0.9, "5": 0.1}}, "relation": rel, "followed": followed}})
        for k in ["told"] * 9 + ["mount"] * 1:
            crow.append({"time": "2099-01-01T00:30:00", "kind": k, "speaker": "m" if k == "told" else "z", "line": k, "e": "tsun"})
        w.persona(1, "c", prow)
        w.consult(1, crow)
        res = pm.run_marathon(self.marathon())
        self.assertEqual([g["group"] for g in res["groups"]], ["consult"])
        m9 = by_id(res, "M9")
        self.assertAlmostEqual(m9["detail"]["disagree"]["followed"] / m9["detail"]["disagree"]["n"], 0.5)
        self.assertFalse(m9["ok"])
        m10 = by_id(res, "M10")
        self.assertFalse(m10["ok"])                 # 1:9
        self.assertIn("1 : 9", m10["display"])
        m11 = by_id(res, "M11")
        self.assertAlmostEqual(m11["value"], 12 / 21)   # ずんだもんの行 20 + mount 1
        self.assertGreater(by_id(res, "M1")["value"], 0.5)   # 案 5 vs 助言 2 → (5−2)/5
        m14 = by_id(res, "M14", "ずんだもん（案）")
        self.assertIn("receptivity", m14["detail"]["estimates"])
        self.assertTrue(any(r.startswith("M9") for r in res["redFlags"]))
        self.assertTrue(any(r.startswith("M10") for r in res["redFlags"]))


class TestMoodDefinitions(TempCase):
    """rules 4.1: M4 は気分で負け後 / 勝ち後を分け、hard は同じ質問どうし。M6 は priorProbabilities。M8 は同じ気分帯・偶然を引く。"""

    def test_mood_split(self):
        w = self.w
        sad = {"conf": 0.2, "sad": 0.4, "smug": 0, "irk": 0, "label": "sad", "lossStreak": 6, "last": "loss"}
        win = {"conf": 0.9, "sad": 0.0, "smug": 0.5, "irk": 0, "label": "smug", "lossStreak": 0, "last": "win"}
        rows = []
        for i in range(40):   # 勝ち後 20 件（M4_Z_MIN_WIN）あれば判定する
            loss = i % 2 == 0
            mood = sad if loss else win
            # 予算: 負けた後 2・勝った後 5。取り返し: 負けた後は情報を買わない（hard 1）、勝った後は買う（hard 0）
            rows += w.dec("zundamon", "budget", 2 if loss else 5, mood=mood, b=0.4 if loss else 1.0, d=0.4, hard=0.0,
                          probabilities={"2": 0.5, "5": 0.5}, finalProbabilities={"2": 0.5, "5": 0.5},
                          priorProbabilities={"2": 0.5, "5": 0.5}, minute=i)
            rows += w.dec("zundamon", "intel", "false" if loss else "true", mood=mood, hard=1.0 if loss else 0.0, minute=i)
        w.persona(1, "z", rows)
        res = pm.run_marathon(self.marathon())
        m4 = by_id(res, "M4", "ずんだもん")
        self.assertEqual(m4["detail"]["split"], "mood")
        self.assertAlmostEqual(m4["value"], 3.0)
        self.assertAlmostEqual(m4["detail"]["hardAfterLoss"], 1.0)
        self.assertTrue(m4["ok"])
        self.assertEqual(m4["detail"]["nWin"], 20)
        m6 = by_id(res, "M6", "ずんだもん")
        self.assertAlmostEqual(m6["value"], 0.0, places=6)
        self.assertIn("priorProbabilities", m6["note"])
        # 前半も後半も同じ配分（気分帯ごとに同じ手）→ ずれの超過は 0
        m8 = by_id(res, "M8", "ずんだもん")
        self.assertLessEqual(m8["value"], 0.1)
        self.assertTrue(m8["ok"])

    def test_few_wins_not_judged(self):
        """負けが基本の設計（2026-10-28）: ずんだもんの勝ち後が 20 件未満なら M4 は NG にせず「件数不足（判定しない）」、赤にもしない。"""
        w = self.w
        sad = {"conf": 0.2, "sad": 0.4, "smug": 0, "irk": 0, "label": "sad", "lossStreak": 6, "last": "loss"}
        win = {"conf": 0.9, "sad": 0.0, "smug": 0.5, "irk": 0, "label": "smug", "lossStreak": 0, "last": "win"}
        rows = []
        for i in range(30):
            loss = i % 6 != 0   # 勝ち後は 5 件だけ。勝ち後の方が慎重（揃っていれば NG になる形）
            rows += w.dec("zundamon", "budget", 4 if loss else 2, mood=sad if loss else win, hard=0.0, minute=i)
        w.persona(1, "z", rows)
        res = pm.run_marathon(self.marathon())
        m4 = by_id(res, "M4", "ずんだもん")
        self.assertIsNone(m4["ok"])
        self.assertEqual(m4["detail"]["nWin"], 5)
        self.assertIn(pm.M4_FEW_WINS, m4["note"])
        self.assertEqual(pm._ok_mark(m4["ok"], m4), pm.M4_FEW_WINS)
        self.assertFalse(any(r.startswith("M4 ずんだもん") for r in res["redFlags"]))
        self.assertIn(pm.M4_FEW_WINS, pm.format_table(res))


if __name__ == "__main__":
    unittest.main()
