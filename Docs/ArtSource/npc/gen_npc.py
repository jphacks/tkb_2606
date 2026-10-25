#!/usr/bin/env python3
"""32x48 deformed villager NPC sprite generator (front/back/left/right)."""
import os, random
from PIL import Image

W, H = 32, 48
CX = 16
OL = (104, 72, 56)
EYE = (62, 42, 42)
BLUSH = (238, 160, 152)
MOUTH = (176, 104, 92)


def mix(c, t, f):
    return tuple(int(c[i] + (t[i] - c[i]) * f) for i in range(3))


def lighten(c, f=0.2):
    return mix(c, (255, 255, 255), f)


def darken(c, f=0.2):
    return mix(c, (70, 36, 48), f)


class Cv:
    def __init__(self):
        self.p = [[None] * W for _ in range(H)]

    def get(self, x, y):
        return self.p[y][x] if 0 <= x < W and 0 <= y < H else None


def paint(cv, cond, col):
    for y in range(H):
        for x in range(W):
            if cond(x, y):
                cv.p[y][x] = col(x, y) if callable(col) else col


def R(x0, y0, x1, y1):
    return lambda x, y: x0 <= x < x1 and y0 <= y < y1


def E(cx, cy, rx, ry):
    return lambda x, y: ((x + .5 - cx) / rx) ** 2 + ((y + .5 - cy) / ry) ** 2 <= 1


def both(a, b):
    return lambda x, y: a(x, y) and b(x, y)


def tone(base, hcx, hcy):
    L, D = lighten(base, .17), darken(base, .17)

    def f(x, y):
        u = (x - hcx) + (y - hcy) * 0.6
        if u > 6:
            return D
        if -9 < u < -5:
            return L
        return base
    return f


G = {
    'child': dict(hcy=18, hrx=11, hry=10, tt=28, tb=37, bt=39, tx0=10, tx1=22, bw=7),
    'adult': dict(hcy=15, hrx=9.5, hry=9, tt=23, tb=35, bt=42, tx0=9, tx1=23, bw=7),
    'elder': dict(hcy=17, hrx=9.5, hry=9, tt=25, tb=37, bt=42, tx0=9, tx1=23, bw=7),
}


