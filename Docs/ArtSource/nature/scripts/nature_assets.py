"""Cartowords 自然素材 v2 (大人びた落ち着いたトーン) 透過PNG・静止画
  木(大) 32x64 / それ以外 32x32
方針: 彩度を落とした自然な色 / 円や楕円の整った形を避けた不規則な輪郭 /
      輪郭線は黒ではなく、隣り合う色を暗くした色で細く(光が当たる左上側はさらに薄く)
実行: python nature_assets.py -> nature_*.png と nature_preview*.png
"""
import math
from PIL import Image, ImageDraw


def hx(s, a=255):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)


def hn(x, y, s=0):
    """決まった疑似乱数 0..1(毎回同じ絵になる)"""
    n = (x * 1619 + y * 31337 + s * 6971) & 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


# 葉(くすんだ緑)
G_HI = hx('9cae78'); G_LT = hx('7b9660'); G_MD = hx('5c7648'); G_DK = hx('3f5439')
# 針葉樹(青みのある深い緑)
P_HI = hx('86a07e'); P_LT = hx('68866a'); P_MD = hx('4a6a52'); P_DK = hx('33493a')
# 幹
W_LT = hx('8a7258'); W_MD = hx('6a5642'); W_DK = hx('463828')
# 岩
R_HI = hx('b8b6aa'); R_LT = hx('9c9a8e'); R_MD = hx('7a786c'); R_DK = hx('54524a')


def darken(c, f, desat=0.15):
    r, g, b = c[0], c[1], c[2]
    m = (r + g + b) / 3
    r, g, b = [m + (v - m) * (1 - desat) for v in (r, g, b)]
    return (int(r * f), int(g * f), int(b * f), 255)


class Cv:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        self.px = self.im.load()

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[x, y] = c

    def get(self, x, y):
        return self.px[x, y] if 0 <= x < self.w and 0 <= y < self.h else (0, 0, 0, 0)

    def paint(self, inside, color):
        for y in range(self.h):
            for x in range(self.w):
                if inside(x, y):
                    self.px[x, y] = color(x, y) if callable(color) else color

    def pts(self, lst, c):
        for x, y in lst:
            self.set(x, y, c)

    def ragged(self, p=0.22, seed=0, below=None):
        """外周のピクセルを少し欠けさせて、整った丸みをなくす"""
        kill = []
        for y in range(self.h):
            for x in range(self.w):
                if self.px[x, y][3] != 255:
                    continue
                if below is not None and y >= below:
                    continue
                free = sum(1 for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if self.get(x + dx, y + dy)[3] == 0)
                if free >= 1 and hn(x, y, seed) < p * (0.6 if free == 1 else 1.4):
                    kill.append((x, y))
        for x, y in kill:
            self.px[x, y] = (0, 0, 0, 0)

    def outline(self):
        """隣のピクセルの色を暗くした輪郭。左上側(光が当たる側)は薄く細く見せる"""
        found = {}
        for y in range(self.h):
            for x in range(self.w):
                c = self.px[x, y]
                if c[3] != 255:
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    n = (x + dx, y + dy)
                    if self.get(*n)[3] == 0 and 0 <= n[0] < self.w and 0 <= n[1] < self.h:
                        lit = (dx == -1 or dy == -1)   # 輪郭が物体の左・上にある = 光が当たる側
                        col = darken(c, 0.62 if lit else 0.38)
                        old = found.get(n)
                        if old is None or sum(col[:3]) < sum(old[:3]):
                            found[n] = col
        for n, col in found.items():
            self.set(n[0], n[1], col)

    def shadow(self, cx, cy, a, b, alpha=60):
        for y in range(self.h):
            for x in range(self.w):
                if ((x + .5 - cx) / a) ** 2 + ((y + .5 - cy) / b) ** 2 <= 1 and self.px[x, y][3] == 0:
                    self.px[x, y] = (30, 24, 14, alpha)


def ell(cx, cy, a, b, rot=0.0):
    cr, sr = math.cos(rot), math.sin(rot)

    def f(x, y):
        dx, dy = x + .5 - cx, y + .5 - cy
        u, v = dx * cr + dy * sr, -dx * sr + dy * cr
        return (u / a) ** 2 + (v / b) ** 2 <= 1
    return f


def lump_r(th, r, seed):
    return r * (1 + 0.16 * math.sin(3 * th + seed) + 0.10 * math.sin(5 * th + seed * 1.7) + 0.06 * math.sin(7 * th + seed * 2.3))


