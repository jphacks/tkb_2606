"""Cartowords 自然素材(土地の上に置く装飾) 透過PNG・静止画
  木(大) 32x64 / それ以外 32x32
実行: python nature_assets.py -> nature_*.png と nature_preview*.png
光源は左上、輪郭は濃い色、アンチエイリアスなし。
"""
import math
from PIL import Image, ImageDraw


def hx(s, a=255):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)


OUT = hx('1f2616')          # 緑系の輪郭
OUT_W = hx('2a1a10')        # 木・キノコなど茶系の輪郭
# 葉(花の茎・葉と同系統の緑)
G_HI = hx('a8e070'); G_LT = hx('7fcf5a'); G_MD = hx('4f9a45'); G_DK = hx('2f6b34')
# 幹
W_LT = hx('9a7a4a'); W_MD = hx('7a5a34'); W_DK = hx('4a3220')
# 岩
R_HI = hx('c8c0ae'); R_LT = hx('a8a090'); R_MD = hx('85806f'); R_DK = hx('5a5648')


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
        """inside(x,y)->bool, color は固定色か (x,y)->色 の関数"""
        for y in range(self.h):
            for x in range(self.w):
                if inside(x, y):
                    self.px[x, y] = color(x, y) if callable(color) else color

    def pts(self, lst, c):
        for x, y in lst:
            self.set(x, y, c)

    def outline(self, c=OUT):
        pts = [(x, y) for y in range(self.h) for x in range(self.w) if self.px[x, y][3] == 255]
        S = set(pts)
        for x, y in pts:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (x + dx, y + dy)
                if n not in S and self.get(*n)[3] == 0:
                    self.set(n[0], n[1], c)

    def shadow(self, cx, cy, a, b, alpha=70):
        """接地の影。透明な所にだけ薄く敷く"""
        for y in range(self.h):
            for x in range(self.w):
                if ((x + .5 - cx) / a) ** 2 + ((y + .5 - cy) / b) ** 2 <= 1 and self.px[x, y][3] == 0:
                    self.px[x, y] = (30, 20, 10, alpha)


def circ(cx, cy, r):
    return lambda x, y: (x + .5 - cx) ** 2 + (y + .5 - cy) ** 2 <= r * r


def ell(cx, cy, a, b, rot=0.0):
    cr, sr = math.cos(rot), math.sin(rot)

    def f(x, y):
        dx, dy = x + .5 - cx, y + .5 - cy
        u, v = dx * cr + dy * sr, -dx * sr + dy * cr
        return (u / a) ** 2 + (v / b) ** 2 <= 1
    return f


def blob(cv, circles, pal=(G_HI, G_LT, G_MD, G_DK)):
    """円を奥から順に重ねた球状の葉のかたまり。左上が明るい"""
    hi, lt, md, dk = pal
    for (cx, cy, r) in circles:
        def col(x, y, cx=cx, cy=cy, r=r):
            nx, ny = (x + .5 - cx) / r, (y + .5 - cy) / r
            s = nx * -0.55 + ny * -0.83
            d = math.hypot(nx, ny)
            if d > 0.82 and s < 0.1:
                return dk
            if s > 0.55:
                return hi
            if s > 0.05:
                return lt
            if s > -0.5:
                return md
            return dk
        cv.paint(circ(cx, cy, r), col)
    # 葉のきらめき(決まった位置に散らす)
    for (cx, cy, r) in circles:
        for k in range(5):
            a = 0.9 + k * 1.37 + cx * 0.3
            x = int(cx + r * 0.45 * math.cos(a) - r * 0.18)
            y = int(cy + r * 0.45 * math.sin(a) - r * 0.25)
            if cv.get(x, y)[3] == 255 and cv.get(x, y) in (lt, md):
                cv.set(x, y, hi if cv.get(x, y) == lt else lt)


def trunk(cv, x0, x1, y0, y1, root_y, root_extra=2):
    def inside(x, y):
        if y0 <= y <= y1 and x0 <= x <= x1:
            return True
        if root_y <= y <= y1 and x0 - root_extra <= x <= x1 + root_extra:
            return True
        return False

    def col(x, y):
        w = x1 - x0 + 1
        if x <= x0 + w * 0.3:
            return W_LT
        if x >= x1 - w * 0.3:
            return W_DK
        return W_MD
    cv.paint(inside, col)
    # 樹皮の線
    for y in range(y0 + 2, root_y, 4):
        cv.set(x0 + 2, y, W_DK)


