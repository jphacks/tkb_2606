"""Cartowords 花スプライト生成 (1輪 32x64px, 透過PNG)
実行: python make_flowers.py  -> flower_*.png と flower_preview.png を出力
"""
import math
from PIL import Image, ImageDraw

W, H = 32, 64
DECO = True  # False にすると飾り(きらめき/炎/涙/泡)を除いた本体のみ描く
T = (0, 0, 0, 0)


def hexc(s, a=255):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)


# 共有パレット(茎・葉)
STEM = hexc('3f7a3a'); STEM_D = hexc('2c5a2e'); LEAF = hexc('4f9a45'); LEAF_L = hexc('6fb85a'); LEAF_D = hexc('2c5a2e')


class Canvas:
    def __init__(self):
        self.im = Image.new('RGBA', (W, H), T)
        self.px = self.im.load()

    def set(self, x, y, c):
        if 0 <= x < W and 0 <= y < H:
            self.px[x, y] = c

    def get(self, x, y):
        return self.px[x, y] if 0 <= x < W and 0 <= y < H else T

    def shape(self, inside, fill, outline=None, shade=None):
        """inside(x,y)->bool の領域を塗り、外周に輪郭線。shade(x,y)->色 で個別色も可"""
        pts = [(x, y) for y in range(H) for x in range(W) if inside(x, y)]
        S = set(pts)
        for x, y in pts:
            self.set(x, y, shade(x, y) if shade else fill)
        if outline:
            for x, y in pts:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    n = (x + dx, y + dy)
                    if n not in S:
                        self.set(n[0], n[1], outline)

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def pts(self, lst, c):
        for x, y in lst:
            self.set(x, y, c)


def ellipse_fn(cx, cy, a, b, rot=0.0):
    cr, sr = math.cos(rot), math.sin(rot)

    def f(x, y):
        dx, dy = x + 0.5 - cx, y + 0.5 - cy
        u, v = dx * cr + dy * sr, -dx * sr + dy * cr
        return (u / a) ** 2 + (v / b) ** 2 <= 1
    return f


def leaf(cv, cx, cy, a, b, rot):
    f = ellipse_fn(cx, cy, a, b, rot)
    cv.shape(f, LEAF, LEAF_D, shade=lambda x, y: LEAF_L if (y < cy - 0.5 and f(x, y)) else LEAF)


def stem_and_leaves(cv, top, bottom=62, x=15, curve=None, leaf_y=54):
    for y in range(top, bottom + 1):
        sx = x + (curve(y) if curve else 0)
        cv.set(sx, y, STEM); cv.set(sx + 1, y, STEM_D)
    sx = x + (curve(leaf_y) if curve else 0)
    leaf(cv, sx - 5, leaf_y, 6.5, 3.2, -0.45)
    leaf(cv, sx + 7, leaf_y - 1, 6.5, 3.2, 0.45)


def polar(cx, cy):
    def g(x, y):
        dx, dy = x + 0.5 - cx, y + 0.5 - cy
        return math.hypot(dx, dy), math.atan2(dy, dx)
    return g


