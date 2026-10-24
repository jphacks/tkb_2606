"""ビル1棟ぶんの部品(幅3マス=96px, 高さ32px)。横は分割せず、縦だけ屋上/中階/1階に分ける。
 building_roof.png    屋上   96x32
 building_mid_a.png   中階   96x32 (上下に何階でも繰り返せる)
 building_mid_b.png   中階   96x32 (窓の明かりだけ違う版。a と交互に積むと単調にならない)
 building_ground.png  1階    96x32 (入口・庇・ショーウィンドウ・植え込み・歩道)
使い方: python3 gen_building_wide.py [出力ディレクトリ]
"""
import os
import sys
import numpy as np
from PIL import Image

import gen_building as gb
from gen_building import (hx, OL, MORTAR, BRICKS, BR_HI, BR_LO, STONE, STONE_HI, STONE_LO, STONE_JT,
                          STONE_SP, CORN_TOP, CORN_FACE, CORN_LO, CORN_SH, GROUND_STONE, PAVE, PAVE_JT,
                          PAVE_HI, lighten, darken, window, bush, ac_unit, tank)

W, H = 96, 32
COL_W = 10
OUT = sys.argv[1] if len(sys.argv) > 1 else "building_wide"


class WTile(gb.Tile):
    def __init__(self):
        self.a = np.zeros((H, W, 4), np.uint8)

    def put(self, x, y, c):
        if 0 <= x < W and 0 <= y < H:
            self.a[y, x] = (*c, 255)


def brick(t, y_from=0, y_to=H):
    for y in range(y_from, y_to):
        c = y // 4
        st = 4 * (c % 2)
        for x in range(W):
            bx = (x + st) // 8
            base = BRICKS[(c * 7 + bx * 3) % 3]
            ly = y % 4
            if ly == 3 or (x + st) % 8 == 7:
                col = MORTAR
            elif ly == 0:
                col = lighten(base, BR_HI)
            elif ly == 2:
                col = darken(base, BR_LO)
            else:
                col = base
            t.put(x, y, col)


def stone_col(t, side, y0=0, y1=H, joints=(15,)):
    xs = list(range(0, COL_W)) if side == "l" else list(range(W - COL_W, W))
    for y in range(y0, y1):
        for x in xs:
            col = STONE
            if (x * 5 + y * 3) % 11 == 0:
                col = STONE_SP[0]
            elif (x * 7 + y * 2) % 13 == 0:
                col = STONE_SP[1]
            outer = x if side == "l" else W - 1 - x
            inner = COL_W - 1 - outer
            if outer <= 1:
                col = STONE_HI if side == "l" else STONE_LO
            if inner <= 1:
                col = STONE_LO if side == "l" else STONE_HI
            t.put(x, y, col)
    for jy in joints:
        for x in xs:
            t.put(x, jy, STONE_JT)
            t.put(x, (jy + 1) % H, STONE_HI)
    sx = COL_W if side == "l" else W - COL_W - 1
    for y in range(y0, y1):
        t.put(sx, y, hx(0x5A2A20))
    ox = 0 if side == "l" else W - 1
    for y in range(y0, y1):
        t.put(ox, y, OL)


def cornice(t, y0=10):
    for x in range(W):
        t.put(x, y0 - 1, OL)
        t.put(x, y0, CORN_TOP)
        t.put(x, y0 + 1, CORN_TOP)
        t.put(x, y0 + 2, CORN_FACE)
        t.put(x, y0 + 3, CORN_FACE)
        t.put(x, y0 + 4, CORN_LO)
        t.put(x, y0 + 5, CORN_SH)


def facade(t):
    for y in range(0, 24):
        for x in range(W):
            col = GROUND_STONE
            if (x * 5 + y * 3) % 11 == 0:
                col = STONE_SP[0]
            elif (x * 7 + y * 2) % 13 == 0:
                col = STONE_SP[1]
            t.put(x, y, col)
    for x in range(W):
        t.put(x, 0, STONE_HI)


def sidewalk(t):
    t.rect(0, 26, W, 6, PAVE)
    for x in range(W):
        t.put(x, 24, hx(0x8A8A8E))
        t.put(x, 25, hx(0x6C6C74))
        t.put(x, 26, PAVE_HI)
        t.put(x, 31, PAVE_JT)
    for x in range(7, W, 16):
        for y in range(27, 31):
            t.put(x, y, PAVE_JT)


WIN_X = (19, 43, 67)   # 窓の左端(3つ。壁の中央 x=48 に対して左右対称)


def mid(states):
    t = WTile(); brick(t)
    for x0, st in zip(WIN_X, states):
        window(t, x0=x0, lit=(st == "lit"), curtain=(st == "curtain"))
    stone_col(t, "l"); stone_col(t, "r")
    return t