def draw(P, d, frame=0):
    """frame: 0=stand, 1=left foot step, 2=stand, 3=right foot step."""
    g = G[P['body']]
    cv = Cv()
    dy = 1 if frame in (1, 3) else 0           # body lowers on contact frames
    sx = {0: 0, 1: 1, 2: 0, 3: -1}[frame]      # hair / item sway
    hcy, hrx, hry = g['hcy'] + dy, g['hrx'], g['hry']
    tt, tb, bt, tx0, tx1 = g['tt'] + dy, g['tb'] + dy, g['bt'], g['tx0'], g['tx1']
    waist = tb - 3
    skin, top, bot, hair = P['skin'], P['top'], P['bottom'], P['hair']
    boot = P.get('boot', (122, 86, 66))
    sleeve = P.get('sleeve', top)
    kind = P['kind']
    st = P['hstyle']
    side = (d == 'left')
    back = (d == 'back')
    if side:
        lx0, lx1 = tx0 + 2, tx1 - 2
    else:
        lx0, lx1 = tx0, tx1
    ht = tone(hair, CX, g['hcy'])

    inner = E(CX, hcy, hrx, hry)
    outer = E(CX, hcy, hrx + .8, hry + .8)

    # ---------- legs / bottom ----------
    if kind == 'pants':
        paint(cv, R(lx0, waist, lx1, bt), bot)
        if not side:
            paint(cv, R(15, tb, 16, bt), darken(bot, .25))
    else:
        paint(cv, R(lx0 + 2, tb, lx1 - 2, bt), skin)
    # ---------- boots ----------
    bw = g['bw']

    def draw_boot(x0, x1, lift):
        y0, y1 = bt - lift, 46 - lift
        paint(cv, R(x0, y0, x1, y1), boot)
        paint(cv, R(x0, y1 - 1, x1, y1), darken(boot, .22))
        paint(cv, R(x0, y0, x1, y0 + 1), lighten(boot, .12))
        for xx in (x0, x1 - 1):
            cv.p[y0][xx] = None            # rounded top corners

    if side:
        # foot1 = near/front foot (toward facing), foot2 = back foot
        f1 = {1: (-2, 0), 3: (2, 1)}.get(frame, (0, 0))
        f2 = {1: (2, 1), 3: (-2, 0)}.get(frame, (0, 0))
        order = [(CX, CX + bw - 1, f2), (CX - bw, CX, f1)]
        if frame == 3:
            order = order[::-1]
        for x0, x1, (sh, lf) in order:
            draw_boot(x0 + sh, x1 + sh, lf)
        if frame in (0, 2):
            paint(cv, R(CX - 1, bt, CX, 46), darken(boot, .3))
    else:
        draw_boot(CX - bw, CX, 2 if frame == 1 else 0)
        draw_boot(CX, CX + bw, 2 if frame == 3 else 0)
        paint(cv, R(CX - 1, bt, CX, 46 - (2 if frame == 1 else 0)), darken(boot, .3))

    # ---------- backpack (behind body for side) ----------
    pack = P.get('pack')
    if pack and side:
        paint(cv, R(lx1 - 1, tt + 1, lx1 + 4, waist + 3), pack)
        paint(cv, R(lx1 + 2, tt + 1, lx1 + 4, waist + 3), darken(pack, .2))

    # long hair behind (side / back drawn behind head, side behind torso)
    if st == 'long' and side:
        paint(cv, both(E(CX + 4 + sx, hcy + 9, 8, 11), lambda x, y: y < tt + 9), ht)
    if st == 'twin' and side:
        paint(cv, E(CX + 8 + sx, hcy + 8 + dy, 4.6, 9.5), ht)

    # ---------- torso ----------
    if kind == 'dress':
        paint(cv, R(lx0, tt, lx1, tb), top)
        paint(cv, R(lx0 - 1, tb - 2, lx1 + 1, tb), top)
        paint(cv, R(lx0 - 1, tb - 1, lx1 + 1, tb), darken(top, .12))
    elif kind == 'skirt':
        paint(cv, R(lx0, tt, lx1, waist), top)
        paint(cv, R(lx0 - 1, waist, lx1 + 1, tb), bot)
    else:
        paint(cv, R(lx0, tt, lx1, waist), top)
    # ---------- arms ----------
    handrows = 2 if P.get('longsleeve') else 3
    if side:
        adx = {1: 2, 3: -2}.get(frame, 0)      # arm opposes the near foot
        paint(cv, R(CX - 1 + adx, tt + 1, CX + 2 + adx, tb - handrows), sleeve)
        paint(cv, R(CX - 1 + adx, tb - handrows, CX + 2 + adx, tb - 1 + 0), skin)
    else:
        for ax0, ao in ((lx0 - 2, {1: -1, 3: 1}.get(frame, 0)),
                        (lx1, {1: 1, 3: -1}.get(frame, 0))):
            paint(cv, R(ax0, tt + 1 + ao, ax0 + 2, tb - handrows + ao), sleeve)
            paint(cv, R(ax0, tb - handrows + ao, ax0 + 2, tb - 1 + ao), skin)
    # shading on right edge
    if not side:
        for y in range(tt, tb):
            for x in (lx1 - 2, lx1 - 1):
                if cv.get(x, y) == top:
                    cv.p[y][x] = darken(top, .13)
            for x in (lx1, lx1 + 1):
                if cv.get(x, y) == sleeve:
                    cv.p[y][x] = darken(sleeve, .13)
    else:
        for y in range(tt, tb):
            for x in (lx1 - 2, lx1 - 1):
                if cv.get(x, y) == top:
                    cv.p[y][x] = darken(top, .13)

    # ---------- torso extras ----------
    if P.get('bib') and True:
        bc = P['bib']
        if d == 'front':
            paint(cv, R(CX - 5, tt, CX - 3, waist), bc)
            paint(cv, R(CX + 3, tt, CX + 5, waist), bc)
            paint(cv, R(CX - 4, waist - 3, CX + 4, waist), bc)
            cv.p[waist - 3][CX - 4] = (240, 210, 90)
            cv.p[waist - 3][CX + 3] = (240, 210, 90)
        elif back:
            paint(cv, R(CX - 5, tt, CX - 3, waist), bc)
            paint(cv, R(CX + 3, tt, CX + 5, waist), bc)
        else:
            paint(cv, R(CX - 4, tt, CX - 2, waist), bc)
    if P.get('apron'):
        ap = P['apron']
        if d == 'front':
            paint(cv, R(tx0 + 1, tt + 4, tx1 - 1, tb), ap)
            paint(cv, R(CX - 3, tt + 1, CX + 3, tt + 4), ap)
            paint(cv, R(tx1 - 3, tt + 4, tx1 - 1, tb), darken(ap, .1))
            paint(cv, R(tx0 + 1, tb - 1, tx1 - 1, tb), darken(ap, .1))
        elif back:
            paint(cv, R(CX - 2, waist - 1, CX + 2, waist + 2), ap)
            paint(cv, R(CX - 4, waist, CX + 4, waist + 1), ap)
        else:
            paint(cv, R(lx0 - 1, tt + 4, lx0 + 2, tb), ap)
    if P.get('shawl'):
        sc = P['shawl']
        n = 4 if d != 'back' else 6
        paint(cv, R(lx0 - 1, tt, lx1 + 1, tt + 4), sc)
        if not side:
            for k in range(n):
                paint(cv, R(lx0 + 1 + k, tt + 4 + k, lx1 - 1 - k, tt + 5 + k), sc)
        else:
            paint(cv, R(lx0 - 1, tt + 4, lx1 + 1, tt + 6), sc)
        paint(cv, R(lx0 - 1, tt + 3, lx1 + 1, tt + 4), darken(sc, .12))
    if pack and d == 'back':
        paint(cv, R(tx0 + 1, tt + 1, tx1 - 1, waist + 3), pack)
        paint(cv, R(tx1 - 3, tt + 1, tx1 - 1, waist + 3), darken(pack, .2))
        paint(cv, R(tx0 + 1, tt + 4, tx1 - 1, tt + 5), darken(pack, .25))
    if pack and d == 'front':
        paint(cv, R(CX - 5, tt, CX - 3, waist), darken(pack, .1))
        paint(cv, R(CX + 3, tt, CX + 5, waist), darken(pack, .1))

    # ---------- cane ----------
    if P.get('cane'):
        cc = (130, 90, 56)
        if d == 'front':
            paint(cv, R(26, tb - 5, 27, 46), cc)
            paint(cv, R(25, tb - 6, 27, tb - 5), cc)
        elif back:
            paint(cv, R(5, tb - 5, 6, 46), cc)
            paint(cv, R(5, tb - 6, 7, tb - 5), cc)
        else:
            paint(cv, R(7, tb - 5, 8, 46), cc)
            paint(cv, R(7, tb - 6, 10, tb - 5), cc)

    # ---------- hair behind head (twin / long) ----------
    if st == 'twin' and not side:
        for s in (-1, 1):
            paint(cv, E(CX + s * 11.2, hcy + 7 + dy, 4.8, 9.5), ht)
    if st == 'long' and not side:
        if back:
            paint(cv, both(E(CX + sx * .5, hcy + 9, hrx + .5, 12.5 + dy), lambda x, y: y < tt + 9 + dy), ht)
        else:
            for s in (-1, 1):
                paint(cv, both(E(CX + s * 10, hcy + 8, 3.6, 12 + dy),
                               lambda x, y: y < tt + 9 + dy), ht)

    # ---------- head ----------
    ear_y = {'short': hcy + 4, 'twin': hcy + 4, 'bun': hcy + 4,
             'long': hcy + 6, 'elder': hcy + 3}[st]
    bang_y = {'short': hcy + 2, 'twin': hcy + 2, 'bun': hcy + 2,
              'long': hcy + 2, 'elder': hcy - 1}[st]
    fhw = hrx - 2.5

    if d == 'front':
        paint(cv, inner, skin)

        def hf(x, y):
            if not outer(x, y):
                return False
            by = bang_y + (0 if abs(x + .5 - CX) < 4.5 else -1)
            if y < by:
                return True
            return abs(x + .5 - CX) > fhw and y < ear_y
        paint(cv, hf, ht)
    elif back:
        def hb(x, y):
            return inner(x, y) or (outer(x, y) and y < hcy + 5)
        paint(cv, hb, ht)
    else:
        def stop(x):
            off = -3 if st == 'elder' else 0
            return hcy - 1 + off + (x - (CX - hrx)) * 0.3

        def skin_c(x, y):
            return inner(x, y) and x + .5 < CX - 2 and y >= stop(x)
        paint(cv, skin_c, skin)

        def hs(x, y):
            if skin_c(x, y):
                return False
            if inner(x, y):
                return True
            if outer(x, y):
                return y < hcy + 3 or (x + .5 > CX and y < hcy + 6)
            return False
        paint(cv, hs, ht)

    # bun / ribbons
    if st == 'bun':
        bx = CX + (3 if side else 0)
        paint(cv, E(bx, hcy - hry - 1.2, 5, 4.2), ht)
    rib = P.get('ribbon')
    if st == 'twin' and rib:
        if side:
            paint(cv, R(CX + 5, hcy + 1, CX + 8, hcy + 3), rib)
        else:
            for s in (-1, 1):
                x0 = CX + s * 9 - (1 if s < 0 else 0)
                paint(cv, R(x0, hcy + 1, x0 + 2, hcy + 3), rib)

    # face
    ey = hcy + (4 if P['body'] == 'child' else 3)
    if d == 'front':
        for x in (CX - 4, CX + 3):
            cv.p[ey][x] = EYE
            cv.p[ey + 1][x] = EYE
        if not P.get('beard'):
            for x in (CX - 7, CX - 6, CX + 5, CX + 6):
                cv.p[ey + 2][x] = BLUSH
            cv.p[ey + 4][CX - 1] = MOUTH
        else:
            bc = P['beard']
            paint(cv, both(inner, lambda x, y: y >= ey + 3 and abs(x + .5 - CX) < 7), bc)
            paint(cv, both(inner, lambda x, y: y >= ey + 5), bc)
            cv.p[ey + 3][CX - 1] = MOUTH
            for x in (CX - 4, CX + 3):
                cv.p[ey - 1][x] = lighten(P['hair'], .0)
    elif side:
        ex = CX - 8
        cv.p[ey][ex] = EYE
        cv.p[ey + 1][ex] = EYE
        if not P.get('beard'):
            cv.p[ey + 2][ex - 1] = BLUSH
            cv.p[ey + 2][ex] = BLUSH
        else:
            bc = P['beard']
            paint(cv, both(skin_c, lambda x, y: y >= ey + 3), bc)
            cv.p[ey - 1][ex] = P['hair']

    # ---------- hat ----------
    hat = P.get('hat')
    hc = P.get('hatc')
    if hat == 'straw':
        by = hcy - hry + 5
        paint(cv, E(CX, by, hrx + 4.5, 3.2), tone(hc, CX, by))
        crown = E(CX, by - 2.5, hrx - 3, 4.6)
        paint(cv, crown, tone(lighten(hc, .08), CX, by - 3))
        paint(cv, both(crown, lambda x, y: by - 2 <= y < by), P.get('hatband', (200, 80, 70)))
    elif hat == 'cap':
        dome = both(outer, lambda x, y: y < hcy - 2)
        paint(cv, dome, tone(hc, CX, hcy - 4))
        if d == 'front':
            paint(cv, R(CX - 9, hcy - 2, CX + 9, hcy), darken(hc, .22))
        elif side:
            paint(cv, R(CX - 15, hcy - 2, CX - 3, hcy), darken(hc, .22))
        else:
            paint(cv, R(CX - 3, hcy - 2, CX + 3, hcy - 1), darken(hc, .2))

    # ---------- outline ----------
    out = [[cv.p[y][x] for x in range(W)] for y in range(H)]
    for y in range(H):
        for x in range(W):
            if cv.p[y][x] is None:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    if cv.get(x + dx, y + dy) is not None:
                        out[y][x] = OL
                        break
    img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    for y in range(H):
        for x in range(W):
            if out[y][x] is not None:
                img.putpixel((x, y), out[y][x] + (255,))
    return img


