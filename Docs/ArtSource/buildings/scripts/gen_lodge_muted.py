"""落ち着いた色合いのロッジ(参考: 暗い赤茶の急な屋根・丸太の小口・屋根裏窓・暖色の灯り)。
 building_lodge_3x2.png (96x64) / building_lodge_3x3.png (96x96)
使い方: python3 gen_lodge_muted.py [出力ディレクトリ]
"""
import os
import sys
import numpy as np
from PIL import Image

import gen_buildings_more as gm
from gen_buildings_more import Img, hx, lighten, darken

OUT = sys.argv[1] if len(sys.argv) > 1 else "lodge_muted"
OL = hx(0x1C130E)
WOOD = [hx(0x8A5A30), hx(0x7A4A26), hx(0x6A3E1E), hx(0x96623A)]
SEAM, WOOD_HI = hx(0x2E1C10), hx(0xA8743F)
TRIM, TRIM_HI = hx(0x8A5424), hx(0xA87038)
RED, RED_DK, RED_LT = hx(0x6A2018), hx(0x3C100C), hx(0x7E2E22)
BOARD = [hx(0x3A2A1C), hx(0x2C2016), hx(0x4A3624)]
GLOW, GLOW_OR = hx(0xF0B050), hx(0xD9782A)
ST = [hx(0x6A645A), hx(0x7A7468), hx(0x504B42)]
GREEN = hx(0x4E7A3A)


def roof_fn(cx):
    def f(x, y):
        row = y // 3
        off = 2 * (row % 2)
        if y % 3 == 2:
            c = RED_DK
        elif (x + off) % 5 == 4:
            c = darken(RED, 0.22)
        else:
            c = RED_LT if y % 3 == 0 else RED
        if x > cx:
            c = darken(c, 0.18)       # 右半分は影
        return c
    return f


