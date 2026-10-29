# -*- coding: utf-8 -*-
"""
坂本アヒルさんの立ち絵 PSD（ずんだもん立ち絵素材V3.2_基本版 / 四国めたん立ち絵素材2.1）から、
感情 × 口の開閉 ごとの PNG を assets/tachie/ に書き出す。

  py -3 -X utf8 tachie.py            # 全部書き出す
  py -3 -X utf8 tachie.py --list     # レイヤー名を一覧

PSD は assets/tachie_src/zundamon.psd, metan.psd に置く（配布元: ニコニコ静画 im10788496 / im10791276。
readme: 公式の規約の範囲なら動画・アイコン等に自由に使える、クレジット表記は任意）。
"""
import os
import sys

from psd_tools import PSDImage

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "assets", "tachie_src")
DST = os.path.join(ROOT, "assets", "tachie")

PSD_FILES = {"z": "zundamon.psd", "m": "metan.psd"}


def norm(name):
    return name.lstrip("*!").strip()


# 感情ごとの指定: {グループ名: 選ぶレイヤー名}  "+": 追加で表示するレイヤー, "-": 消すレイヤー
# 口は mouth_closed / mouth_open の2つを別に指定し、それ以外の指定は共通。
ZUNDA_BASE = {"右腕": "腰", "左腕": "腰", "枝豆": "枝豆通常", "顔色": "ほっぺ基本", "眉": "基本眉", "目": "基本目"}
ZUNDA = {
    "normal":   dict(ZUNDA_BASE, mouth=("ん", "ほあ")),
    "ama":      dict(ZUNDA_BASE, 目="にっこり", 顔色="ほっぺ赤め", mouth=("ほほえみ", "あは")),
    "tsun":     dict(ZUNDA_BASE, 眉="怒り眉", 目="ジト目", 枝豆="枝豆立ち", 右腕="(非表示)", 左腕="腕組み(右腕は非表示に)", mouth=("むくー", "うわー")),
    "sexy":     dict(ZUNDA_BASE, 眉="基本眉2", 目="細め目", 顔色="ほっぺ赤め", 左腕="あごに指", mouth=("にやり", "ほあ")),
    "sasayaki": dict(ZUNDA_BASE, 目="細め目", 左腕="口元", mouth=("ん", "ほあ")),
    "hiso":     dict(ZUNDA_BASE, 目="ジト目←", 右腕="口元", mouth=("ん", "ほあ")),
    "hero":     dict(ZUNDA_BASE, 眉="困り眉", 目="UU", 顔色="青ざめ", 枝豆="枝豆萎え", mouth=("んー", "うへえ"), **{"+": ["汗"]}),
    "namida":   dict(ZUNDA_BASE, 眉="困り眉", 目="><", 枝豆="枝豆萎え", mouth=("Δ", "うわー"), **{"+": ["涙", "汗"]}),
    "surprise": dict(ZUNDA_BASE, 眉="上がり眉", 目="〇〇", 枝豆="枝豆立ち", 右腕="手を挙げる", mouth=("お", "ほあー")),
    "thumb":    dict(ZUNDA_BASE, 眉="上がり眉", 目="にっこり", 顔色="ほっぺ赤め", 右腕="指差し横", 枝豆="枝豆立ち", mouth=("あは", "あは")),
}