# ---------------- 木(大) 32x64 ----------------
def tree_round_l():
    cv = Cv(32, 64)
    trunk(cv, 14, 17, 43, 61, 57, root_extra=2)
    # 枝
    cv.pts([(13, 43), (13, 42), (12, 41), (19, 43), (19, 42), (20, 41)], W_MD)
    blob(cv, [(16, 30, 12), (8, 36, 7), (24, 36, 7), (10, 23, 8), (22, 24, 8), (16, 17, 9), (12, 31, 6), (21, 32, 6)])
    cv.outline(OUT)
    cv.shadow(16, 62, 10, 2)
    return cv.im


def pine_tier(cv, apex_y, base_y, hw, cx=16):
    h = base_y - apex_y

    def inside(x, y):
        if y < apex_y or y > base_y:
            return False
        half = hw * (y - apex_y) / h
        edge = base_y - (1 if (x % 4 < 2) else 0)
        return abs(x + .5 - cx) <= half + 0.5 and y <= edge

    def col(x, y):
        half = hw * (y - apex_y) / h
        if y >= base_y - 2:
            return G_DK
        if x + .5 < cx - half * 0.25:
            return G_LT if (x + .5 < cx - half * 0.6) else G_MD
        return G_MD if x + .5 < cx + half * 0.3 else G_DK
    cv.paint(inside, col)


def tree_pine_l():
    cv = Cv(32, 64)
    trunk(cv, 14, 17, 50, 61, 58, root_extra=2)
    for (a, b, hw) in ((34, 54, 14), (23, 42, 12), (12, 31, 10), (3, 20, 7)):
        pine_tier(cv, a, b, hw)
    cv.outline(OUT)
    cv.shadow(16, 62, 10, 2)
    return cv.im


# ---------------- 木(小) 32x32 ----------------
def tree_round_s():
    cv = Cv(32, 32)
    trunk(cv, 14, 17, 21, 29, 27, root_extra=2)
    blob(cv, [(16, 12, 9), (9, 16, 6), (23, 16, 6), (11, 9, 6), (21, 9, 6), (16, 5, 5)])
    cv.outline(OUT)
    cv.shadow(16, 30, 8, 1.5)
    return cv.im


def tree_pine_s():
    cv = Cv(32, 32)
    trunk(cv, 14, 17, 24, 29, 27, root_extra=1)
    for (a, b, hw) in ((16, 26, 9), (10, 19, 7), (3, 12, 5)):
        pine_tier(cv, a, b, hw)
    cv.outline(OUT)
    cv.shadow(16, 30, 8, 1.5)
    return cv.im


# ---------------- 草・低木 32x32 ----------------
def bush(berries=False):
    cv = Cv(32, 32)
    blob(cv, [(16, 20, 9), (8, 22, 7), (24, 22, 7), (12, 15, 7), (21, 15, 7)])
    if berries:
        red = hx('d8402e'); redd = hx('8a1e1e'); wh = hx('ffd8c8')
        for (x, y) in ((10, 20), (17, 13), (22, 21), (14, 24), (25, 17)):
            cv.pts([(x, y), (x + 1, y), (x, y + 1), (x + 1, y + 1)], red)
            cv.set(x + 1, y + 1, redd)
            cv.set(x, y, wh)
    cv.outline(OUT)
    cv.shadow(16, 30, 11, 1.8)
    return cv.im


def blade(base, tip, ctrl, wmax):
    """草の葉1枚(ベジェ曲線)。根元が太く、先が細い"""
    S = set()
    n = 40
    for i in range(n + 1):
        t = i / n
        x = (1 - t) ** 2 * base[0] + 2 * (1 - t) * t * ctrl[0] + t * t * tip[0]
        y = (1 - t) ** 2 * base[1] + 2 * (1 - t) * t * ctrl[1] + t * t * tip[1]
        X, Y = int(round(x)), int(round(y))
        S.add((X, Y))
        if t < 0.65 and wmax >= 2:
            S.add((X + 1, Y))
        if t < 0.3 and wmax >= 3:
            S.add((X - 1, Y))
    return S