def blob(cv, lumps, pal=(G_HI, G_LT, G_MD, G_DK), texture=True):
    """不規則な形の葉のかたまりを奥から重ねる。左上が明るい"""
    hi, lt, md, dk = pal
    for k, (cx, cy, r) in enumerate(lumps):
        seed = k * 1.3 + cx * 0.17

        def inside(x, y, cx=cx, cy=cy, r=r, seed=seed):
            dx, dy = x + .5 - cx, y + .5 - cy
            return math.hypot(dx, dy) <= lump_r(math.atan2(dy, dx), r, seed)

        def col(x, y, cx=cx, cy=cy, r=r, seed=seed):
            dx, dy = x + .5 - cx, y + .5 - cy
            rr = lump_r(math.atan2(dy, dx), r, seed)
            d = math.hypot(dx, dy) / rr
            s = (dx / rr) * -0.55 + (dy / rr) * -0.83
            if d > 0.8 and s < 0.15:
                return dk
            if s > 0.6:
                return hi
            if s > 0.1:
                return lt
            if s > -0.5:
                return md
            return dk
        cv.paint(inside, col)
    if texture:  # 葉の細かい明暗
        for y in range(cv.h):
            for x in range(cv.w):
                c = cv.px[x, y]
                if c not in (lt, md):
                    continue
                v = hn(x, y, 7)
                if v < 0.07:
                    cv.px[x, y] = dk if c == md else md
                elif v > 0.94:
                    cv.px[x, y] = hi if c == lt else lt


def trunk(cv, cx, y0, y1, w_top, w_base, lean=0.0, fork=False):
    for y in range(y0, y1 + 1):
        t = (y - y0) / max(1, y1 - y0)
        w = w_top + (w_base - w_top) * t ** 2.2
        c = cx + lean * math.sin(t * 2.4)
        xa, xb = int(round(c - w / 2)), int(round(c + w / 2 - 0.01))
        if y >= y1 - 2:  # 根元の広がり(左右非対称)
            xa -= 1 + int(hn(y, 3) * 2); xb += 1 + int(hn(y, 5) * 2)
        for x in range(xa, xb + 1):
            span = max(1, xb - xa)
            u = (x - xa) / span
            col = W_LT if u < 0.3 else (W_DK if u > 0.7 else W_MD)
            if hn(x, y, 11) < 0.1 and col != W_DK:
                col = W_DK if col == W_MD else W_MD
            cv.set(x, y, col)
    if fork:
        yb = y0
        for i in range(4):
            cv.set(int(cx) - 1 - i, yb - i, W_MD); cv.set(int(cx) - i, yb - i, W_DK)
            cv.set(int(cx) + 2 + i, yb - 1 - i, W_MD); cv.set(int(cx) + 3 + i, yb - 1 - i, W_DK)


# ---------------- 木(大) 32x64 ----------------
def tree_round_l():
    cv = Cv(32, 64)
    trunk(cv, 15.5, 32, 61, 3.0, 4.5, lean=1.2, fork=True)
    blob(cv, [(16, 19, 10), (9, 25, 7), (23, 26, 7), (11, 12, 6.5), (21, 12, 6), (16, 8, 6),
              (18, 28, 5), (6, 18, 4.5), (26, 19, 4.5)])
    cv.ragged(0.18, seed=1)
    cv.outline()
    cv.shadow(16, 62, 10, 1.8)
    return cv.im


def pine_tier(cv, apex_y, base_y, hw, cx=16.0, seed=0, pal=(P_HI, P_LT, P_MD, P_DK)):
    hi, lt, md, dk = pal
    h = base_y - apex_y
    for y in range(apex_y, base_y + 1):
        t = (y - apex_y) / h
        half = hw * (t ** 0.92)
        j = (hn(y, seed) - 0.5) * 1.6          # 左右で長さを不揃いに
        j2 = (hn(y, seed + 5) - 0.5) * 1.6
        xa, xb = int(round(cx - half + j)), int(round(cx + half + j2))
        edge_cut = int(hn(y, seed + 9) * 2) if y >= base_y - 2 else 0
        for x in range(xa, xb + 1):
            if y == base_y and hn(x, y, seed + 3) < 0.45:
                continue
            u = (x - xa) / max(1, xb - xa)
            c = lt if u < 0.28 else (md if u < 0.62 else dk)
            if y >= base_y - 1:
                c = dk
            if hn(x, y, seed + 13) < 0.07:
                c = hi if c == lt else (lt if c == md else md)
            cv.set(x, y, c)


