"""乾いた土の道タイル生成 (32x32, 継ぎ目なし)。
すべての描画をトーラス(端が反対側につながる)上で行うので、上下左右どの向きに並べてもつながる。
使い方: python3 gen_dirt_tiles.py [出力ディレクトリ]
"""
import sys
import numpy as np
from PIL import Image

T = 32
OUT = sys.argv[1] if len(sys.argv) > 1 else "dirt_tiles"

# ---- パレット(暖色の乾いた土 + 灰褐色の砂利) ----
DIRT = [  # 明 -> 暗
    (0xD2, 0xA9, 0x72),
    (0xC4, 0x98, 0x62),
    (0xB4, 0x87, 0x56),
    (0xA0, 0x74, 0x48),
]
DIRT_SPECK_DARK = (0x7E, 0x58, 0x38)
DIRT_SPECK_LIGHT = (0xE0, 0xBC, 0x88)
SHADOW = (0x6E, 0x4D, 0x30)
STONES = [  # (明, 中, 暗)
    ((0xE2, 0xD6, 0xBE), (0xB5, 0xA8, 0x93), (0x86, 0x7A, 0x69)),  # 灰ベージュ
    ((0xD8, 0xC2, 0xA6), (0xAC, 0x93, 0x78), (0x7E, 0x69, 0x54)),  # 黄みの石
    ((0xCC, 0xA0, 0x80), (0xA3, 0x72, 0x55), (0x76, 0x4E, 0x38)),  # 赤みの石
]


def wrap_blur(a, passes):
    for _ in range(passes):
        a = (a * 4 + np.roll(a, 1, 0) + np.roll(a, -1, 0)
             + np.roll(a, 1, 1) + np.roll(a, -1, 1)) / 8.0
    return a


def make_base(rng):
    """土の濃淡(かたまり感のあるノイズ)を4階調に量子化。"""
    n = wrap_blur(rng.random((T, T)), 3) + 0.12 * rng.random((T, T))
    q = np.quantile(n, [0.22, 0.55, 0.85])
    idx = np.digitize(n, q)  # 0..3  (0=明 側にしたいので反転)
    idx = 3 - idx
    img = np.zeros((T, T, 3), np.uint8)
    for i, c in enumerate(DIRT):
        img[idx == i] = c
    return img


def put(img, x, y, c):
    img[y % T, x % T] = c


def speckles(img, rng, n_dark, n_light):
    for _ in range(n_dark):
        put(img, rng.integers(T), rng.integers(T), DIRT_SPECK_DARK)
    for _ in range(n_light):
        put(img, rng.integers(T), rng.integers(T), DIRT_SPECK_LIGHT)


def ellipse_mask(w, h, rng):
    m = np.zeros((h, w), bool)
    cx, cy = (w - 1) / 2, (h - 1) / 2
    for y in range(h):
        for x in range(w):
            dx = (x - cx) / (w / 2)
            dy = (y - cy) / (h / 2)
            if dx * dx + dy * dy <= 1.05:
                m[y, x] = True
    if w >= 4 and h >= 3 and rng.random() < 0.5:
        m[rng.integers(h), rng.choice([0, w - 1])] = False  # いびつにする
    return m


def draw_stone(img, occ, ox, oy, w, h, pal, rng):
    m = ellipse_mask(w, h, rng)
    light, mid, dark = pal

    def inside(x, y):
        return 0 <= x < w and 0 <= y < h and m[y, x]

    # 影(右下に1px) を先に塗る(石本体で上書きされる)
    for y in range(h):
        for x in range(w):
            if m[y, x]:
                for sx, sy in ((x + 1, y + 1), (x + 1, y), (x, y + 1)):
                    if not inside(sx, sy):
                        put(img, ox + sx, oy + sy, SHADOW)
    for y in range(h):
        for x in range(w):
            if not m[y, x]:
                continue
            up, left = inside(x, y - 1), inside(x - 1, y)
            down, right = inside(x, y + 1), inside(x + 1, y)
            if (not up or not left) and down and right:
                c = light
            elif (not down or not right) and up and left:
                c = dark
            else:
                c = mid
            if w <= 2 and h <= 2:
                c = mid if (x + y) % 2 else light
            put(img, ox + x, oy + y, c)
    # 占有マップ(周囲1pxの余白込み)
    for y in range(-1, h + 2):
        for x in range(-1, w + 2):
            occ[(oy + y) % T, (ox + x) % T] = True


def scatter_stones(img, rng, count):
    occ = np.zeros((T, T), bool)
    sizes = [(2, 2), (3, 2), (2, 3), (3, 3), (4, 3), (3, 4), (4, 4), (5, 3)]
    weights = np.array([3, 4, 3, 4, 3, 3, 2, 2], float)
    weights /= weights.sum()
    placed = 0
    tries = 0
    while placed < count and tries < 4000:
        tries += 1
        w, h = sizes[rng.choice(len(sizes), p=weights)]
        ox, oy = rng.integers(T), rng.integers(T)
        region = [occ[(oy + y) % T, (ox + x) % T]
                  for y in range(h + 1) for x in range(w + 1)]
        if any(region):
            continue
        pal = STONES[rng.choice(3, p=[0.55, 0.3, 0.15])]
        draw_stone(img, occ, ox, oy, w, h, pal, rng)
        placed += 1
    return placed


def cracks(img, rng, n):
    """細かいひび(2px幅の短い暗線ではなく、点を連ねた乾燥ひび)。"""
    for _ in range(n):
        x, y = rng.integers(T), rng.integers(T)
        for _ in range(rng.integers(3, 6)):
            put(img, x, y, DIRT[3])
            x += rng.choice([-1, 0, 1, 1])
            y += rng.choice([0, 1, 1])


def make_tile(seed, stones):
    rng = np.random.default_rng(seed)
    img = make_base(rng)
    cracks(img, rng, 2)
    speckles(img, rng, 12, 9)
    scatter_stones(img, rng, stones)
    return img


def main():
    import os
    os.makedirs(OUT, exist_ok=True)
    seeds = [11, 23, 37, 52]
    stones = [17, 16, 18, 16]  # 砂利多め
    tiles = []
    for i, (s, n) in enumerate(zip(seeds, stones), 1):
        t = make_tile(s, n)
        Image.fromarray(t, "RGB").save(f"{OUT}/tile_dirt_{i:02d}.png")
        tiles.append(t)

    # ---- 確認用プレビュー: 8x6 マスにランダム敷き詰め, 3倍拡大 ----
    rng = np.random.default_rng(1)
    cols, rows, scale = 8, 6, 3
    sheet = np.zeros((rows * T, cols * T, 3), np.uint8)
    for r in range(rows):
        for c in range(cols):
            sheet[r * T:(r + 1) * T, c * T:(c + 1) * T] = tiles[rng.integers(len(tiles))]
    Image.fromarray(sheet).resize((cols * T * scale, rows * T * scale), Image.NEAREST) \
        .save(f"{OUT}/preview_mixed.png")

    # 1枚だけを繰り返した場合(継ぎ目の最悪ケース確認)
    for i, t in enumerate(tiles, 1):
        s = np.tile(t, (4, 4, 1))
        Image.fromarray(s).resize((s.shape[1] * 3, s.shape[0] * 3), Image.NEAREST) \
            .save(f"{OUT}/preview_single_{i:02d}.png")


if __name__ == "__main__":
    main()
