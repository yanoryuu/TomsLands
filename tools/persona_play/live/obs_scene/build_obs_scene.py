# -*- coding: utf-8 -*-
"""
縦配信（1080x1920）用の OBS シーンコレクションと背景フレームを作る。

  py -I -X utf8 tools/persona_play/live/obs_scene/build_obs_scene.py            # JSON と frame.png を作る
  py -I -X utf8 tools/persona_play/live/obs_scene/build_obs_scene.py --preview  # preview_mock.png も作る

出力（このフォルダ）:
  トコトム縦配信.json  … OBS の「シーンコレクション → インポート」で読むファイル
  frame.png            … 背景・枠（PIL で図形だけで描く。AI 画像は使わない）
  preview_mock.png     … 見た目の確認用（ゲーム画面・立ち絵・字幕を PIL で重ねたもの）

レイアウトの数値は下の定数にまとめてある。Unity の Game ビューの位置が変わったら CROP_WINDOW / CROP を直す
（OBS 上で Alt を押しながら赤枠をドラッグしてもよい）。
"""
import argparse
import json
import os
import uuid

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
LIVE = os.path.dirname(HERE)
OBS_DIR = os.path.join(LIVE, "obs")
REPO = os.path.abspath(os.path.join(LIVE, "..", "..", ".."))
COLLECTION = "トコトム縦配信"
SCENE = "縦配信"

W, H = 1080, 1920

# ---- レイアウト（px、キャンバス 1080x1920 基準）
TITLE_BAND = (0, 0, W, 140)
DAY_BADGE = (812, 28, 1052, 112)            # 日付の札（day.txt は札の中央）
GAME = (0, 150, W, 608)                       # x, y, 幅, 高さ（16:9 を横いっぱい）
PANEL = (28, 786, 1052, 1126)                # 吹き出し
SPEAKER_POS = (68, 806)                       # 話している人の名前（speaker.txt）
LINE_POS = (68, 862)                          # 一言（metan_line.txt / zundamon_line.txt を同じ場所に）
LINE_BOX = (944, 250)                         # 一言の折り返し枠
PORTRAIT_BOTTOM = 1872                        # 立ち絵の足元
METAN_LEFT = 44                               # めたん: 左下（反転済みで右を向く）
ZUNDA_RIGHT = W - 44                          # ずんだもん: 右下
CARD = (402, 1556, 626, 1790)                 # 二人の間の小さな札（Steam 検索の案内）
CREDIT_BAND = (0, 1876, W, H)
CREDIT_TEXT = "VOICEVOX:ずんだもん　VOICEVOX:四国めたん　立ち絵：坂本アヒル様"

# ---- Unity の Game ビューを切り抜く値（プライマリモニター 1920x1080 の画面キャプチャ基準）
#  2026-10-29 に unity eval で GameView.position=(8,484,862,494)・targetInView=(10.56,0,840.89,473)（841x473）を読み、
#  OBS の画面キャプチャの実画像で位置を合わせた（Unity の y は画面より 24px 上にずれていた）。
#  ゲームの描画は画面座標 x 19〜859、y 529〜1001。
#  既定は画面キャプチャ（プライマリモニター）。Game ビューの上に別のウィンドウや通知が重なるとそれも映る。
#  ウィンドウキャプチャ（WGC）は重なりの影響を受けないが、Unity のタイトルはシーンごとに変わり
#  （VillageScene / TomsShop）、OBS の起動時にタイトルが外れると、浮いている Project ウィンドウを拾って
#  真っ黒になった（2026-10-29 に確認）。浮いている Unity のウィンドウを全部ドッキングしたら、こちらに切り替えてよい。
CROP = {"left": 6, "top": 528, "right": 1057, "bottom": 70}             # 2 枚目のモニター（Redmi、1920x1080）基準。2026-10-29 23:50 に測り直した
CROP_WINDOW = {"left": 11, "top": 478, "right": 1052, "bottom": 22}     # クライアント領域 1904x973（画面の 8,51 から）基準
UNITY_WINDOW = "wt-04 - TomsShop - Windows, Mac, Linux - Unity 6 (6000.0.32f1) <DX11>:UnityContainerWndClass:Unity.exe"
MONITOR_ID = r"\\?\DISPLAY#ACR0A6C#5&39dfcd35&0&UID4358#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"  # KG251Q（プライマリ）