def tree_pine_l():
    cv = Cv(32, 64)
    trunk(cv, 15.5, 48, 61, 3.0, 4.2, lean=0.5)
    for i, (a, b, hw) in enumerate(((33, 54, 12.5), (22, 42, 10.5), (11, 31, 8.5), (3, 20, 6))):
        pine_tier(cv, a, b, hw, cx=16 + (0.6 if i % 2 else -0.4), seed=i * 17)
    cv.ragged(0.12, seed=4, below=48)
    cv.outline()
    cv.shadow(16, 62, 9, 1.8)
    return cv.im


# ---------------- 木(小) 32x32 ----------------
def tree_round_s():
    cv = Cv(32, 32)
    trunk(cv, 15.5, 19, 29, 2.6, 4.0, lean=0.8, fork=False)
    blob(cv, [(15, 12, 8), (9, 15, 5.5), (22, 15, 5.5), (11, 8, 5), (20, 7, 5), (16, 17, 4)])
    cv.ragged(0.18, seed=2)
    cv.outline()
    cv.shadow(16, 30, 8, 1.4)
    return cv.im


def tree_pine_s():
    cv = Cv(32, 32)
    trunk(cv, 15.5, 25, 29, 2.6, 3.6)
    for i, (a, b, hw) in enumerate(((15, 26, 8), (9, 19, 6), (2, 12, 4.2))):
        pine_tier(cv, a, b, hw, cx=16 + (0.5 if i % 2 else -0.3), seed=i * 23 + 3)
    cv.ragged(0.1, seed=6, below=25)
    cv.outline()
    cv.shadow(16, 30, 7, 1.4)
    return cv.im


# ---------------- 草・低木 32x32 ----------------
def bush(berries=False):
    cv = Cv(32, 32)
    blob(cv, [(14, 21, 8), (22, 22, 7), (8, 24, 5), (15, 15, 6), (23, 17, 4.5), (6, 19, 3.5)])
    if berries:
        red = hx('a8433a'); dred = hx('6e2a26'); cr = hx('d8b8a0')
        for (x, y) in ((10, 22), (17, 14), (23, 20), (14, 25)):
            cv.pts([(x, y), (x + 1, y)], red)
            cv.set(x, y + 1, dred); cv.set(x + 1, y + 1, dred)
            cv.set(x, y, cr)
    cv.ragged(0.16, seed=8, below=27)
    cv.outline()
    cv.shadow(16, 30, 11, 1.5)
    return cv.im


def blade(base, tip, ctrl, wmax):
    S = set()
    n = 40
    for i in range(n + 1):
        t = i / n
        x = (1 - t) ** 2 * base[0] + 2 * (1 - t) * t * ctrl[0] + t * t * tip[0]
        y = (1 - t) ** 2 * base[1] + 2 * (1 - t) * t * ctrl[1] + t * t * tip[1]
        X, Y = int(round(x)), int(round(y))
        S.add((X, Y))
        if t < 0.55 and wmax >= 2:
            S.add((X + 1, Y))
    return S


def grass_tuft():
    cv = Cv(32, 32)
    dry = hx('9a8a58')
    # (根元x, 先x, 先y, 制御x, 太さ, 色)
    blades = [(7, 2, 17, 6, 1, G_DK), (25, 30, 15, 26, 1, G_DK), (10, 6, 11, 9, 2, G_MD),
              (22, 27, 10, 23, 2, G_MD), (13, 11, 5, 13, 2, G_LT), (18, 21, 7, 17, 2, dry),
              (16, 17, 3, 15, 1, G_LT), (20, 24, 16, 21, 1, G_LT), (12, 8, 17, 12, 1, G_LT)]
    for (bx, tx, ty, cx_, w, c) in blades:
        S = blade((bx, 29), (tx, ty), (cx_, 21), w)
        cv.paint(lambda x, y, S=S: (x, y) in S, c)
    # 土は途切れた小さな塊で
    for x in range(8, 25):
        if hn(x, 30, 2) < 0.7:
            cv.set(x, 30, hx('5a4a34'))
            if hn(x, 31, 4) < 0.35:
                cv.set(x, 29, hx('6a5640'))
    cv.outline()
    return cv.im