def grass_tuft():
    cv = Cv(32, 32)
    blades = [  # (根元x, 先x, 先y, 制御x, 太さ, 色)
        (8, 4, 14, 8, 2, G_DK), (24, 28, 13, 24, 2, G_DK), (13, 9, 7, 12, 3, G_MD),
        (19, 24, 8, 20, 3, G_MD), (16, 16, 3, 15, 3, G_LT), (11, 14, 15, 11, 2, G_LT),
        (21, 20, 15, 21, 2, G_LT)]
    for (bx, tx, ty, cx_, w, c) in blades:
        S = blade((bx, 29), (tx, ty), (cx_, 20), w)
        cv.paint(lambda x, y, S=S: (x, y) in S, lambda x, y, S=S, c=c: G_HI if ((x - 1, y) not in S and c == G_LT) else c)
    # 地面の土
    cv.paint(ell(16, 30, 10, 1.3), hx('6a4a2a'))
    cv.outline(OUT)
    return cv.im


def sprout():
    cv = Cv(32, 32)
    cv.paint(ell(16, 28.5, 8, 2.4), lambda x, y: hx('8a6a40') if x < 15 else hx('6a4a2a'))
    S = blade((16, 28), (16, 15), (15, 22), 2)
    cv.paint(lambda x, y: (x, y) in S, G_MD)
    for (cx, cy, a, b, rot) in ((10.5, 17, 5.8, 2.7, -0.45), (22, 16, 5.8, 2.7, 0.45), (16, 11, 2.6, 4.0, 0.0)):
        f = ell(cx, cy, a, b, rot)
        cv.paint(f, lambda x, y, cx=cx, cy=cy: G_LT if (x + y) < cx + cy - .5 else G_MD)
    cv.pts([(8, 17), (9, 16), (21, 15), (22, 14), (16, 9)], G_HI)
    cv.outline(OUT)
    return cv.im


# ---------------- キノコ・岩・切り株 32x32 ----------------
def mushroom(kind='red'):
    cv = Cv(32, 32)
    if kind == 'red':
        cap = (hx('e85a3a'), hx('d8402e'), hx('a82a26')); spot = hx('fff0e0')
    else:
        cap = (hx('c89050'), hx('a8703a'), hx('7a4e28')); spot = hx('f0dcb0')
    st = (hx('fff4dc'), hx('f0e0c0'), hx('c8b088'))

    def one(cx, base_y, cap_w, cap_h, stem_h):
        top = base_y - stem_h
        # 柄
        def stem_in(x, y):
            half = 1.9 + 1.2 * (y - top) / max(1, stem_h)
            return top <= y <= base_y and abs(x + .5 - cx) <= half
        cv.paint(stem_in, lambda x, y: st[0] if x + .5 < cx - 0.8 else (st[1] if x + .5 < cx + 1.2 else st[2]))
        cv.pts([(cx - 1, top + 1), (cx, top + 1)], hx('c8b088'))   # かさの影
        cy = top

        def cap_in(x, y):
            return y <= cy + 1 and ((x + .5 - cx) / cap_w) ** 2 + ((y + .5 - cy) / cap_h) ** 2 <= 1

        def cap_col(x, y):
            nx, ny = (x + .5 - cx) / cap_w, (y + .5 - cy) / cap_h
            s = nx * -0.55 + ny * -0.83
            if y >= cy:
                return cap[2]
            return cap[0] if s > 0.35 else (cap[1] if s > -0.4 else cap[2])
        cv.paint(cap_in, cap_col)
        # 斑点
        for (dx, dy) in ((-cap_w * 0.5, -cap_h * 0.45), (cap_w * 0.15, -cap_h * 0.75), (cap_w * 0.55, -cap_h * 0.3)):
            x, y = int(cx + dx), int(cy + dy)
            cv.pts([(x, y), (x + 1, y), (x, y + 1), (x + 1, y + 1)] if cap_w > 6 else [(x, y), (x + 1, y)], spot)
    one(12, 29, 9.5, 8, 9)
    one(24, 29, 5.5, 5, 6)
    cv.outline(OUT_W)
    cv.shadow(16, 30, 12, 1.5)
    return cv.im


