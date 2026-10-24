"""ビル・小屋・家の色を落ち着かせる(彩度を下げ、少し暗く、暖色寄りに)。ロッジは専用パレットなので対象外。
使い方: python3 apply_tone.py <入力ディレクトリ> <出力ディレクトリ>
"""
import os, sys, glob
import numpy as np
from PIL import Image

SAT, VAL, WARM = 0.74, 0.92, (5, 1, -6)


def tone(a):
    out = a.copy()
    rgb = a[:, :, :3].astype(float)
    lum = (rgb[:, :, 0] * 0.3 + rgb[:, :, 1] * 0.59 + rgb[:, :, 2] * 0.11)[:, :, None]
    c = lum + (rgb - lum) * SAT
    c = c * VAL + np.array(WARM, float)
    out[:, :, :3] = np.clip(c, 0, 255).astype(np.uint8)
    return out


def main(src, dst):
    os.makedirs(dst, exist_ok=True)
    for f in sorted(glob.glob(f"{src}/building_*.png")):
        if "lodge" in f:
            continue
        a = np.array(Image.open(f).convert("RGBA"))
        Image.fromarray(tone(a), "RGBA").save(f"{dst}/{os.path.basename(f)}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
