"""Cartowords 枯れた花 (1コマ 32x32px, 透過PNG, 静止画)
実行: python withered_flowers.py -> flower_<emotion>_withered.png と withered_preview*.png
"""
import math
from PIL import Image, ImageDraw
import make_flowers as mf
from make_flowers import hexc, T

mf.H = 32  # このスクリプトでは 32x32 のキャンバスを使う
DEBRIS = True  # False で空中のかけらを除く(アニメ用)
W, H = 32, 32

OUT = hexc('2a1a10')       # 共通の輪郭(焦げ茶)
STEM_C = hexc('7a5a34'); STEM_D = hexc('4a3220'); STEM_L = hexc('9a7a4a')
LEAF_C = hexc('8a7a32'); LEAF_D = hexc('4a4018'); LEAF_L = hexc('a89a48')


def new():
    return mf.Canvas()


def bez(p0, p1, p2, p3, n=60):
    out = []
    for i in range(n + 1):
        t = i / n
        a = (1 - t) ** 3; b = 3 * (1 - t) ** 2 * t; c = 3 * (1 - t) * t ** 2; d = t ** 3
        out.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0],
                    a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return out


def stem(cv, curve, fill=STEM_C, dark=STEM_D, light=STEM_L, outline=OUT):
    S = set()
    for i, (x, y) in enumerate(curve):
        j = min(i + 1, len(curve) - 1)
        dx = abs(curve[j][0] - curve[i - 1 if i else 0][0]); dy = abs(curve[j][1] - curve[i - 1 if i else 0][1])
        X, Y = int(round(x)), int(round(y))
        S.add((X, Y))
        if dy >= dx:
            S.add((X + 1, Y))
        else:
            S.add((X, Y + 1))
    cv.shape(lambda x, y: (x, y) in S, fill, outline,
             shade=lambda x, y: light if ((x - 1, y) not in S) else (dark if ((x + 1, y) not in S) else fill))


def leaf(cv, cx, cy, a, b, rot, fill=LEAF_C, dark=LEAF_D, light=LEAF_L):
    f = mf.ellipse_fn(cx, cy, a, b, rot)
    cv.shape(f, fill, dark, shade=lambda x, y: light if (x + y) < cx + cy - 0.5 else fill)


def petal_bits(cv, items, outline=OUT):
    """地面に落ちた花びら: (cx, cy, a, b, rot, fill, light)"""
    for (cx, cy, a, b, rot, fill, light) in items:
        f = mf.ellipse_fn(cx, cy, a, b, rot)
        cv.shape(f, fill, outline, shade=lambda x, y, f=fill, l=light, cx=cx, cy=cy: l if x < cx - 0.5 else f)


def face(cv, cx, cy, col=hexc('120a08')):
    """閉じた目と、への字口"""
    cv.pts([(cx - 4, cy - 1), (cx - 3, cy - 1), (cx - 2, cy - 1)], col)
    cv.pts([(cx + 1, cy), (cx + 2, cy), (cx + 3, cy)], col)
    cv.pts([(cx - 1, cy + 3), (cx, cy + 2), (cx + 1, cy + 2), (cx + 2, cy + 3)], col)


def polar(cx, cy):
    def g(x, y):
        dx, dy = x + 0.5 - cx, y + 0.5 - cy
        return math.hypot(dx, dy), math.atan2(dy, dx)
    return g