# ---------------- 喜: ひまわり ----------------
def joy():
    cv = Canvas()
    stem_and_leaves(cv, 34)
    cx, cy = 16, 19
    P = polar(cx, cy)

    def petal_r(th):
        return 10.5 + 3.0 * abs(math.cos(6 * th / 2 * 2 / 2 * 2))  # 12枚の花びら

    def inside(x, y):
        r, th = P(x, y)
        return r <= 10.2 + 3.2 * abs(math.cos(6 * th))

    def shade(x, y):
        r, th = P(x, y)
        return hexc('f6c82a') if abs(math.cos(6 * th)) > 0.45 else hexc('e3a416')
    cv.shape(inside, None, hexc('9a5a14'), shade=shade)
    # 花びらの明るい先端ハイライト
    for y in range(H):
        for x in range(W):
            r, th = P(x, y)
            if r > 9 and r <= 10.2 + 3.2 * abs(math.cos(6 * th)) - 1 and math.sin(th) < -0.3 and abs(math.cos(6 * th)) > 0.8:
                cv.set(x, y, hexc('ffe36a'))
    # 中心の顔(明るい黄)
    cv.shape(lambda x, y: P(x, y)[0] <= 7.3, hexc('ffe064'), hexc('c98a14'),
             shade=lambda x, y: hexc('ffe978') if (x + y) < cx + cy - 2 else hexc('ffdc50'))
    # 顔: にっこり目(^ ^)・笑顔・ほっぺ
    dk = hexc('6a3a10'); pk = hexc('f19a8a')
    cv.pts([(11, 19), (12, 18), (13, 18), (14, 19)], dk)
    cv.pts([(18, 19), (19, 18), (20, 18), (21, 19)], dk)
    cv.pts([(10, 21), (11, 21), (10, 22), (11, 22), (21, 21), (22, 21), (21, 22), (22, 22)], pk)
    cv.pts([(13, 22), (14, 23), (15, 23), (16, 23), (17, 23), (18, 22)], dk)
    if not DECO:
        return cv.im
    # きらめき
    sp = hexc('fff3a0')
    cv.pts([(3, 4), (3, 3), (3, 5), (2, 4), (4, 4)], sp)
    cv.pts([(28, 3), (28, 2), (28, 4), (27, 3), (29, 3)], sp)
    cv.pts([(29, 10), (29, 9), (29, 11), (28, 10), (30, 10)], sp)
    return cv.im


# ---------------- 怒: 怒りのユリ ----------------
def anger():
    cv = Canvas()
    stem_and_leaves(cv, 38)
    cx, cy = 16, 22
    P = polar(cx, cy)

    def R(th):
        return 6.0 + 8.2 * (abs(math.cos(4 * th)) ** 2.2)  # 8本のとがり

    def inside(x, y):
        r, th = P(x, y)
        return r <= R(th)

    def shade(x, y):
        r, th = P(x, y)
        if r < 9:
            return hexc('c4281e')
        return hexc('e03a28') if abs(math.cos(4 * th)) > 0.6 and (x + y) % 5 else hexc('a81e1a')
    cv.shape(inside, None, hexc('140808'), shade=shade)
    # 顔(濃い赤→やや明るい赤)
    cv.shape(lambda x, y: P(x, y)[0] <= 7.0, hexc('d8382a'), hexc('6a1010'),
             shade=lambda x, y: hexc('ec4a34') if (x + y) < cx + cy - 3 else hexc('cf3426'))
    dk = hexc('2a0808'); wh = hexc('fff4ea')
    # 怒り眉(内側が下がる)と目
    cv.pts([(11, 18), (12, 19), (13, 19), (14, 20)], dk)
    cv.pts([(21, 18), (20, 19), (19, 19), (18, 20)], dk)
    cv.pts([(12, 21), (13, 21), (19, 21), (20, 21)], dk)
    # ギザ歯の口
    cv.rect(12, 24, 20, 26, wh)
    cv.pts([(11, 24), (21, 24), (11, 25), (21, 25), (12, 27), (13, 27), (14, 27), (15, 27), (16, 27), (17, 27), (18, 27), (19, 27), (20, 27), (12, 23), (13, 23), (14, 23), (15, 23), (16, 23), (17, 23), (18, 23), (19, 23), (20, 23)], dk)
    cv.pts([(14, 24), (14, 25), (16, 24), (16, 25), (18, 24), (18, 25)], hexc('8a6a60'))
    if not DECO:
        return cv.im
    # 炎
    def flame(ox, oy):
        o = hexc('ff9a2a'); y_ = hexc('ffd84a'); r_ = hexc('d8481e')
        rows = [(2, 0, 0), (1, 1, 1), (1, 2, 2), (0, 3, 3), (0, 4, 4), (0, 5, 4)]
        shape = ['..#..', '..##.', '.###.', '.####', '#####', '.###.', '.###.']
        for j, row in enumerate(shape):
            for i, ch in enumerate(row):
                if ch == '#':
                    cv.set(ox + i, oy + j, o if j > 1 else y_)
        cv.pts([(ox + 2, oy + 5), (ox + 2, oy + 4), (ox + 1, oy + 5), (ox + 3, oy + 5)], y_)
        cv.pts([(ox, oy + 5), (ox + 4, oy + 4), (ox, oy + 4)], r_)
    flame(2, 2)
    flame(25, 2)
    return cv.im


