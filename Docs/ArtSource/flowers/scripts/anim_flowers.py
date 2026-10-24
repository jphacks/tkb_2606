"""8コマアニメ生成: 各感情 4列x2行 = 128x128px のスプライトシート (1コマ 32x64px)
実行: python anim_flowers.py
順番: 左上から右へ、次の行も左から右へ。最後のコマの次は最初のコマへ自然に戻る。
"""
import math
from PIL import Image
import make_flowers as mf
from make_flowers import W, H, hexc, T

N = 8
TAU = 2 * math.pi


def sway_shift(y, t, amp, pattern=None):
    """高さに応じた横ずれ(根元は動かない)。pattern があればそれを使う"""
    w = max(0.0, (60 - y) / 60.0) ** 1.5
    ph = pattern[t] if pattern else math.sin(TAU * t / N)
    return int(round(amp * ph * w))


def breathe(im, t, k, ymax=36, cx=16):
    """頭部だけ横に伸縮(呼吸)。k=振れ幅"""
    s = 1 + k * math.sin(TAU * t / N + math.pi / 2)
    src = im.load()
    out = Image.new('RGBA', (W, H), T)
    dst = out.load()
    for y in range(H):
        for x in range(W):
            if y < ymax:
                sx = int(math.floor(cx + (x + 0.5 - cx) / s))
            else:
                sx = x
            if 0 <= sx < W:
                dst[x, y] = src[sx, y]
    return out


def shear(im, t, amp, pattern=None):
    src = im.load()
    out = Image.new('RGBA', (W, H), T)
    dst = out.load()
    for y in range(H):
        d = sway_shift(y, t, amp, pattern)
        for x in range(W):
            nx = x + d
            if 0 <= nx < W and src[x, y][3]:
                dst[nx, y] = src[x, y]
    return out


def canvas_from(im):
    cv = mf.Canvas()
    cv.im = im
    cv.px = im.load()
    return cv


# ---------- 飾り ----------
def sparkle(cv, x, y, level):
    c = hexc('fff3a0'); c2 = hexc('ffe36a')
    if level == 3:      # 大
        cv.pts([(x, y), (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)], c)
    elif level == 2:    # 中
        cv.pts([(x, y)], c)
        cv.pts([(x - 1, y - 1), (x + 1, y - 1), (x - 1, y + 1), (x + 1, y + 1)], c2)
    elif level == 1:    # 小
        cv.pts([(x, y)], c2)


SPARK_CYCLE = [3, 3, 2, 1, 0, 1, 2, 3]


def deco_joy(cv, t):
    for (x, y, off) in ((3, 4, 0), (28, 3, 3), (29, 10, 5)):
        sparkle(cv, x + sway_shift(y, t, 2), y, SPARK_CYCLE[(t + off) % N])


FLAME = [
    ['..#..', '..##.', '.###.', '.####', '#####', '.###.', '.###.'],
    ['.#...', '.##..', '.###.', '####.', '#####', '.###.', '.###.'],
    ['...#.', '..##.', '.###.', '.####', '#####', '.###.', '.###.'],
    ['..#..', '.###.', '.###.', '#####', '#####', '.###.', '.###.'],
]


def flame(cv, ox, oy, var):
    o = hexc('ff9a2a'); y_ = hexc('ffd84a'); r_ = hexc('d8481e')
    pat = FLAME[var]
    for j, row in enumerate(pat):
        for i, ch in enumerate(row):
            if ch == '#':
                cv.set(ox + i, oy + j, y_ if j <= 1 or (j > 4 and 1 <= i <= 3) else o)
    bottom = len(pat) - 1
    for i, ch in enumerate(pat[bottom]):
        if ch == '#' and i in (1, 3):
            cv.set(ox + i, oy + bottom, r_)
    cv.set(ox, oy + 4, r_); cv.set(ox + 4, oy + 4, r_)


SHAKE = [0, 1, 0, -1, 0, 1, 0, -1]


def deco_anger(cv, t):
    sh = sway_shift(6, t, 2, SHAKE)
    flame(cv, 2 + sh, 2 + (t % 2), [0, 1, 2, 3, 1, 0, 3, 2][t])
    flame(cv, 25 + sh, 2 + ((t + 1) % 2), [2, 3, 0, 1, 3, 2, 1, 0][t])