def lodge(H):
    W = 96
    t = Img(W, H)
    roof_h = H - 34
    y_b = 2 + roof_h                  # 屋根の下端
    wall_top, y_g = y_b + 2, H - 6    # 丸太壁の上端 / 地面
    cx = 48
    # --- 屋根(外側の大きな三角) ---
    t.tri(1, 95, 2, y_b, roof_fn(cx), outline=OL)
    for x in range(0, 96):            # 軒先
        t.put(x, y_b + 1, OL)
    # --- 煙突 ---
    def roof_y(x):
        return 2 + int(abs(x - cx) / 47 * roof_h)
    chy = roof_y(74)
    for y in range(chy - 11, chy + 4):
        for x in range(70, 78):
            c = ST[(x + y // 3) % 3]
            if x == 70:
                c = lighten(ST[1], 0.1)
            if x == 77:
                c = ST[2]
            t.put(x, y, c)
        t.put(69, y, OL); t.put(78, y, OL)
    for x in range(68, 80):
        t.put(x, chy - 12, OL); t.put(x, chy - 11, ST[2])
    t.rect(70, chy - 11, 8, 1, hx(0x3A3630))
    for (sx, sy) in [(73, chy - 14), (74, chy - 15), (73, chy - 16), (72, chy - 17), (73, chy - 18), (75, chy - 19)]:
        t.put(sx, sy, hx(0xB8B0A4))
    # --- 妻面(正面の三角) ---
    y_a = 2 + int(roof_h * 0.40)
    gb = y_b - 1
    def board(x, y):
        return BOARD[(y // 3 + (x // 11)) % 3] if y % 3 != 2 else hx(0x1E160E)
    t.tri(8, 88, y_a, gb, board, outline=OL)
    # 破風の飾り板(傾斜に沿って)
    for y in range(y_a, gb + 1):
        half = (y - y_a + 0.5) / (gb - y_a + 1) * 40
        xl, xr = int(round(cx - half)), int(round(cx + half))
        for k in range(2):
            t.put(xl + k, y, TRIM_HI if k == 0 else TRIM)
            t.put(xr - 1 - k, y, TRIM if k == 0 else darken(TRIM, 0.2))
    # 屋根裏窓
    wy = y_a + int((gb - y_a) * 0.42)
    t.rect(cx - 8, wy - 2, 16, 2, TRIM_HI); t.frame(cx - 8, wy - 3, 16, 3, OL)
    t.rect(cx - 6, wy, 12, 9, OL)
    t.rect(cx - 5, wy + 1, 10, 7, GLOW)
    for yy in range(3):
        for xx in range(2):
            t.put(cx - 5 + xx, wy + 1 + yy, GLOW_OR); t.put(cx + 4 - xx, wy + 1 + yy, GLOW_OR)
    t.rect(cx - 1, wy + 1, 2, 7, TRIM); t.rect(cx - 5, wy + 4, 10, 1, TRIM)
    t.rect(cx - 8, wy + 9, 16, 2, TRIM); t.frame(cx - 8, wy + 9, 16, 2, OL)
    # --- 軒の梁 ---
    for x in range(6, 91):
        t.put(x, y_b, OL)
        t.put(x, y_b + 1, hx(0x5A3A1E))
    # --- 丸太の壁 ---
    wl, wr = 11, 85
    n = (y_g - wall_top) // 4
    for c in range(n):
        y0 = wall_top + c * 4
        base = WOOD[(c * 3 + 1) % 4]
        for x in range(wl, wr + 1):
            for yy in range(4):
                if yy == 3:
                    col = SEAM
                elif yy == 0:
                    col = WOOD_HI
                else:
                    col = base if ((x // 9 + c) % 3) else darken(base, 0.1)
                t.put(x, y0 + yy, col)
        # 丸太の小口(左右の端)
        for ex, side in ((wl - 6, 0), (wr + 1, 1)):
            for yy in range(4):
                for xx in range(6):
                    col = hx(0xB48650) if 1 <= xx <= 4 and 0 <= yy <= 2 else WOOD[1]
                    if (xx, yy) in ((2, 1), (3, 1)):
                        col = hx(0x8A5A30)
                    if yy == 3:
                        col = SEAM
                    t.put(ex + xx, y0 + yy, col)
    for y in range(wall_top, y_g):
        t.put(wl - 7, y, OL); t.put(wr + 7, y, OL)
    # --- 左の窓 + 花箱 ---
    wx, wy2 = 17, wall_top + 3
    t.rect(wx - 3, wy2 - 3, 17, 3, TRIM_HI); t.frame(wx - 3, wy2 - 3, 17, 3, OL)
    t.rect(wx - 2, wy2, 15, 12, OL)
    t.rect(wx - 1, wy2 + 1, 13, 10, TRIM)
    t.rect(wx, wy2 + 2, 11, 8, GLOW)
    for yy in range(3):
        t.put(wx, wy2 + 2 + yy, GLOW_OR); t.put(wx + 10, wy2 + 2 + yy, GLOW_OR)
    t.rect(wx + 5, wy2 + 2, 1, 8, TRIM); t.rect(wx, wy2 + 5, 11, 1, TRIM)
    t.rect(wx - 2, wy2 + 12, 15, 4, hx(0x5A3A1E)); t.frame(wx - 2, wy2 + 12, 15, 4, OL)
    for i in range(13):
        t.put(wx - 1 + i, wy2 + 12, [GREEN, hx(0xD9822B), GREEN, hx(0xE0B860)][i % 4])
    # --- ランタン ---
    lx = 36
    t.rect(lx, wall_top + 7, 5, 7, hx(0x3A3A40)); t.rect(lx + 1, wall_top + 9, 3, 3, GLOW)
    t.put(lx + 2, wall_top + 8, GLOW_OR)
    # --- 扉 ---
    dx = 42
    t.rect(dx - 3, wall_top - 1, 22, 4, RED); t.frame(dx - 3, wall_top - 1, 22, 4, OL)
    for x in range(dx - 2, dx + 17, 4):
        t.rect(x, wall_top, 1, 2, RED_DK)
    t.rect(dx - 3, wall_top + 3, 22, 1, TRIM)
    t.frame(dx - 1, wall_top + 4, 18, y_g - wall_top - 3, OL)
    t.rect(dx, wall_top + 4, 2, y_g - wall_top - 4, TRIM); t.rect(dx + 14, wall_top + 4, 2, y_g - wall_top - 4, TRIM)
    t.rect(dx + 2, wall_top + 4, 12, y_g - wall_top - 4, WOOD[2])
    for x in range(dx + 2, dx + 14, 3):
        t.rect(x, wall_top + 4, 1, y_g - wall_top - 4, hx(0x4A2C14))
    for yy in (wall_top + 8, y_g - 7):
        t.rect(dx + 2, yy, 12, 2, hx(0x4A4A52))
    t.rect(dx + 11, wall_top + 12, 1, 4, hx(0x9A9AA2))
    t.rect(dx - 1, y_g, 18, 2, WOOD[1]); t.rect(dx + 3, y_g, 10, 2, hx(0x3A3228))
    # --- 右: 鹿の角 + 看板 ---
    ax, ay = 66, wall_top + 2
    for (x, y) in [(0, 4), (1, 3), (1, 2), (2, 1), (2, 0), (3, 4), (4, 4), (5, 4), (6, 3), (6, 2), (7, 1), (7, 0), (3, 2), (6, 1)]:
        t.put(ax + 3 + x, ay + y, hx(0xB49864))
    t.rect(ax + 4, ay + 4, 4, 2, hx(0x7A4A26))
    t.rect(ax - 2, ay + 8, 20, 7, hx(0x7A4A26)); t.frame(ax - 2, ay + 8, 20, 7, OL)
    t.frame(ax - 1, ay + 9, 18, 5, TRIM)
    for x in range(ax + 1, ax + 15, 3):
        t.rect(x, ay + 11, 2, 1, hx(0xC8A870))
    # --- 足元: 石の基礎・草・シャベル・薪 ---
    for x in range(wl - 6, wr + 7):
        for yy in range(3):
            col = ST[((x // 6) + yy) % 3]
            if yy == 2:
                col = ST[2]
            t.put(x, y_g + yy, col)
        t.put(x, y_g + 3, OL)
    for gx in (wl - 8, wl - 4, wr + 4, wr + 8, 30, 70):
        t.put(gx, y_g + 3, GREEN); t.put(gx + 1, y_g + 2, GREEN); t.put(gx, y_g + 2, hx(0x6E9A4C))
    sx = 66
    for y in range(y_g - 13, y_g):
        t.put(sx, y, hx(0x8A5A30)); t.put(sx + 1, y, WOOD[2])
    t.rect(sx - 1, y_g - 14, 4, 2, hx(0x8A8A92))
    t.rect(sx - 1, y_g - 3, 4, 4, hx(0x7A7A82)); t.put(sx, y_g, hx(0x9A9AA2))
    for r, (fx, fy) in enumerate([(73, y_g - 3), (77, y_g - 3), (81, y_g - 3), (75, y_g - 7), (79, y_g - 7)]):
        t.rect(fx, fy, 4, 4, hx(0xA06C3A)); t.frame(fx, fy, 4, 4, OL)
        t.put(fx + 1, fy + 1, hx(0xC08A50)); t.put(fx + 2, fy + 2, hx(0x7A4A26))
    # ベンチ
    t.rect(20, y_g - 3, 12, 2, hx(0x7A4A26)); t.frame(20, y_g - 3, 12, 2, OL)
    for lx2 in (21, 30):
        t.rect(lx2, y_g - 1, 1, 3, OL)
    # 飛び石
    if False:
        for (x, y, w) in [(44, y_g + 6, 6), (53, y_g + 4, 8), (47, y_g + 10, 4)]:
            t.rect(x, y, w, 3, ST[0]); t.frame(x, y, w, 3, OL)
            t.put(x + 1, y, ST[1])
    return t


def main():
    os.makedirs(OUT, exist_ok=True)
    for H, name in ((64, "building_lodge_3x2"), (96, "building_lodge_3x3")):
        im = lodge(H)
        Image.fromarray(im.a, "RGBA").save(f"{OUT}/{name}.png")
    bg = (30, 34, 40, 255)
    sheet = Image.new("RGBA", (96 * 2 + 36, 96 + 16), bg)
    for i, name in enumerate(("building_lodge_3x2", "building_lodge_3x3")):
        im = Image.open(f"{OUT}/{name}.png").convert("RGBA")
        sheet.alpha_composite(im, (12 + i * 108, 8 + 96 - im.height))
    sheet.resize((sheet.width * 5, sheet.height * 5), Image.NEAREST).save(f"{OUT}/preview_lodge_x5.png")


if __name__ == "__main__":
    main()