# ---------------- 哀: うなだれる釣鐘花 ----------------
def sorrow():
    cv = Canvas()
    # 茎: 下から上へゆるくカーブ
    def curve(y):
        return int(round(3 * math.sin((y - 34) / 12.0)))
    # 茎(釣鐘の下からのびる)
    for y in range(30, 62):
        sx = 15 + curve(y)
        cv.set(sx, y, STEM); cv.set(sx + 1, y, STEM_D)
    leaf(cv, 7, 56, 6.5, 3.2, -0.45)
    leaf(cv, 24, 56, 6.5, 3.2, 0.45)
    # 釣鐘: 上がすぼまり下に広がる
    def half_w(y):
        t = (y - 8) / 26.0
        if t < 0:
            return 0
        body = 3.2 + 7.0 * math.sin(min(t, 0.72) / 0.72 * math.pi / 2) ** 0.8
        flare = 5.0 * max(0.0, (t - 0.72) / 0.28) ** 1.8
        return body + flare

    def inside(x, y):
        if y < 8 or y > 34:
            return False
        return abs(x + 0.5 - 16) <= half_w(y)

    def shade(x, y):
        if y >= 30:
            return hexc('8a62c8')   # 縁(紫)
        return hexc('6a74d8') if x < 17 else hexc('5a62c0')
    cv.shape(inside, None, hexc('2a2a6a'), shade=shade)
    # 縁のスカラップ(下端を波打たせる)
    for x in (4, 8, 12, 16, 20, 24):
        cv.set(x, 35, hexc('2a2a6a'))
    # ハイライト
    cv.pts([(12, 14), (12, 15), (11, 17), (11, 18), (10, 20)], hexc('9aa6f0'))
    # 上のヘタ(緑)
    cv.pts([(15, 7), (16, 7), (14, 8), (17, 8)], STEM_D)
    cv.rect(13, 9, 18, 10, LEAF); cv.rect(12, 10, 19, 10, LEAF); cv.pts([(13, 9), (18, 9)], LEAF_D)
    cv.pts([(15, 4), (16, 4), (15, 5), (16, 5), (16, 6), (17, 3), (18, 2), (19, 2), (19, 3)], STEM)
    cv.pts([(15, 6), (16, 6)], STEM_D)
    cv.pts([(17, 3), (18, 2), (19, 2)], LEAF_L)
    # 顔: 閉じた目(―)、への字口、涙
    dk = hexc('1e1e50'); tr = hexc('9fd8ff')
    cv.pts([(11, 22), (12, 22), (13, 22), (19, 22), (20, 22), (21, 22)], dk)
    cv.pts([(11, 23), (21, 23)], hexc('4a52b0'))
    cv.pts([(14, 27), (15, 26), (16, 26), (17, 26), (18, 27)], dk)
    cv.pts([(12, 24), (12, 25), (20, 24), (20, 25)], tr)
    # しずく(下に落ちる)
    def drop(ox, oy):
        d = hexc('7ec8f0'); dl = hexc('cdeeff'); do = hexc('3a78b0')
        pat = ['.#.', '###', '###', '.#.']
        cv.pts([(ox + 1, oy - 1)], do)
        for j, row in enumerate(pat):
            for i, ch in enumerate(row):
                if ch == '#':
                    cv.set(ox + i, oy + j, d)
        cv.pts([(ox + 1, oy + 1)], dl)
        cv.pts([(ox, oy + 1), (ox, oy + 2), (ox + 2, oy + 2), (ox + 1, oy + 3)], do)
    drop(5, 39)
    drop(25, 41)
    drop(10, 44)
    return cv.im


