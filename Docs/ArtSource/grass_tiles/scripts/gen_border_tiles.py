"""土地(明るい地面)と、壁との境目タイルの生成 (32x32)。
- tile_ground_01..04 : 砂利少なめの明るい土地(継ぎ目なし)
- tile_border_00..15 : 壁に接する辺に、崩れたレンガのがれきと影を描いたタイル
                       番号 = 壁が 上=1 右=2 下=4 左=8 の和 (依頼書どおり)
- tile_border_inner_ul/ur/dr/dl : 上下左右は土地で、斜めだけ壁のマスの角 (左上・右上・右下・左下)
がれきの並びは「辺ごとに1つ」の固定パターンなので、同じ辺に壁があるタイル同士は
隣り合わせてもつながる(辺の両端が周期でそろう)。
使い方: python3 gen_border_tiles.py [出力ディレクトリ]
"""
import os
import sys
import numpy as np
from PIL import Image

import gen_dirt_tiles as gd   # 砂利の描画を再利用

T = 32
OUT = sys.argv[1] if len(sys.argv) > 1 else "border_tiles"

# ---- 土地パレット(道より明るく、黄みの暖色) ----
GROUND = [(0xE4, 0xC2, 0x8C), (0xDA, 0xB5, 0x7D), (0xCF, 0xA9, 0x72), (0xC4, 0x9D, 0x68)]
G_SPECK_D = (0xAE, 0x86, 0x58)
G_SPECK_L = (0xEE, 0xD2, 0xA0)

# ---- がれき(壁のレンガと同系色) ----
BRICK_BASES = [(0x1B, 0x1C, 0x1F), (0x21, 0x23, 0x27), (0x17, 0x18, 0x1B), (0x25, 0x27, 0x2B)]
HI = (0x47, 0x4F, 0x54)
HI2 = (0x37, 0x3E, 0x43)
SHADE = (0x11, 0x12, 0x14)
SOIL_DARK = (0x2E, 0x22, 0x18)   # がれきの隙間の暗い土


def dark(c, f):
    return tuple(int(v * f) for v in c)


# ---------------------------------------------------------------- 土地
def make_ground(seed, stones=5):
    rng = np.random.default_rng(seed)
    n = gd.wrap_blur(rng.random((T, T)), 3) + 0.10 * rng.random((T, T))
    q = np.quantile(n, [0.25, 0.55, 0.85])
    idx = 3 - np.digitize(n, q)
    img = np.zeros((T, T, 3), np.uint8)
    for i, c in enumerate(GROUND):
        img[idx == i] = c
    for _ in range(9):
        gd.put(img, rng.integers(T), rng.integers(T), G_SPECK_D)
    for _ in range(9):
        gd.put(img, rng.integers(T), rng.integers(T), G_SPECK_L)
    gd.scatter_stones(img, rng, stones)
    return img


# ---------------------------------------------------------------- 辺ごとのがれき層
SIDES = ["up", "right", "down", "left"]
MORTAR = (0x08, 0x08, 0x0A)
FOOT_COLORS = [MORTAR, SHADE, (0x17, 0x18, 0x1B), (0x1B, 0x1C, 0x1F)]
SHADOW_W = {"up": 3, "left": 3, "down": 2, "right": 2}  # 光は左上: 上と左の壁は影が長い


def periodic_profile(rng, lo, hi):
    r = rng.random(T)
    for _ in range(3):
        r = (2 * r + np.roll(r, 1) + np.roll(r, -1)) / 4
    r = (r - r.min()) / (r.max() - r.min() + 1e-9)
    return (lo + np.round(r * (hi - lo))).astype(int)


def to_xy(side, i, d):
    """辺に沿った位置 i / 壁からの距離 d -> タイル内の (x, y)"""
    if side == "up":
        return i, d
    if side == "down":
        return i, T - 1 - d
    if side == "left":
        return d, i
    return T - 1 - d, i


def dist_from(side, x, y):
    if side == "up":
        return y, x
    if side == "down":
        return T - 1 - y, x
    if side == "left":
        return x, y
    return T - 1 - x, y


