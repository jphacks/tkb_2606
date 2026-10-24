"""草原タイル(土地)+ 壁との境目タイルの生成。gen_border_tiles.py の土地部分だけを草原に差し替える。
使い方: python3 gen_grass_tiles.py [出力ディレクトリ]
"""
import sys
import numpy as np
import gen_dirt_tiles as gd
import gen_border_tiles as gb

T = 32
GRASS = [(0xAE, 0xC6, 0x5E), (0xA2, 0xBD, 0x54), (0x95, 0xB2, 0x4B), (0x88, 0xA6, 0x43)]  # 明->暗
BLADE_DARK = (0x6E, 0x90, 0x3A)
BLADE_LIGHT = (0xC8, 0xDA, 0x7A)
TUFT_WARM = (0xBC, 0xCC, 0x62)   # 黄みの明るい房


def make_grass(seed, stones=0):
    rng = np.random.default_rng(seed)
    n = gd.wrap_blur(rng.random((T, T)), 3) + 0.10 * rng.random((T, T))
    q = np.quantile(n, [0.25, 0.55, 0.85])
    idx = 3 - np.digitize(n, q)
    img = np.zeros((T, T, 3), np.uint8)
    for i, c in enumerate(GRASS):
        img[idx == i] = c
    # 草の葉先: V字の短い房 (3x2 の暗い葉 + 先端の明るい点)
    occ = np.zeros((T, T), bool)
    placed, tries = 0, 0
    while placed < 11 and tries < 400:
        tries += 1
        x, y = int(rng.integers(T)), int(rng.integers(T))
        cells = [((x + dx) % T, (y + dy) % T) for dx in range(-1, 5) for dy in range(-2, 3)]
        if any(occ[cy, cx] for cx, cy in cells):
            continue
        light = BLADE_LIGHT if rng.random() < 0.65 else TUFT_WARM
        gd.put(img, x, y, BLADE_DARK)
        gd.put(img, x + 1, y + 1, BLADE_DARK)
        gd.put(img, x + 2, y, BLADE_DARK)
        gd.put(img, x, y - 1, light)
        gd.put(img, x + 2, y - 1, light)
        for cx, cy in cells:
            occ[cy, cx] = True
        placed += 1
    return img


if __name__ == "__main__":
    gb.make_ground = make_grass
    gb.OUT = sys.argv[1] if len(sys.argv) > 1 else "grass_tiles"
    gb.main()
