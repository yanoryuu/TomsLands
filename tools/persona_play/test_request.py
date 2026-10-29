"""人格プレイの判断リクエストをローカルの Jev 互換エンドポイントへ送って、答えと待ち時間を表示する（標準ライブラリだけ）。

C# の PersonaRequests.BuildDayRequest（Assets/Scripts/Debug/AutoPlay/Jev/PersonaRequests.cs）と同じ JSON を、
決め打ちのサンプルの 1 日（5 日目・配信前日・弱点が分かっている）について組み立てて、ずんだもん / 四国めたん それぞれのモデルへ送る。

使い方:
    py -I tools/persona_play/test_request.py                 # 2 人ぶん送る
    py -I tools/persona_play/test_request.py --dump out_dir  # 送る JSON をファイルにも書く
    py -I tools/persona_play/test_request.py --compare dir   # dir/csharp_day_<persona>.json（C# が作った JSON）と一致するか確かめる
設定は UserSettings/PersonaPlay.json（無ければ既定値: http://localhost:11434/v1/systemone）。
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

DEFAULT_CONFIG = {
    "baseUrl": "http://localhost:11434",
    "path": "/v1/systemone",
    "timeoutSec": 60,
    "models": {"zundamon": "kev:4b-q8_0", "metan": "kev:9b-q4_K_M"},
    "lessonsInState": 5,
}

# ---- PersonaProfile.cs と同じ前置き ----
PERSONAS = {
    "zundamon": {
        "preamble": (
            "You are Zundamon, an honest and overconfident young shopkeeper. You trust your gut more than the manual, "
            "and your reasons are often unrelated to the numbers (a lucky number, a strong-sounding item name, three viewers asking for it). "
            "You love big bets: spending most of your cash, backing one side of the stream fully, trying the hard route without buying information. "
            "You get sad when you lose but bounce back immediately and bet again."
        ),
        "cashFloor": 0,
    },
    "metan": {
        "preamble": (
            "You are Shikoku Metan, a careful shopkeeper who is always short of money. You read the newspaper and buy information "
            "before you act, and you reason from causes to effects. You hate spending everything: you always keep at least 10,000G in cash "
            "and avoid bets whose downside you cannot afford. You only pay for a super chat or the demon army when the expected gain clearly beats the price."
        ),
        "cashFloor": 10000,
    },
}

# ---- PersonaRequests.cs と同じ文言 ----
CONTEXT = (
    "You run an item shop in a fantasy town (the game TomsLands). Each shop day you may buy weapons and armor at today's price "
    "and display them; displayed goods sell to customers and are paid at the next day's price, so you profit when you buy low and prices rise. "
    "Prices drift toward a fair value set by demand; newspaper articles describe causes that may move demand a few days later. "
    "On stream days the hero fights a dungeon live while viewers buy your goods. If the hero loses, you receive the dungeon's defeat reward. "
    "Paying the demon army raises a dungeon's level: stronger monsters, lower hero clear chance, larger defeat reward. "
    "During a stream you may pay for super chats: hero-side (heal, skill, special) or dungeon-side (trap, curse, reinforce, boss buff). "
    "Each dungeon's weak attribute is reshuffled every run, so it cannot be guessed from the dungeon's name or looks; only the information broker reveals it. "
    "Viewer excitement rises on highlights: the boss appearing, the hero falling into a pinch, a comeback, the hero falling; "
    "any super chat you pay for also counts as a highlight. It cools down when nothing happens for a few turns. "
    "Before a stream you choose the hero's weapon, armor and tactics; what he carries rises in price when he wins. "
    "You can also remodel the shop (more kinds on display, more machine slots), develop the blacksmith (stronger goods) and the information broker, "
    "place machines that work every day, and buy bonds or funds at the broker's exchange; all of these last until the end of the run. "
    "A debt payment is due every few days; failing to pay means bankruptcy. Goal: finish the run with as much money as possible."
)
BUDGET_LEVELS = [
    "Spend nothing today", "Spend about 20% of spendable cash", "Spend about 40% of spendable cash",
    "Spend about 60% of spendable cash", "Spend about 80% of spendable cash", "Spend almost all spendable cash",
]
SUPPORT_LEVELS = ["Do not pay the demon army", "Raise the dungeon by 1 level", "Raise the dungeon by 2 levels", "Raise it as far as cash allows"]
SHARE_LEVELS = ["None of the budget", "About 25% of the budget", "About half of the budget", "About 75% of the budget", "All of the budget"]
MAX_ITEMS = 20
MAX_CHOICES = 26  # Ollama の choice は 26 件まで

# ---- サンプルの 1 日（C# 側の検証でも同じ値を使った） ----
SAMPLE = {
    "day": 5, "cash": 84200, "debtAmount": 30000, "debtDaysLeft": 2, "shopLevel": 2, "blacksmithLevel": 2,
    "items": [
        dict(id="flame_sword", type="Weapon", attribute="Fire", price=3200, prev=3000, demand=1.25, prevDemand=1.1, stock=4, maxStock=20, displayed=2, dividend=0, unlocked=True, er=4000.0),
        dict(id="aqua_spear", type="Weapon", attribute="Water", price=2800, prev=2900, demand=0.9, prevDemand=0.95, stock=0, maxStock=20, displayed=0, dividend=0, unlocked=True, er=2520.0),
        dict(id="leaf_shield", type="Armor", attribute="Wood", price=1500, prev=1500, demand=0.6, prevDemand=0.6, stock=10, maxStock=20, displayed=5, dividend=120, unlocked=True, er=900.0),
    ],
    "nextStream": dict(dungeon="VolcanoCave", daysUntil=0, level=1, weaknessKnown=True, weakness="Water", intelCost=-1, defeatReward=10000, supportCost=20000, rewardAfterSupport=30000),
    "weapons": [
        dict(id="aqua_spear", attribute="Water", unitPrice=2800, remain=20, weak=True),
        dict(id="tide_bow", attribute="Water", unitPrice=4100, remain=15, weak=True),
    ],
    "lessons": ["Day 3: backing the hero at ForestOfBeasts worked (net +12000G, super chats skill x1)."],
}


def pct_change(price, prev):
    if prev <= 0:
        return "0%"
    v = (price - prev) * 100.0 / prev
    s = f"{abs(v):.1f}"
    if s == "0.0":
        return "+0.0%" if v > 0 else ("-0.0%" if v < 0 else "0%")
    return ("+" if v > 0 else "-") + s + "%"


def r2(v):
    return round(float(v), 2)


def build_day_request(persona_id, model, s):
    p = PERSONAS[persona_id]
    items = [i for i in s["items"] if i["unlocked"] or i["stock"] > 0]
    items.sort(key=lambda i: (-(1 if i["stock"] > 0 else 0), -i["er"]))
    state = {
        "persona": p["preamble"],
        "lessonsFromPastRuns": list(s["lessons"]),
        "context": CONTEXT,
        "day": s["day"],
        "cash": s["cash"],
        "nextDebt": {"amount": s["debtAmount"], "daysLeft": s["debtDaysLeft"]},
        "shop": {"level": s["shopLevel"], "blacksmithLevel": s["blacksmithLevel"]},
        "items": [{
            "id": i["id"], "type": i["type"], "attribute": i["attribute"], "price": i["price"],
            "change1d": pct_change(i["price"], i["prev"]), "demand": r2(i["demand"]),
            "demandChange1d": r2(i["demand"] - i["prevDemand"]), "stock": i["stock"], "maxStock": i["maxStock"],
            "displayed": i["displayed"], "dividendPerDay": i["dividend"],
        } for i in items[:MAX_ITEMS]],
    }
    if p["cashFloor"] > 0:
        state["cashToKeep"] = p["cashFloor"]
    n = s.get("nextStream")
    if n:
        state["nextStream"] = {
            "dungeon": n["dungeon"], "daysUntil": n["daysUntil"], "dungeonLevel": n["level"],
            "weakAttribute": n["weakness"] if n["weaknessKnown"] else "unknown (buy information to learn it)",
            "defeatRewardIfHeroLoses": n["defeatReward"],
        }
    q = {}
    prep = bool(n) and n["daysUntil"] <= 0
    q["budget"] = {"type": "score", "instructions": "How much of your spendable cash should you spend on stock today? Remember the next debt payment.", "criteria": BUDGET_LEVELS}
    styles = {
        "recommend": "Recommended order: items with the best expected revenue first.",
        "bargain": "Bargain: items whose price is lowest compared with their usual price.",
        "dividend": "Dividend: items that pay the best dividend per day for their price.",
    }
    if prep and n["weaknessKnown"]:
        styles["dungeon_focus"] = f"Dungeon focus: weapons and armor of {n['weakness']}, the weak attribute of {n['dungeon']}, the next stream."
    q["buy_style"] = {"type": "choice", "instructions": "How should the automatic purchase choose items today?", "criteria": styles}
    q["newspaper"] = {"type": "noul", "instructions": "Should you read the newspaper this morning before buying?",
                      "criteria": {"true": "Read it first: the articles hint at demand changes.", "false": "Skip it and buy right away."}}
    if n and not n["weaknessKnown"] and 0 < n["intelCost"] <= s["cash"]:
        q["intel"] = {"type": "noul", "instructions": (
            f"Should you pay {n['intelCost']}G to the information broker to learn the weak attribute of {n['dungeon']}, "
            f"the next stream dungeon ({n['daysUntil']} days away)? Knowing it lets you stock goods that sell well in that stream."),
            "criteria": {"true": "Buy the information.", "false": "Do not buy it; save the money."}}
    if prep and 0 < n["supportCost"] <= s["cash"]:
        q["support"] = {"type": "score", "instructions": (
            f"Should you pay the demon army to strengthen {n['dungeon']} before the stream? One level costs {n['supportCost']}G "
            f"and raises the defeat reward from {n['defeatReward']}G to {n['rewardAfterSupport']}G, but the money is gone even if the hero wins."),
            "criteria": SUPPORT_LEVELS}
    if prep and n["weaknessKnown"]:
        ws = [w for w in s["weapons"] if 0 < w["unitPrice"] <= s["cash"] and w["remain"] > 0]
        ws.sort(key=lambda w: (-(1 if w["weak"] else 0), w["unitPrice"]))
        ws = ws[:MAX_CHOICES]
        if ws:
            q["focus_share"] = {"type": "score", "instructions": f"How much of today's budget should go to goods for the {n['dungeon']} stream (weak attribute {n['weakness']})?", "criteria": SHARE_LEVELS}
            q["weapon"] = {"type": "choice", "instructions": f"Which weapon should you stock for the {n['dungeon']} stream?", "criteria": {
                w["id"]: f"{w['attribute']} weapon, {w['unitPrice']}G each, room for {w['remain']}" + (", matches the weak attribute" if w["weak"] else ", effective attribute")
                for w in ws}}
    return {"model": model, "state": state, "questions": q}


def load_config():
    path = os.path.join(ROOT, "UserSettings", "PersonaPlay.json")
    cfg = dict(DEFAULT_CONFIG)
    if os.path.exists(path):
        with open(path, encoding="utf-8-sig") as f:
            cfg.update(json.load(f))
    return cfg


def send(cfg, body):
    url = cfg["baseUrl"].rstrip("/") + "/" + cfg["path"].lstrip("/")
    data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"}, method="POST")
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=float(cfg.get("timeoutSec", 60))) as res:
            text = res.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return None, time.perf_counter() - t0, f"HTTP {e.code}: {e.read().decode('utf-8', 'replace')[:300]}"
    except Exception as e:  # 接続できない・時間切れ
        return None, time.perf_counter() - t0, str(e)
    return json.loads(text), time.perf_counter() - t0, None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", help="送る JSON を書き出すフォルダ")
    ap.add_argument("--compare", help="C# が作った csharp_day_<persona>.json のあるフォルダ")
    ap.add_argument("--runs", type=int, default=2, help="1 人あたり何回送るか（1 回目はモデルの読み込みで遅い）")
    args = ap.parse_args()
    cfg = load_config()
    ok = True
    for pid in ("zundamon", "metan"):
        model = cfg["models"].get(pid)
        body = build_day_request(pid, model, SAMPLE)
        if args.dump:
            os.makedirs(args.dump, exist_ok=True)
            with open(os.path.join(args.dump, f"python_day_{pid}.json"), "w", encoding="utf-8") as f:
                json.dump(body, f, ensure_ascii=False)
        if args.compare:
            with open(os.path.join(args.compare, f"csharp_day_{pid}.json"), encoding="utf-8-sig") as f:
                same = json.load(f) == body
            print(f"[{pid}] C# の JSON と一致: {same}")
            ok &= same
        print(f"=== {pid} ({model}) 質問 {len(body['questions'])} 件 ===")
        for run in range(args.runs):
            # 2 回目は同じ答えのキャッシュにならないよう所持金だけ変える
            if run > 0:
                body["state"]["cash"] = SAMPLE["cash"] + 1000 * run
            resp, sec, err = send(cfg, body)
            if err:
                print(f"  run {run + 1}: 失敗 {sec:.2f}s {err}")
                ok = False
                continue
            print(f"  run {run + 1}: {sec:.2f}s  usage={resp.get('usage')}")
            for key, a in resp.get("answers", {}).items():
                if a.get("type") == "noul":
                    print(f"    {key:12s} noul={a.get('noul'):.2f}")
                elif a.get("type") == "score":
                    print(f"    {key:12s} score={a.get('score'):.2f} conf={a.get('confidence', 0):.2f}")
                else:
                    probs = sorted((a.get("probabilities") or {}).items(), key=lambda kv: -kv[1])[:4]
                    print(f"    {key:12s} choice={a.get('choice')} conf={a.get('confidence', 0):.2f} " +
                          " ".join(f"{k}:{v:.2f}" for k, v in probs))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