FONT_FACE = "BIZ UDPGothic"
FONT_B = r"C:\Windows\Fonts\BIZ-UDGothicB.ttc"  # index 1 = BIZ UDPGothic Bold

# ---- 色（暖かい木と羊皮紙。ゲームの UI に寄せる）
WOOD_D = (43, 26, 17)
WOOD = (74, 45, 28)
WOOD_L = (107, 66, 39)
GOLD = (222, 172, 82)
GOLD_D = (160, 112, 46)
PARCH = (248, 236, 210)
PARCH_D = (231, 212, 176)
INK = (58, 34, 20)
ZUNDA_INK = (34, 84, 30)
METAN_INK = (112, 30, 86)


def font(size):
    return ImageFont.truetype(FONT_B, size, index=1)


def obs_color(rgb, a=255):
    """OBS の色（0xAABBGGRR）。"""
    r, g, b = rgb
    return (a << 24) | (b << 16) | (g << 8) | r


# ---------------------------------------------------------------- frame.png
def vgrad(size, top, bottom):
    w, h = size
    im = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / max(1, h - 1)
        im.putpixel((0, y), tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return im.resize((w, h))


def draw_frame():
    im = vgrad((W, H), (92, 56, 34), (38, 23, 15)).convert("RGBA")
    d = ImageDraw.Draw(im)
    # うっすら菱形の柄（店の床・壁紙っぽく）
    pat = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    pd = ImageDraw.Draw(pat)
    step = 72
    for y in range(-step, H + step, step):
        for x in range(-step, W + step, step):
            ox = x + (step // 2 if (y // step) % 2 else 0)
            pd.polygon([(ox, y - 14), (ox + 14, y), (ox, y + 14), (ox - 14, y)], fill=(255, 220, 160, 14))
    im = Image.alpha_composite(im, pat)
    d = ImageDraw.Draw(im)

    # 上の帯（タイトル）
    x0, y0, x1, y1 = TITLE_BAND
    d.rectangle(TITLE_BAND, fill=WOOD_D)
    d.rectangle((0, y1 - 8, W, y1 - 5), fill=GOLD)
    d.rectangle((0, y1 - 5, W, y1), fill=GOLD_D)
    title = "トコのトムトムランド"
    f = font(62)
    d.text((40, 18), title, font=f, fill=PARCH, stroke_width=4, stroke_fill=(20, 10, 5))
    d.text((44, 92), "ずんだもんと四国めたんが店を回す AI 生配信", font=font(28), fill=GOLD)

    # 日付の札
    bx0, by0, bx1, by1 = DAY_BADGE
    d.rounded_rectangle((bx0 - 4, by0 - 4, bx1 + 4, by1 + 4), radius=22, fill=GOLD_D)
    d.rounded_rectangle(DAY_BADGE, radius=18, fill=(120, 40, 26))
    d.rounded_rectangle((bx0 + 6, by0 + 6, bx1 - 6, by1 - 6), radius=14, outline=GOLD, width=2)

    # ゲーム画面の枠（ゲームの下に黒、上下に金の線）
    gx, gy, gw, gh = GAME
    d.rectangle((0, gy - 4, W, gy + gh + 4), fill=(10, 6, 4))
    d.rectangle((0, gy + gh + 4, W, gy + gh + 8), fill=GOLD)
    d.rectangle((0, gy + gh + 8, W, gy + gh + 11), fill=GOLD_D)

    # 吹き出し（影 → 枠 → 羊皮紙 → 金の内線）
    px0, py0, px1, py1 = PANEL
    sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((px0 + 4, py0 + 10, px1 + 4, py1 + 14), radius=34, fill=(0, 0, 0, 120))
    sh = sh.filter(ImageFilter.GaussianBlur(8))
    im = Image.alpha_composite(im, sh)
    d = ImageDraw.Draw(im)
    # しっぽ（左はめたん、右はずんだもんの方へ）
    for tx, sgn in ((250, -1), (830, 1)):
        d.polygon([(tx - 40, py1 - 6), (tx + 40, py1 - 6), (tx + sgn * 30, py1 + 54)], fill=WOOD_L)
        d.polygon([(tx - 30, py1 - 12), (tx + 30, py1 - 12), (tx + sgn * 24, py1 + 40)], fill=PARCH)
    d.rounded_rectangle(PANEL, radius=32, fill=PARCH, outline=WOOD_L, width=8)
    d.rounded_rectangle((px0 + 14, py0 + 14, px1 - 14, py1 - 14), radius=22, outline=GOLD, width=2)
    # 名前の下の区切り線
    d.line((px0 + 34, LINE_POS[1] - 10, px1 - 34, LINE_POS[1] - 10), fill=PARCH_D, width=3)

    # 二人の間の札（Steam で検索の案内）
    cx0, cy0, cx1, cy1 = CARD
    d.rounded_rectangle((cx0 - 4, cy0 - 4, cx1 + 4, cy1 + 4), radius=20, fill=GOLD_D)
    d.rounded_rectangle(CARD, radius=16, fill=WOOD_D)
    cw = cx1 - cx0
    yy = cy0 + 20
    for txt, size, col in (("Steam で検索", 30, GOLD), ("トコの", 40, PARCH), ("トムトム", 40, PARCH),
                           ("ランド", 40, PARCH)):
        f = font(size)
        tw = d.textlength(txt, font=f)
        d.text((cx0 + (cw - tw) / 2, yy), txt, font=f, fill=col)
        yy += size + (16 if size >= 30 else 8)

    # 下の帯（クレジットの台）
    d.rectangle(CREDIT_BAND, fill=(18, 11, 7))
    d.rectangle((0, CREDIT_BAND[1], W, CREDIT_BAND[1] + 3), fill=GOLD_D)
    return im.convert("RGB")


# ---------------------------------------------------------------- OBS の JSON
def uid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "tokotom-vertical/" + name))


