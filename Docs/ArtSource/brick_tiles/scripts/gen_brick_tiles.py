"""黒いレンガ壁タイル生成 (32x32, 継ぎ目なし)。
レンガ 14x6 px + 目地 2px (横16px周期 / 縦8px周期)、段ごとに半分(8px)ずらす。
32 = 16x2 = 8x4 なので、上下左右どの向きに並べても周期がそろう。
使い方: python3 gen_brick_tiles.py [出力ディレクトリ]
"""
import os
import sys
import numpy as np
from PIL import Image

T = 32
BW, BH = 14, 6          # レンガの大きさ
PX, PY = 16, 8          # 周期(レンガ+目地)
OUT = sys.argv[1] if len(sys.argv) > 1 else "brick_tiles"

MORTAR = (0x08, 0x08, 0x0A)
# レンガの面の色(暗い順に数種類。ブロックごとにランダムに選ぶ)
BASES = [(0x1B, 0x1C, 0x1F), (0x21, 0x23, 0x27), (0x17, 0x18, 0x1B), (0x25, 0x27, 0x2B)]
HI_TOP = (0x47, 0x4F, 0x54)     # 上辺のハイライト(左上光源)
HI_LEFT = (0x37, 0x3E, 0x43)    # 左辺のハイライト
SHADE = (0x11, 0x12, 0x14)      # 右・下辺の影


def add(c, d):
    return tuple(int(min(255, max(0, v + d))) for v in c)


def brick(base, rng):
    """14x6 のレンガ1個分の画素を返す。"""
    b = np.zeros((BH, BW, 3), np.uint8)
    for y in range(BH):
        for x in range(BW):
            c = add(base, 4) if y <= 2 else base  # 上半分をわずかに明るく
            if y == 0 and x < BW - 1:
                c = HI_TOP
            elif x == 0 and y >= 1:
                c = HI_LEFT
            elif y == BH - 1 or x == BW - 1:
                c = SHADE
            elif rng.random() < 0.06:
                c = add(base, int(rng.choice([-3, 3])))  # ごく弱いざらつき
            b[y, x] = c
    # 右下の角は一段暗く(参考画像の角の落ち)
    b[BH - 1, BW - 1] = add(SHADE, -3)
    return b


def make_tile(seed):
    rng = np.random.default_rng(seed)
    img = np.zeros((T, T, 3), np.uint8)
    img[:] = MORTAR
    rows = T // PY            # 4
    per_row = T // PX         # 2
    last = -1
    for r in range(rows):
        off = (PX // 2) * (r % 2)
        for k in range(per_row):
            # 直前と同じ色が続きすぎないようにする
            idx = int(rng.integers(len(BASES)))
            while idx == last:
                idx = int(rng.integers(len(BASES)))
            last = idx
            b = brick(BASES[idx], rng)
            x0, y0 = off + k * PX, r * PY
            for y in range(BH):
                for x in range(BW):
                    img[(y0 + y) % T, (x0 + x) % T] = b[y, x]  # 端は反対側へ回り込む
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    seeds = [3, 8, 15, 21]
    tiles = []
    for i, s in enumerate(seeds, 1):
        t = make_tile(s)
        Image.fromarray(t, "RGB").save(f"{OUT}/tile_wall_{i:02d}.png")
        tiles.append(t)
    # 依頼書どおりのシート tile_wall.png (4列x1行 = 128x32)
    Image.fromarray(np.concatenate(tiles, axis=1), "RGB").save(f"{OUT}/tile_wall.png")

    # 確認用: 混ぜ敷き 8x6 マス(3倍)
    rng = np.random.default_rng(1)
    cols, rows, sc = 8, 6, 3
    sheet = np.zeros((rows * T, cols * T, 3), np.uint8)
    for r in range(rows):
        for c in range(cols):
            sheet[r*T:(r+1)*T, c*T:(c+1)*T] = tiles[rng.integers(len(tiles))]
    Image.fromarray(sheet).resize((cols*T*sc, rows*T*sc), Image.NEAREST) \
        .save(f"{OUT}/preview_mixed.png")
    # 確認用: 0.5倍表示相当(16px/マス)を4倍で拡大
    small = Image.fromarray(sheet).resize((cols*T//2, rows*T//2), Image.NEAREST)
    small.resize((small.width * 4, small.height * 4), Image.NEAREST) \
        .save(f"{OUT}/preview_half_scale.png")


if __name__ == "__main__":
    main()
