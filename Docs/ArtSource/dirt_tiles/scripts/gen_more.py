import numpy as np
from PIL import Image
import gen_dirt_tiles as g

OUT = "dirt_tiles"
base = [Image.open(f"{OUT}/tile_dirt_{i:02d}.png").convert("RGB") for i in range(1, 5)]
tiles = [np.array(b) for b in base]
rng = np.random.default_rng(2026)
for i in range(5, 17):
    seed = 100 + i * 17
    n = int(rng.integers(15, 20))  # 砂利は多め
    t = g.make_tile(seed, n)
    Image.fromarray(t, "RGB").save(f"{OUT}/tile_dirt_{i:02d}.png")
    tiles.append(t)

# 全16種ランダム敷き詰めプレビュー(3倍)
r = np.random.default_rng(5)
cols, rows, s = 8, 6, 3
sheet = np.zeros((rows * 32, cols * 32, 3), np.uint8)
for y in range(rows):
    for x in range(cols):
        sheet[y*32:(y+1)*32, x*32:(x+1)*32] = tiles[r.integers(len(tiles))]
Image.fromarray(sheet).resize((cols*32*s, rows*32*s), Image.NEAREST).save(f"{OUT}/preview_mixed_all16.png")

# 一覧(4x4, 4倍)
sh = np.zeros((128, 128, 3), np.uint8)
for k, t in enumerate(tiles):
    sh[(k//4)*32:(k//4+1)*32, (k%4)*32:(k%4+1)*32] = t
Image.fromarray(sh).resize((512, 512), Image.NEAREST).save(f"{OUT}/contact_sheet_16.png")
# 重複チェック
print(len({t.tobytes() for t in tiles}), "unique of", len(tiles))