class Layer:
    """壁1辺ぶんの「足元の暗がり + 崩れたレンガ片 + 影」。同じ辺は常に同じ絵になる。"""

    def __init__(self, side, seed):
        rng = np.random.default_rng(seed)
        self.side = side
        self.F = periodic_profile(rng, 2, 4)             # 足元の深さ
        self.rgb = np.zeros((T, T, 3), np.uint8)
        self.alpha = np.zeros((T, T), bool)
        self.shadow = np.zeros((T, T), np.uint8)         # 0 なし / 1 薄い影 / 2 濃い影
        self._footing(rng)
        self._chunks(rng)
        self._shadow()

    def _footing(self, rng):
        for i in range(T):
            for d in range(8):
                x, y = to_xy(self.side, i, d)
                f = self.F[i]
                if d < f or (d == f and rng.random() < 0.5):
                    c = FOOT_COLORS[rng.integers(len(FOOT_COLORS))]
                    if d >= 1 and rng.random() < 0.07:
                        c = HI2
                    self.rgb[y, x] = c
                    self.alpha[y, x] = True

    def _chunks(self, rng):
        occ = np.zeros((T, T), bool)
        sizes = [(2, 2), (3, 2), (3, 3), (4, 3), (3, 2), (2, 3), (4, 2)]
        placed, tries = 0, 0
        while placed < 7 and tries < 300:
            tries += 1
            lw, lh = sizes[rng.integers(len(sizes))]       # 辺に沿った幅 / 深さ方向の厚み
            i0 = int(rng.integers(1, T - lw - 1))
            d0 = int(rng.integers(max(0, int(self.F[i0]) - 1), int(self.F[i0]) + 4))
            aw, ah = (lw, lh) if self.side in ("up", "down") else (lh, lw)
            if self.side == "up":
                x0, y0 = i0, d0
            elif self.side == "down":
                x0, y0 = i0, T - d0 - ah
            elif self.side == "left":
                x0, y0 = d0, i0
            else:
                x0, y0 = T - d0 - aw, i0
            if x0 < 0 or y0 < 0 or x0 + aw > T or y0 + ah > T:
                continue
            region = occ[max(0, y0 - 1):y0 + ah + 1, max(0, x0 - 1):x0 + aw + 1]
            if region.any():
                continue
            self._draw_chunk(x0, y0, aw, ah, rng)
            occ[y0:y0 + ah, x0:x0 + aw] = True
            placed += 1

    def _draw_chunk(self, x0, y0, aw, ah, rng):
        m = gd.ellipse_mask(aw, ah, rng)
        base = BRICK_BASES[rng.integers(len(BRICK_BASES))]

        def ins(x, y):
            return 0 <= x < aw and 0 <= y < ah and m[y, x]

        for y in range(ah):
            for x in range(aw):
                if not m[y, x]:
                    continue
                up, left, down, right = ins(x, y - 1), ins(x - 1, y), ins(x, y + 1), ins(x + 1, y)
                if (not up or not left) and down and right:
                    c = HI
                elif (not down or not right) and up and left:
                    c = SHADE
                else:
                    c = base
                self.rgb[y0 + y, x0 + x] = c
                self.alpha[y0 + y, x0 + x] = True
                for sx, sy in ((x + 1, y + 1), (x + 1, y), (x, y + 1)):  # 右下に落ちる影
                    if not ins(sx, sy):
                        X, Y = x0 + sx, y0 + sy
                        if 0 <= X < T and 0 <= Y < T and not self.alpha[Y, X]:
                            self.shadow[Y, X] = 2

    def _shadow(self):
        w = SHADOW_W[self.side]
        for y in range(T):
            for x in range(T):
                d, i = dist_from(self.side, x, y)
                s = d - self.F[i]
                if self.alpha[y, x]:
                    continue
                if s < w - 1:
                    self.shadow[y, x] = 2
                elif s < w and (x + y) % 2 == 0:
                    self.shadow[y, x] = max(self.shadow[y, x], 1)


class CornerLayer:
    """斜めだけ壁のマスの角(内角)。隣の辺のタイルとつながるよう、足元の深さをそろえる。"""

    def __init__(self, corner, layers, seed):
        rng = np.random.default_rng(seed)
        cx0 = 0 if corner in ("ul", "dl") else T - 1
        cy0 = 0 if corner in ("ul", "ur") else T - 1
        sx = "left" if cx0 == 0 else "right"
        sy = "up" if cy0 == 0 else "down"
        rA = max(layers[sx].F[cy0], layers[sy].F[cx0]) + 1.0
        self.side = sy
        self.rgb = np.zeros((T, T, 3), np.uint8)
        self.alpha = np.zeros((T, T), bool)
        self.shadow = np.zeros((T, T), np.uint8)
        w = 3 if corner == "ul" else 2
        for y in range(T):
            for x in range(T):
                dd = ((x - cx0) ** 2 + (y - cy0) ** 2) ** 0.5
                if dd < rA or (dd < rA + 0.8 and rng.random() < 0.5):
                    self.rgb[y, x] = FOOT_COLORS[rng.integers(len(FOOT_COLORS))]
                    self.alpha[y, x] = True
        # 角の足元に、破片を1つ
        for _ in range(60):
            aw, ah = [(3, 2), (2, 3), (3, 3)][rng.integers(3)]
            ox = int(rng.integers(0, 8)); oy = int(rng.integers(0, 8))
            x0 = ox if cx0 == 0 else T - ox - aw
            y0 = oy if cy0 == 0 else T - oy - ah
            dd = ((x0 + aw / 2 - cx0) ** 2 + (y0 + ah / 2 - cy0) ** 2) ** 0.5
            if rA + 2 <= dd <= rA + 5 and 0 <= x0 and 0 <= y0 and x0 + aw <= T and y0 + ah <= T:
                Layer._draw_chunk(self, x0, y0, aw, ah, rng)
                break
        for y in range(T):
            for x in range(T):
                if self.alpha[y, x]:
                    continue
                dd = ((x - cx0) ** 2 + (y - cy0) ** 2) ** 0.5
                s = dd - rA
                if s < w - 1:
                    self.shadow[y, x] = 2
                elif s < w and (x + y) % 2 == 0:
                    self.shadow[y, x] = max(self.shadow[y, x], 1)


