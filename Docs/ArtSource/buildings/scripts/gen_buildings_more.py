"""ビルの別デザイン6種(幅1・2・3マス) + 固定サイズの建物3種(小1x1・中2x2・大3x2)。
どれも正面から見た立面図風。ビルは「屋上 / 中階(上下に繰り返し) / 1階」の縦分割で、横は分割しない。
 ビル: stone_tower(32) cottage_tower(32) tile_shop(64) timber_inn(64) modern_office(96) factory(96)
       各 roof / mid_a / mid_b / ground  (幅 x 32)
 固定: hut_1x1(32x32) house_2x2(64x64) lodge_3x2(96x64)
使い方: python3 gen_buildings_more.py [出力ディレクトリ]
"""
import os
import sys
import numpy as np
from PIL import Image

OUT = sys.argv[1] if len(sys.argv) > 1 else "buildings_more"


def hx(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


def lighten(c, f):
    return tuple(int(min(255, v + (255 - v) * f)) for v in c)


def darken(c, f):
    return tuple(int(v * (1 - f)) for v in c)


OL = hx(0x1C130E)
SIDEWALK, SW_JT, SW_HI = hx(0xCFC9D0), hx(0xA8A3AB), hx(0xE2DEE4)
GLASS_DK, GLASS_MID, GLASS_HI = hx(0x2A1B12), hx(0x4A3020), hx(0xF2BC5E)


class Img:
    def __init__(s, w, h):
        s.w, s.h = w, h
        s.a = np.zeros((h, w, 4), np.uint8)

    def put(s, x, y, c):
        if 0 <= x < s.w and 0 <= y < s.h:
            s.a[y, x] = (*c, 255)

    def rect(s, x, y, w, h, c):
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                s.put(xx, yy, c)

    def frame(s, x, y, w, h, c):
        for xx in range(x, x + w):
            s.put(xx, y, c); s.put(xx, y + h - 1, c)
        for yy in range(y, y + h):
            s.put(x, yy, c); s.put(x + w - 1, yy, c)

    def fill(s, y0, y1, fn):
        for y in range(y0, y1):
            for x in range(s.w):
                s.put(x, y, fn(x, y))

    def tri(s, xl, xr, peak_y, base_y, fn, outline=OL):
        cx = (xl + xr) / 2
        for y in range(peak_y, base_y + 1):
            half = (y - peak_y + 0.5) / (base_y - peak_y + 1) * (xr - xl) / 2
            x0, x1 = int(round(cx - half)), int(round(cx + half))
            for x in range(x0, x1):
                s.put(x, y, fn(x, y))
            if outline:
                s.put(x0 - 1, y, outline); s.put(x1, y, outline)
        if outline:
            for x in range(int(cx) - 1, int(cx) + 1):
                s.put(x, peak_y - 1, outline)

    def edge_outline(s, y0=0, y1=None):
        for y in range(y0, y1 if y1 is not None else s.h):
            s.put(0, y, OL); s.put(s.w - 1, y, OL)


def save(img, name):
    Image.fromarray(img.a, "RGBA").save(f"{OUT}/{name}.png")


# ---- 共通パーツ ---------------------------------------------------------------
def sidewalk(t, y0=26):
    t.rect(0, y0, t.w, t.h - y0, SIDEWALK)
    for x in range(t.w):
        t.put(x, y0, SW_HI); t.put(x, t.h - 1, SW_JT)
    for x in range(7, t.w, 16):
        for y in range(y0 + 1, t.h - 1):
            t.put(x, y, SW_JT)


def bush(t, x0, y0):
    G1, G2, G3, SOIL = hx(0x5C9A44), hx(0x3C7034), hx(0x7CBF5C), hx(0x5A3A26)
    t.rect(x0 - 1, y0 + 5, 9, 2, SOIL)
    for j, (dx, w) in enumerate([(2, 4), (1, 6), (0, 8), (0, 8), (1, 7)]):
        for i in range(w):
            col = G1
            if j == 0 or (i < 2 and j < 3):
                col = G3
            if j >= 3 and i > w - 3:
                col = G2
            t.put(x0 + dx + i, y0 + j, col)


def glass(t, x, y, w, h, lit=False, base=None):
    b = base or (hx(0xD08A34) if lit else GLASS_DK)
    t.rect(x, y, w, h, b)
    for yy in range(h):
        for xx in range(w):
            if 2 <= xx + yy <= 4 + (2 if lit else 0):
                t.put(x + xx, y + yy, GLASS_HI if lit else GLASS_MID)
    if lit:
        t.rect(x + w - 4, y + h - 5, 3, 3, hx(0xF6D88A))


def win_rect(t, x, y, w, h, frame_c, lit=False, mullion=True, sill=None, outline=True):
    if outline:
        t.frame(x - 1, y - 1, w + 2, h + 2, OL)
    t.frame(x, y, w, h, frame_c)
    glass(t, x + 1, y + 1, w - 2, h - 2, lit)
    if mullion:
        my = y + 1 + int((h - 2) * 0.4)
        for xx in range(x + 1, x + w - 1):
            t.put(xx, my, frame_c)
        for yy in range(y + 1, y + h - 1):
            t.put(x + w // 2, yy, frame_c)
    if sill:
        t.rect(x - 2, y + h + 1, w + 4, 2, sill)
        t.rect(x - 2, y + h + 3, w + 4, 1, darken(sill, 0.45))


def corrugated(c, x, y, base, w=2):
    return darken(base, 0.18) if y % 3 == 2 else base


# ================================ 壁面パターン ===================================
def hash2(x, y, n=7):
    return (x * 73856093 ^ y * 19349663 ^ n * 83492791) & 0xFFFF


def ashlar(base, joint, hi):
    def f(x, y):
        row = y // 8
        st = 8 * (row % 2)
        xx = (x + st) % 16
        if y % 8 == 7 or xx == 15:
            return joint
        c = base
        h = hash2((x + st) // 16, row) % 3
        if h == 1:
            c = darken(base, 0.06)
        elif h == 2:
            c = lighten(base, 0.05)
        if y % 8 == 0 or xx == 0:
            c = hi
        return c
    return f


def stucco(base):
    def f(x, y):
        h = hash2(x, y, 3) % 23
        if h == 0:
            return darken(base, 0.08)
        if h == 1:
            return lighten(base, 0.06)
        return base
    return f


def tiles4(base, grout, hi):
    def f(x, y):
        if x % 4 == 3 or y % 4 == 3:
            return grout
        if x % 4 == 0 and y % 4 == 0:
            return hi
        return base if hash2(x // 4, y // 4) % 5 else darken(base, 0.07)
    return f


def planks(base, gap, hi):
    def f(x, y):
        if y % 4 == 3:
            return gap
        if (x + (y // 4) * 11) % 24 == 0:
            return darken(base, 0.3)
        c = base if hash2(x // 6, y // 4, 5) % 3 else darken(base, 0.07)
        if y % 4 == 0:
            c = hi
        return c
    return f


def concrete(base):
    def f(x, y):
        if x % 32 == 31 or y % 32 == 31:
            return darken(base, 0.22)
        if x % 32 == 0 or y % 32 == 0:
            return lighten(base, 0.18)
        h = hash2(x, y, 9) % 31
        if h == 0:
            return darken(base, 0.07)
        if h == 1:
            return lighten(base, 0.06)
        return base
    return f


def dark_brick(c1, c2, c3, mortar):
    cols = [c1, c2, c3]

    def f(x, y):
        c = y // 4
        st = 4 * (c % 2)
        ly = y % 4
        if ly == 3 or (x + st) % 8 == 7:
            return mortar
        base = cols[(c * 7 + ((x + st) // 8) * 3) % 3]
        return lighten(base, 0.08) if ly == 0 else (darken(base, 0.1) if ly == 2 else base)
    return f


# 屋根(瓦・スレート)
def shingles(base, dark):
    def f(x, y):
        row = y // 3
        off = 3 * (row % 2)
        if y % 3 == 2:
            return dark
        if (x + off) % 6 == 5:
            return darken(base, 0.18)
        return lighten(base, 0.08) if y % 3 == 0 else base
    return f


# ================================ D1 石造りの塔 (幅32) ===========================
def stone_tower():
    W = 32
    wall = ashlar(hx(0xC9B994), hx(0x8F7F5E), hx(0xDDD0B0))
    parts = {}

    def arch_window(t, lit):
        x0, y0, w, h = 13, 8, 6, 14
        frame_c = hx(0x7B6B4C)
        for yy in range(h):
            for xx in range(w):
                top = yy < 3
                if top and (xx < 2 - yy or xx >= w - (2 - yy)):
                    continue
                edge = xx in (0, w - 1) or yy in (0, h - 1) or (yy < 3 and (xx == 2 - yy or xx == w - 1 - (2 - yy)))
                t.put(x0 + xx, y0 + yy, frame_c if edge else (hx(0xE8B44A) if lit else GLASS_DK))
        if not lit:
            t.put(x0 + 2, y0 + 5, GLASS_HI); t.put(x0 + 2, y0 + 6, GLASS_MID)
        t.rect(x0 - 1, y0 + h, w + 2, 2, hx(0xA89870))
        t.rect(x0 - 1, y0 + h + 2, w + 2, 1, hx(0x6A5C3E))
        for yy in range(h):  # 外側の輪郭
            pass

    for name, lit in (("mid_a", False), ("mid_b", True)):
        t = Img(W, 32); t.fill(0, 32, wall); arch_window(t, lit)
        for y in range(32):
            t.put(0, y, OL); t.put(W - 1, y, OL)
            t.put(1, y, hx(0xE2D6B8)); t.put(W - 2, y, hx(0xA89870))
        parts[name] = t
    # 屋上: 胸壁(クレネル) + 旗
    t = Img(W, 32)
    t.rect(0, 14, W, 18, (0, 0, 0))
    t.fill(14, 32, wall)
    for y in range(32):
        t.put(0, y, OL); t.put(W - 1, y, OL)
    for x in range(W):
        t.put(x, 13, OL)
    # 張り出し(コーベル)
    for x in range(W):
        t.put(x, 14, hx(0xE2D6B8)); t.put(x, 15, hx(0xB3A37E)); t.put(x, 16, hx(0x7A6B4C))
    for mx in (1, 13, 25):
        t.rect(mx, 5, 6, 9, hx(0xC9B994))
        t.frame(mx - 1, 4, 8, 10, OL)
        t.rect(mx, 5, 6, 1, hx(0xE2D6B8)); t.rect(mx + 5, 6, 1, 8, hx(0x9C8C6C))
        t.rect(mx, 13, 6, 1, hx(0x8F7F5E))
    # 旗竿
    for y in range(0, 4):
        t.put(16, y, hx(0x6B4426))
    parts["roof"] = t
    # 1階: アーチ型の木の扉 + 石段 + 石畳
    t = Img(W, 32); t.fill(0, 26, wall)
    for y in range(32):
        t.put(0, y, OL); t.put(W - 1, y, OL)
        if y < 26:
            t.put(1, y, hx(0xE2D6B8)); t.put(W - 2, y, hx(0xA89870))
    x0, y0, w, h = 10, 8, 12, 17
    for yy in range(h):
        for xx in range(w):
            if yy < 4 and (xx < 4 - yy or xx >= w - (4 - yy)):
                continue
            edge = xx in (0, w - 1) or yy == 0 or (yy < 4 and (xx == 4 - yy or xx == w - 1 - (4 - yy)))
            col = hx(0x3A2412) if edge else (hx(0x7A4A2A) if (xx % 3) else hx(0x6B4426))
            t.put(x0 + xx, y0 + yy, col)
    for yy in (12, 19):   # 鉄の帯
        for xx in range(1, w - 1):
            t.put(x0 + xx, y0 + yy, hx(0x2B2B30))
    t.put(x0 + 9, y0 + 9, hx(0xE0C060)); t.put(x0 + 9, y0 + 10, hx(0xE0C060))
    t.rect(8, 25, 16, 1, hx(0x8F7F5E)); t.rect(8, 24, 16, 1, hx(0xA89870))
    # ランタン
    t.rect(4, 10, 3, 4, hx(0x2B2B30)); t.rect(5, 11, 1, 2, hx(0xFFD75A))
    t.rect(25, 10, 3, 4, hx(0x2B2B30)); t.rect(26, 11, 1, 2, hx(0xFFD75A))
    for y in range(26, 32):
        for x in range(W):
            t.put(x, y, hx(0x8A8A90) if (x // 4 + y // 3) % 2 else hx(0x9A9AA0))
    for x in range(W):
        t.put(x, 26, hx(0xB4B4BA)); t.put(x, 31, hx(0x6C6C74))
    parts["ground"] = t
    return parts


# ================================ D2 漆喰の家型ビル (幅32) =======================
def cottage_tower():
    W = 32
    wall = stucco(hx(0xEAE0C8))
    parts = {}

    def shuttered(t, lit):
        x0, y0, w, h = 12, 8, 8, 12
        t.frame(x0 - 1, y0 - 1, w + 2, h + 2, OL)
        t.frame(x0, y0, w, h, hx(0x8A6A44))
        glass(t, x0 + 1, y0 + 1, w - 2, h - 2, lit)
        t.rect(x0 + w // 2, y0 + 1, 1, h - 2, hx(0x8A6A44))
        for sx in (x0 - 5, x0 + w + 1):   # 窓の鎧戸
            t.rect(sx, y0 - 1, 4, h + 2, hx(0x4E7A52))
            t.frame(sx, y0 - 1, 4, h + 2, hx(0x2E5236))
            for yy in range(y0 + 1, y0 + h, 3):
                t.rect(sx + 1, yy, 2, 1, hx(0x3C6240))
        # 花箱
        t.rect(x0 - 2, y0 + h + 1, w + 4, 3, hx(0x7A4A2A))
        for i in range(w + 4):
            t.put(x0 - 2 + i, y0 + h, hx(0xE05A78) if i % 2 == 0 else hx(0x5C9A44))

    for name, lit in (("mid_a", False), ("mid_b", True)):
        t = Img(W, 32); t.fill(0, 32, wall); shuttered(t, lit)
        for y in range(32):
            t.put(0, y, OL); t.put(W - 1, y, OL)
            for x in (1, 2):
                t.put(x, y, hx(0x8A6A44)); t.put(W - 1 - x, y, hx(0x6B4F30))
        parts[name] = t
    # 屋上: 三角屋根 + 煙突
    t = Img(W, 32)
    t.tri(0, W, 4, 27, shingles(hx(0xC25A30), hx(0x8E3A1C)))
    t.rect(21, 1, 6, 9, hx(0xA9553C)); t.frame(20, 0, 8, 10, OL)
    t.rect(21, 1, 6, 1, hx(0xC87A5C)); t.rect(21, 4, 6, 1, hx(0x6B3226))
    t.tri(0, W, 4, 27, shingles(hx(0xC25A30), hx(0x8E3A1C)))
    for x in range(W):
        t.put(x, 28, hx(0x7A4A2A)); t.put(x, 29, hx(0x5A3A26)); t.put(x, 30, hx(0x3A2412))
    for y in range(28, 32):
        t.put(0, y, OL); t.put(W - 1, y, OL)
    for x in range(W):
        t.put(x, 31, OL)
    parts["roof"] = t
    # 1階: 木のドア + 庇 + 鉢植え
    t = Img(W, 32); t.fill(0, 26, wall)
    for y in range(32):
        t.put(0, y, OL); t.put(W - 1, y, OL)
        if y < 26:
            for x in (1, 2):
                t.put(x, y, hx(0x8A6A44)); t.put(W - 1 - x, y, hx(0x6B4F30))
    t.frame(11, 9, 10, 16, OL)
    t.rect(12, 10, 8, 15, hx(0x8A4A2A))
    for y in range(10, 25):
        t.put(15, y, hx(0x6B3A20))
    glass(t, 13, 12, 6, 5)
    t.put(18, 19, hx(0xE0C060))
    t.rect(8, 6, 16, 3, hx(0x3E7A5A)); t.rect(8, 9, 16, 1, hx(0x244A36))
    for x in range(8, 24, 4):
        t.rect(x, 6, 2, 3, hx(0xF0F0E0))
    t.rect(10, 25, 12, 1, hx(0x9A9AA0))
    bush(t, 3, 19); bush(t, 21, 19)
    sidewalk(t)
    parts["ground"] = t
    return parts


# ================================ D3 タイル張りの店舗ビル (幅64) =================
def tile_shop():
    W = 64
    wall = tiles4(hx(0x4FA39B), hx(0x2F6F69), hx(0x7CC8BF))
    WHITE, WHITE_SH = hx(0xEFEFEA), hx(0xB8B8B2)
    parts = {}
    for name, lits in (("mid_a", (False, True)), ("mid_b", (True, False))):
        t = Img(W, 32); t.fill(0, 32, wall)
        for i, x0 in enumerate((9, 39)):
            win_rect(t, x0, 7, 16, 16, WHITE, lit=lits[i], sill=WHITE)
        for y in range(32):
            t.put(0, y, OL); t.put(W - 1, y, OL)
            for x in (1, 2, 3):
                t.put(x, y, WHITE); t.put(W - 1 - x, y, WHITE_SH)
        parts[name] = t
    # 屋上: 平らな屋根 + 水タンク + ソーラーパネル + アンテナ
    t = Img(W, 32)
    t.fill(16, 32, wall)
    for y in range(32):
        t.put(0, y, OL); t.put(W - 1, y, OL)
        if y >= 16:
            for x in (1, 2, 3):
                t.put(x, y, WHITE); t.put(W - 1 - x, y, WHITE_SH)
    for x in range(W):
        t.put(x, 9, OL); t.put(x, 10, WHITE); t.put(x, 11, WHITE); t.put(x, 12, hx(0xD8D8D2))
        t.put(x, 13, WHITE_SH); t.put(x, 14, hx(0x8A8A86)); t.put(x, 15, hx(0x5A5A56))
    for px in (6, 18):
        t.rect(px, 3, 10, 6, hx(0x3E4656)); t.frame(px - 1, 2, 12, 8, OL)
        for yy in range(3, 9, 2):
            for xx in range(px, px + 10):
                t.put(xx, yy, hx(0x5C677A))
        for xx in range(px + 3, px + 10, 3):
            for yy in range(3, 9):
                t.put(xx, yy, hx(0x2A303C))
    t.rect(38, 2, 9, 7, hx(0x7388AC)); t.frame(37, 1, 11, 9, OL)
    t.rect(38, 2, 9, 2, hx(0x9FB0CE))
    for y in range(0, 9):
        t.put(54, y, hx(0x6E7078))
    for y in (2, 4):
        t.rect(52, y, 5, 1, hx(0x9A9CA6))
    parts["roof"] = t
    # 1階: 縞の日除け + ショーウィンドウ + ドア + 看板
    t = Img(W, 32); t.fill(0, 24, wall)
    for y in range(32):
        t.put(0, y, OL); t.put(W - 1, y, OL)
        if y < 24:
            for x in (1, 2, 3):
                t.put(x, y, WHITE); t.put(W - 1 - x, y, WHITE_SH)
    for x in range(5, 59):
        for y in range(3, 9):
            t.put(x, y, hx(0xD84A3A) if (x // 4) % 2 == 0 else hx(0xF4F0E4))
        t.put(x, 9, hx(0x8A2A20)); t.put(x, 2, OL)
    for x in range(5, 59):
        t.put(x, 8, darken(hx(0xD84A3A) if (x // 4) % 2 == 0 else hx(0xF4F0E4), 0.25))
    t.frame(7, 11, 28, 13, OL); t.frame(8, 12, 26, 11, WHITE)
    glass(t, 9, 13, 24, 9)
    t.rect(21, 13, 1, 9, WHITE)
    t.frame(40, 11, 14, 14, OL); t.frame(41, 12, 12, 12, WHITE)
    glass(t, 42, 13, 10, 10)
    t.rect(47, 13, 1, 10, WHITE); t.put(46, 19, hx(0xFFFFFF)); t.put(49, 19, hx(0xFFFFFF))
    t.rect(55, 12, 6, 5, hx(0xF2C94C)); t.frame(55, 12, 6, 5, OL)
    for x in range(8, 56, 1):
        pass
    bush(t, 36, 19)
    t.rect(0, 24, W, 2, hx(0x6C6C74))
    sidewalk(t)
    parts["ground"] = t
    return parts


# ================================ D4 木造の宿屋 (幅64) ===========================
def timber_inn():
    W = 64
    wall = planks(hx(0x8A5A34), hx(0x4A2E1A), hx(0xA6743F))
    POST, POST_HI = hx(0x4A2E1A), hx(0x6B4426)
    parts = {}

    def window(t, x0, lit):
        win_rect(t, x0, 8, 12, 14, hx(0xEFE6D2), lit=lit, sill=hx(0xEFE6D2))
        t.rect(x0 - 2, 25, 16, 3, hx(0x6B4426))
        for i in range(16):
            t.put(x0 - 2 + i, 24, hx(0xE05A78) if i % 3 == 0 else hx(0x5C9A44))

    for name, lits in (("mid_a", (False, True)), ("mid_b", (True, False))):
        t = Img(W, 32); t.fill(0, 32, wall)
        for x0, lit in zip((12, 40), lits):
            window(t, x0, lit)
        for y in range(32):
            for x in (0, 1, 2, 3):
                t.put(x, y, POST_HI if x == 1 else POST)
                t.put(W - 1 - x, y, POST if x != 1 else darken(POST_HI, 0.2))
            t.put(0, y, OL); t.put(W - 1, y, OL)
        parts[name] = t
    # 屋上: 切妻屋根(スレート) + 石の煙突
    t = Img(W, 32)
    t.rect(46, 3, 8, 14, hx(0x9A9AA0)); t.frame(45, 2, 10, 16, OL)
    for y in range(3, 17, 3):
        t.rect(46, y, 8, 1, hx(0x6C6C74))
    t.rect(46, 3, 8, 1, hx(0xBEBEC4))
    t.tri(0, W, 3, 27, shingles(hx(0x5A647C), hx(0x363E52)))
    for x in range(W):
        t.put(x, 28, hx(0x6B4426)); t.put(x, 29, POST); t.put(x, 30, hx(0x2A1A0E)); t.put(x, 31, OL)
    t.rect(30, 1, 4, 2, hx(0x8A5A34))
    parts["roof"] = t
    # 1階: 両開きの木の扉 + 石の基礎 + 看板 + 窓
    t = Img(W, 32); t.fill(0, 22, wall)
    t.fill(22, 26, lambda x, y: hx(0x8A8A90) if (x + y // 2 * 5) % 9 else hx(0x6C6C74))
    for y in range(32):
        for x in (0, 1, 2, 3):
            if y < 26:
                t.put(x, y, POST_HI if x == 1 else POST); t.put(W - 1 - x, y, POST if x != 1 else darken(POST_HI, 0.2))
        t.put(0, y, OL); t.put(W - 1, y, OL)
    t.frame(24, 6, 16, 19, OL); t.rect(25, 7, 14, 18, hx(0x5A3A22))
    for x0 in (25, 32):
        t.frame(x0, 7, 7, 18, hx(0x3A2412))
        glass(t, x0 + 1, 9, 5, 5, lit=True)
    t.put(30, 18, hx(0xE0C060)); t.put(33, 18, hx(0xE0C060))
    t.rect(22, 3, 20, 3, hx(0x6B4426)); t.frame(21, 2, 22, 5, OL)
    t.rect(28, 4, 8, 1, hx(0xF2C94C))
    win_rect(t, 8, 9, 10, 11, hx(0xEFE6D2), lit=True, sill=hx(0xEFE6D2))
    win_rect(t, 46, 9, 10, 11, hx(0xEFE6D2), lit=False, sill=hx(0xEFE6D2))
    bush(t, 14, 19); bush(t, 42, 19)
    sidewalk(t)
    parts["ground"] = t
    return parts


# ================================ D5 モダンなオフィス (幅96) ======================
def modern_office():
    W = 96
    wall = concrete(hx(0xC4C8CC))
    TRIM, TRIM_LT = hx(0x6D7279), hx(0x9AA0A6)
    parts = {}
    gl_dk, gl_mid, gl_hi = hx(0x2A2420), hx(0x4E4034), hx(0x9A7A52)

    def ribbon(t, lit_idx=()):
        x0, x1, y0, h = 8, 88, 8, 14
        t.frame(x0 - 1, y0 - 1, x1 - x0 + 2, h + 2, OL)
        t.rect(x0, y0, x1 - x0, h, hx(0xE8EAEC))
        k = 0
        for x in range(x0 + 1, x1 - 1, 10):
            w = min(9, x1 - 1 - x)
            lit = k in lit_idx
            t.rect(x, y0 + 1, w, h - 2, hx(0xF6D88A) if lit else gl_dk)
            if not lit:
                for yy in range(h - 2):
                    for xx in range(w):
                        if 2 <= xx + yy <= 4 or 9 <= xx + yy <= 10:
                            t.put(x + xx, y0 + 1 + yy, gl_mid if xx + yy <= 4 else gl_hi)
            k += 1
        t.rect(x0 - 2, y0 + h + 1, x1 - x0 + 4, 2, hx(0xE8EAEC))
        t.rect(x0 - 2, y0 + h + 3, x1 - x0 + 4, 1, hx(0x8A8E92))

    for name, lit in (("mid_a", (2, 6)), ("mid_b", (0, 4))):
        t = Img(W, 32); t.fill(0, 32, wall); ribbon(t, lit)
        for y in range(32):
            for x in (0, 1, 2):
                t.put(x, y, TRIM if x else OL); t.put(W - 1 - x, y, TRIM_LT if x else OL)
        parts[name] = t
    t = Img(W, 32); t.fill(14, 32, wall)
    for y in range(14, 32):
        for x in (0, 1, 2):
            t.put(x, y, TRIM if x else OL); t.put(W - 1 - x, y, TRIM_LT if x else OL)
    for x in range(W):
        t.put(x, 11, OL)
        for y, c in ((12, hx(0xE0E3E6)), (13, hx(0xB8BCC0))):
            t.put(x, y, c)
    for px in (8, 24, 40):    # ソーラーパネル
        for yy in range(0, 9):
            slope = (8 - yy) // 3
            t.rect(px + slope, 2 + yy, 13, 1, hx(0x3E4656) if yy % 3 else hx(0x5C677A))
        t.frame(px - 1, 1, 17, 10, OL)
    for y in range(0, 11):
        t.put(66, y, hx(0x6E7078))
    t.rect(63, 2, 7, 1, hx(0x9A9CA6)); t.rect(64, 5, 5, 1, hx(0x9A9CA6))
    for ax in (74, 84):
        t.rect(ax, 3, 9, 7, hx(0x8A8E98)); t.frame(ax - 1, 2, 11, 9, OL); t.rect(ax, 3, 9, 2, hx(0xB0B4BE))
        t.rect(ax + 3, 6, 3, 3, hx(0x5A5E6A))
    parts["roof"] = t
    # 1階: ガラスのロビー
    t = Img(W, 32); t.fill(0, 24, wall)
    for y in range(24):
        for x in (0, 1, 2):
            t.put(x, y, TRIM if x else OL); t.put(W - 1 - x, y, TRIM_LT if x else OL)
    t.frame(7, 5, 82, 19, OL); t.rect(8, 6, 80, 18, hx(0xE8EAEC))
    for x in range(9, 87, 13):
        w = min(12, 87 - x)
        t.rect(x, 7, w, 16, gl_dk)
        for yy in range(16):
            for xx in range(w):
                if 3 <= xx + yy <= 6 or 12 <= xx + yy <= 14:
                    t.put(x + xx, 7 + yy, gl_mid if xx + yy <= 6 else gl_hi)
    t.rect(36, 2, 24, 3, hx(0xE0E3E6)); t.frame(35, 1, 26, 5, OL); t.rect(36, 5, 24, 1, hx(0x8A8E92))
    t.frame(41, 9, 14, 15, OL); t.rect(42, 10, 12, 14, hx(0xDDE3EC))
    t.rect(43, 11, 5, 12, gl_hi); t.rect(49, 11, 4, 12, gl_hi)
    for x in range(W):
        t.put(x, 24, hx(0x6C6C74)); t.put(x, 25, hx(0x4A4A52))
    bush(t, 14, 19); bush(t, 74, 19)
    sidewalk(t)
    parts["ground"] = t
    return parts


# ================================ D6 工場(煙突つき) (幅96) ========================
def factory():
    W = 96
    wall = dark_brick(hx(0x6E3C30), hx(0x7A4638), hx(0x62352A), hx(0x3A1E18))
    IRON, IRON_HI = hx(0x2A2D34), hx(0x484C56)
    parts = {}

    def steel_win(t, x0, lit):
        w, h = 16, 18
        t.frame(x0 - 1, 6, w + 2, h + 2, OL)
        t.rect(x0, 7, w, h, IRON)
        for gy in range(4):
            for gx in range(4):
                bx, by = x0 + 1 + gx * 4, 8 + gy * 4
                c = hx(0x7A7468) if (gx + gy) % 2 == 0 else hx(0x5A5348)
                if lit and (gx + gy) in (2, 3):
                    c = hx(0xF6D88A)
                t.rect(bx, by, 3, 3, c)
                t.put(bx, by, lighten(c, 0.35))
        t.rect(x0 - 2, 7 + h + 1, w + 4, 2, hx(0x9C8C80))
        t.rect(x0 - 2, 7 + h + 3, w + 4, 1, hx(0x3A2E28))
        t.rect(x0 - 2, 5, w + 4, 1, hx(0x9C8C80))   # まぐさ石

    for name, lit in (("mid_a", (False, True, False)), ("mid_b", (True, False, True))):
        t = Img(W, 32); t.fill(0, 32, wall)
        for x0, l in zip((12, 40, 68), lit):
            steel_win(t, x0, l)
        for y in range(32):
            for x in (0, 1, 2, 3):
                t.put(x, y, IRON if x else OL); t.put(W - 1 - x, y, IRON if x else OL)
            t.put(1, y, IRON_HI); t.put(W - 2, y, IRON_HI)
            if y % 8 == 3:
                t.put(2, y, hx(0xA0A4AE)); t.put(W - 3, y, hx(0xA0A4AE))
        parts[name] = t
    # 屋上: 大きな煙突 + 配管
    t = Img(W, 32); t.fill(16, 32, wall)
    for y in range(16, 32):
        for x in (0, 1, 2, 3):
            t.put(x, y, IRON if x else OL); t.put(W - 1 - x, y, IRON if x else OL)
        t.put(1, y, IRON_HI); t.put(W - 2, y, IRON_HI)
    for x in range(W):
        t.put(x, 9, OL)
        for y, c in zip(range(10, 16), (hx(0xB8A89C), hx(0xB8A89C), hx(0x9C8C80), hx(0x9C8C80), hx(0x6E5E54), hx(0x3A2E28))):
            t.put(x, y, c)
    # 煙突
    for y in range(0, 10):
        for x in range(76, 88):
            t.put(x, y, wall(x, y))
        t.put(75, y, OL); t.put(88, y, OL)
        t.put(76, y, lighten(hx(0x6E3C30), 0.12)); t.put(87, y, darken(hx(0x6E3C30), 0.2))
    t.rect(74, 0, 16, 2, hx(0x3A1E18)); t.rect(74, 0, 16, 1, hx(0x5A3A30))
    for y in (4, 7):
        t.rect(76, y, 12, 1, hx(0x3A1E18))
    # 配管と換気扇
    t.rect(10, 5, 4, 4, IRON); t.rect(10, 5, 4, 1, IRON_HI)
    for x in range(14, 40):
        t.put(x, 6, hx(0x8A8E98)); t.put(x, 7, hx(0xB0B4BE)); t.put(x, 8, hx(0x5A5E6A))
    t.rect(38, 2, 6, 7, hx(0x8A8E98)); t.frame(37, 1, 8, 9, OL); t.rect(39, 4, 2, 2, IRON)
    for vx in (52, 62):
        t.rect(vx, 4, 5, 5, hx(0x8A8E98)); t.frame(vx - 1, 3, 7, 7, OL); t.put(vx + 2, 6, IRON)
    parts["roof"] = t
    # 1階: 大きなシャッター + 通用口 + ランプ
    t = Img(W, 32); t.fill(0, 24, wall)
    for y in range(24):
        for x in (0, 1, 2, 3):
            t.put(x, y, IRON if x else OL); t.put(W - 1 - x, y, IRON if x else OL)
        t.put(1, y, IRON_HI); t.put(W - 2, y, IRON_HI)
    t.frame(13, 5, 34, 20, OL)
    base = hx(0x8A8F98)
    for y in range(6, 25):
        for x in range(14, 46):
            t.put(x, y, darken(base, 0.18) if y % 3 == 2 else base)
    for x in range(14, 46):
        t.put(x, 6, hx(0xC0C4CC)); t.put(x, 7, hx(0xC0C4CC))
    for x in range(14, 46):   # 足元の黄黒ストライプ
        for y in (22, 23, 24):
            t.put(x, y, hx(0xF2C94C) if ((x + y) // 3) % 2 == 0 else IRON)
    t.rect(28, 12, 4, 2, IRON)
    t.frame(60, 9, 12, 16, OL); t.rect(61, 10, 10, 15, hx(0x3A4A5A))
    t.rect(61, 10, 10, 1, hx(0x5A6A7A)); t.put(69, 18, hx(0xE0C060))
    t.rect(53, 8, 3, 3, IRON); t.rect(54, 9, 1, 1, hx(0xFFD75A))
    t.rect(77, 14, 12, 6, hx(0xF2C94C)); t.frame(77, 14, 12, 6, OL)
    for x in range(79, 88, 3):
        t.rect(x, 15, 1, 4, IRON)
    t.rect(0, 24, W, 2, hx(0x4A4A52))
    sidewalk(t)
    parts["ground"] = t
    return parts


# ================================ 固定サイズの建物 ================================
def hut():
    t = Img(32, 32)
    t.tri(2, 30, 3, 17, shingles(hx(0xC9A24E), hx(0x8E6A28)))
    for x in range(4, 28):
        t.put(x, 17, hx(0x6E5224)); t.put(x, 18, OL)
    t.rect(5, 18, 22, 11, hx(0xB9A98A)); t.frame(4, 18, 24, 12, OL)
    for y in range(19, 29):
        for x in range(5, 27):
            if (x + (y // 3) * 5) % 8 == 0 or y % 3 == 2:
                t.put(x, y, hx(0x8F7F5E))
    t.rect(13, 20, 6, 9, hx(0x6B4426)); t.frame(13, 20, 6, 9, OL)
    t.put(17, 25, hx(0xE0C060))
    t.rect(7, 21, 4, 4, GLASS_DK); t.frame(6, 20, 6, 6, OL); t.put(8, 22, GLASS_HI)
    t.rect(21, 21, 4, 4, hx(0xE8B44A)); t.frame(20, 20, 6, 6, OL)
    t.rect(21, 1, 4, 6, hx(0x9A9AA0)); t.frame(20, 0, 6, 8, OL)
    t.rect(4, 30, 24, 1, (0, 0, 0))
    for x in range(4, 28):
        t.put(x, 30, hx(0x40305A) if x % 2 else hx(0x2C2640))
    t.a[30, :, 3] = np.where(t.a[30, :, 3] > 0, 110, 0)
    return t


def house():
    t = Img(64, 64)
    t.tri(2, 62, 3, 27, shingles(hx(0xC25A30), hx(0x8E3A1C)))
    t.rect(46, 1, 7, 11, hx(0xA9553C)); t.frame(45, 0, 9, 12, OL)
    t.tri(2, 62, 3, 27, shingles(hx(0xC25A30), hx(0x8E3A1C)))
    for x in range(4, 60):
        t.put(x, 28, hx(0x7A4A2A)); t.put(x, 29, OL)
    wall = stucco(hx(0xF0E8D4))
    for y in range(30, 60):
        for x in range(6, 58):
            t.put(x, y, wall(x, y))
    t.frame(5, 30, 54, 30, OL)
    for y in range(30, 60):
        t.put(6, y, hx(0xFFFFFF)); t.put(57, y, hx(0xB8B0A0))
    for x0, lit in ((12, False), (40, True)):
        win_rect(t, x0, 33, 12, 12, hx(0x8A6A44), lit=lit, sill=hx(0x8A6A44))
    t.frame(26, 45, 12, 15, OL); t.rect(27, 46, 10, 14, hx(0x8A4A2A))
    glass(t, 29, 48, 6, 5)
    for y in range(46, 60):
        t.put(32, y, hx(0x6B3A20))
    t.put(35, 54, hx(0xE0C060))
    win_rect(t, 11, 48, 10, 8, hx(0x8A6A44), lit=True, sill=hx(0x8A6A44), mullion=False)
    win_rect(t, 43, 48, 10, 8, hx(0x8A6A44), lit=False, sill=hx(0x8A6A44), mullion=False)
    t.rect(24, 60, 16, 2, hx(0x9A9AA0)); t.rect(22, 62, 20, 2, hx(0x7A7A82))
    for x in range(8, 56):
        pass
    return t


def lodge():
    t = Img(96, 64)
    SH, SD = hx(0x4A5568), hx(0x2C3342)
    t.tri(0, 96, 3, 29, shingles(hx(0x56637A), SD))
    t.rect(70, 4, 10, 18, hx(0x8A8A90)); t.frame(69, 3, 12, 20, OL)
    for y in range(4, 22, 4):
        t.rect(70, y, 10, 1, hx(0x5A5A62))
    t.rect(70, 4, 10, 1, hx(0xB0B0B8))
    t.tri(0, 96, 3, 29, shingles(hx(0x56637A), SD))
    for x in range(2, 94):
        t.put(x, 30, hx(0x6B4426)); t.put(x, 31, OL)
    # 丸太の壁
    def log(x, y):
        ly = y % 5
        if ly == 4:
            return hx(0x3A2412)
        c = hx(0x9A6A3C) if (x // 7 + y // 5) % 3 else hx(0x8A5A30)
        if ly == 0:
            c = hx(0xB8844C)
        elif ly == 3:
            c = darken(c, 0.15)
        return c
    for y in range(32, 62):
        for x in range(5, 91):
            t.put(x, y, log(x, y))
    t.frame(4, 32, 88, 30, OL)
    for y in range(32, 62):   # 丸太の小口(角)
        for x in (5, 6, 88, 89):
            t.put(x, y, hx(0xD9A86C) if (y // 5) % 2 else hx(0xC59555))
    # ドア・窓
    t.frame(41, 41, 14, 21, OL); t.rect(42, 42, 12, 20, hx(0x5A3A22))
    for y in range(42, 62):
        t.put(48, y, hx(0x3A2412)); t.put(47, y, hx(0x3A2412))
    glass(t, 43, 44, 4, 6, lit=True); glass(t, 49, 44, 4, 6, lit=True)
    for x0, lit in ((9, True), (21, False), (66, False), (78, True)):
        win_rect(t, x0, 40, 10, 10, hx(0xEFE6D2), lit=lit, sill=hx(0xEFE6D2))
    # ポーチ(庇と柱)
    t.rect(34, 34, 28, 3, hx(0x4A2E1A)); t.frame(33, 33, 30, 5, OL)
    for px in (35, 59):
        t.rect(px, 37, 3, 25, hx(0x6B4426)); t.frame(px - 1, 37, 5, 25, OL)
    for x in range(36, 60, 4):
        t.rect(x, 51, 1, 8, hx(0x6B4426))
    t.rect(36, 51, 24, 2, hx(0x6B4426))
    t.rect(30, 62, 36, 2, hx(0x9A9AA0)); t.rect(27, 64 - 1, 42, 1, hx(0x7A7A82))
    # ランタン
    t.rect(30, 44, 3, 5, hx(0x2B2B30)); t.rect(31, 45, 1, 3, hx(0xFFD75A))
    t.rect(63, 44, 3, 5, hx(0x2B2B30)); t.rect(64, 45, 1, 3, hx(0xFFD75A))
    return t


def main():
    os.makedirs(OUT, exist_ok=True)
    towers = {
        "stone_tower": stone_tower(), "cottage_tower": cottage_tower(), "tile_shop": tile_shop(),
        "timber_inn": timber_inn(), "modern_office": modern_office(), "factory": factory(),
    }
    for name, parts in towers.items():
        for pn, im in parts.items():
            save(im, f"building_{name}_{pn}")
    fixed = {"hut_1x1": hut(), "house_2x2": house(), "lodge_3x2": lodge()}
    for name, im in fixed.items():
        save(im, f"building_{name}")

    # ---- 確認用: 各ビルを 屋上+中階x3+1階 で組み立て、横に並べる ----
    bg = (22, 40, 74, 255)
    cols = []
    for name, parts in towers.items():
        rows = [parts["roof"].a] + [parts["mid_a" if i % 2 == 0 else "mid_b"].a for i in range(3)] + [parts["ground"].a]
        cols.append(np.concatenate(rows, 0))
    gap = 12
    Hh = max(c.shape[0] for c in cols) + 16
    Ww = sum(c.shape[1] for c in cols) + gap * (len(cols) + 1)
    sheet = Image.new("RGBA", (Ww, Hh), bg)
    x = gap
    for c in cols:
        sheet.alpha_composite(Image.fromarray(c, "RGBA"), (x, 8 + (Hh - 16 - c.shape[0])))
        x += c.shape[1] + gap
    sheet.resize((sheet.width * 3, sheet.height * 3), Image.NEAREST).save(f"{OUT}/preview_towers_x3.png")
    # 固定サイズ3種
    sheet2 = Image.new("RGBA", (32 + 64 + 96 + gap * 4, 64 + 16), bg)
    x = gap
    for name, im in fixed.items():
        sheet2.alpha_composite(Image.fromarray(im.a, "RGBA"), (x, 8 + 64 - im.h))
        x += im.w + gap
    sheet2.resize((sheet2.width * 4, sheet2.height * 4), Image.NEAREST).save(f"{OUT}/preview_fixed_x4.png")


if __name__ == "__main__":
    main()
