"""枯れた花の8コマアニメ: 各感情 4列x2行 = 128x64px (1コマ 32x32px)
実行: python anim_withered.py
順番: 左上から右へ、次の行も左から右へ。8コマ目の次は1コマ目へ自然に戻る。
動き: 頭がゆっくり揺れる + 花びら/かけらが落ちる
"""
import math
from PIL import Image
import make_flowers as mf
import withered_flowers as wf
from make_flowers import hexc, T

W, H, N = 32, 32, 8
TAU = 2 * math.pi


def shift(y, t, amp):
    w = max(0.0, (31 - y) / 31.0) ** 1.5
    return int(round(amp * math.sin(TAU * t / N) * w))


def shear(im, t, amp):
    src = im.load(); out = Image.new('RGBA', (W, H), T); dst = out.load()
    for y in range(H):
        d = shift(y, t, amp)
        for x in range(W):
            nx = x + d
            if 0 <= nx < W and src[x, y][3]:
                dst[nx, y] = src[x, y]
    return out


def canvas_from(im):
    cv = mf.Canvas(); cv.im = im; cv.px = im.load(); return cv


def falling(cv, t, x0, y0, speed, drift, phase, y_end, c1, c2, amp=3):
    """phase ずらしで8コマごとに一周する落下物。ひらひら揺れながら落ちる"""
    k = (t + phase) % N
    y = y0 + int(round(speed * k))
    if y > y_end:
        return
    x = x0 + int(round(drift * math.sin(TAU * k / N))) + shift(y0, t, amp)
    if k % 2 == 0:
        cv.pts([(x, y), (x + 1, y)], c1); cv.pts([(x + 1, y + 1)], c2)
    else:
        cv.pts([(x, y), (x, y + 1)], c1); cv.pts([(x + 1, y + 1)], c2)


# 感情ごと: 落ちるもの (x0, y0, speed, drift, phase, y_end, 色1, 色2)
DROPS = {
    'joy': [(17, 18, 1.4, 1.5, 0, 28, hexc('b58a2a'), hexc('7a6038')),
            (24, 17, 1.4, 1.5, 4, 28, hexc('7a6038'), hexc('4a3a20'))],
    'anger': [(18, 19, 1.3, 1.5, 0, 28, hexc('7a3a28'), hexc('3a1e18')),
              (26, 18, 1.3, 1.5, 4, 28, hexc('4e2a22'), hexc('1a0e0a'))],
    'sorrow': [(15, 24, 0.7, 1.0, 0, 28, hexc('6a4a30'), hexc('3a2818')),
               (24, 24, 0.7, 1.0, 4, 28, hexc('6a4a30'), hexc('3a2818'))],
    'pleasure': [(6, 21, 1.1, 1.5, 0, 28, hexc('b8808c'), hexc('7a4a5a')),
                 (15, 22, 1.1, 1.5, 4, 28, hexc('d0a4ae'), hexc('8a5666'))],
}
AMP = {'joy': 3, 'anger': 3, 'sorrow': 3, 'pleasure': 3}


def build(name):
    wf.DEBRIS = False
    base = wf.WITHERED[name]()
    frames = []
    for t in range(N):
        im = shear(base, t, AMP[name])
        cv = canvas_from(im)
        for d in DROPS[name]:
            falling(cv, t, *d)
        frames.append(im)
    return frames


if __name__ == '__main__':
    allf = {}
    for name in wf.WITHERED:
        fr = build(name); allf[name] = fr
        sheet = Image.new('RGBA', (W * 4, H * 2), T)
        for i, f in enumerate(fr):
            sheet.alpha_composite(f, (W * (i % 4), H * (i // 4)))
        sheet.save(f'flower_{name}_withered_anim.png')
        print(name, sheet.size)
    S = 6
    grid = Image.new('RGBA', (N * W * S, 4 * H * S), (0, 0, 0, 0))
    for r, name in enumerate(allf):
        for t, f in enumerate(allf[name]):
            grid.alpha_composite(f.resize((W * S, H * S), Image.NEAREST), (t * W * S, r * H * S))
    grid.save('withered_anim_overview_transparent.png')
    bg = Image.new('RGBA', grid.size, (205, 205, 205, 255)); bg.alpha_composite(grid)
    bg.save('withered_anim_overview.png')
    for name, fr in allf.items():
        gs = []
        for f in fr:
            b = Image.new('RGBA', (W * 8, H * 8), (205, 205, 205, 255))
            b.alpha_composite(f.resize((W * 8, H * 8), Image.NEAREST))
            gs.append(b.convert('P', palette=Image.ADAPTIVE))
        gs[0].save(f'flower_{name}_withered_anim_preview.gif', save_all=True, append_images=gs[1:], duration=125, loop=0)