def build_layers(seed=7):
    return {s: Layer(s, seed + 11 * k) for k, s in enumerate(SIDES)}


def compose(base, layers, sides, corners=()):
    """base: 土地タイル。sides: 壁がある辺。corners: 斜めの壁 ('ul','ur','dr','dl')。"""
    use = [layers[s] for s in sides] + list(corners)
    img = base.copy()
    sh = np.zeros((T, T), np.uint8)
    for L in use:
        sh = np.maximum(sh, L.shadow)
    for y in range(T):
        for x in range(T):
            if sh[y, x] == 2:
                img[y, x] = dark(base[y, x], 0.68)
            elif sh[y, x] == 1:
                img[y, x] = dark(base[y, x], 0.84)
    for L in use:
        m = L.alpha
        img[m] = L.rgb[m]
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    grounds = [make_ground(s) for s in (5, 17, 29, 41)]
    for i, g in enumerate(grounds, 1):
        Image.fromarray(g, "RGB").save(f"{OUT}/tile_ground_{i:02d}.png")
    Image.fromarray(np.concatenate(grounds, 1), "RGB").save(f"{OUT}/tile_ground.png")

    layers = build_layers()
    base = grounds[0]
    bits = {"up": 1, "right": 2, "down": 4, "left": 8}
    borders = []
    for n in range(16):
        sides = [s for s in SIDES if n & bits[s]]
        t = compose(base, layers, sides)
        borders.append(t)
        Image.fromarray(t, "RGB").save(f"{OUT}/tile_border_{n:02d}.png")
    sheet = np.zeros((4 * T, 4 * T, 3), np.uint8)
    for n, t in enumerate(borders):
        sheet[(n // 4) * T:(n // 4 + 1) * T, (n % 4) * T:(n % 4 + 1) * T] = t
    Image.fromarray(sheet, "RGB").save(f"{OUT}/tile_border.png")

    inner = []
    for k, c in enumerate(("ul", "ur", "dr", "dl")):
        t = compose(base, layers, [], [CornerLayer(c, layers, 90 + k)])
        inner.append(t)
        Image.fromarray(t, "RGB").save(f"{OUT}/tile_border_inner_{c}.png")
    Image.fromarray(np.concatenate(inner, 1), "RGB").save(f"{OUT}/tile_border_inner.png")

    # ---------- 確認用: 壁と土地を実際に並べたマップ ----------
    wall_tiles = [np.array(Image.open(f"brick_tiles/tile_wall_{i:02d}.png").convert("RGB"))
                  for i in range(1, 5)]
    shape = [
        "WWWWWWWWWWWWWW",
        "WWGGGWWWWWGGWW",
        "WGGGGGWWWGGGGW",
        "WGGGGGGWWWGGGW",
        "WWGGGGGGGGGGWW",
        "WWWGGGWWWGGWWW",
        "WWWWWWWWWWWGWW",
        "WWGWWWWWWWWWWW",
        "WWWWWWWWWWWWWW",
    ]
    H, W = len(shape), len(shape[0])
    rng = np.random.default_rng(3)
    inner_idx = {"ul": 0, "ur": 1, "dr": 2, "dl": 3}

    def is_wall(r, c):
        return not (0 <= r < H and 0 <= c < W) or shape[r][c] == "W"

    canvas = np.zeros((H * T, W * T, 3), np.uint8)
    for r in range(H):
        for c in range(W):
            if is_wall(r, c):
                t = wall_tiles[rng.integers(4)]
            else:
                m = (1 if is_wall(r - 1, c) else 0) | (2 if is_wall(r, c + 1) else 0) \
                    | (4 if is_wall(r + 1, c) else 0) | (8 if is_wall(r, c - 1) else 0)
                if m:
                    t = borders[m]
                else:
                    diag = [k for k, (dr, dc) in
                            {"ul": (-1, -1), "ur": (-1, 1), "dr": (1, 1), "dl": (1, -1)}.items()
                            if is_wall(r + dr, c + dc)]
                    t = inner[inner_idx[diag[0]]] if diag else grounds[rng.integers(4)]
            canvas[r * T:(r + 1) * T, c * T:(c + 1) * T] = t
    Image.fromarray(canvas).resize((W * T * 3, H * T * 3), Image.NEAREST) \
        .save(f"{OUT}/preview_map.png")
    # 20枚の一覧(4倍)
    allt = borders + inner
    sh = np.zeros((5 * T, 4 * T, 3), np.uint8)
    for k, t in enumerate(allt):
        sh[(k // 4) * T:(k // 4 + 1) * T, (k % 4) * T:(k % 4 + 1) * T] = t
    Image.fromarray(sh).resize((4 * T * 4, 5 * T * 4), Image.NEAREST) \
        .save(f"{OUT}/contact_sheet_20.png")


if __name__ == "__main__":
    main()