def rel_pos(x, y):
    """OBS 31 の相対座標（キャンバスの高さの半分を 1 とし、中央が 0）。"""
    return {"x": (x - W / 2) / (H / 2), "y": (y - H / 2) / (H / 2)}


def source(name, sid, settings, vid=None, mixers=0, muted=False, filters=None):
    s = {
        "prev_ver": 520093698,  # OBS 31.0.2
        "name": name, "uuid": uid(name), "id": sid, "versioned_id": vid or sid,
        "settings": settings, "mixers": mixers, "sync": 0, "flags": 0, "volume": 1.0, "balance": 0.5,
        "enabled": True, "muted": muted, "push-to-mute": False, "push-to-mute-delay": 0,
        "push-to-talk": False, "push-to-talk-delay": 0, "hotkeys": {}, "deinterlace_mode": 0,
        "deinterlace_field_order": 0, "monitoring_type": 0, "private_settings": {},
    }
    if filters:
        s["filters"] = filters
    return s


def item(name, iid, x, y, align=5, scale=1.0, bounds=None, crop=None, locked=True, visible=True):
    bt, bw, bh = (2, bounds[0], bounds[1]) if bounds else (0, 0.0, 0.0)
    c = crop or {}
    return {
        "name": name, "source_uuid": uid(name), "visible": visible, "locked": locked, "rot": 0.0,
        "scale_ref": {"x": float(W), "y": float(H)},
        "align": align, "bounds_type": bt, "bounds_align": 0, "bounds_crop": False,
        "crop_left": c.get("left", 0), "crop_top": c.get("top", 0),
        "crop_right": c.get("right", 0), "crop_bottom": c.get("bottom", 0),
        "id": iid, "group_item_backup": False,
        "pos": {"x": float(x), "y": float(y)}, "pos_rel": rel_pos(x, y),
        "scale": {"x": scale, "y": scale}, "scale_rel": {"x": scale, "y": scale},
        "bounds": {"x": float(bw), "y": float(bh)},
        "bounds_rel": {"x": bw / (H / 2), "y": bh / (H / 2)},
        "scale_filter": "disable", "blend_method": "default", "blend_type": "normal",
        "show_transition": {"duration": 0}, "hide_transition": {"duration": 0}, "private_settings": {},
    }


