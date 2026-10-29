# -*- coding: utf-8 -*-
"""
トコのトムトムランド 実況動画メーカー（ずんだもん × 四国めたん）

  py -3 jikkyo.py assets                 # ミームカード・ゲーム内の絵・顔アイコン・効果音・フォントをそろえる
  py -3 jikkyo.py plan   A_tantan        # 台本の見積もり（行数・推定尺）
  py -3 jikkyo.py voice  A_tantan        # VOICEVOX で音声を作る（キャッシュあり）
  py -3 jikkyo.py render A_tantan        # 動画を書き出す（out/A_tantan/A_tantan.mp4）
  py -3 jikkyo.py thumb  A_tantan        # サムネ（out/A_tantan/thumb.png）
  py -3 jikkyo.py all    A_tantan        # voice → render → thumb
  py -3 jikkyo.py all                    # 3本ぜんぶ
  py -3 jikkyo.py bgmplan E_soudan       # BGM の計画の案（章・行の中身から）を出す。--write で scripts/E_soudan.bgm.json に書く
  py -3 jikkyo.py render E_soudan --preview --bgm-plan=D:/path/plan.json   # 計画ファイルを当てて書き出す（台本はそのまま）

環境変数: JIKKYO_MAXT（書き出す秒数の上限）、JIKKYO_OUT（render の出力先。声のキャッシュは out/ のまま）、
          JIKKYO_FLIP_M（0/1。四国めたんの立ち絵を左右反転するか。既定は STYLE_PRESET の flip_m）
BGM（CC0、assets/bgm_cc0）とゲーム音の大きさの書き方は bgm.py の冒頭と assets/bgm_cc0/README.md。

必要なもの: VOICEVOX エンジン（http://127.0.0.1:50021）、ffmpeg / ffprobe、Pillow、numpy
素材の割り当ては footage.json、台本は scripts/*.json。
"""
import hashlib
import io
import json
import math
import os
import shutil
import subprocess
import sys
import urllib.parse
import urllib.request
import wave

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(ROOT, "..", ".."))
ASSETS = os.path.join(ROOT, "assets")
OUT = os.path.join(ROOT, "out")
VOICEVOX = "http://127.0.0.1:50021"
W, H, FPS = 1920, 1080, 30

USER_FONTS = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Microsoft", "Windows", "Fonts")
FONT_FILES = {
    "main": "ZenKakuGothicNew-Black.ttf",   # Google Fonts (SIL OFL)
    "bold": "ZenKakuGothicNew-Bold.ttf",
    "meme": "GN-KillGothic-U-KanaNA.ttf",  # フリーフォント（キルゴ U）
    "round": "MPLUSRounded1c-Bold.ttf",    # Google Fonts
}
FONT_FACE_MAIN = "Zen Kaku Gothic New Black"

# 話者 × 感情 → VOICEVOX のスタイル id
STYLES = {
    "z": {"normal": 3, "ama": 1, "tsun": 7, "sexy": 5, "sasayaki": 22, "hiso": 38, "hero": 75, "namida": 76, "surprise": 3},
    "m": {"normal": 2, "ama": 0, "tsun": 6, "sexy": 4, "sasayaki": 36, "hiso": 37, "hero": 6, "namida": 36, "surprise": 2},
}
SPEAKER_NAME = {"z": "ずんだもん", "m": "四国めたん"}
SPEAKER_UUID_NAME = {"ずんだもん": "z", "四国めたん": "m"}

# 軸ごとの演出パラメータ
STYLE_PRESET = {
    "tantan":   {"speed": 1.12, "gap": 0.38, "sub_size": 68, "bgm_vol": 0.10, "game_vol": 0.45, "name_tag": False, "cutin_pos": "top", "intonation": 1.0, "flip_m": True},
    "kaisetsu": {"speed": 1.15, "gap": 0.22, "sub_size": 74, "bgm_vol": 0.10, "game_vol": 0.25, "name_tag": False,  "cutin_pos": "right", "intonation": 1.05, "flip_m": True},
    "play":     {"speed": 1.15, "gap": 0.22, "sub_size": 72, "bgm_vol": 0.14, "game_vol": 0.35, "name_tag": False, "cutin_pos": "top", "intonation": 1.2, "flip_m": True},
}
# flip_m: 四国めたん（左）の立ち絵を左右反転して、右のずんだもんの方を向かせる（立ち絵には文字が無いので反転してよい）。
#         台本の "flip_m": false か環境変数 JIKKYO_FLIP_M=0 で前の向きに戻せる。

# BGM の計画で使う行のキー（あるときだけ timeline に写す）
LINE_PASS_KEYS = ("bgm", "bgm_stop", "bgm_sting", "bgm_duck", "game_vol", "mood")

MEMES = {
    # --- 元ネタの画像・BB素材を assets/memes_user/<id>.* に置くとそれが使われる。無い間は決め台詞の文字カード ---
    "meme_owarai": ("その気になっていたお前の姿はお笑いだったぜ", (40, 40, 60)),
    "meme_volga": ("ボルガ博士、お許しください！", (160, 60, 30)),
    "meme_handshake_cat": ("（握手）", (90, 140, 200)),
    "meme_blackcat_clap": ("（拍手）", (50, 50, 50)),
    "meme_ehho": ("エッホエッホ", (150, 120, 80)),
    "meme_whisper": ("（耳打ち）", (120, 90, 60)),
    "meme_prison": ("罪状：", (60, 60, 70)),
    "meme_poison": ("ヤバい！これ毒かも！間に合え間に合え！", (200, 60, 60)),
    "meme_kigakiku": ("気が利くねぇ", (110, 140, 110)),
    "meme_dagaima": ("だが今は違う！", (200, 40, 40)),
    "meme_ketsuron": ("結論から言うね", (70, 110, 170)),
    "meme_yarimasu": ("やりますねぇ！", (60, 60, 60)),
    "meme_arigatonasu": ("ありがとナス！", (60, 60, 60)),
    "meme_fa": ("ファッ!?", (60, 60, 60)),
    "meme_misedaseru": ("これ店出せるよ", (200, 140, 40)),
    "meme_metsu": ("〇〇すぎて滅", (120, 40, 120)),
    "meme_kusa": ("草", (70, 170, 60)),
    "meme_kita": ("ｷﾀ━━(ﾟ∀ﾟ)━━!!", (230, 60, 60)),
    "meme_kan": ("完", (40, 40, 40)),
    "meme_owata": ("＼(^o^)／ｵﾜﾀ", (90, 90, 200)),
    "meme_zawa": ("ざわ…ざわ…", (60, 40, 90)),
    "meme_doya": ("ドヤァ", (240, 150, 30)),
    "meme_nandatte": ("な、なんだってー！！", (200, 30, 90)),
    "meme_kaisan": ("はい解散", (60, 60, 60)),
    "meme_shosetsu": ("※諸説あります", (120, 120, 120)),
    "meme_kojin": ("※個人の感想です", (120, 120, 120)),
    "meme_shh": ("しーっ…", (70, 120, 180)),
    "meme_shobu": ("勝負！", (220, 40, 40)),
    "meme_chance": ("チャンス！", (240, 180, 20)),
    "meme_hirameki": ("閃いた！", (250, 200, 40)),
}