def drop(cv, ox, oy, big):
    d = hexc('78c4f0'); dl = hexc('d6f0ff'); do = hexc('2e6ea8')
    pat = ['..#..', '.###.', '#####', '#####', '.###.'] if big else ['.#.', '###', '###', '.#.']
    w = len(pat[0])
    cv.pts([(ox + w // 2, oy - 1)], do)
    for j, row in enumerate(pat):
        for i, ch in enumerate(row):
            if ch == '#':
                edge = i in (0, w - 1) or j == len(pat) - 1
                cv.set(ox + i, oy + j, do if edge else d)
    cv.set(ox + 1, oy + 1, dl)


def deco_sorrow(cv, t):
    # しずくはベルの縁から落ちて、8コマで一周
    sh = sway_shift(36, t, 3)
    for (x, off, big) in ((4, 0, False), (25, 8, False), (9, 16, True)):
        y = 38 + ((t * 3 + off) % 24)
        h = 5 if big else 4
        if y + h <= 63:
            drop(cv, x + sh, y, big)


def bubble(cv, cx_, cy_, r):
    f = mf.ellipse_fn(cx_, cy_, r, r)
    cv.shape(f, hexc('f58a78'), hexc('c8484a'),
             shade=lambda x, y: hexc('ffb0a0') if (x + y) < cx_ + cy_ - 0.5 else hexc('f58a78'))


def deco_pleasure(cv, t):
    for (x, y, r, ph) in ((4, 5, 2.4, 0), (27, 6, 2.4, 3), (3, 36, 2.0, 5), (29, 33, 1.8, 1), (26, 38, 1.2, 6)):
        dy = round(1.6 * math.sin(TAU * (t + ph) / N))
        dx = round(1.0 * math.cos(TAU * (t + ph) / N)) + sway_shift(y, t, 2)
        bubble(cv, x + dx, y + dy, r)


# 感情ごとの設定: 本体の動かし方 + 飾り
CONFIG = {
    'joy':      dict(body=lambda im, t: shear(breathe(im, t, 0.05), t, 2), deco=deco_joy),
    'anger':    dict(body=lambda im, t: shear(im, t, 2, SHAKE), deco=deco_anger),
    'sorrow':   dict(body=lambda im, t: shear(im, t, 3), deco=deco_sorrow),
    'pleasure': dict(body=lambda im, t: shear(breathe(im, t, 0.05), t, 2), deco=deco_pleasure),
}


def build(name):
    mf.DECO = False
    base = mf.FLOWERS[name]()
    frames = []
    for t in range(N):
        im = CONFIG[name]['body'](base, t)
        CONFIG[name]['deco'](canvas_from(im), t)
        frames.append(im)
    return frames


if __name__ == '__main__':
    all_frames = {}
    for name in CONFIG:
        fr = build(name)
        all_frames[name] = fr
        sheet = Image.new('RGBA', (W * 4, H * 2), T)
        for i, f in enumerate(fr):
            sheet.alpha_composite(f, (W * (i % 4), H * (i // 4)))
        sheet.save(f'flower_{name}_anim.png')
        # 端が切れていないか確認
        clip = sum(1 for f in fr for y in range(H) if f.getpixel((0, y))[3] or f.getpixel((W - 1, y))[3])
        print(name, sheet.size, 'edge-touching px:', clip)
    # 確認用: 透過のままの 4種x8コマ一覧(4倍) と、GIF(灰背景)
    S = 4
    grid = Image.new('RGBA', (N * W * S, 4 * H * S), (0, 0, 0, 0))
    for r, name in enumerate(CONFIG):
        for t, f in enumerate(all_frames[name]):
            grid.alpha_composite(f.resize((W * S, H * S), Image.NEAREST), (t * W * S, r * H * S))
    grid.save('anim_overview_transparent.png')
    bg = Image.new('RGBA', grid.size, (205, 205, 205, 255)); bg.alpha_composite(grid)
    bg.save('anim_overview.png')
    for name, fr in all_frames.items():
        gs = []
        for f in fr:
            b = Image.new('RGBA', (W * 6, H * 6), (205, 205, 205, 255))
            b.alpha_composite(f.resize((W * 6, H * 6), Image.NEAREST))
            gs.append(b.convert('P', palette=Image.ADAPTIVE))
        gs[0].save(f'flower_{name}_anim_preview.gif', save_all=True, append_images=gs[1:], duration=125, loop=0)