def roof():
    t = WTile(); brick(t, 16, H); cornice(t)
    ac_unit(t, 16, 2)
    ac_unit(t, 31, 4, 9, 6)
    tank(t, 49, 1)
    ac_unit(t, 66, 2)
    for y in range(3, 9):   # 排気管
        t.put(84, y, hx(0x6E7078)); t.put(85, y, hx(0x9A9CA6))
    t.rect(83, 2, 4, 1, OL)
    for side in ("l", "r"):
        stone_col(t, side, 16, H, joints=(31,))
        xs = list(range(0, COL_W)) if side == "l" else list(range(W - COL_W, W))
        for y in range(4, 10):       # 柱頭
            for x in xs:
                t.put(x, y, STONE)
        for x in xs:
            t.put(x, 4, STONE_HI); t.put(x, 3, OL)
        for y in range(4, 10):
            t.put(xs[0], y, STONE_HI if side == "l" else STONE)
            t.put(xs[-1], y, STONE if side == "l" else STONE_LO)
        ox = 0 if side == "l" else W - 1
        for y in range(3, 10):
            t.put(ox, y, OL)
    return t


def shop_window(t, x0, w=22):
    t.frame(x0 - 1, 4, w + 2, 21, OL)
    t.frame(x0, 5, w, 19, hx(0xC4C4C8))
    t.frame(x0 + 1, 6, w - 2, 17, hx(0xA8A8AC))
    gw = w - 4
    t.rect(x0 + 2, 7, gw, 15, hx(0x2A1B12))
    for yy in range(15):
        for xx in range(gw):
            if 4 <= xx + yy <= 8 or 14 <= xx + yy <= 16:
                t.put(x0 + 2 + xx, 7 + yy, hx(0x6A4426))
    for y in range(7, 22):
        t.put(x0 + 2 + gw // 2, y, hx(0xA8A8AC))


def ground():
    t = WTile(); facade(t); sidewalk(t)
    shop_window(t, 14); shop_window(t, 60)
    # 中央のドア(両開き) x=40..55
    dx = 40
    t.frame(dx, 8, 16, 17, OL)
    t.rect(dx + 1, 9, 14, 15, hx(0x7A5028))
    for yy in range(15):
        for xx in range(14):
            if 3 <= xx + yy <= 6:
                t.put(dx + 1 + xx, 9 + yy, hx(0xC8903C))
    light = hx(0xDDE3EC)
    for y in range(9, 24):
        t.put(dx + 7, y, light); t.put(dx + 8, y, light)
    for x in range(dx + 1, dx + 15):
        t.put(x, 9, light); t.put(x, 23, light)
    for y in (15, 16, 17):
        t.put(dx + 6, y, hx(0xFFFFFF)); t.put(dx + 9, y, hx(0xFFFFFF))
    # 庇
    t.rect(dx - 3, 3, 22, 2, hx(0x6E6F78))
    t.rect(dx - 4, 5, 24, 3, hx(0x4A4B52))
    for x in range(dx - 4, dx + 20):
        t.put(x, 5, hx(0x8A8B94)); t.put(x, 8, OL)
    # 番地プレート
    t.rect(dx + 19, 12, 4, 3, hx(0xD8D8DC)); t.put(dx + 20, 13, OL); t.put(dx + 22, 13, OL)
    bush(t, 36, 19); bush(t, 53, 19); bush(t, 12, 19); bush(t, 78, 19)
    stone_col(t, "l", 0, 24); stone_col(t, "r", 0, 24)
    return t


def main():
    os.makedirs(OUT, exist_ok=True)
    parts = {
        "roof": roof(),
        "mid_a": mid(["dark", "lit", "curtain"]),
        "mid_b": mid(["curtain", "dark", "lit"]),
        "ground": ground(),
    }
    for k, t in parts.items():
        Image.fromarray(t.a, "RGBA").save(f"{OUT}/building_{k}.png")
    # 確認用: 屋上 + 中階(交互)x4 + 1階 を縦に積む。階数を変えて伸縮を確認
    def tower(n):
        rows = [parts["roof"].a] + [parts["mid_a" if i % 2 == 0 else "mid_b"].a for i in range(n)] + [parts["ground"].a]
        return np.concatenate(rows, 0)
    for n in (2, 5):
        img = tower(n)
        bg = Image.new("RGBA", (img.shape[1] + 32, img.shape[0] + 16), (22, 40, 74, 255))
        bg.alpha_composite(Image.fromarray(img, "RGBA"), (16, 8))
        bg.resize((bg.width * 3, bg.height * 3), Image.NEAREST).save(f"{OUT}/preview_tower_{n}floors_x3.png")
    img = tower(3)
    bg = Image.new("RGBA", (img.shape[1] + 32, img.shape[0] + 16), (22, 40, 74, 255))
    bg.alpha_composite(Image.fromarray(img, "RGBA"), (16, 8))
    h = bg.resize((bg.width // 2, bg.height // 2), Image.NEAREST)
    h.resize((h.width * 4, h.height * 4), Image.NEAREST).save(f"{OUT}/preview_half_scale.png")


if __name__ == "__main__":
    main()
