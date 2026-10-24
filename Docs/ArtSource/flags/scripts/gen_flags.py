"""拠点の旗 10色 + 8コマのはためきアニメ(各色)。 64x64 / フレーム。
出力: node_flag_<色>.png (静止画 64x64), node_flag_<色>_anim.png (4列x2行 = 256x128)
使い方: python3 gen_flags.py [出力ディレクトリ]
"""
import os
import sys
import numpy as np
from PIL import Image

W, H = 64, 64
OUT = sys.argv[1] if len(sys.argv) > 1 else "flags"
OUTLINE = (0x2B, 0x17, 0x17)
POLE_HI, POLE_MID, POLE_DK = (0xA8, 0x70, 0x3F), (0x8C, 0x5A, 0x36), (0x6B, 0x40, 0x26)
BALL_HI = (0xC8, 0x90, 0x5A)


def c(h):
    return ((h >> 16) & 255, (h >> 8) & 255, h & 255)


# 色ごとのパレット: cloth=(明, 基本, 影, 最暗) / border=(明, 中, 暗) / star=(明, 暗)
PALETTES = {
    "navy":   dict(cloth=(0x303B70, 0x27305C, 0x1E2548, 0x181D3A), border=(0xF7BC3C, 0xE8861F, 0xB8581C), star=(0xF0C04E, 0xCE9A2E)),
    "red":    dict(cloth=(0xC63C3C, 0xB02A2E, 0x8E1F26, 0x6E1620), border=(0xFFE08A, 0xF2C24A, 0xC28A22), star=(0xF6EFD8, 0xD8CDAE)),
    "green":  dict(cloth=(0x3F9650, 0x2F7D3E, 0x235E30, 0x1A4824), border=(0xEDEFD8, 0xC8CFA8, 0x8E9670), star=(0xF0C04E, 0xCE9A2E)),
    "purple": dict(cloth=(0x7F52BA, 0x6A3FA0, 0x523080, 0x3E2362), border=(0xFFC8E8, 0xE88CC4, 0xB05A94), star=(0xF0C04E, 0xCE9A2E)),
    "black":  dict(cloth=(0x3C3E4A, 0x2E2F38, 0x23242B, 0x1A1B20), border=(0xE0E4EA, 0xAEB6C2, 0x7A8494), star=(0xF4F1E8, 0xC9C4B4), outline=(0x0C, 0x0C, 0x10)),
    "white":  dict(cloth=(0xF6F3EC, 0xE8E4DA, 0xC9C4B8, 0xAAA597), border=(0x8FA6C8, 0x5C79A8, 0x3B547E), star=(0xC8433A, 0x9A2E2E)),
    "sky":    dict(cloth=(0x6BB8EE, 0x4FA3E0, 0x3A83BC, 0x2D6694), border=(0xF4FAFF, 0xC8E2F6, 0x8FB8D8), star=(0xF5C94E, 0xD09A2E)),
    "yellow": dict(cloth=(0xF8D459, 0xF0C030, 0xD09E1C, 0xA87C12), border=(0xC98A4B, 0x9A5F2E, 0x6B3E1E), star=(0xC8433A, 0x9A2E2E)),
    "pink":   dict(cloth=(0xF288B4, 0xE86FA0, 0xC45582, 0x9C3D66), border=(0xFFF0F6, 0xF8C8DC, 0xD090AC), star=(0xF7D44E, 0xD9A62E)),
    "teal":   dict(cloth=(0x3DBBB0, 0x2AA39A, 0x1F807A, 0x17605C), border=(0xF7E3A0, 0xE4BE62, 0xB08A38), star=(0xF4F1E8, 0xC9C4B4)),
}

STAR = [
    "....X....",
    "...XXX...",
    "XXXXXXXXX",
    ".XXXXXXX.",
    "..XXXXX..",
    "..XXXXX..",
    ".XXX.XXX.",
    ".XX...XX.",
]
X0, YTOP, YBOT, XTIP, YTIP = 7, 8, 38, 58, 23


def conv(t):
    return tuple(c(v) if isinstance(v, int) else v for v in t)