def text_settings(size, color, *, file=None, text="", outline=None, extents=None, align="left", valign="top"):
    s = {
        "font": {"face": FONT_FACE, "flags": 1, "size": size, "style": "Bold"},
        "color": obs_color(color), "opacity": 100, "antialiasing": True,
        "align": align, "valign": valign,
    }
    if file:
        s.update({"read_from_file": True, "file": file})
    else:
        s.update({"read_from_file": False, "text": text})
    if outline:
        s.update({"outline": True, "outline_size": outline[0], "outline_color": obs_color(outline[1]), "outline_opacity": 100})
    if extents:
        s.update({"extents": True, "extents_cx": extents[0], "extents_cy": extents[1], "extents_wrap": True})
    return s


def build_collection():
    p = lambda n: os.path.join(OBS_DIR, n)  # noqa: E731
    frame = os.path.join(HERE, "frame.png")
    TXT = "text_gdiplus"
    TXT_V = "text_gdiplus_v2"
    srcs = [
        source("背景フレーム", "image_source", {"file": frame, "unload": False}),
        source("Unity 画面キャプチャ", "monitor_capture",
               {"monitor_id": MONITOR_ID, "method": 0, "capture_cursor": False, "force_sdr": False}),
        source("Unity ウィンドウ（予備）", "window_capture",
               {"window": UNITY_WINDOW, "method": 2, "priority": 2, "cursor": False,
                "client_area": True, "compatibility": False, "force_sdr": False}),
        source("日付", TXT, text_settings(50, (255, 240, 205), file=p("day.txt"), outline=(4, (60, 16, 8)),
                                        align="center", valign="center"), TXT_V),
        source("話している人", TXT, text_settings(40, INK, file=p("speaker.txt")), TXT_V),
        source("めたんの一言", TXT, text_settings(60, METAN_INK, file=p("metan_line.txt"), extents=LINE_BOX), TXT_V),
        source("ずんだもんの一言", TXT, text_settings(60, ZUNDA_INK, file=p("zundamon_line.txt"), extents=LINE_BOX), TXT_V),
        source("めたん立ち絵", "image_source", {"file": p("metan.png"), "unload": False}),
        source("ずんだもん立ち絵", "image_source", {"file": p("zundamon.png"), "unload": False}),
        source("クレジット", TXT, text_settings(26, (240, 228, 205), text=CREDIT_TEXT, align="center"), TXT_V),
    ]
    gx, gy, gw, gh = GAME
    bx0, by0, bx1, by1 = DAY_BADGE
    rows = [
        ("背景フレーム", dict(x=0, y=0)),
        ("Unity ウィンドウ（予備）", dict(x=gx, y=gy, bounds=(float(gw), float(gh)), crop=CROP_WINDOW, visible=False)),
        ("Unity 画面キャプチャ", dict(x=gx, y=gy, bounds=(float(gw), float(gh)), crop=CROP)),
        ("日付", dict(x=(bx0 + bx1) / 2, y=(by0 + by1) / 2 + 2, align=0)),
        ("話している人", dict(x=SPEAKER_POS[0], y=SPEAKER_POS[1])),
        ("めたんの一言", dict(x=LINE_POS[0], y=LINE_POS[1])),
        ("ずんだもんの一言", dict(x=LINE_POS[0], y=LINE_POS[1])),
        ("めたん立ち絵", dict(x=METAN_LEFT, y=PORTRAIT_BOTTOM, align=8 | 1)),       # 左下を基準
        ("ずんだもん立ち絵", dict(x=ZUNDA_RIGHT, y=PORTRAIT_BOTTOM, align=8 | 2)),  # 右下を基準
        ("クレジット", dict(x=W / 2, y=(CREDIT_BAND[1] + CREDIT_BAND[3]) / 2 + 1, align=0)),
    ]
    items = [item(n, i + 1, **kw) for i, (n, kw) in enumerate(rows)]
    scene = source(SCENE, "scene", {"id_counter": len(items), "custom_size": False, "items": items})
    desktop = source("デスクトップ音声", "wasapi_output_capture", {"device_id": "default"}, mixers=255)
    return {
        "DesktopAudioDevice1": desktop,
        "current_scene": SCENE, "current_program_scene": SCENE,
        "scene_order": [{"name": SCENE}],
        "name": COLLECTION,
        "sources": [scene] + srcs,
        "groups": [],
        "quick_transitions": [
            {"name": "カット", "duration": 300, "hotkeys": [], "id": 1, "fade_to_black": False},
            {"name": "フェード", "duration": 300, "hotkeys": [], "id": 2, "fade_to_black": False},
        ],
        "transitions": [], "saved_projectors": [],
        "current_transition": "フェード", "transition_duration": 300,
        "preview_locked": False, "scaling_enabled": False, "scaling_level": 0,
        "scaling_off_x": 0.0, "scaling_off_y": 0.0,
        "modules": {"scripts-tool": [], "auto-scene-switcher": {"interval": 300, "non_matching_scene": "",
                                                                "switch_if_not_matching": False, "active": False,
                                                                "switches": []}},
        "resolution": {"x": W, "y": H}, "version": 2, "migration_resolution": {"x": W, "y": H},
    }


