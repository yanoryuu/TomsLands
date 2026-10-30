"""UI アニメの連番 PNG（Editor/UIMotionCapture.cs の出力）を GIF にする。

使い方（Python は `py -3 -I`）:
    py -3 -I tools/ui_motion_gif.py <連番の親フォルダ> <GIF の出力先フォルダ> [--width 640] [--fps 30] [--max-mb 3]

親フォルダの中の `<番号>_<名前>/f_000.png ...` を 1 本ずつ `<番号>_<名前>.gif` にする（ループ再生）。
3MB を超えたら、幅を 560 → 480 に下げ、それでも超えるならコマを 1 つおきにして作り直す。
"""
import argparse
import os
import sys

from PIL import Image


def load_frames(folder, width):
    names = sorted(n for n in os.listdir(folder) if n.lower().endswith(".png"))
    frames = []
    for n in names:
        im = Image.open(os.path.join(folder, n)).convert("RGB")
        if im.width != width:
            h = round(im.height * width / im.width)
            im = im.resize((width, h), Image.LANCZOS)
        frames.append(im)
    return frames


def to_gif(frames, path, fps):
    # 全コマ共通のパレット（コマごとに色がちらつかないように、何コマかを並べた絵から作る）
    sample_idx = sorted(set(int(i * (len(frames) - 1) / 7) for i in range(8)))
    w, h = frames[0].size
    sheet = Image.new("RGB", (w, h * len(sample_idx)))
    for k, i in enumerate(sample_idx):
        sheet.paste(frames[i], (0, h * k))
    palette = sheet.quantize(colors=255, method=Image.MEDIANCUT)
    quantized = [f.quantize(palette=palette, dither=Image.FLOYDSTEINBERG) for f in frames]
    duration = int(round(1000 / fps))
    quantized[0].save(path, save_all=True, append_images=quantized[1:], duration=duration, loop=0, optimize=True, disposal=1)
    return os.path.getsize(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--width", type=int, default=640)
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--max-mb", type=float, default=3.0)
    args = ap.parse_args()

    os.makedirs(args.dst, exist_ok=True)
    limit = args.max_mb * 1024 * 1024
    dirs = sorted(d for d in os.listdir(args.src) if os.path.isdir(os.path.join(args.src, d)))
    if not dirs:
        print("連番のフォルダが無い:", args.src)
        return 1
    for d in dirs:
        folder = os.path.join(args.src, d)
        out = os.path.join(args.dst, d + ".gif")
        size = 0
        for width, step in ((args.width, 1), (560, 1), (480, 1), (480, 2)):
            if width > args.width:
                continue
            frames = load_frames(folder, width)[::step]
            if not frames:
                break
            size = to_gif(frames, out, args.fps / step)
            if size <= limit:
                break
        print(f"{d}.gif  {size / 1024 / 1024:.2f} MB  ({width}px, {len(frames)} コマ)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