def char_sheet(P):
    sheet = Image.new('RGBA', (W * 4, H), (0, 0, 0, 0))
    front = draw(P, 'front')
    back = draw(P, 'back')
    left = draw(P, 'left')
    right = left.transpose(Image.FLIP_LEFT_RIGHT)
    for i, im in enumerate((front, back, left, right)):
        sheet.paste(im, (i * W, 0))
    return sheet


def walk_frame(P, d, f):
    if d == 'right':
        return draw(P, 'left', f).transpose(Image.FLIP_LEFT_RIGHT)
    return draw(P, d, f)


DIRS = ['front', 'back', 'left', 'right']   # rows: down, up, left, right


def walk_sheet(P):
    """cols = 4 walk frames, rows = down / up / left / right  (128x192)."""
    sheet = Image.new('RGBA', (W * 4, H * 4), (0, 0, 0, 0))
    for r, d in enumerate(DIRS):
        for f in range(4):
            sheet.paste(walk_frame(P, d, f), (f * W, r * H))
    return sheet


def walk_gif(P, path, scale=5, ms=160):
    """One GIF per character: 4 directions side by side, each walking."""
    bg = grass(W * 4, H, seed=7).convert('RGBA')
    frames = []
    for f in range(4):
        im = bg.copy()
        for k, d in enumerate(DIRS):
            for y in (45, 46):
                for x in range(k * W + 7, k * W + 25):
                    px = im.getpixel((x, y))
                    im.putpixel((x, y), (int(px[0] * .82), int(px[1] * .82), int(px[2] * .82), 255))
            im.alpha_composite(walk_frame(P, d, f), (k * W, 0))
        frames.append(im.resize((W * 4 * scale, H * scale), Image.NEAREST).convert('RGB'))
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=ms, loop=0)