# ---------------- 楽: コスモス ----------------
def pleasure():
    cv = Canvas()
    stem_and_leaves(cv, 34)
    cx, cy = 16, 20
    P = polar(cx, cy)

    def inside(x, y):
        r, th = P(x, y)
        return r <= 8.6 + 5.2 * (abs(math.cos(4 * th)) ** 0.8)  # 丸い8枚

    def shade(x, y):
        r, th = P(x, y)
        # 花びらの筋
        if r > 8 and abs(math.cos(4 * th)) < 0.12:
            return hexc('f3c8d8')
        return hexc('ffeef4') if (x + y) % 7 else hexc('fdd8e4')
    cv.shape(inside, None, hexc('c8607e'), shade=shade)
    # 花びらの内側の放射線(橙)
    for k in range(8):
        th = k * math.pi / 4 + math.pi / 8
    ray = hexc('f7b08a')
    cv.pts([(16, 9), (16, 10), (16, 11), (16, 29), (16, 30), (16, 28),
            (5, 20), (6, 20), (7, 20), (26, 20), (25, 20), (27, 20),
            (8, 12), (9, 13), (23, 13), (24, 12), (8, 28), (9, 27), (23, 27), (24, 28)], ray)
    # 中心の顔(橙)
    cv.shape(lambda x, y: P(x, y)[0] <= 6.6, hexc('ffb24a'), hexc('c8602a'),
             shade=lambda x, y: hexc('ffd470') if (x + y) < cx + cy - 3 else hexc('ffae44'))
    dk = hexc('7a3a10'); pk = hexc('f08a74')
    cv.pts([(12, 19), (13, 19), (14, 19), (18, 19), (19, 19), (20, 19)], dk)
    cv.pts([(11, 21), (12, 21), (20, 21), (21, 21)], pk)
    cv.pts([(14, 22), (15, 23), (16, 23), (17, 23), (18, 22)], dk)
    if not DECO:
        return cv.im
    # 泡(橙〜桃)
    def bubble(cx_, cy_, r):
        f = ellipse_fn(cx_, cy_, r, r)
        cv.shape(f, hexc('f58a78'), hexc('c8484a'),
                 shade=lambda x, y: hexc('ffb0a0') if (x + y) < cx_ + cy_ - 0.5 else hexc('f58a78'))
    bubble(4, 5, 2.4)
    bubble(27, 6, 2.4)
    bubble(3, 36, 2.0)
    bubble(29, 33, 1.8)
    bubble(26, 38, 1.2)
    return cv.im