def rock():
    cv = Cv(32, 32)
    parts = [ell(16, 21, 11, 7.5), ell(10, 24, 7, 5), ell(23, 24, 8, 5)]

    def inside(x, y):
        return any(p(x, y) for p in parts)

    def col(x, y):
        s = (-0.55 * (x - 16) - 0.83 * (y - 19)) / 9.0
        if s > 0.75:
            return R_HI
        if s > 0.25:
            return R_LT
        if s > -0.35:
            return R_MD
        return R_DK
    cv.paint(inside, col)
    # ひび
    cv.pts([(15, 17), (16, 18), (16, 19), (17, 20), (17, 21), (22, 22), (23, 23), (23, 24)], R_DK)
    # 苔
    moss = [(9, 17), (10, 16), (11, 16), (12, 16), (13, 15), (14, 15), (10, 17), (11, 17), (12, 17), (8, 18), (9, 18), (13, 16)]
    for (x, y) in moss:
        if cv.get(x, y)[3] == 255:
            cv.set(x, y, G_MD if (x + y) % 3 else G_LT)
    cv.outline(OUT)
    cv.shadow(16, 30, 12, 1.5)
    return cv.im


def stump():
    cv = Cv(32, 32)
    top_c = hx('d8b070'); ring = hx('a87a40')

    def side(x, y):
        in_rect = 16 <= y <= 27 and 8 - (1.5 if y > 24 else 0) <= x + .5 - 8 + 8 - 8 + 8 - 0 and True
        half = 8.0 + (1.8 if y >= 25 else 0)
        in_rect = 16 <= y <= 27 and abs(x + .5 - 16) <= half
        bottom = y >= 27 and ((x + .5 - 16) / (half + 0.5)) ** 2 + ((y + .5 - 27) / 3.0) ** 2 <= 1
        return in_rect or bottom

    def side_col(x, y):
        if x < 12:
            c = W_LT if x % 4 else W_MD
        elif x > 20:
            c = W_DK if x % 4 else hx('3a2616')
        else:
            c = W_MD if x % 4 else W_DK
        return c
    cv.paint(side, side_col)
    # 上面(年輪)
    def top_in(x, y):
        return ((x + .5 - 16) / 8.8) ** 2 + ((y + .5 - 16) / 3.9) ** 2 <= 1

    def top_col(x, y):
        d = math.hypot((x + .5 - 16) / 8.8, (y + .5 - 16) / 3.9)
        if d > 0.88:
            return W_MD
        if 0.62 < d <= 0.72 or 0.30 < d <= 0.40:
            return ring
        return top_c if (x + y) < 33 else hx('c8a060')
    cv.paint(top_in, top_col)
    cv.pts([(16, 16)], ring)
    # 苔と芽
    for (x, y) in ((8, 26), (9, 25), (9, 26), (10, 26), (10, 27), (11, 27), (22, 25), (23, 26), (22, 26)):
        if cv.get(x, y)[3] == 255:
            cv.set(x, y, G_MD if (x + y) % 2 else G_LT)
    cv.pts([(23, 13), (24, 12), (24, 11), (25, 11), (22, 12), (21, 12)], G_LT)
    cv.pts([(23, 14), (23, 13)], G_MD)
    cv.outline(OUT_W)
    cv.shadow(16, 30, 12, 1.5)
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

if __name__ == '__main__':
    imgs = {}
    for name, fn in ASSETS.items():
        im = fn()
        im.save(f'nature_{name}.png')
        imgs[name] = im
    # プレビュー(8倍・ニアレスト)。地面色の背景版と透過版
    S = 8; gap = 12; cols = 6
    names = list(imgs)
    cw = 32 * S + gap
    rows = [names[:2] + names[2:6][:0], ]  # (未使用)
    big = [n for n in names if imgs[n].size[1] == 64]
    small = [n for n in names if imgs[n].size[1] == 32]
    total_w = max(len(big), len(small[:6])) * cw + gap
    row_h1 = 64 * S + gap
    row_h2 = 32 * S + gap
    n_rows_small = (len(small) + 5) // 6
    H = gap + row_h1 + n_rows_small * row_h2
    for tag, bgc in (('', (217, 184, 112, 255)), ('_transparent', (0, 0, 0, 0))):
        pv = Image.new('RGBA', (6 * cw + gap, H), bgc)
        x = gap
        for n in big:
            pv.alpha_composite(imgs[n].resize((32 * S, 64 * S), Image.NEAREST), (x, gap)); x += cw
        for i, n in enumerate(small):
            r, c = divmod(i, 6)
            pv.alpha_composite(imgs[n].resize((32 * S, 32 * S), Image.NEAREST), (gap + c * cw, gap + row_h1 + r * row_h2))
        pv.save(f'nature_preview{tag}.png')
    print('done', {k: v.size for k, v in imgs.items()})