SK = (244, 214, 186)
CHARS = [
    ('01_girl_twintail', dict(body='child', hstyle='twin', hair=(190, 140, 105), skin=SK,
                              top=(168, 205, 160), bottom=(168, 205, 160), kind='dress',
                              ribbon=(238, 140, 160))),
    ('02_boy_short', dict(body='child', hstyle='short', hair=(190, 140, 105), skin=SK,
                          top=(168, 205, 160), bottom=(112, 148, 206), kind='pants')),
    ('03_girl_long_black', dict(body='child', hstyle='long', hair=(74, 62, 92), skin=SK,
                                top=(222, 104, 108), bottom=(222, 104, 108), kind='dress')),
    ('04_boy_cap', dict(body='child', hstyle='short', hair=(226, 160, 84), skin=(234, 196, 160),
                        top=(244, 218, 112), bottom=(150, 112, 86), kind='pants',
                        hat='cap', hatc=(92, 136, 214), pack=(204, 92, 84))),
    ('05_woman_apron', dict(body='adult', hstyle='bun', hair=(150, 94, 72), skin=SK,
                            top=(124, 154, 212), bottom=(124, 154, 212), kind='dress',
                            apron=(250, 246, 236))),
    ('06_man_farmer', dict(body='adult', hstyle='short', hair=(228, 192, 104), skin=(228, 184, 142),
                           top=(216, 122, 88), bottom=(90, 116, 178), kind='pants', bib=(90, 116, 178),
                           hat='straw', hatc=(232, 200, 124), hatband=(200, 84, 72))),
    ('07_elder_man', dict(body='elder', hstyle='elder', hair=(236, 234, 230), skin=(238, 204, 174),
                          top=(134, 100, 162), bottom=(112, 94, 78), kind='pants',
                          beard=(244, 244, 240), cane=True, longsleeve=True)),
    ('08_elder_woman', dict(body='elder', hstyle='bun', hair=(208, 208, 214), skin=(238, 204, 174),
                            top=(196, 164, 204), bottom=(196, 164, 204), kind='dress',
                            shawl=(236, 150, 152), longsleeve=True)),
]