GAME_CUTINS = {
    "game_title": ("Assets/BackGround/タイトル画面.png", 0.9),
    "game_map": ("Assets/BackGround/マップ.png", 0.9),
    "game_hero": ("Assets/Character/キャラ立ち絵/勇者.png", 1.0),
    "game_maou": ("Assets/Character/キャラ立ち絵/魔王.png", 1.0),
    "game_blacksmith": ("Assets/Character/キャラ立ち絵/鍛冶屋.png", 1.0),
}

BGM_FILES = {
    "通常営業": "Assets/Sounds/BGM/通常営業.mp3",
    "配信フェーズ": "Assets/Sounds/BGM/配信フェーズ.mp3",
    "リザルト画面": "Assets/Sounds/BGM/リザルト画面.mp3",
    "OP": "Assets/Sounds/BGM/OP.mp3",
}


# ---------------------------------------------------------------- 共通
def log(*a):
    print("[jikkyo]", *a, flush=True)


def run(cmd, cwd=None):
    log(" ".join(c if " " not in c else f'"{c}"' for c in cmd)[:300])
    r = subprocess.run(cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if r.returncode != 0:
        sys.stderr.write(r.stderr.decode("utf-8", "replace")[-4000:])
        raise SystemExit(f"command failed: {cmd[0]}")
    return r


def font(kind, size):
    p = os.path.join(ASSETS, "fonts", FONT_FILES[kind])
    if not os.path.exists(p):
        p = os.path.join(USER_FONTS, FONT_FILES[kind])
    return ImageFont.truetype(p, size)


def load_script(name):
    p = os.path.join(ROOT, "scripts", name + ".json")
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def load_footage(name=None):
    """footage.json を読む。per_script[台本id] があれば sources / tags / outcomes をその台本用に上書きする。"""
    with open(os.path.join(ROOT, "footage.json"), encoding="utf-8") as f:
        fo = json.load(f)
    ov = (fo.get("per_script") or {}).get(name) if name else None
    if ov:
        fo = dict(fo)
        for k in ("sources", "tags"):
            if k in ov:
                fo[k] = dict(fo.get(k, {}), **ov[k])
        if "outcomes" in ov:
            fo["outcomes"] = dict(fo.get("outcomes", {}), **ov["outcomes"])
    return fo


def line_text(line, outcomes):
    if "alt" in line and line.get("on") in outcomes:
        return line["alt"].get(outcomes[line["on"]], line["t"])
    return line["t"]


def ffprobe_duration(path):
    r = run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path])
    return float(r.stdout.decode().strip())


# ---------------------------------------------------------------- assets
def key_white(im, thresh=235):
    """白い背景を透過にする（ゲームの立ち絵 PNG が白地のときに使う）。"""
    im = im.convert("RGBA")
    a = np.array(im).astype(np.int16)
    rgb = a[:, :, :3]
    mn = rgb.min(axis=2)
    alpha = np.clip((thresh - mn) * 8, 0, 255)  # 235 以上で透明、少し下から不透明
    a[:, :, 3] = np.minimum(a[:, :, 3], alpha)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def make_rounded_card(size, color, radius=28):
    im = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius=radius, fill=color)
    return im


def draw_text_outline(d, xy, text, fnt, fill, outline, width, anchor="la"):
    x, y = xy
    for dx in range(-width, width + 1):
        for dy in range(-width, width + 1):
            if dx * dx + dy * dy <= width * width:
                d.text((x + dx, y + dy), text, font=fnt, fill=outline, anchor=anchor)
    d.text((x, y), text, font=fnt, fill=fill, anchor=anchor)