def sprout():
    cv = Cv(32, 32)
    cv.paint(ell(16, 28.6, 7, 1.9), lambda x, y: hx('7a6446') if x < 15 else hx('5a4a34'))
    S = blade((16, 28), (17, 14), (15, 22), 1)
    cv.paint(lambda x, y: (x, y) in S, G_MD)
    for (cx, cy, a, b, rot) in ((11.5, 17.5, 4.6, 1.9, -0.5), (21.5, 14.5, 4.4, 1.8, 0.55), (17, 10.5, 1.8, 3.2, 0.1)):
        f = ell(cx, cy, a, b, rot)
        cv.paint(f, lambda x, y, cx=cx, cy=cy: G_LT if (x + y) < cx + cy - .5 else G_MD)
    cv.pts([(9, 17), (10, 16), (23, 13), (24, 12), (17, 8)], G_HI)
    cv.ragged(0.1, seed=3, below=26)
    cv.outline()
    return cv.im


# ---------------- キノコ・岩・切り株 32x32 ----------------
def mushroom(kind='red'):
    cv = Cv(32, 32)
    if kind == 'red':
        cap = (hx('c4614c'), hx('a14a3a'), hx('6e2e28')); spot = hx('e8dcc4')
    else:
        cap = (hx('b09068'), hx('8c6a44'), hx('5e4630')); spot = hx('e0d2b0')
    st = (hx('e4dcc2'), hx('d0c4a4'), hx('a89c80'))

    def one(cx, base_y, cap_w, cap_h, stem_h, tilt, seed):
        top = base_y - stem_h

        def stem_in(x, y):
            half = 1.5 + 1.0 * (y - top) / max(1, stem_h)
            c = cx + tilt * (y - top) / max(1, stem_h)
            return top <= y <= base_y and abs(x + .5 - c) <= half

        def stem_col(x, y):
            c = cx + tilt * (y - top) / max(1, stem_h)
            u = x + .5 - c
            return st[0] if u < -0.7 else (st[1] if u < 0.9 else st[2])
        cv.paint(stem_in, stem_col)
        cy = top

        def cap_in(x, y):
            dx = (x + .5 - cx) / cap_w
            dy = (y + .5 - cy) / cap_h
            n = 1 + 0.10 * math.sin(4 * math.atan2(dy, dx) + seed)
            return y <= cy + 1 and dx * dx + dy * dy <= n

        def cap_col(x, y):
            dx, dy = (x + .5 - cx) / cap_w, (y + .5 - cy) / cap_h
            s = dx * -0.55 + dy * -0.83
            if y >= cy:
                return cap[2]
            return cap[0] if s > 0.4 else (cap[1] if s > -0.35 else cap[2])
        cv.paint(cap_in, cap_col)
        for (dx, dy, big) in ((-0.5, -0.45, True), (0.2, -0.75, False), (0.55, -0.25, False)):
            x, y = int(cx + dx * cap_w), int(cy + dy * cap_h)
            if cv.get(x, y)[3] == 255 and cv.get(x, y) != cap[2]:
                cv.set(x, y, spot)
                if big and cap_w > 6:
                    cv.set(x + 1, y, spot)
    one(12, 29, 9.0, 5.8, 10, -0.8, 0.5)
    one(24, 29, 5.2, 3.6, 6, 0.8, 2.1)
    cv.ragged(0.08, seed=9, below=27)
    cv.outline()
    cv.shadow(16, 30, 12, 1.4)
    return cv.im


def poly_mask(points, w=32, h=32):
    m = Image.new('L', (w, h), 0)
    ImageDraw.Draw(m).polygon(points, fill=255)
    px = m.load()
    return lambda x, y: px[x, y] > 0


def rock():
    cv = Cv(32, 32)
    main = poly_mask([(4, 27), (3, 21), (7, 15), (12, 11), (18, 9), (23, 12), (27, 17), (29, 23), (27, 27)])
    side = poly_mask([(24, 27), (25, 22), (29, 21), (30, 25), (29, 27)])
    inside = lambda x, y: main(x, y) or side(x, y)

    def col(x, y):
        s = (-0.55 * (x - 16) - 0.83 * (y - 18)) / 11.0
        # 面ごとの段差(縦の割れ目で明るさをずらす)
        if x > 20 and y > 15:
            s -= 0.25
        if y < 14 and x < 16:
            s += 0.15
        if s > 0.7:
            c = R_HI
        elif s > 0.2:
            c = R_LT
        elif s > -0.3:
            c = R_MD
        else:
            c = R_DK
        if hn(x, y, 21) < 0.08:
            c = R_DK if c != R_DK else R_MD
        return c
    cv.paint(inside, col)
    # 割れ目
    cv.pts([(14, 12), (14, 13), (15, 14), (15, 15), (16, 16), (16, 17), (17, 18), (21, 19), (21, 20), (22, 21), (22, 22)], R_DK)
    cv.pts([(8, 21), (9, 22), (10, 22), (10, 23), (11, 24)], R_DK)
    # 苔
    for y in range(8, 22):
        for x in range(4, 20):
            if cv.get(x, y)[3] == 255 and y < 17 and x < 13 and hn(x, y, 31) < 0.55 - (y - 11) * 0.06:
                cv.set(x, y, G_MD if hn(x, y, 8) < 0.6 else G_LT)
    cv.ragged(0.05, seed=2)
    cv.outline()
    cv.shadow(16, 30, 12, 1.4)
    return cv.im