# ---------------- 喜 (ひまわり) ----------------
def joy():
    cv = new()
    cx, cy = 20, 11
    petal_bits(cv, [(23, 29, 2.8, 1.2, 0.1, hexc('b58a2a'), hexc('d6aa3a')),
                    (27, 27, 2.4, 1.1, -0.3, hexc('6f5a3a'), hexc('8a7a60')),
                    (17, 30, 2.4, 1.0, 0.0, hexc('8a7a60'), hexc('a89878'))])
    leaf(cv, 5, 22, 5.2, 2.2, 1.15)
    leaf(cv, 15, 25, 5.0, 2.2, 0.75)
    stem(cv, bez((10, 31), (9, 21), (9, 15), (14, 14)))
    P = polar(cx, cy)
    cols = [hexc('b58a2a'), hexc('7a6038')]

    def R(th):
        return 5.4 + 3.0 * abs(math.cos(5 * th))

    def inside(x, y):
        r, th = P(x, y)
        return r <= R(th)

    def shade(x, y):
        r, th = P(x, y)
        k = int(round(((th + math.pi) / (2 * math.pi)) * 10)) % 2
        c = cols[k]
        if x + y > cx + cy + 4:  # 右下は暗く
            c = (int(c[0] * .8), int(c[1] * .8), int(c[2] * .8), 255)
        return c
    cv.shape(inside, None, OUT, shade=shade)
    cv.shape(lambda x, y: P(x, y)[0] <= 4.4, hexc('8a6a48'), hexc('3e2a1a'),
             shade=lambda x, y: hexc('a07c54') if (x + y) < cx + cy - 1 else hexc('86664a'))
    face(cv, cx, cy)
    return cv.im


# ---------------- 怒 (怒りのユリ) ----------------
def anger():
    cv = new()
    cx, cy = 21, 13
    petal_bits(cv, [(5, 30, 3.2, 1.2, 0.0, hexc('6a4228'), hexc('8a5a34')),
                    (15, 30, 2.8, 1.1, 0.1, hexc('7a3a28'), hexc('9a4a34')),
                    (24, 29, 3.2, 1.2, -0.2, hexc('4e2a22'), hexc('6a3a2e')),
                    (28, 31, 1.8, 0.8, 0.0, hexc('3a1e18'), hexc('5a2e24'))], outline=hexc('0a0606'))
    leaf(cv, 5, 18, 5.0, 2.0, 1.45)
    stem(cv, bez((9, 31), (7, 16), (8, 5), (17, 8)), outline=hexc('0a0606'))
    P = polar(cx, cy)

    def R(th):
        r = 4.4 + 5.2 * (abs(math.cos(4 * th)) ** 2.2)
        if -0.2 < th < 0.7:  # 折れた花びら
            r *= 0.62
        return r

    def inside(x, y):
        r, th = P(x, y)
        return r <= R(th)

    def shade(x, y):
        r, th = P(x, y)
        a = abs(math.cos(4 * th))
        if a > 0.6:
            return hexc('7a3a28') if (x + y) % 4 else hexc('8e4430')
        return hexc('4e2a22')
    cv.shape(inside, None, hexc('0a0606'), shade=shade)
    cv.shape(lambda x, y: P(x, y)[0] <= 4.4, hexc('6a4a2a'), hexc('2a1a10'),
             shade=lambda x, y: hexc('7e5a34') if (x + y) < cx + cy - 1 else hexc('624424'))
    face(cv, cx, cy)
    return cv.im


# ---------------- 哀 (釣鐘) ----------------
def sorrow():
    cv = new()
    petal_bits(cv, [(4, 30, 3.0, 1.1, 0.0, hexc('5a3e28'), hexc('7a5a3a')),
                    (24, 30, 3.0, 1.1, 0.1, hexc('4a3220'), hexc('6a4a30')),
                    (14, 31, 2.0, 0.9, 0.0, hexc('5a3e28'), hexc('7a5a3a'))])
    leaf(cv, 5, 12, 4.6, 2.0, 1.2)
    leaf(cv, 14, 25, 5.0, 2.2, 0.7)
    stem(cv, bez((9, 31), (8, 10), (10, 2), (19, 5)))
    # 枯れた釣鐘(茶)
    def hw(y):
        if y < 8 or y > 23:
            return -1
        if y <= 10:
            return 2.4 + (y - 8) * 0.7
        if y <= 19:
            return 3.8 + (y - 10) * 0.13
        return 5.0 + (y - 19) * 0.6
    cx = 19

    def inside(x, y):
        return abs(x + 0.5 - cx) <= hw(y)

    def shade(x, y):
        if y >= 20:
            return hexc('5a4028')
        return hexc('76583a') if x < cx - 1 else hexc('5e442c')
    cv.shape(inside, None, hexc('241810'), shade=shade)
    # 縁の下端を欠けさせる
    for x in range(13, 26):
        if (x * 5) % 7 < 2:
            cv.set(x, 23, T)
    # がく(オリーブ)
    cv.pts([(17, 8), (18, 8), (19, 8), (20, 8), (21, 8)], hexc('8a7a32'))
    cv.pts([(16, 8), (22, 8)], LEAF_D)
    face(cv, cx, 14)
    # 落ちる茶色のかけら
    for (x, y) in (((25, 20), (26, 23), (7, 26)) if DEBRIS else ()):
        cv.set(x, y, hexc('6a4a30'))
    return cv.im