def grass(w, h, seed=3):
    rnd = random.Random(seed)
    im = Image.new('RGB', (w, h))
    base = [(128, 176, 56), (136, 184, 60), (120, 168, 52), (142, 190, 66)]
    for y in range(h):
        for x in range(w):
            im.putpixel((x, y), rnd.choice(base))
    for _ in range(w * h // 260):
        x, y = rnd.randrange(w - 3), rnd.randrange(h - 3)
        for dx, dy in ((0, 0), (1, 1), (2, 0)):
            im.putpixel((x + dx, y + dy), (104, 152, 44))
    return im


def preview(sheets, scale=3):
    cw, ch = W * 4 * scale, H * scale
    cols, rows = 2, (len(sheets) + 1) // 2
    bg = grass(cw * cols // scale, ch * rows // scale).convert('RGBA')
    for i, sh in enumerate(sheets):
        ox, oy = (i % cols) * (W * 4), (i // cols) * H
        shadow = Image.new('RGBA', bg.size, (0, 0, 0, 0))
        for k in range(4):
            for y in (45, 46):
                for x in range(k * W + 7, k * W + 25):
                    if sh.getpixel((x, 44))[3] == 0 or True:
                        px = bg.getpixel((ox + x, oy + y))
                        bg.putpixel((ox + x, oy + y), (int(px[0] * .82), int(px[1] * .82), int(px[2] * .82), 255))
        bg.alpha_composite(sh, (ox, oy))
    return bg.resize((bg.width * scale, bg.height * scale), Image.NEAREST)


if __name__ == '__main__':
    os.makedirs('npc_sprites', exist_ok=True)
    sheets = []
    for name, P in CHARS:
        sh = char_sheet(P)
        sh.save(f'npc_sprites/{name}.png')
        sheets.append(sh)
    preview(sheets, 3).save('npc_sprites/preview_sheet.png')
    os.makedirs('npc_walk', exist_ok=True)
    for name, P in CHARS:
        walk_sheet(P).save(f'npc_walk/{name}_walk.png')
        walk_gif(P, f'npc_walk/{name}_walk.gif')
    print('done')