def stump():
    cv = Cv(32, 32)
    top_c = hx('c0a070'); top_d = hx('a88858'); ring = hx('8a6a3c')

    def half(y):
        return 7.5 + (1.8 if y >= 25 else 0.0) + (hn(y, 3) - 0.5) * 1.2

    def side(x, y):
        if 16 <= y <= 27 and abs(x + .5 - 16) <= half(y):
            return True
        return y >= 27 and ((x + .5 - 16) / (half(27) + 0.5)) ** 2 + ((y + .5 - 27) / 2.6) ** 2 <= 1

    def side_col(x, y):
        u = (x + .5 - 16) / 8.0
        base = W_LT if u < -0.35 else (W_MD if u < 0.35 else W_DK)
        if hn(x // 2, 0, 5) < 0.35 and y > 17:   # 縦の樹皮の筋
            base = W_DK if base != W_DK else hx('2e2418')
        return base
    cv.paint(side, side_col)

    def top_in(x, y):
        d = ((x + .5 - 16) / 8.3) ** 2 + ((y + .5 - 16) / 3.7) ** 2
        notch = (x >= 21 and y <= 14)     # 欠けた部分
        return d <= 1 and not notch

    def top_col(x, y):
        d = math.hypot((x + .5 - 16) / 8.3, (y + .5 - 16) / 3.7)
        if d > 0.88:
            return W_MD
        if 0.58 < d <= 0.7 or 0.26 < d <= 0.38:
            return ring
        return top_c if (x + y) < 33 else top_d
    cv.paint(top_in, top_col)
    cv.pts([(16, 16)], ring)
    # 苔
    for (x, y) in ((8, 26), (9, 25), (9, 26), (10, 26), (10, 27), (11, 27), (22, 26), (23, 26), (21, 27)):
        if cv.get(x, y)[3] == 255:
            cv.set(x, y, G_MD if (x + y) % 2 else G_LT)
    cv.ragged(0.06, seed=5, below=24)
    cv.outline()
    cv.shadow(16, 30, 12, 1.4)
    return cv.im


ASSETS = {
    'tree_round_l': tree_round_l,
    'tree_pine_l': tree_pine_l,
    'tree_round_s': tree_round_s,
    'tree_pine_s': tree_pine_s,
    'bush': lambda: bush(False),
    'bush_berry': lambda: bush(True),
    'grass_tuft': grass_tuft,
    'sprout': sprout,
    'mushroom_red': lambda: mushroom('red'),
    'mushroom_brown': lambda: mushroom('brown'),
    'rock': rock,
    'stump': stump,
}


def sheet(imgs, S=8, gap=12, bg=(184, 156, 108, 255)):
    names = list(imgs)
    big = [n for n in names if imgs[n].size[1] == 64]
    small = [n for n in names if imgs[n].size[1] == 32]
    cw = 32 * S + gap
    row1 = 64 * S + gap
    row2 = 32 * S + gap
    rows_small = (len(small) + 5) // 6
    pv = Image.new('RGBA', (6 * cw + gap, gap + row1 + rows_small * row2), bg)
    x = gap
    for n in big:
        pv.alpha_composite(imgs[n].resize((32 * S, 64 * S), Image.NEAREST), (x, gap)); x += cw
    for i, n in enumerate(small):
        r, c = divmod(i, 6)
        pv.alpha_composite(imgs[n].resize((32 * S, 32 * S), Image.NEAREST), (gap + c * cw, gap + row1 + r * row2))
    return pv


if __name__ == '__main__':
    imgs = {}
    for name, fn in ASSETS.items():
        im = fn()
        im.save(f'nature_{name}.png')
        imgs[name] = im
    sheet(imgs).save('nature_preview.png')
    sheet(imgs, bg=(0, 0, 0, 0)).save('nature_preview_transparent.png')
    print('done', {k: v.size for k, v in imgs.items()})