def make_layers(name):
    p = PALETTES[name]
    cl_l, cl_b, cl_s, cl_d = conv(p["cloth"])
    b_l, b_m, b_d = conv(p["border"])
    st_l, st_d = conv(p["star"])
    outline = p.get("outline", OUTLINE)
    pole = np.zeros((H, W, 4), np.uint8)
    cloth = np.zeros((H, W, 4), np.uint8)

    def put(img, x, y, col):
        if 0 <= x < W and 0 <= y < H:
            img[y, x] = (*col, 255)

    # 旗竿
    for y in range(7, H - 1):
        for x, col in ((2, OUTLINE), (3, POLE_HI), (4, POLE_MID), (5, POLE_DK), (6, OUTLINE)):
            put(pole, x, y, col)
    for x in range(3, 6):
        put(pole, x, H - 1, OUTLINE)
    for y in range(1, 8):
        for x in range(1, 8):
            d = ((x - 4) ** 2 + (y - 4) ** 2) ** 0.5
            if d <= 3.3:
                put(pole, x, y, OUTLINE if d > 2.4 else POLE_MID)
    for (x, y) in [(3, 3), (4, 3), (3, 4)]:
        put(pole, x, y, BALL_HI)
    for (x, y) in [(5, 5), (5, 4), (4, 5)]:
        put(pole, x, y, POLE_DK)

    # 布
    slope = (YTIP - YTOP) / (XTIP - X0)
    cos = 1 / (1 + slope ** 2) ** 0.5
    for y in range(YTOP, YBOT + 1):
        for x in range(X0, XTIP + 1):
            yt = YTOP + (x - X0) * slope
            yb = YBOT - (x - X0) * slope
            cy = y + 0.5
            if not (yt <= cy <= yb):
                continue
            pt, pb, pl = (cy - yt) * cos, (yb - cy) * cos, (x + 0.5) - X0
            q = min(pt, pb, pl)
            if q < 1:
                put(cloth, x, y, outline)
            elif q < 3:
                if pt <= pb and pt <= pl:
                    col = b_l if pt < 2 else b_m
                elif pb <= pl:
                    col = b_d if pb < 2 else b_m
                else:
                    col = b_l if pl < 2 else b_m
                put(cloth, x, y, col)
            else:
                col = cl_b
                if x >= 38:
                    col = cl_s
                if x >= 49 or (x >= 44 and pb < 4.2):
                    col = cl_d
                put(cloth, x, y, col)
    sx, sy = 20, 19
    for j, row in enumerate(STAR):
        for i, ch in enumerate(row):
            if ch == "X":
                put(cloth, sx + i, sy + j, st_d if j >= 5 else st_l)
    for (cx, cy) in [(12, 16), (13, 30), (34, 20)]:
        for dx, dy in [(0, 0), (-1, 0), (1, 0), (0, -1), (0, 1)]:
            put(cloth, cx + dx, cy + dy, st_l)
    for (x, y) in [(17, 24), (31, 27), (26, 15)]:
        put(cloth, x, y, st_l)
    return pole, cloth, dict(light=cl_l, base=cl_b, shade=cl_s, deep=cl_d)


def wave_offsets(k, n=8):
    """フレーム k の各列の縦ずれ(px)。竿側は0、先端ほど大きく、k で位相が進む。"""
    offs = np.zeros(W, int)
    ph = 2 * np.pi * k / n
    for x in range(X0, XTIP + 1):
        amp = 3.0 * min(1.0, (x - X0) / 30.0)
        offs[x] = int(round(amp * np.sin(2 * np.pi * (x - X0) / 34.0 - ph)))
    return offs


def render_frame(pole, cloth, tones, k):
    offs = wave_offsets(k)
    out = pole.copy()
    moved = np.zeros_like(cloth)
    for x in range(W):
        o = offs[x]
        col = cloth[:, x]
        # 傾きで明暗をつける(上り坂 = 明るい面, 下り坂 = 暗い面)
        sl = (offs[min(W - 1, x + 3)] - offs[max(0, x - 3)]) if X0 < x < XTIP else 0
        for y in range(H):
            if col[y, 3] == 0:
                continue
            ny = y + o
            if not (0 <= ny < H):
                continue
            px = col[y].copy()
            rgb = tuple(int(v) for v in px[:3])
            if rgb == tones["base"]:
                if sl <= -3:
                    px[:3] = tones["light"]
                elif sl >= 3:
                    px[:3] = tones["shade"]
            moved[ny, x] = px
    m = moved[:, :, 3] > 0
    out[m] = moved[m]
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    grass = [np.array(Image.open(f"grass_tiles/tile_ground_0{i}.png").convert("RGBA")) for i in (1, 2, 3, 4)]
    bg = np.concatenate([np.concatenate(grass[:2], 1), np.concatenate(grass[2:], 1)], 0)

    statics = {}
    for name in PALETTES:
        pole, cloth, tones = make_layers(name)
        rest = pole.copy()
        m = cloth[:, :, 3] > 0
        rest[m] = cloth[m]
        statics[name] = rest
        Image.fromarray(rest, "RGBA").save(f"{OUT}/node_flag_{name}.png")
        frames = [render_frame(pole, cloth, tones, k) for k in range(8)]
        sheet = np.zeros((2 * H, 4 * W, 4), np.uint8)
        for k, f in enumerate(frames):
            r, cc = divmod(k, 4)
            sheet[r * H:(r + 1) * H, cc * W:(cc + 1) * W] = f
        Image.fromarray(sheet, "RGBA").save(f"{OUT}/node_flag_{name}_anim.png")
        # アニメ確認用GIF(草原の上)
        gif = []
        for f in frames:
            comp = Image.fromarray(bg, "RGBA")
            comp.alpha_composite(Image.fromarray(f, "RGBA"))
            gif.append(comp.convert("RGB").resize((W * 4, H * 4), Image.NEAREST))
        gif[0].save(f"{OUT}/preview_{name}_anim.gif", save_all=True, append_images=gif[1:], duration=140, loop=0)

    # 10色の一覧(草原の上, 5列x2行)
    names = list(PALETTES)
    big = np.zeros((2 * H, 5 * W, 4), np.uint8)
    for i, n in enumerate(names):
        r, cc = divmod(i, 5)
        tile = Image.fromarray(bg, "RGBA")
        tile.alpha_composite(Image.fromarray(statics[n], "RGBA"))
        big[r * H:(r + 1) * H, cc * W:(cc + 1) * W] = np.array(tile)
    Image.fromarray(big, "RGBA").resize((5 * W * 3, 2 * H * 3), Image.NEAREST).save(f"{OUT}/preview_all_colors.png")


if __name__ == "__main__":
    main()
