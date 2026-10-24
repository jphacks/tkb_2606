"""ビルの部品(正面から見た立面図風) 32x32 を生成。
 屋上 : building_roof_a / roof_b / roof_pillar_l / roof_pillar_r
 中階 : building_mid_a / mid_b / mid_c (窓違い) / mid_pillar_l / mid_pillar_r   ※上下に繰り返し可
 1階  : building_ground_door / ground_shop / ground_pillar_l / ground_pillar_r
左右に並べる / 上下に積むと継ぎ目が出ないように、レンガは 横8px・縦4px 周期。
使い方: python3 gen_building.py [出力ディレクトリ]
"""
import os
import sys
import numpy as np
from PIL import Image

T = 32
OUT = sys.argv[1] if len(sys.argv) > 1 else "building"


def hx(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


OL = hx(0x1C130E)          # 輪郭(紺)
MORTAR = hx(0x6B3226)
BRICKS = [hx(0xA9553C), hx(0xB05C42), hx(0xA24E38)]
BR_HI, BR_LO = 0.10, 0.12   # 各レンガの上段を明るく / 下段を暗く
STONE, STONE_HI, STONE_LO, STONE_JT = hx(0xA4A4A8), hx(0xC0C0C4), hx(0x84848A), hx(0x6C6C74)
STONE_SP = [hx(0x9C9CA2), hx(0xAEAEB2)]
SILL, SILL_LO = hx(0xDAD6CE), hx(0x5E2A20)
FRAME = hx(0x8E9096)


def lighten(c, f):
    return tuple(int(min(255, v + (255 - v) * f)) for v in c)


def darken(c, f):
    return tuple(int(v * (1 - f)) for v in c)


class Tile:
    def __init__(self):
        self.a = np.zeros((T, T, 4), np.uint8)

    def put(self, x, y, c):
        if 0 <= x < T and 0 <= y < T:
            self.a[y, x] = (*c, 255)

    def rect(self, x0, y0, w, h, c):
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                self.put(x, y, c)

    def frame(self, x0, y0, w, h, c):
        for x in range(x0, x0 + w):
            self.put(x, y0, c)
            self.put(x, y0 + h - 1, c)
        for y in range(y0, y0 + h):
            self.put(x0, y, c)
            self.put(x0 + w - 1, y, c)


def brick(t, y_from=0, y_to=T):
    """レンガ壁。段の高さ4px・長さ8px・段ごとに半分ずらし。全タイル共通の周期。"""
    for y in range(y_from, y_to):
        c = y // 4
        st = 4 * (c % 2)
        for x in range(T):
            bx = (x + st) // 8
            idx = (c * 7 + bx * 3) % 3
            base = BRICKS[idx]
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


def stone(t, y_from=0, y_to=T, joints=(15,)):
    """石の面。左右の縁で明暗、段ごとの目地。"""
    for y in range(y_from, y_to):
        for x in range(T):
            col = STONE
            if (x * 5 + y * 3) % 11 == 0:
                col = STONE_SP[0]
            elif (x * 7 + y * 2) % 13 == 0:
                col = STONE_SP[1]
            if x <= 1:
                col = STONE_HI
            elif x >= T - 2:
                col = STONE_LO
            t.put(x, y, col)
    for jy in joints:
        for x in range(T):
            t.put(x, jy, STONE_JT)
            t.put(x, (jy + 1) % T, STONE_HI if x < T - 2 else STONE)


def window(t, x0=11, y0=6, w=10, h=15, lit=False, curtain=False):
    t.frame(x0 - 1, y0 - 1, w + 2, h + 2, OL)
    t.frame(x0, y0, w, h, FRAME)
    gx0, gy0, gw, gh = x0 + 1, y0 + 1, w - 2, h - 2
    base = hx(0xD08A34) if lit else hx(0x2A1B12)
    t.rect(gx0, gy0, gw, gh, base)
    # 左上から差す反射
    for yy in range(gh):
        for xx in range(gw):
            if 2 <= xx + yy <= 4 + (2 if lit else 0):
                t.put(gx0 + xx, gy0 + yy, hx(0xF2BC5E) if lit else hx(0x4A3020))
    if lit:
        t.rect(gx0 + gw - 4, gy0 + gh - 5, 3, 3, hx(0xF6D88A))
    if curtain:
        t.rect(gx0, gy0, 2, gh, hx(0xC9B8A0))
    # 窓の桟(横: 上から40%、縦: 中央)
    my = gy0 + int(gh * 0.4)
    for xx in range(gw):
        t.put(gx0 + xx, my, FRAME)
    for yy in range(gh):
        t.put(gx0 + gw // 2, gy0 + yy, FRAME)
    # 窓台
    sy = y0 + h + 1
    t.rect(x0 - 2, sy, w + 4, 2, SILL)
    t.rect(x0 - 2, sy + 2, w + 4, 1, SILL_LO)


def edge_outline(t, side):
    x = 0 if side == "l" else T - 1
    for y in range(T):
        t.put(x, y, OL)


# ---- 中階 ----------------------------------------------------------------------
def mid_a():
    t = Tile(); brick(t); window(t); return t


def mid_b():
    t = Tile(); brick(t); window(t, lit=True); return t


def mid_c():
    t = Tile(); brick(t); window(t, curtain=True); return t


COL_W = 10   # 石柱の幅(px)


def stone_col(t, side, y0=0, y1=T, joints=(15,)):
    """タイルの左端/右端に石柱を重ねる(柱の内側に影を落とす)。"""
    xs = range(0, COL_W) if side == "l" else range(T - COL_W, T)
    for y in range(y0, y1):
        for x in xs:
            col = STONE
            if (x * 5 + y * 3) % 11 == 0:
                col = STONE_SP[0]
            elif (x * 7 + y * 2) % 13 == 0:
                col = STONE_SP[1]
            outer = x if side == "l" else T - 1 - x
            inner = COL_W - 1 - outer
            if outer <= 1:
                col = STONE_HI if side == "l" else STONE_LO
            if inner <= 1:
                col = STONE_LO if side == "l" else STONE_HI
            t.put(x, y, col)
    for jy in joints:
        for x in xs:
            t.put(x, jy, STONE_JT)
            t.put(x, (jy + 1) % T, STONE_HI)
    # 柱の内側の影
    sx = COL_W if side == "l" else T - COL_W - 1
    for y in range(y0, y1):
        t.put(sx, y, hx(0x5A2A20))
    ox = 0 if side == "l" else T - 1
    for y in range(y0, y1):
        t.put(ox, y, OL)


def mid_pillar(side):
    t = Tile(); brick(t)
    window(t, x0=17 if side == "l" else 5)
    stone_col(t, side)
    return t


# ---- 1階 -----------------------------------------------------------------------
GROUND_STONE = hx(0xA0A0A4)
PAVE, PAVE_JT, PAVE_HI = hx(0xCFC9D0), hx(0xA8A3AB), hx(0xE2DEE4)


def sidewalk(t):
    t.rect(0, 26, T, 6, PAVE)
    for x in range(T):
        t.put(x, 26, PAVE_HI)
        t.put(x, 31, PAVE_JT)
    for x in (7, 23):
        for y in range(27, 31):
            t.put(x, y, PAVE_JT)
    for y in (27, 28, 29, 30):
        pass
    # 縁石と土台
    for x in range(T):
        t.put(x, 24, hx(0x8A8A8E))
        t.put(x, 25, hx(0x6C6C74))


def facade(t):
    for y in range(0, 24):
        for x in range(T):
            col = GROUND_STONE
            if (x * 5 + y * 3) % 11 == 0:
                col = STONE_SP[0]
            elif (x * 7 + y * 2) % 13 == 0:
                col = STONE_SP[1]
            t.put(x, y, col)
    for x in range(T):
        t.put(x, 0, STONE_HI)


def bush(t, x0, y0):
    G1, G2, G3, SOIL = hx(0x5C9A44), hx(0x3C7034), hx(0x7CBF5C), hx(0x5A3A26)
    t.rect(x0 - 1, y0 + 5, 9, 2, SOIL)
    rows = [(2, 4), (1, 6), (0, 8), (0, 8), (1, 7)]
    for j, (dx, w) in enumerate(rows):
        for i in range(w):
            col = G1
            if j == 0 or (i < 2 and j < 3):
                col = G3
            if j >= 3 and i > w - 3:
                col = G2
            t.put(x0 + dx + i, y0 + j, col)
    t.put(x0 + 3, y0 + 1, G3); t.put(x0 + 5, y0 + 2, G3)


def ground_door():
    t = Tile(); facade(t); sidewalk(t)
    # ドア(両開き)
    t.frame(8, 8, 16, 17, OL)
    t.rect(9, 9, 14, 15, hx(0x7A5028))
    for yy in range(15):
        for xx in range(14):
            if 3 <= xx + yy <= 6:
                t.put(9 + xx, 9 + yy, hx(0xC8903C))
    for y in range(9, 24):
        t.put(15, y, hx(0xDDE3EC)); t.put(16, y, hx(0xDDE3EC))
    for x in range(9, 23):
        t.put(x, 9, hx(0xDDE3EC)); t.put(x, 23, hx(0xDDE3EC))
    for y in (15, 16, 17):
        t.put(14, y, hx(0xFFFFFF)); t.put(17, y, hx(0xFFFFFF))
    # カーニス(庇)
    t.rect(5, 3, 22, 2, hx(0x6E6F78))
    t.rect(4, 5, 24, 3, hx(0x4A4B52))
    for x in range(4, 28):
        t.put(x, 5, hx(0x8A8B94))
        t.put(x, 8, OL)
    # 番地プレート
    t.rect(27, 12, 4, 3, hx(0xD8D8DC)); t.put(28, 13, OL); t.put(30, 13, OL)
    bush(t, 1, 19)
    return t


def ground_shop():
    t = Tile(); facade(t); sidewalk(t)
    t.frame(3, 4, 26, 21, OL)
    t.frame(4, 5, 24, 19, hx(0xC4C4C8))
    t.frame(5, 6, 22, 17, hx(0xA8A8AC))
    t.rect(6, 7, 20, 15, hx(0x2A1B12))
    for yy in range(15):
        for xx in range(20):
            if 4 <= xx + yy <= 8 or 14 <= xx + yy <= 16:
                t.put(6 + xx, 7 + yy, hx(0x6A4426))
    for y in range(7, 22):
        t.put(15, y, hx(0xA8A8AC)); t.put(16, y, hx(0xA8A8AC))
    bush(t, 22, 19)
    return t


def ground_pillar(side):
    t = Tile(); facade(t); sidewalk(t)
    x0 = 14 if side == "l" else 4
    t.frame(x0 - 1, 5, 14, 20, OL)
    t.frame(x0, 6, 12, 18, hx(0xC4C4C8))
    t.rect(x0 + 1, 7, 10, 16, hx(0x2A1B12))
    for yy in range(16):
        for xx in range(10):
            if 3 <= xx + yy <= 6:
                t.put(x0 + 1 + xx, 7 + yy, hx(0x6A4426))
    stone_col(t, side, 0, 24)
    # 柱の足元(土台・歩道は共通)
    return t


# ---- 屋上 ----------------------------------------------------------------------
CORN_TOP, CORN_FACE, CORN_LO, CORN_SH = hx(0xD6D6D9), hx(0xB4B4B8), hx(0x8A8A90), hx(0x6C6C74)


def cornice(t, y0=10):
    for x in range(T):
        t.put(x, y0, CORN_TOP)
        t.put(x, y0 + 1, CORN_TOP)
        for y in (y0 + 2, y0 + 3):
            t.put(x, y, CORN_FACE)
        t.put(x, y0 + 4, CORN_LO)
        t.put(x, y0 + 5, CORN_SH)
        t.put(x, y0 - 1, OL)


def ac_unit(t, x0, y0, w=12, h=8):
    body, top, dk = hx(0x8A8E98), hx(0xB0B4BE), hx(0x5A5E6A)
    t.frame(x0 - 1, y0 - 1, w + 2, h + 2, OL)
    t.rect(x0, y0, w, h, body)
    t.rect(x0, y0, w, 2, top)
    cx, cy = x0 + w // 2, y0 + 4
    for dx, dy in [(-2, 0), (-1, -1), (0, -1), (1, -1), (2, 0), (-1, 1), (0, 1), (1, 1), (0, 0)]:
        t.put(cx + dx, cy + dy, dk)
    for y in range(y0 + 2, y0 + h):
        t.put(x0 + w - 1, y, darken(body, 0.18))


def tank(t, x0, y0):
    body, top, dk = hx(0x7388AC), hx(0x9FB0CE), hx(0x566A8C)
    t.rect(x0 - 1, y0 - 1, 11, 7, OL)
    t.rect(x0, y0, 9, 5, body)
    t.rect(x0, y0, 9, 2, top)
    for y in range(y0 + 2, y0 + 5):
        t.put(x0 + 8, y, dk); t.put(x0 + 7, y, dk)
    t.put(x0, y0, OL); t.put(x0 + 8, y0, OL)
    for lx in (x0 + 1, x0 + 7):
        for y in range(y0 + 5, y0 + 9):
            t.put(lx, y, OL)


def roof_a():
    t = Tile(); brick(t, 16, T); cornice(t)
    ac_unit(t, 3, 2); tank(t, 19, 1)
    return t


def roof_b():
    t = Tile(); brick(t, 16, T); cornice(t)
    ac_unit(t, 2, 4, 9, 6); ac_unit(t, 15, 2, 12, 8)
    for y in range(3, 9):     # 排気管
        t.put(29, y, hx(0x6E7078)); t.put(30, y, hx(0x9A9CA6))
    t.rect(28, 2, 4, 1, OL)
    return t


def roof_pillar(side):
    t = Tile(); brick(t, 16, T); cornice(t)
    if side == "l":
        tank(t, 15, 1)
    else:
        ac_unit(t, 4, 2)
    stone_col(t, side, 16, T, joints=(31,))
    xs = range(0, COL_W) if side == "l" else range(T - COL_W, T)
    # 柱頭: パラペットの上に立ち上がる石ブロック
    for y in range(4, 10):
        for x in xs:
            t.put(x, y, STONE)
    for x in xs:
        t.put(x, 4, STONE_HI)
        t.put(x, 3, OL)
    for y in range(4, 10):
        t.put(xs[0], y, STONE_HI if side == "l" else STONE)
        t.put(xs[-1], y, STONE if side == "l" else STONE_LO)
    ox = 0 if side == "l" else T - 1
    for y in range(3, 10):
        t.put(ox, y, OL)
    return t


def main():
    os.makedirs(OUT, exist_ok=True)
    tiles = {
        "roof_a": roof_a(), "roof_b": roof_b(), "roof_pillar_l": roof_pillar("l"), "roof_pillar_r": roof_pillar("r"),
        "mid_a": mid_a(), "mid_b": mid_b(), "mid_c": mid_c(), "mid_pillar_l": mid_pillar("l"), "mid_pillar_r": mid_pillar("r"),
        "ground_door": ground_door(), "ground_shop": ground_shop(),
        "ground_pillar_l": ground_pillar("l"), "ground_pillar_r": ground_pillar("r"),
    }
    for k, t in tiles.items():
        Image.fromarray(t.a, "RGBA").save(f"{OUT}/building_{k}.png")

    # ---- 確認用: 5マス幅 x (屋上 + 中階3 + 1階) を組み立てる ----
    def row(names):
        return np.concatenate([tiles[n].a for n in names], 1)

    rows = [
        row(["roof_pillar_l", "roof_a", "roof_b", "roof_a", "roof_pillar_r"]),
        row(["mid_pillar_l", "mid_a", "mid_b", "mid_c", "mid_pillar_r"]),
        row(["mid_pillar_l", "mid_c", "mid_a", "mid_b", "mid_pillar_r"]),
        row(["mid_pillar_l", "mid_b", "mid_c", "mid_a", "mid_pillar_r"]),
        row(["ground_pillar_l", "ground_shop", "ground_door", "ground_shop", "ground_pillar_r"]),
    ]
    img = np.concatenate(rows, 0)
    bg = Image.new("RGBA", (img.shape[1] + 32, img.shape[0] + 16), (22, 40, 74, 255))
    bg.alpha_composite(Image.fromarray(img, "RGBA"), (16, 8))
    bg.resize((bg.width * 3, bg.height * 3), Image.NEAREST).save(f"{OUT}/preview_building_x3.png")
    # 0.5倍表示相当
    h = bg.resize((bg.width // 2, bg.height // 2), Image.NEAREST)
    h.resize((h.width * 4, h.height * 4), Image.NEAREST).save(f"{OUT}/preview_half_scale.png")


if __name__ == "__main__":
    main()