# ---------------------------------------------------------------- 確認用の合成
def wrap(d, text, f, width):
    lines, cur = [], ""
    for ch in text:
        if d.textlength(cur + ch, font=f) > width:
            lines.append(cur)
            cur = ch
        else:
            cur += ch
    return lines + ([cur] if cur else [])


def preview(frame, game_png, out):
    im = frame.convert("RGBA")
    gx, gy, gw, gh = GAME
    if game_png and os.path.exists(game_png):
        g = Image.open(game_png).convert("RGB").resize((gw, gh), Image.LANCZOS)
        im.paste(g, (gx, gy))
    d = ImageDraw.Draw(im)
    bx0, by0, bx1, by1 = DAY_BADGE
    f = font(50)
    t = "13日目"
    tw = d.textlength(t, font=f)
    d.text(((bx0 + bx1 - tw) / 2, (by0 + by1) / 2 - 27), t, font=f, fill=(255, 240, 205), stroke_width=4, stroke_fill=(60, 16, 8))
    d.text(SPEAKER_POS, "ずんだもん", font=font(40), fill=INK)
    f = font(60)
    y = LINE_POS[1]
    for ln in wrap(d, "中型の貯金ゴーレムを強化するのだ。寿命も新品に戻るのだ！", f, LINE_BOX[0]):
        d.text((LINE_POS[0], y), ln, font=f, fill=ZUNDA_INK)
        y += 72
    for name, dim, left in (("metan.png", True, True), ("zundamon.png", False, False)):
        pp = os.path.join(OBS_DIR, name)
        if not os.path.exists(pp):
            continue
        pi = Image.open(pp).convert("RGBA")
        if dim:
            a = pi.getchannel("A")
            pi = ImageEnhance.Brightness(pi.convert("RGB")).enhance(0.5).convert("RGBA")
            pi.putalpha(a)
        x = METAN_LEFT if left else ZUNDA_RIGHT - pi.width
        im.alpha_composite(pi, (x, PORTRAIT_BOTTOM - pi.height))
    f = font(26)
    tw = d.textlength(CREDIT_TEXT, font=f)
    d.text(((W - tw) / 2, CREDIT_BAND[1] + 7), CREDIT_TEXT, font=f, fill=(240, 228, 205))
    im.convert("RGB").save(out, optimize=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--game", default=r"D:\WorkTree\TomsLands\_scratch_wt04\obs_scene_work\game.png",
                    help="確認用の合成に使うゲーム画面（1920x1080 の PNG）")
    a = ap.parse_args()
    frame = draw_frame()
    frame.save(os.path.join(HERE, "frame.png"), optimize=True)
    col = build_collection()
    with open(os.path.join(HERE, COLLECTION + ".json"), "w", encoding="utf-8") as f:
        json.dump(col, f, ensure_ascii=False, indent=4)
    print("書いた:", os.path.join(HERE, "frame.png"), os.path.join(HERE, COLLECTION + ".json"))
    if a.preview:
        out = os.path.join(HERE, "preview_mock.png")
        preview(frame, a.game, out)
        print("書いた:", out)


if __name__ == "__main__":
    main()