METAN_BASE = {"右腕": "普通", "左腕": "普通", "顔色": "普通2", "眉": "ごきげん", "目": "目セット", "目セット": "普通白目", "黒目": "カメラ目線"}
METAN = {
    "normal":   dict(METAN_BASE, mouth=("ほほえみ", "お")),
    "ama":      dict(METAN_BASE, 目="目閉じ2", 顔色="赤面", mouth=("ほほえみ", "▽")),
    "tsun":     dict(METAN_BASE, 眉="おこ", 黒目="目そらし", 右腕="指差す", mouth=("いー", "うえー")),
    "sexy":     dict(METAN_BASE, 黒目="目そらし2", 顔色="赤面", 左腕="口元に指", mouth=("にやり", "ぺろり")),
    "sasayaki": dict(METAN_BASE, 左腕="ひそひそ", 黒目="普通目", mouth=("む", "ゆ")),
    "hiso":     dict(METAN_BASE, 左腕="ひそひそ", 黒目="目そらし", mouth=("む", "ゆ")),
    "hero":     dict(METAN_BASE, 眉="こまり", 黒目="普通目2", 顔色="青ざめ", mouth=("もむー", "うえー"), **{"+": ["汗"]}),
    "namida":   dict(METAN_BASE, 眉="こまり", 目="目閉じ", mouth=("△", "うえー"), **{"+": ["涙"]}),
    "surprise": dict(METAN_BASE, 眉="ややおこ", 目="見上げ", 右腕="手をかざす", mouth=("お", "わあー")),
    "thumb":    dict(METAN_BASE, 眉="太眉ごきげん", 黒目="カメラ目線2", 右腕="指差す", 左腕="マイク", mouth=("にやり", "にやり")),
}

EMOTIONS = {"z": ZUNDA, "m": METAN}


def walk(layers, parent=None):
    for l in layers:
        yield l, parent
        if l.is_group():
            yield from walk(l, l)


def list_layers(psd):
    def rec(ls, d=0):
        for l in ls:
            print("  " * d + ("[G] " if l.is_group() else "    ") + l.name, "vis" if l.visible else "---")
            if l.is_group():
                rec(l, d + 1)
    rec(psd)


def composite(psd, spec, mouth):
    """spec に従ってレイヤーの表示を決めて合成する。"""
    choice = {k: v for k, v in spec.items() if k not in ("mouth", "+", "-")}
    choice["口"] = mouth
    extra_on = set(spec.get("+", []))
    extra_off = set(spec.get("-", []))
    parents = {}
    for l, p in walk(psd):
        parents[id(l)] = p

    def own_visible(l):
        n = norm(l.name)
        if n in extra_off:
            return False
        if n in extra_on:
            return True
        if l.is_group() and n in choice:
            return True   # 選択対象のグループ（目セット・黒目など）は中の選択に任せる
        p = parents.get(id(l))
        pn = norm(p.name) if p is not None else None
        if pn in choice:
            return n == choice[pn]
        return l.visible

    def visible(l):
        while l is not None:
            if not own_visible(l):
                return False
            l = parents.get(id(l))
        return True

    missing = []
    for grp, want in choice.items():
        found = any(norm(l.name) == want and parents.get(id(l)) is not None and norm(parents[id(l)].name) == grp
                    for l, _ in walk(psd))
        if not found:
            missing.append(f"{grp}/{want}")
    if missing:
        print("  !! レイヤーが見つからない:", ", ".join(missing))
    return psd.composite(layer_filter=visible, force=True)


def crop_alpha(im, pad=8):
    bbox = im.getbbox()
    if not bbox:
        return im
    l, t, r, b = bbox
    return im.crop((max(0, l - pad), max(0, t - pad), min(im.width, r + pad), min(im.height, b + pad)))


def main():
    os.makedirs(DST, exist_ok=True)
    for code, fn in PSD_FILES.items():
        path = os.path.join(SRC, fn)
        if not os.path.exists(path):
            print("PSD が無い:", path)
            continue
        psd = PSDImage.open(path)
        if "--list" in sys.argv:
            print("###", fn)
            list_layers(psd)
            continue
        # 全感情で同じ切り抜き枠にする（体の位置が揃うように、キャンバス全体を残す）
        for emo, spec in EMOTIONS[code].items():
            for mouth_key, mouth in zip(("closed", "open"), spec["mouth"]):
                im = composite(psd, spec, mouth).convert("RGBA")
                out = os.path.join(DST, f"{code}_{emo}_{mouth_key}.png")
                im.save(out)
                print("→", os.path.relpath(out, ROOT), im.size)
                if emo == "thumb":
                    break


if __name__ == "__main__":
    main()
