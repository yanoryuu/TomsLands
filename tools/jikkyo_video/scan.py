# -*- coding: utf-8 -*-
"""
録画からコマを等間隔で抜いて、時刻入りのコンタクトシートにする（プレイ内容を読むため）。
  py -3 -X utf8 scan.py <mp4> [--step 10] [--cols 4] [--rows 3] [--out out/scan/<name>]
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.abspath(__file__))


def duration(path):
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                       capture_output=True, text=True)
    return float(r.stdout.strip())


def main():
    args = sys.argv[1:]
    src = args[0]
    def opt(name, default):
        return type(default)(args[args.index(name) + 1]) if name in args else default
    step = opt("--step", 10)
    cols = opt("--cols", 4)
    rows = opt("--rows", 3)
    name = os.path.splitext(os.path.basename(src))[0]
    out_dir = opt("--out", os.path.join(ROOT, "out", "scan", name))
    os.makedirs(out_dir, exist_ok=True)
    total = duration(src)
    cell_w, cell_h = 640, 360
    fnt = ImageFont.truetype(os.path.join(ROOT, "assets", "fonts", "ZenKakuGothicNew-Bold.ttf"), 28)
    t0 = opt("--from", 0); t1 = opt("--to", int(total))
    times = list(range(t0, min(int(total), t1), step))
    per = cols * rows
    frames_dir = os.path.join(out_dir, "frames")
    os.makedirs(frames_dir, exist_ok=True)
    for i in range(0, len(times), per):
        chunk = times[i:i + per]
        sheet = Image.new("RGB", (cell_w * cols, cell_h * rows), (20, 20, 20))
        d = ImageDraw.Draw(sheet)
        for j, t in enumerate(chunk):
            fp = os.path.join(frames_dir, f"{t:05d}.jpg")
            if not os.path.exists(fp):
                subprocess.run(["ffmpeg", "-y", "-v", "error", "-ss", str(t), "-i", src, "-frames:v", "1",
                                "-vf", f"scale={cell_w}:{cell_h}", "-q:v", "4", fp])
            if os.path.exists(fp):
                im = Image.open(fp)
                x, y = (j % cols) * cell_w, (j // cols) * cell_h
                sheet.paste(im, (x, y))
                label = f"{t // 60:02d}:{t % 60:02d}"
                d.rectangle([x, y, x + 110, y + 36], fill=(0, 0, 0))
                d.text((x + 8, y + 2), label, font=fnt, fill=(255, 255, 0))
        sp = os.path.join(out_dir, f"sheet_{chunk[0]:05d}.jpg")
        sheet.save(sp, quality=85)
        print("→", sp, f"{chunk[0]}s–{chunk[-1]}s")


if __name__ == "__main__":
    main()
