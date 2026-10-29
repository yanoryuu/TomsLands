# -*- coding: utf-8 -*-
"""bgm.py の確認（py -3 -I test_bgm.py）。VOICEVOX は使わない（文字数から見積もった timeline）。"""
import json
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bgm  # noqa: E402
import jikkyo  # noqa: E402


def tl_of(name):
    sc = jikkyo.load_script(name)
    return sc, jikkyo.build_timeline(name, sc, jikkyo.load_footage(name), synth=False)


def test_old_scripts_untouched():
    for fn in sorted(os.listdir(os.path.join(HERE, "scripts"))):
        if fn.endswith(".json") and not fn.endswith(".bgm.json"):
            sc = jikkyo.load_script(fn[:-5])
            assert not bgm.is_plan(sc), fn
            assert not bgm.uses_game_vol(sc), fn


def test_resolve_at():
    sc, tl = tl_of("E_soudan")
    c = tl["chapters"][2]
    assert bgm.resolve_at(12.5, tl) == 12.5
    assert abs(bgm.resolve_at(f"ch:{c['id']}", tl) - c["start"]) < 1e-6
    assert abs(bgm.resolve_at(f"ch:{c['id']}+2", tl) - (c["start"] + 2)) < 1e-6
    l3 = [e for e in tl["events"] if e["kind"] == "line" and e["chapter"] == c["id"]][3]
    assert abs(bgm.resolve_at(f"line:{c['id']}/3", tl) - l3["start"]) < 1e-6
    assert abs(bgm.resolve_at("end-10", tl) - (tl["total"] - 10)) < 1e-6


def test_propose_rules():
    idx = bgm.load_index()
    for name in ("E_soudan", "C_play", "D_maou"):
        sc, tl = tl_of(name)
        plan = bgm.propose_plan(sc, tl, idx)
        tracks = [p for p in plan if "track" in p]
        ts = [bgm.resolve_at(p["at"], tl) for p in tracks] + [tl["total"]]
        for i, p in enumerate(tracks):
            assert ts[i + 1] - ts[i] <= bgm.MAX_RUN + 30, (name, p)
            if i:
                assert tracks[i - 1]["track"] != p["track"], (name, p)
        for p in plan:
            assert (p.get("track") or p.get("sting")) in idx


def test_render_short():
    sc, tl = tl_of("E_soudan")
    sc = dict(sc, bgm=[{"at": 0, "track": "shop_rabbitbop"}, {"at": 20, "track": "stream_trap", "fade": 2},
                       {"at": 30, "sting": "sting_victory_bell"}, {"at": 40, "track": None}])
    tl = dict(tl, total=50.0, events=[e for e in tl["events"] if e["start"] < 48],
              chapters=[dict(c, end=min(c["end"], 50.0)) for c in tl["chapters"] if c["start"] < 50])
    with tempfile.TemporaryDirectory() as d:
        secs, stings = bgm.render_bgm(sc, tl, os.path.join(HERE, "scripts"), os.path.join(d, "b.wav"))
    assert [s["track"] for s in secs] == ["shop_rabbitbop", "stream_trap"]
    assert stings and stings[0][1] == "sting_victory_bell"


if __name__ == "__main__":
    for k, f in list(globals().items()):
        if k.startswith("test_"):
            f()
            print("ok", k)