# ---------------- 楽 (コスモス) ----------------
def pleasure():
    cv = new()
    cx, cy = 11, 14
    petal_bits(cv, [(4, 29, 2.8, 1.2, 0.0, hexc('b8808c'), hexc('d0a4ae')),
                    (9, 30, 2.4, 1.0, 0.2, hexc('9a6676'), hexc('c094a0')),
                    (14, 30, 2.6, 1.1, -0.1, hexc('b8808c'), hexc('d0a4ae')),
                    (3, 24, 1.6, 0.9, 0.5, hexc('9a6676'), hexc('c094a0'))], outline=hexc('4a2230'))
    leaf(cv, 27, 24, 5.0, 2.0, 1.2)
    leaf(cv, 18, 26, 4.6, 2.0, 0.6)
    stem(cv, bez((24, 31), (25, 14), (23, 4), (15, 6)))
    P = polar(cx, cy)

    def inside(x, y):
        r, th = P(x, y)
        return r <= 4.2 + 3.8 * (abs(math.cos(4 * th)) ** 0.8)

    def shade(x, y):
        r, th = P(x, y)
        if abs(math.cos(4 * th)) < 0.14:
            return hexc('8a5666')
        return hexc('c89aa6') if (x + y) % 6 else hexc('b8808c')
    cv.shape(inside, None, hexc('4a2230'), shade=shade)
    cv.shape(lambda x, y: P(x, y)[0] <= 4.3, hexc('a87a50'), hexc('4a3220'),
             shade=lambda x, y: hexc('c09868') if (x + y) < cx + cy - 1 else hexc('a07448'))
    face(cv, cx, cy)
    return cv.im


WITHERED = {'joy': joy, 'anger': anger, 'sorrow': sorrow, 'pleasure': pleasure}

if __name__ == '__main__':
    imgs = {}
    for name, fn in WITHERED.items():
        im = fn()
        assert im.size == (W, H), im.size
        im.save(f'flower_{name}_withered.png')
        imgs[name] = im
    S = 12; gap = 12; pw = W * S
    prev = Image.new('RGBA', (len(imgs) * (pw + gap) + gap, pw + gap * 2), (200, 200, 200, 255))
    tp = Image.new('RGBA', prev.size, (0, 0, 0, 0))
    for i, im in enumerate(imgs.values()):
        bg = Image.new('RGBA', (pw, pw)); d = ImageDraw.Draw(bg)
        for yy in range(0, pw, 16):
            for xx in range(0, pw, 16):
                c = (235, 235, 235, 255) if (xx // 16 + yy // 16) % 2 == 0 else (190, 190, 190, 255)
                d.rectangle([xx, yy, xx + 15, yy + 15], fill=c)
        big = im.resize((pw, pw), Image.NEAREST)
        bg.alpha_composite(big)
        prev.paste(bg, (gap + i * (pw + gap), gap))
        tp.alpha_composite(big, (gap + i * (pw + gap), gap))
    prev.save('withered_preview.png')
    tp.save('withered_preview_transparent.png')
    row = Image.new('RGBA', (W * 4, H), (0, 0, 0, 0))
    for i, im in enumerate(imgs.values()):
        row.alpha_composite(im, (W * i, 0))
    row.save('flower_all_withered_32x32.png')
    print('done')