def sorrow():
    """原画像に合わせた釣鐘: 丸いドーム + 先がとがって広がる縁(紫) + 緑のがくと巻いた芽"""
    cv = Canvas()
    NAVY = hexc('26286a')
    # S字の茎(ベルの下から)
    def curve(y):
        return int(round(3 * math.sin((y - 34) / 8.0)))
    for y in range(31, 63):
        sx = 15 + curve(y)
        cv.set(sx, y, STEM); cv.set(sx + 1, y, STEM_D)
    leaf(cv, 7, 57, 6.5, 3.2, -0.45)
    leaf(cv, 24, 57, 6.5, 3.2, 0.45)

    def half_w(y):
        if y < 9 or y > 36:
            return -1
        if y <= 15:      # 丸いドーム
            t = (y - 9) / 6.0
            return 2.8 + 5.4 * math.sin(t * math.pi / 2)
        if y <= 29:      # ふくらんだ胴
            t = (y - 15) / 14.0
            return 8.2 + 2.2 * math.sin(t * math.pi / 2)
        # 縁: 先のとがった花びらが外へ広がる
        t = (y - 29) / 7.0
        return 10.4 + 3.6 * t ** 1.3 * (1 - 0.0 * t)

    def inside(x, y):
        return abs(x + 0.5 - 16) <= half_w(y)

    def shade(x, y):
        if y >= 31:
            return hexc('9a6ad0') if x < 16 else hexc('7e52b8')   # 紫の縁
        if x < 12 and 12 <= y <= 27:
            return hexc('7a86e6')      # 左上のハイライト寄り
        return hexc('6674d8') if x < 18 else hexc('545fc0')
    cv.shape(inside, None, NAVY, shade=shade)
    # 縁の下端: 中央をえぐって左右に先端をとがらせる
    for x in range(4, 28):
        if 9 <= x <= 22:
            cv.set(x, 36, T)
            if x in (12, 13, 14, 15, 16, 17, 18, 19):
                cv.set(x, 35, T)
        cv.set(x, 37, T)
    for x in range(7, 25):
        # 新しい下端に輪郭
        if cv.get(x, 34)[3] and not cv.get(x, 35)[3]:
            cv.set(x, 35, NAVY)
    cv.pts([(6, 36), (25, 36)], NAVY)
    for y in range(34, 40):
        sx = 15 + curve(y)
        cv.set(sx, y, STEM); cv.set(sx + 1, y, STEM_D)
    # ハイライト
    cv.pts([(12, 13), (12, 14), (11, 16), (11, 17), (10, 19)], hexc('a8b2f6'))
    # がく(緑)と巻いた芽
    cv.rect(13, 7, 19, 9, LEAF)
    cv.pts([(13, 7), (19, 7), (12, 8), (20, 8), (12, 9), (20, 9)], LEAF_D)
    cv.rect(14, 7, 18, 7, LEAF_L)
    cv.pts([(15, 6), (16, 6), (16, 5), (16, 4), (17, 3), (18, 2), (19, 2), (20, 3)], STEM)
    cv.pts([(17, 3), (18, 2), (19, 2)], LEAF_L)
    cv.pts([(15, 5), (15, 4)], STEM_D)
    # 顔: 閉じた目 + 目の下の涙、への字口
    dk = hexc('1a1a52'); tr = hexc('a8e0ff')
    cv.pts([(10, 21), (11, 22), (12, 22), (13, 22), (14, 21)], dk)
    cv.pts([(18, 21), (19, 22), (20, 22), (21, 22), (22, 21)], dk)
    cv.pts([(11, 24), (11, 25), (21, 24), (21, 25)], tr)
    cv.pts([(11, 26), (21, 26)], hexc('6ab8e8'))
    cv.pts([(14, 28), (15, 27), (16, 27), (17, 27), (18, 28)], dk)
    if not DECO:
        return cv.im
    # しずく3つ(ベルの外)
    def drop(ox, oy, big=False):
        d = hexc('78c4f0'); dl = hexc('d6f0ff'); do = hexc('2e6ea8')
        pat = ['..#..', '.###.', '#####', '#####', '.###.'] if big else ['.#.', '###', '###', '.#.']
        w = len(pat[0])
        cv.pts([(ox + w // 2, oy - 1)], do)
        for j, row in enumerate(pat):
            for i, ch in enumerate(row):
                if ch == '#':
                    cv.set(ox + i, oy + j, d)
        cv.pts([(ox + 1, oy + 1)], dl)
        for j, row in enumerate(pat):
            for i, ch in enumerate(row):
                if ch == '#' and (i in (0, w - 1) or j == len(pat) - 1):
                    cv.set(ox + i, oy + j, do)
    drop(4, 40)
    drop(25, 42)
    drop(9, 47, big=True)
    return cv.im

FLOWERS = {'joy': joy, 'anger': anger, 'sorrow': sorrow, 'pleasure': pleasure}

if __name__ == '__main__':
    imgs = {}
    for name, fn in FLOWERS.items():
        im = fn()
        im.save(f'flower_{name}.png')
        imgs[name] = im
    # プレビュー: 8倍拡大(ニアレスト)、市松背景
    S = 8
    gap = 16
    pw, ph = W * S, H * S
    prev = Image.new('RGBA', (len(imgs) * (pw + gap) + gap, ph + gap * 2), (200, 200, 200, 255))
    for i, (name, im) in enumerate(imgs.items()):
        bg = Image.new('RGBA', (pw, ph))
        d = ImageDraw.Draw(bg)
        for yy in range(0, ph, 16):
            for xx in range(0, pw, 16):
                c = (235, 235, 235, 255) if (xx // 16 + yy // 16) % 2 == 0 else (190, 190, 190, 255)
                d.rectangle([xx, yy, xx + 15, yy + 15], fill=c)
        big = im.resize((pw, ph), Image.NEAREST)
        bg.alpha_composite(big)
        prev.paste(bg, (gap + i * (pw + gap), gap))
    prev.save('flower_preview.png')
    # 背景透過のプレビュー(4枚を横並び、8倍拡大)
    tp = Image.new('RGBA', (len(imgs) * (pw + gap) + gap, ph + gap * 2), (0, 0, 0, 0))
    for i, im in enumerate(imgs.values()):
        tp.alpha_composite(im.resize((pw, ph), Image.NEAREST), (gap + i * (pw + gap), gap))
    tp.save('flower_preview_transparent.png')
    sheet = Image.new('RGBA', (W * len(imgs), H), (0, 0, 0, 0))
    for i, im in enumerate(imgs.values()):
        sheet.alpha_composite(im, (W * i, 0))
    sheet.save('flower_all_32x64.png')
    print('done', {k: v.size for k, v in imgs.items()})