def make_meme_card(text, color, out_path):
    fnt = font("main", 110)
    tw = fnt.getlength(text)
    pad = 60
    cw, ch = int(tw + pad * 2), 120 + pad * 2
    card = make_rounded_card((cw, ch), color + (255,))
    d = ImageDraw.Draw(card)
    draw_text_outline(d, (cw // 2, ch // 2), text, fnt, (255, 255, 255), (0, 0, 0), 6, anchor="mm")
    # 白フチ
    border = make_rounded_card((cw + 16, ch + 16), (255, 255, 255, 255))
    border.alpha_composite(card, (8, 8))
    rot = border.rotate(-6, expand=True, resample=Image.BICUBIC)
    shadow = Image.new("RGBA", rot.size, (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 140), (0, 0), rot.split()[3])
    shadow = shadow.filter(ImageFilter.GaussianBlur(10))
    final = Image.new("RGBA", (rot.size[0] + 30, rot.size[1] + 30), (0, 0, 0, 0))
    final.alpha_composite(shadow, (18, 18))
    final.alpha_composite(rot, (0, 0))
    final.save(out_path)


USER_MEME_EXTS = (".png", ".jpg", ".jpeg", ".webp", ".gif", ".mp4", ".webm", ".mov", ".mkv")


def find_user_meme(img):
    """assets/memes_user/<id>.<ext> を探す（_bb/_gb の接尾辞付きも可）。無ければ None。"""
    d = os.path.join(ASSETS, "memes_user")
    if not os.path.isdir(d):
        return None
    for fn in sorted(os.listdir(d)):
        stem, ext = os.path.splitext(fn)
        if ext.lower() not in USER_MEME_EXTS:
            continue
        if stem == img or stem in (img + "_bb", img + "_gb", img + "_BB", img + "_GB"):
            return os.path.join(d, fn)
    return None


def media_has_audio(path):
    r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a", "-show_entries", "stream=codec_type", "-of", "csv=p=0", path],
                       capture_output=True, text=True)
    return "audio" in (r.stdout or "")


def meme_key_color(path):
    """ファイル名の接尾辞で抜く色を決める: _bb=青（BB素材）, _gb=緑（GB素材）, それ以外は抜かない。"""
    stem = os.path.splitext(os.path.basename(path))[0].lower()
    if stem.endswith("_bb"):
        return "0x0000ff"
    if stem.endswith("_gb"):
        return "0x00ff00"
    return None


def make_cutin_card(src_path, out_path, scale=1.0, max_w=620, max_h=460):
    """差し込み画像を白いカード（フチ・影つき）にする。"""
    im = Image.open(src_path).convert("RGBA")
    bbox = im.getbbox()
    if bbox and im.mode == "RGBA" and im.getextrema()[3][0] < 255:
        im = im.crop(bbox)
    r = min(max_w * scale / im.width, max_h * scale / im.height)
    im = im.resize((max(1, int(im.width * r)), max(1, int(im.height * r))), Image.LANCZOS)
    pad = 14
    card = Image.new("RGBA", (im.width + pad * 2, im.height + pad * 2), (255, 255, 255, 255))
    card.alpha_composite(im, (pad, pad))
    d = ImageDraw.Draw(card)
    d.rectangle([0, 0, card.width - 1, card.height - 1], outline=(40, 40, 40, 255), width=4)
    shadow = Image.new("RGBA", (card.width + 40, card.height + 40), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rectangle([20, 20, 20 + card.width, 20 + card.height], fill=(0, 0, 0, 150))
    shadow = shadow.filter(ImageFilter.GaussianBlur(12))
    final = Image.new("RGBA", shadow.size, (0, 0, 0, 0))
    final.alpha_composite(shadow, (0, 0))
    final.alpha_composite(card, (10, 10))
    final.save(out_path)


def synth_se():
    sr = 48000
    def write(name, data):
        data = np.clip(data, -1, 1)
        with wave.open(os.path.join(ASSETS, "se", name), "wb") as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
            w.writeframes((data * 32767).astype(np.int16).tobytes())
    t = np.arange(int(sr * 0.12)) / sr
    pop = np.sin(2 * np.pi * (900 - 2500 * t) * t) * np.exp(-t * 35) * 0.7
    write("pop.wav", pop)
    t = np.arange(int(sr * 0.6)) / sr
    ding = (np.sin(2 * np.pi * 1320 * t) + 0.5 * np.sin(2 * np.pi * 2640 * t)) * np.exp(-t * 6) * 0.5
    write("ding.wav", ding)
    t = np.arange(int(sr * 0.35)) / sr
    rng = np.random.default_rng(1)
    whoosh = rng.normal(0, 1, t.size) * np.sin(np.pi * t / 0.35) ** 2 * 0.35
    write("whoosh.wav", whoosh)
    t = np.arange(int(sr * 0.5)) / sr
    boom = np.sin(2 * np.pi * (70 + 30 * np.exp(-t * 8)) * t) * np.exp(-t * 7) * 0.9
    write("boom.wav", boom)


def fetch_faces():
    """VOICEVOX エンジンからスタイル別アイコンと立ち絵を取る。"""
    spk = json.load(urllib.request.urlopen(VOICEVOX + "/speakers"))
    import base64
    for s in spk:
        code = SPEAKER_UUID_NAME.get(s["name"])
        if not code:
            continue
        info = json.load(urllib.request.urlopen(f"{VOICEVOX}/speaker_info?speaker_uuid={s['speaker_uuid']}"))
        with open(os.path.join(ASSETS, "chars", f"{code}_portrait.png"), "wb") as f:
            f.write(base64.b64decode(info["portrait"]))
        for st in info["style_infos"]:
            with open(os.path.join(ASSETS, "chars", f"{code}_icon_{st['id']}.png"), "wb") as f:
                f.write(base64.b64decode(st["icon"]))
        with open(os.path.join(ASSETS, "chars", f"{code}_policy.txt"), "w", encoding="utf-8") as f:
            f.write(info["policy"])
        log("faces:", s["name"], [st["id"] for st in info["style_infos"]])


def make_face_badge(src, out_path, size=150):
    im = Image.open(src).convert("RGBA").resize((size, size), Image.LANCZOS)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).ellipse([0, 0, size - 1, size - 1], fill=255)
    badge = Image.new("RGBA", (size + 12, size + 12), (0, 0, 0, 0))
    ImageDraw.Draw(badge).ellipse([0, 0, size + 11, size + 11], fill=(255, 255, 255, 255))
    ImageDraw.Draw(badge).ellipse([2, 2, size + 9, size + 9], outline=(30, 30, 30, 255), width=3)
    circ = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    circ.paste(im, (0, 0), mask)
    badge.alpha_composite(circ, (6, 6))
    badge.save(out_path)


def cmd_assets():
    for d in ("cards", "chars", "faces", "se", "fonts", "memes_user"):
        os.makedirs(os.path.join(ASSETS, d), exist_ok=True)
    for k, fn in FONT_FILES.items():
        src = os.path.join(USER_FONTS, fn)
        dst = os.path.join(ASSETS, "fonts", fn)
        if os.path.exists(src) and not os.path.exists(dst):
            shutil.copy(src, dst)
    # ミームの文字カードと生成画像は作らない（元ネタの動画・BB素材を assets/memes_user に置く）
    log("game cutins")
    for gid, (rel, scale) in GAME_CUTINS.items():
        src = os.path.join(REPO, rel)
        if os.path.exists(src):
            make_cutin_card(src, os.path.join(ASSETS, "cards", gid + ".png"), scale)



# ---------------------------------------------------------------- voice / timeline
def voice_path(name, text, spk, speed, intonation):
    h = hashlib.sha1(f"{text}|{spk}|{speed}|{intonation}".encode("utf-8")).hexdigest()[:16]
    return os.path.join(OUT, name, "voice", f"{spk}_{h}.wav")


def synth_voice(text, spk, speed, intonation, out_path):
    if os.path.exists(out_path):
        return
    q = urllib.request.urlopen(urllib.request.Request(
        f"{VOICEVOX}/audio_query?text={urllib.parse.quote(text)}&speaker={spk}", method="POST")).read()
    qj = json.loads(q)
    qj["speedScale"] = speed
    qj["intonationScale"] = intonation
    qj["prePhonemeLength"] = 0.05
    qj["postPhonemeLength"] = 0.08
    qj["outputSamplingRate"] = 24000
    qj["outputStereo"] = False
    w = urllib.request.urlopen(urllib.request.Request(
        f"{VOICEVOX}/synthesis?speaker={spk}", data=json.dumps(qj).encode("utf-8"),
        headers={"Content-Type": "application/json"}, method="POST")).read()
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(w)


def wav_duration(path):
    with wave.open(path, "rb") as w:
        return w.getnframes() / w.getframerate()


def build_timeline(name, sc, footage, synth=True):
    """台本 → 行ごとの開始・終了（秒）。synth=False なら文字数から推定。"""
    preset = STYLE_PRESET[sc["style"]]
    outcomes = footage.get("outcomes", {})
    t = 0.4
    chapters = []
    events = []  # dict(kind=line|pause, ...)
    segments = []  # dict(footage, at, freeze, start, end): 映像の切れ目（章の頭と、at/freeze/footage を持つ行）
    def new_segment(ch, line, at_t):
        if segments and segments[-1]["start"] >= at_t - 0.01:
            segments[-1].update({"at": line.get("at", segments[-1]["at"]), "freeze": bool(line.get("freeze")),
                                 "footage": line.get("footage", segments[-1]["footage"])})
            return
        if segments:
            segments[-1]["end"] = at_t
        segments.append({"footage": line.get("footage", ch["footage"]), "at": line.get("at"),
                         "freeze": bool(line.get("freeze")), "start": at_t, "end": None})
    for ch in sc["chapters"]:
        cstart = t
        new_segment(ch, {}, t)
        if ch.get("card"):
            events.append({"kind": "card", "text": ch["card"], "start": t, "end": t + 1.8})
            t += 1.2
        for line in ch["lines"]:
            if "at" in line or "freeze" in line or "footage" in line:
                new_segment(ch, line, t)
            if "pause" in line:
                t += float(line["pause"])
                continue
            text = line_text(line, outcomes)
            spk = STYLES[line["s"]][line.get("e", "normal")]
            speed = float(line.get("speed", preset["speed"]))
            if synth:
                p = voice_path(name, text, spk, speed, preset["intonation"])
                synth_voice(text, spk, speed, preset["intonation"], p)
                dur = wav_duration(p)
            else:
                p = None
                dur = len(text) / (6.6 * speed) + 0.35
            ev = {"kind": "line", "s": line["s"], "e": line.get("e", "normal"), "spk": spk, "text": text,
                  "start": t, "end": t + dur, "wav": p, "img": line.get("img"), "telop": line.get("telop"),
                  "chapter": ch["id"]}
            for k in LINE_PASS_KEYS:
                if k in line:
                    ev[k] = line[k]
            if line.get("snd"):
                # ミーム音（assets/sounds_meme/<id>.mp3）。snd_at は行の頭からの秒（負なら前）、
                # "end" なら言い終わり、"end+0.3" / "end-0.5" のように終わりからずらせる。snd_vol は音量
                ev["snd"] = line["snd"]
                at = line.get("snd_at", 0.0)
                if isinstance(at, str) and at.strip().startswith("end"):
                    ev["snd_at"] = (ev["end"] - ev["start"]) + float(at.strip()[3:] or 0.0)
                else:
                    ev["snd_at"] = float(at)
                ev["snd_vol"] = float(line.get("snd_vol", 0.7))
            events.append(ev)
            # 行間: 行ごとの gap 指定 > 短いツッコミ（相手が14文字以内で返す）は詰める > プリセット
            gap = line.get("gap")
            if gap is None:
                rest = ch["lines"][ch["lines"].index(line) + 1:]
                nxt = next((x for x in rest if "t" in x or "pause" in x), None)
                if nxt and "t" in nxt and nxt["s"] != line["s"] and len(line_text(nxt, outcomes)) <= 14:
                    gap = min(preset["gap"], 0.12)
                else:
                    gap = preset["gap"]
            t = ev["end"] + float(gap)
        chapters.append({"id": ch["id"], "footage": ch["footage"], "start": cstart, "end": t})
    total = t + 2.0
    chapters[-1]["end"] = total
    segments[-1]["end"] = total
    return {"events": events, "chapters": chapters, "segments": segments, "total": total, "preset": preset}


def cmd_plan(name):
    sc = load_script(name)
    tl = build_timeline(name, sc, load_footage(name), synth=False)
    lines = [e for e in tl["events"] if e["kind"] == "line"]
    chars = sum(len(e["text"]) for e in lines)
    log(f"{name}: {len(lines)} 行 / {chars} 文字 / 推定 {tl['total']/60:.1f} 分（章 {len(tl['chapters'])}）")
    for c in tl["chapters"]:
        log(f"  {c['id']:16} {c['footage']:14} {c['start']:6.1f} → {c['end']:6.1f}")


def cmd_voice(name):
    sc = load_script(name)
    tl = build_timeline(name, sc, load_footage(name), synth=True)
    log(f"{name}: 音声 {sum(1 for e in tl['events'] if e['kind']=='line')} 行、尺 {tl['total']/60:.2f} 分")
    return tl


# ---------------------------------------------------------------- render
def mix_voice_track(tl, out_path, sr=24000):
    total = int(tl["total"] * sr) + sr
    buf = np.zeros(total, dtype=np.float32)
    for e in tl["events"]:
        if e["kind"] != "line":
            continue
        with wave.open(e["wav"], "rb") as w:
            assert w.getframerate() == sr and w.getnchannels() == 1
            data = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
        s = int(e["start"] * sr)
        buf[s:s + data.size] += data[: max(0, total - s)]
    with wave.open(out_path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
        w.writeframes((np.clip(buf, -1, 1) * 32767).astype(np.int16).tobytes())


def mix_se_track(tl, out_path, sr=48000):
    total = int(tl["total"] * sr) + sr
    buf = np.zeros(total, dtype=np.float32)
    cache = {}
    def se(nm):
        if nm not in cache:
            with wave.open(os.path.join(ASSETS, "se", nm + ".wav"), "rb") as w:
                cache[nm] = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
        return cache[nm]
    for e in tl["events"]:
        nm = None
        if e["kind"] == "card":
            nm = "whoosh"
        elif e["kind"] == "line" and e.get("img") and find_user_meme(e["img"]):
            # 元ネタの素材があるときだけ効果音（無いときは声と表情だけなので鳴らさない）
            nm = "ding"
        if nm:
            d = se(nm); s = int(e["start"] * sr)
            buf[s:s + d.size] += d[: max(0, total - s)]
    with wave.open(out_path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
        w.writeframes((np.clip(buf, -1, 1) * 32767).astype(np.int16).tobytes())


def ass_time(t):
    t = max(0.0, t)
    h = int(t // 3600); m = int(t % 3600 // 60); s = t % 60
    return f"{h}:{m:02d}:{s:05.2f}"


def ass_escape(s):
    return s.replace("\\", "\\\\").replace("{", "｛").replace("}", "｝").replace("\n", "\\N")


def wrap_line(text, size, width=1080):
    """libass の自動折り返しに頼らず、字幕を自分で改行する（句読点の近くで均等に割る）。"""
    cpl = max(10, int(width / (size * 0.66)))
    if len(text) <= cpl:
        return text
    lines_left = math.ceil(len(text) / cpl)
    out, rest = [], text
    while lines_left > 1:
        target = math.ceil(len(rest) / lines_left)
        cut = target
        for k in range(target, max(target - 8, 1), -1):   # 少し手前の句読点で切る（残りが収まる範囲で）
            if rest[k - 1] in "、。！？…ー」）" and len(rest) - k <= cpl * (lines_left - 1):
                cut = k
                break
        out.append(rest[:cut])
        rest = rest[cut:]
        lines_left -= 1
    out.append(rest)
    return "\\N".join(out)


def write_ass(tl, out_path, preset, sc):
    size = preset["sub_size"]
    name_tag = preset["name_tag"]
    head = f"""[Script Info]
ScriptType: v4.00+
PlayResX: {W}
PlayResY: {H}
WrapStyle: 0
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Z,{FONT_FACE_MAIN},{size},&H00FFFFFF,&H00FFFFFF,&H00328C28,&H96000000,-1,0,0,0,100,100,0,0,1,5,2,2,420,420,60,1
Style: M,{FONT_FACE_MAIN},{size},&H00FFFFFF,&H00FFFFFF,&H00A040C8,&H96000000,-1,0,0,0,100,100,0,0,1,5,2,2,420,420,60,1
Style: Telop,{FONT_FACE_MAIN},54,&H0030F0FF,&H00FFFFFF,&H00202020,&HA0202020,-1,0,0,0,100,100,2,0,3,10,0,8,40,40,36,1
Style: Corner,{FONT_FACE_MAIN},30,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,-1,0,0,0,100,100,0,0,1,2,0,7,24,24,18,1
Style: Band,{FONT_FACE_MAIN},20,&H00000000,&H00000000,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,2,0,0,0,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
    rows = [head]
    rows.append(f"Dialogue: 0,{ass_time(0)},{ass_time(tl['total'])},Corner,,0,0,0,,トコのトムトムランド\n")
    for e in tl["events"]:
        if e["kind"] != "line":
            continue
        st = "Z" if e["s"] == "z" else "M"
        # 次の行と重なると libass が上に積んで字幕が浮くので、次の行の直前で切る
        nxt_line = next((x for x in tl["events"] if x["kind"] == "line" and x["start"] > e["start"]), None)
        sub_end = e["end"] + 0.25
        if nxt_line:
            sub_end = min(sub_end, nxt_line["start"] - 0.03)
        text = wrap_line(ass_escape(e["text"]), size)
        if name_tag:
            text = "{\\fs34}" + SPEAKER_NAME[e["s"]] + "{\\fs" + str(size) + "}\\N" + text
        # 字幕の背後に半透明の帯（ゲーム側の文字と重なって読めなくなるのを防ぐ）
        n_lines = text.count("\\N") + 1
        band_h = int(((n_lines - (1 if name_tag else 0)) * size + (34 if name_tag else 0)) * 1.22 + 30)
        band_w = W - 2 * 380
        band = ("{\\an2\\pos(" + str(W // 2) + "," + str(H - 60 + 15) + ")\\alpha&H5A&\\1c&H101010&\\bord0\\shad0\\p1}"
                + f"m 0 0 l {band_w} 0 l {band_w} {band_h} l 0 {band_h}" + "{\\p0}")
        rows.append(f"Dialogue: 0,{ass_time(e['start'])},{ass_time(sub_end)},Band,,0,0,0,,{band}\n")
        rows.append(f"Dialogue: 1,{ass_time(e['start'])},{ass_time(sub_end)},{st},,0,0,0,,{text}\n")
        if e.get("telop"):
            nxt = next((x for x in tl["events"] if x["kind"] == "line" and x.get("telop") and x["start"] > e["start"]), None)
            t_end = min(e["end"] + 2.5, nxt["start"]) if nxt else e["end"] + 2.5
            rows.append(f"Dialogue: 2,{ass_time(e['start'])},{ass_time(t_end)},Telop,,0,0,0,,{ass_escape(e['telop'])}\n")
    with open(out_path, "w", encoding="utf-8-sig") as f:
        f.writelines(rows)


def write_srt(tl, out_path):
    def srt_time(t):
        h = int(t // 3600); m = int(t % 3600 // 60); s = int(t % 60); ms = int((t - int(t)) * 1000)
        return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"
    with open(out_path, "w", encoding="utf-8") as f:
        n = 1
        for e in tl["events"]:
            if e["kind"] != "line":
                continue
            f.write(f"{n}\n{srt_time(e['start'])} --> {srt_time(e['end'] + 0.25)}\n{SPEAKER_NAME[e['s']]}: {e['text']}\n\n")
            n += 1


def make_card_png(text, out_path):
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, H], fill=(0, 0, 0, 150))
    d.rectangle([0, H // 2 - 130, W, H // 2 + 130], fill=(250, 210, 60, 255))
    d.rectangle([0, H // 2 - 130, W, H // 2 - 118], fill=(40, 30, 10, 255))
    d.rectangle([0, H // 2 + 118, W, H // 2 + 130], fill=(40, 30, 10, 255))
    fnt = font("main", 84)
    draw_text_outline(d, (W // 2, H // 2), text, fnt, (40, 30, 10), (255, 255, 255), 0, anchor="mm")
    im.save(out_path)


def make_end_card(sc, out_path):
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, H], fill=(10, 10, 20, 215))
    fnt = font("main", 72)
    draw_text_outline(d, (W // 2, 300), "トコのトムトムランド", fnt, (255, 220, 80), (0, 0, 0), 4, anchor="mm")
    fnt2 = font("main", 54)
    draw_text_outline(d, (W // 2, 420), "Steam でウィッシュリスト登録をお願いします", fnt2, (255, 255, 255), (0, 0, 0), 3, anchor="mm")
    fnt3 = font("bold", 34)
    d.text((W // 2, 560), "VOICEVOX:ずんだもん　VOICEVOX:四国めたん", font=fnt3, fill=(220, 220, 220), anchor="mm")
    if os.path.isdir(os.path.join(ASSETS, "tachie")):
        d.text((W // 2, 610), "立ち絵：坂本アヒル", font=fnt3, fill=(220, 220, 220), anchor="mm")
    d.text((W // 2, 670), "store.steampowered.com/app/4535810", font=fnt3, fill=(180, 200, 255), anchor="mm")
    im.save(out_path)


def merged_intervals(evs, pad_end=0.0):
    ivs = sorted((e["start"], e["end"] + pad_end) for e in evs)
    out = []
    for s, e in ivs:
        if out and s <= out[-1][1] + 0.05:
            out[-1][1] = max(out[-1][1], e)
        else:
            out.append([s, e])
    return out


def enable_expr(ivs):
    return "+".join(f"between(t,{s:.2f},{e:.2f})" for s, e in ivs) or "0"


def build_base_video(tl, footage, out_dir):
    """映像の切れ目（segments）ごとに素材を切り出して連結する（base.mp4）。freeze は1コマの静止画。"""
    seg_dir = os.path.join(out_dir, "segs")
    os.makedirs(seg_dir, exist_ok=True)
    list_path = os.path.join(seg_dir, "list.txt")
    with open(list_path, "w", encoding="utf-8") as lf:
        for i, c in enumerate(tl.get("segments") or tl["chapters"]):
            tag = footage["tags"].get(c["footage"]) or footage["tags"]["shop"]
            src = footage["sources"][tag["src"]]
            at = c.get("at") if c.get("at") is not None else tag.get("start", 0)
            freeze = c.get("freeze", False)
            dur = c["end"] - c["start"]
            if dur <= 0.02:
                continue
            seg = os.path.join(seg_dir, f"seg_{i:03d}.mp4")
            key = f"{src}|{at}|{dur:.2f}|{'freeze' if freeze else 'tpad'}"
            kp = seg + ".key"
            if os.path.exists(seg) and os.path.exists(kp) and open(kp).read() == key:
                lf.write(f"file 'seg_{i:03d}.mp4'\n")
                continue
            if freeze:
                # そのコマで静止（一時停止）。音は無音
                run(["ffmpeg", "-y", "-v", "error", "-ss", str(at), "-i", src,
                     "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-map", "0:v", "-map", "1:a",
                     "-vf", f"scale={W}:{H}:flags=bicubic,trim=end_frame=1,tpad=stop_mode=clone:stop_duration={dur:.3f},fps={FPS},format=yuv420p",
                     "-t", f"{dur:.3f}",
                     "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-c:a", "aac", "-ar", "48000", "-ac", "2", seg])
            else:
                # 素材が足りないときは最後のコマで止める（頭からループさせない）
                run(["ffmpeg", "-y", "-v", "error", "-ss", str(at), "-i", src,
                     "-vf", f"scale={W}:{H}:flags=bicubic,fps={FPS},format=yuv420p,tpad=stop_mode=clone:stop_duration={dur:.3f}",
                     "-af", "apad", "-t", f"{dur:.3f}",
                     "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-c:a", "aac", "-ar", "48000", "-ac", "2", seg])
            open(kp, "w").write(key)
            lf.write(f"file 'seg_{i:03d}.mp4'\n")
    base = os.path.join(out_dir, "base.mp4")
    # 音声は AAC の継ぎ目でタイムスタンプが重なるので、連結時に作り直す（映像はそのままコピー）
    run(["ffmpeg", "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", list_path,
         "-c:v", "copy", "-af", "aresample=async=1:first_pts=0", "-c:a", "aac", "-ar", "48000", base])
    return base


def flip_metan(sc, preset):
    env = os.environ.get("JIKKYO_FLIP_M")
    if env is not None and env.strip() != "":
        return env.strip() not in ("0", "false", "no")
    return bool(sc.get("flip_m", preset.get("flip_m", False)))


def cmd_render(name, preview=False, bgm_plan=None):
    import bgm as bgmlib
    sc = load_script(name)
    if bgm_plan:
        with open(bgm_plan, encoding="utf-8") as f:
            sc["bgm"] = json.load(f)
    footage = load_footage(name)
    preset = STYLE_PRESET[sc["style"]]
    flip_m = flip_metan(sc, preset)
    out_dir = os.path.join(os.environ.get("JIKKYO_OUT") or OUT, name)
    os.makedirs(out_dir, exist_ok=True)
    tl = build_timeline(name, sc, footage, synth=True)
    log(f"尺 {tl['total']/60:.2f} 分")
    with open(os.path.join(out_dir, "timeline.json"), "w", encoding="utf-8") as f:
        json.dump(tl, f, ensure_ascii=False, indent=1)

    voice_wav = os.path.join(out_dir, "voice.wav"); mix_voice_track(tl, voice_wav)
    se_wav = os.path.join(out_dir, "se.wav"); mix_se_track(tl, se_wav)
    ass_path = os.path.join(out_dir, "subs.ass"); write_ass(tl, ass_path, preset, sc)
    write_srt(tl, os.path.join(out_dir, name + ".srt"))
    base = build_base_video(tl, footage, out_dir)

    # ---- 入力とフィルタ
    inputs = [base]
    filters = []
    def add_input(path, opts=None):
        inputs.append((list(opts or []), path)); return len(inputs) - 1

    lines = [e for e in tl["events"] if e["kind"] == "line"]
    # 立ち絵（しゃべっている間は上下に揺れる）
    #   坂本アヒルさんの立ち絵（assets/tachie, tachie.py で書き出す）があれば、感情ごとの表情＋口パク。
    #   無ければ VOICEVOX の立ち絵 1 枚＋顔アイコンで代用。
    use_tachie = all(os.path.exists(os.path.join(ASSETS, "tachie", f"{c}_normal_{m}.png")) for c in "zm" for m in ("closed", "open"))
    port_h = 640
    portraits = {}
    tachie = {}   # code -> list of (emotion, closed_idx, open_idx, emo_ivs, speak_ivs)
    for code, side in (("m", "left"), ("z", "right")):
        own = sorted((e for e in lines if e["s"] == code), key=lambda e: e["start"])
        ivs = merged_intervals(own)
        if not use_tachie:
            idx = add_input(os.path.join(ASSETS, "chars", f"{code}_portrait.png"))
            portraits[code] = (idx, side, ivs)
            continue
        # 表情はセリフの頭で切り替わり、次の自分のセリフまで持つ
        segs = []
        for i, e in enumerate(own):
            emo = e["e"] if os.path.exists(os.path.join(ASSETS, "tachie", f"{code}_{e['e']}_closed.png")) else "normal"
            s = e["start"] if i > 0 else 0.0
            en = own[i + 1]["start"] if i + 1 < len(own) else tl["total"] + 1
            segs.append((emo, s, en))
        if not segs:
            segs.append(("normal", 0.0, tl["total"] + 1))
        by_emo = {}
        for emo, s, en in segs:
            by_emo.setdefault(emo, []).append((s, en))
        entries = []
        for emo, emo_ivs in by_emo.items():
            ci = add_input(os.path.join(ASSETS, "tachie", f"{code}_{emo}_closed.png"))
            oi = add_input(os.path.join(ASSETS, "tachie", f"{code}_{emo}_open.png"))
            speak_ivs = [(s, e) for s, e in ivs if any(s < en and e > st for st, en in emo_ivs)]
            entries.append((emo, ci, oi, emo_ivs, speak_ivs))
        tachie[code] = (side, entries)
    # 顔アイコン（感情ごと）— 立ち絵が表情を持つときは出さない
    faces = {}
    face_inputs = {}
    if not use_tachie:
        for e in lines:
            key = (e["s"], e["spk"])
            faces.setdefault(key, []).append(e)
        for key in faces:
            p = os.path.join(ASSETS, "faces", f"{key[0]}_icon_{key[1]}.png")
            if os.path.exists(p):
                face_inputs[key] = add_input(p)
    # 差し込み画像（次の画像が出るか、行の終わり+1.2秒で消える）
    cut_ivs = {}
    for i, e in enumerate(lines):
        if not e.get("img"):
            continue
        nxt = next((x for x in lines[i + 1:] if x.get("img")), None)
        end = e["end"] + 1.2
        if nxt:
            end = min(end, nxt["start"])
        cut_ivs.setdefault(e["img"], []).append((e["start"], end))
    cut_inputs = {}
    user_memes = {}   # img -> [(idx, start, end, is_video, key)]
    meme_audio = []   # (idx, start, end): 元ネタ動画の音を出現中だけ鳴らす
    for img, ivs in cut_ivs.items():
        up = find_user_meme(img)
        if up:
            # 元ネタの画像・BB素材。動画/GIF は出すたびに頭から再生する（出現ごとに入力を分ける）
            is_video = up.lower().endswith((".mp4", ".webm", ".mov", ".gif", ".mkv"))
            key = meme_key_color(up)
            has_audio = is_video and media_has_audio(up)
            for (s0, e0) in ivs:
                opts = ["-stream_loop", "-1"] if is_video else []
                idx = add_input(up, opts)
                user_memes.setdefault(img, []).append((idx, s0, e0, is_video, key))
                if has_audio:
                    meme_audio.append((idx, s0, e0))
            continue
        log("素材が無いので出さない（assets/memes_user に置く）:", img)
    # 章カード・エンドカード
    card_inputs = []
    for i, e in enumerate(x for x in tl["events"] if x["kind"] == "card"):
        p = os.path.join(out_dir, f"card_{i:02d}.png"); make_card_png(e["text"], p)
        card_inputs.append((add_input(p), e["start"], e["end"]))
    end_png = os.path.join(out_dir, "end_card.png"); make_end_card(sc, end_png)
    end_idx = add_input(end_png)
    end_start = tl["total"] - 7.0

    cur = "[0:v]"
    n = 0
    def nxt_label():
        nonlocal n; n += 1; return f"[v{n}]"
    # 立ち絵
    for code, (idx, side, ivs) in portraits.items():
        filters.append(f"[{idx}:v]scale=-1:{port_h}:flags=lanczos{',hflip' if code == 'm' and flip_m else ''}[p{code}]")
        x = "-30" if side == "left" else f"W-w+30"
        speak = enable_expr(ivs)
        y = f"H-h+40-if({speak},12*abs(sin(t*11)),0)"
        lab = nxt_label()
        filters.append(f"{cur}[p{code}]overlay=x={x}:y='{y}':eval=frame{lab}")
        cur = lab
    # 立ち絵（感情ごと・口パク）: 全身を 860px に縮めて、上 600px だけ画面に入れる
    tachie_h = 860
    for code, (side, entries) in tachie.items():
        x = "-40" if side == "left" else "W-w+40"
        for emo, ci, oi, emo_ivs, speak_ivs in entries:
            E = enable_expr(emo_ivs)
            S = enable_expr(speak_ivs)
            mouth_open = f"({S})*lt(mod(t,0.26),0.13)"
            y = f"H-600-if({S},10*abs(sin(t*11)),0)"
            for idx, en in ((ci, f"({E})*not({mouth_open})"), (oi, f"({E})*{mouth_open}")):
                filters.append(f"[{idx}:v]scale=-1:{tachie_h}:flags=lanczos{',hflip' if code == 'm' and flip_m else ''}[t{idx}]")
                lab = nxt_label()
                filters.append(f"{cur}[t{idx}]overlay=x={x}:y='{y}':eval=frame:enable='{en}'{lab}")
                cur = lab
    # 元ネタの画像・BB素材（画面上部中央。BB は青/緑を抜く）
    for img, ents in user_memes.items():
        for idx, s0, e0, is_video, key in ents:
            chain = f"[{idx}:v]"
            f = []
            if is_video:
                f.append(f"setpts=PTS-STARTPTS+{s0:.3f}/TB")
            f.append("format=rgba")
            if key:
                f.append(f"chromakey={key}:0.28:0.08")
            f.append("scale='min(760,iw)':-1:flags=lanczos")
            f.append("scale=iw:'min(500,ih)':flags=lanczos")
            filters.append(chain + ",".join(f) + f"[u{idx}]")
            lab = nxt_label()
            filters.append(f"{cur}[u{idx}]overlay=x=(W-w)/2:y=110:enable='between(t,{s0:.2f},{e0:.2f})'{lab}")
            cur = lab
    # 差し込み画像
    for img, ivs in cut_ivs.items():
        if img not in cut_inputs:
            continue
        idx, folder = cut_inputs[img]
        if folder == "memes":
            x, y = "(W-w)/2", "140"
        elif preset["cutin_pos"] == "right":
            x, y = "W-w-70", "120"
        else:
            x, y = "(W-w)/2", "40"
        lab = nxt_label()
        filters.append(f"{cur}[{idx}:v]overlay=x={x}:y='{y}+4*sin(t*6)':eval=frame:enable='{enable_expr(ivs)}'{lab}")
        cur = lab
    # 字幕
    lab = nxt_label()
    filters.append(f"{cur}ass=subs.ass:fontsdir=fonts{lab}")
    cur = lab
    # 顔アイコン（字幕の左端・話者側）
    for key, idx in face_inputs.items():
        evs = faces[key]
        ivs = [(e["start"], e["end"] + 0.25) for e in evs]
        x = "70" if key[0] == "m" else "W-70-w"
        lab = nxt_label()
        filters.append(f"[{idx}:v]scale=170:170[f{idx}]")
        filters.append(f"{cur}[f{idx}]overlay=x={x}:y=H-h-30:enable='{enable_expr(ivs)}'{lab}")
        cur = lab
    # 章カード
    for idx, s, e in card_inputs:
        lab = nxt_label()
        filters.append(f"{cur}[{idx}:v]overlay=x=0:y=0:enable='between(t,{s:.2f},{e:.2f})'{lab}")
        cur = lab
    # エンドカード
    lab = nxt_label()
    filters.append(f"{cur}[{end_idx}:v]overlay=x=0:y=0:enable='gte(t,{end_start:.2f})'[vout]")

    # ---- 音声
    # BGM の計画（sc["bgm"] がリスト・dict・"auto"・*.json、または章・行に bgm 系のキー）があるときだけ CC0 BGM を使う。
    # 無い台本は今まで通り（ゲームの BGM 1 曲を bgm_vol で流し、ゲーム音は game_vol 固定）
    use_plan = bgmlib.is_plan(sc)
    use_game_env = bgmlib.uses_game_vol(sc)
    a_voice = add_input(voice_wav)
    a_se = add_input(se_wav)
    if use_plan:
        bgm_wav = os.path.join(out_dir, "bgm.wav")
        secs, stings = bgmlib.render_bgm(sc, tl, os.path.join(ROOT, "scripts"), bgm_wav)
        for s in secs:
            log(f"  BGM {s['t0']:7.1f} → {s['t1']:7.1f}  {s['track']}  vol {s['vol']:.2f}")
        for t, sid in stings:
            log(f"  一発 {t:7.1f}  {sid}")
        for w in bgmlib.check_plan(secs, tl["total"]):
            log("  BGM の注意:", w)
        a_bgm = add_input(bgm_wav)
    else:
        bgm = os.path.join(REPO, BGM_FILES.get(sc.get("bgm", "通常営業"), BGM_FILES["通常営業"]))
        a_bgm = add_input(bgm)
    if use_game_env:
        game_wav = os.path.join(out_dir, "game_audio.wav")
        bgmlib.apply_game_vol(sc, tl, preset["game_vol"], base, game_wav)
        a_game = add_input(game_wav)
        filters.append(f"[{a_game}:a]aresample=48000,asetpts=PTS-STARTPTS[ga]")
    else:
        filters.append(f"[0:a]aresample=48000:async=1:first_pts=0,volume={preset['game_vol']}[ga]")
    filters.append(f"[{a_voice}:a]aresample=48000,volume=1.0[va]")
    filters.append(f"[{a_se}:a]volume=0.55[sa]")
    if use_plan:
        filters.append(f"[{a_bgm}:a]aresample=48000,asetpts=PTS-STARTPTS[ba]")
    else:
        filters.append(f"[{a_bgm}:a]aloop=loop=-1:size=2e9,volume={preset['bgm_vol']},afade=t=out:st={tl['total']-4:.2f}:d=4[ba]")
    mix = ["[ga]", "[va]", "[sa]", "[ba]"]
    for idx, s0, e0 in meme_audio:
        ms = int(s0 * 1000)
        filters.append(f"[{idx}:a]atrim=0:{e0 - s0:.3f},asetpts=PTS-STARTPTS,aresample=48000,adelay={ms}|{ms},volume=0.8[ma{idx}]")
        mix.append(f"[ma{idx}]")
    # ミーム音（台本の snd）。無い id は一度だけ知らせて鳴らさない
    snd_missing = set()
    for e in lines:
        sid = e.get("snd")
        if not sid:
            continue
        sp = os.path.join(ASSETS, "sounds_meme", sid + ".mp3")
        if not os.path.exists(sp):
            if sid not in snd_missing:
                log("ミーム音が無いので鳴らさない（assets/sounds_meme）:", sid)
                snd_missing.add(sid)
            continue
        idx = add_input(sp)
        ms = int(max(0.0, e["start"] + e.get("snd_at", 0.0)) * 1000)
        filters.append(f"[{idx}:a]aresample=48000,asetpts=PTS-STARTPTS,adelay={ms}:all=1,volume={e.get('snd_vol', 0.7):.2f}[sn{idx}]")
        mix.append(f"[sn{idx}]")
    filters.append("".join(mix) + f"amix=inputs={len(mix)}:duration=first:normalize=0,alimiter=limit=0.95,asetpts=N/SR/TB[aout]")

    script_path = os.path.join(out_dir, "filters.txt")
    with open(script_path, "w", encoding="utf-8") as f:
        f.write(";\n".join(filters))
    # fonts は cwd からの相対で参照する
    fonts_link = os.path.join(out_dir, "fonts")
    if not os.path.exists(fonts_link):
        shutil.copytree(os.path.join(ASSETS, "fonts"), fonts_link)

    out_mp4 = os.path.join(out_dir, name + ("_preview" if preview else "") + ".mp4")
    cmd = ["ffmpeg", "-y", "-v", "error", "-stats"]
    for ent in inputs:
        opts, p = ent if isinstance(ent, tuple) else ([], ent)
        cmd += opts + ["-i", p]
    cmd += ["-filter_complex_script", "filters.txt", "-map", "[vout]", "-map", "[aout]",
            "-t", f"{min(tl['total'], float(os.environ.get('JIKKYO_MAXT', '1e9'))):.2f}", "-r", str(FPS),
            "-c:v", "libx264", "-preset", "veryfast" if preview else "medium", "-crf", "23" if preview else "19",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", out_mp4]
    log("ffmpeg 合成 …")
    r = subprocess.run(cmd, cwd=out_dir)
    if r.returncode != 0:
        raise SystemExit("ffmpeg failed")
    log("→", out_mp4)
    # 投稿用のテキスト
    with open(os.path.join(out_dir, "youtube.txt"), "w", encoding="utf-8") as f:
        y = sc["youtube"]
        desc = y["description"]
        if os.path.isdir(os.path.join(ASSETS, "tachie")) and "坂本アヒル" not in desc:
            desc += "\n立ち絵：坂本アヒル様"
        f.write(y["title"] + "\n\n" + desc + "\n\nタグ: " + ", ".join(y["tags"]) + "\n")
    return out_mp4


# ---------------------------------------------------------------- thumbnail
ACCENT = {"gold": (255, 200, 40), "red": (240, 50, 50), "purple": (180, 80, 255), "green": (80, 220, 100)}


def grab_frame(footage, tag, out_path, offset=2.0):
    t = footage["tags"].get(tag) or footage["tags"]["shop"]
    src = footage["sources"][t["src"]]
    run(["ffmpeg", "-y", "-v", "error", "-ss", str(t.get("start", 0) + offset), "-i", src, "-frames:v", "1", out_path])


def cmd_thumb(name):
    sc = load_script(name)
    th = sc["thumbnail"]
    footage = load_footage(name)
    out_dir = os.path.join(OUT, name); os.makedirs(out_dir, exist_ok=True)
    TW, TH = 1280, 720
    # 背景
    bg_kind = th.get("bg", "title")
    if bg_kind == "title":
        bg = Image.open(os.path.join(REPO, "Assets/BackGround/タイトル画面.png")).convert("RGB")
    elif bg_kind == "maou":
        bg = Image.new("RGB", (TW, TH), (30, 10, 50))
        d = ImageDraw.Draw(bg)
        for i in range(TH):
            d.line([(0, i), (TW, i)], fill=(30 + i // 12, 10, 50 + i // 8))
        maou = key_white(Image.open(os.path.join(REPO, "Assets/Character/キャラ立ち絵/魔王.png")))
        bb = maou.getbbox(); maou = maou.crop(bb)
        r = 900 / maou.height; maou = maou.resize((int(maou.width * r), 900), Image.LANCZOS)
        bg.paste(maou, (TW - maou.width + 80, -40), maou)
    else:
        p = os.path.join(out_dir, "thumb_bg.jpg"); grab_frame(footage, bg_kind, p)
        bg = Image.open(p).convert("RGB")
    bg = bg.resize((TW, TH), Image.LANCZOS)
    # 左を少し暗くして文字を読ませる
    grad = Image.new("L", (TW, TH), 0)
    gd = ImageDraw.Draw(grad)
    for x in range(TW):
        gd.line([(x, 0), (x, TH)], fill=int(max(0, 150 - x * 150 / 820)))
    bg = Image.composite(Image.new("RGB", (TW, TH), (0, 0, 0)), bg, grad)
    bg = bg.convert("RGBA")
    # キャラ（右側）: VOICEVOX の立ち絵を上半身で
    chars = th.get("chars", ["z"])
    x_right = TW + 10
    for i, code in enumerate(reversed(chars)):
        tp = os.path.join(ASSETS, "tachie", f"{code}_thumb_closed.png")
        if os.path.exists(tp):
            im = Image.open(tp).convert("RGBA")
            im = im.crop(im.getbbox())
            im = im.crop((0, 0, im.width, int(im.height * 0.55)))
        else:
            im = Image.open(os.path.join(ASSETS, "chars", f"{code}_portrait.png")).convert("RGBA")
            im = im.crop((0, 0, im.width, int(im.height * 0.62)))
        r = 560 / im.height
        im = im.resize((int(im.width * r), 560), Image.LANCZOS)
        if bg_kind == "maou":
            im = im.resize((int(im.width * 0.78), int(im.height * 0.78)), Image.LANCZOS)
        shadow = Image.new("RGBA", im.size, (0, 0, 0, 0)); shadow.paste((0, 0, 0, 160), (0, 0), im.split()[3])
        shadow = shadow.filter(ImageFilter.GaussianBlur(8))
        x = x_right - im.width - (0 if i == 0 else 40)
        y = TH - im.height + 20
        bg.alpha_composite(shadow, (x + 10, y + 10)); bg.alpha_composite(im, (x, y))
        x_right = x + 120
    # 文字（左・2行・1フォント・3色）
    d = ImageDraw.Draw(bg)
    accent = ACCENT.get(th.get("accent", "gold"), ACCENT["gold"])
    fnt = font("meme", int(th.get("size", 150)))
    y = int(th.get("text_y", 80))
    for i, line in enumerate((th["line1"], th["line2"])):
        fill = (255, 255, 255) if i == 0 else accent
        draw_text_outline(d, (56, y), line, fnt, fill, (20, 20, 20), 12)
        y += 190
    # 下の帯にゲーム名
    d.rectangle([0, TH - 70, TW, TH], fill=(0, 0, 0, 170))
    draw_text_outline(d, (24, TH - 35), "トコのトムトムランド", font("main", 40), (255, 255, 255), (0, 0, 0), 2, anchor="lm")
    out = os.path.join(out_dir, "thumb.png")
    bg.convert("RGB").save(out, quality=95)
    log("→", out)
    return out


# ---------------------------------------------------------------- BGM の計画の案
def cmd_bgmplan(name, write=False):
    import bgm as bgmlib
    sc = load_script(name)
    footage = load_footage(name)
    preset = STYLE_PRESET[sc["style"]]
    outcomes = footage.get("outcomes", {})
    # 声のキャッシュが全部あれば本当の長さで、無ければ文字数から見積もる
    synth = all(os.path.exists(voice_path(name, line_text(l, outcomes), STYLES[l["s"]][l.get("e", "normal")],
                                          float(l.get("speed", preset["speed"])), preset["intonation"]))
                for ch in sc["chapters"] for l in ch["lines"] if "t" in l)
    tl = build_timeline(name, sc, footage, synth=synth)
    plan = bgmlib.propose_plan(sc, tl)
    log(f"{name}: 尺 {tl['total']/60:.1f} 分（{'声の長さ' if synth else '文字数から推定'}）")
    for p in plan:
        if "sting" in p:
            log(f"  {p['_t']:7.1f}  一発 {p['sting']:22} ← {p.get('_why', '')}")
        else:
            log(f"  {p['_t']:7.1f}  {p['_mood']:8} {p['track']:22} at={p['at']}")
    out = {"plan": plan, "game_vol": bgmlib.DEFAULT_GAME_VOL}
    txt = json.dumps(out, ensure_ascii=False, indent=1)
    if write:
        p = os.path.join(ROOT, "scripts", name + ".bgm.json")
        with open(p, "w", encoding="utf-8") as f:
            f.write(txt)
        log("→", p, f'（台本に "bgm": "{name}.bgm.json" と書くと使われる）')
    else:
        print(txt)
    return out


# ---------------------------------------------------------------- main
def main():
    if len(sys.argv) < 2:
        print(__doc__); return
    cmd = sys.argv[1]
    names = sys.argv[2:] or ["A_tantan", "B_kaisetsu", "C_play"]
    preview = "--preview" in names
    write = "--write" in names
    bgm_plan = next((n.split("=", 1)[1] for n in names if n.startswith("--bgm-plan=")), None)
    names = [n for n in names if not n.startswith("--")]
    if cmd == "assets":
        cmd_assets(); return
    for nm in names:
        if cmd == "plan":
            cmd_plan(nm)
        elif cmd == "voice":
            cmd_voice(nm)
        elif cmd == "render":
            cmd_render(nm, preview, bgm_plan)
        elif cmd == "bgmplan":
            cmd_bgmplan(nm, write)
        elif cmd == "thumb":
            cmd_thumb(nm)
        elif cmd == "all":
            cmd_voice(nm); cmd_render(nm, preview, bgm_plan); cmd_thumb(nm)
        else:
            print(__doc__); return


if __name__ == "__main__":
    main()
