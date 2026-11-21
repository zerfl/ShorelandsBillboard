"""Draws the remote control's sprites in the style of the game's windows (their browns, 1 px black
outlines, flat fills) and writes them next to this script:

    remote.png        the body: the display, the wells of the channel list and of the name field
    remote_icons.png  8x8 pictures for the buttons, side by side
    remote_hud.png    the picture on the button next to the game's menu button

    python art/make_remote.py [--check]      (needs Pillow)

--check fails if the committed PNGs differ from what the script draws. The numbers below are mirrored in
src/Core/RemoteLayout.cs; the tests compare the PNGs against it. The buttons themselves are the game's own
button sprite, taken at run time.
"""
import os
import sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))

K = (0, 0, 0, 255)
CLEAR = (0, 0, 0, 0)
WHITE = (255, 255, 255, 255)
# the game's dark window panel: fill, lit edge, shaded edge
BODY, BODY_LIGHT, BODY_DARK = (93, 51, 37, 255), (117, 68, 47, 255), (72, 38, 29, 255)
WELL = (52, 28, 22, 255)
# the billboard's switched-off screen and its slate lip
GLASS, LIP = (9, 10, 18, 255), (0x26, 0x28, 0x37, 255)
LENS, LENS_LIGHT = (0x4a, 0x12, 0x18, 255), (0xa6, 0x20, 0x20, 255)
LIVE, LIVE_DARK, LIVE_LIGHT = (0xe0, 0x3c, 0x32, 255), (0x7f, 0x15, 0x1c, 255), (0xff, 0x9a, 0x8a, 255)

W, H = 112, 200
LEFT, RIGHT = 6, W - 6
LCD = (LEFT, 24, RIGHT - LEFT, 28)
ROWS, ROW_H, ROW_GAP = 4, 14, 2
LIST = (LEFT, 102, RIGHT - LEFT, ROWS * ROW_H + (ROWS - 1) * ROW_GAP + 4)
ADD_W = 16
FIELD = (LEFT, 172, RIGHT - ADD_W - 3 - LEFT, 14)
CORNER = [4, 2, 1, 1]             # pixels cut from each of the body's first rows
ICON = 8

ICONS = [
    ('power',    ['...##...', '.#.##.#.', '#..##..#', '#..##..#', '#......#', '#......#', '.#....#.', '..####..']),
    ('plus',     ['........', '...##...', '...##...', '.######.', '.######.', '...##...', '...##...', '........']),
    ('minus',    ['........', '........', '........', '.######.', '.######.', '........', '........', '........']),
    ('up',       ['........', '........', '...##...', '..####..', '.######.', '########', '........', '........']),
    ('down',     ['........', '........', '########', '.######.', '..####..', '...##...', '........', '........']),
    ('sound',    ['...#....', '..##..#.', '####...#', '####.#.#', '####...#', '..##..#.', '...#....', '........']),
    ('muted',    ['...#....', '..##....', '####.#.#', '####..#.', '####.#.#', '..##....', '...#....', '........']),
    ('top',      ['........', '.######.', '........', '...##...', '..####..', '.######.', '...##...', '........']),
    ('cross',    ['........', '.#....#.', '..#..#..', '...##...', '...##...', '..#..#..', '.#....#.', '........']),
    ('live',     ['........', '..dddd..', '.drrrrd.', '.drlrrd.', '.drrrrd.', '.drrrrd.', '..dddd..', '........']),
    ('not_live', ['........', '..kkkk..', '.kwwwwk.', '.kwwwwk.', '.kwwwwk.', '.kwwwwk.', '..kkkk..', '........']),
]
KEY = {'#': WHITE, 'r': LIVE, 'd': LIVE_DARK, 'l': LIVE_LIGHT, 'k': BODY_DARK, 'w': WELL}

HUD = ['..#......#..', '...#....#...', '....#..#....', '############', '#........#.#', '#........###', '#........#.#',
       '#........###', '#........#.#', '############']


def rect(px, x0, y0, x1, y1, c):
    for y in range(y0, y1):
        for x in range(x0, x1):
            px[x, y] = c


def well(px, box, fill):
    x, y, w, h = box
    rect(px, x, y, x + w, y + h, K)
    rect(px, x + 1, y + 1, x + w - 1, y + h - 1, fill)


def body():
    im = Image.new('RGBA', (W, H), CLEAR)
    px = im.load()
    for y in range(H):
        cut = CORNER[min(y, H - 1 - y)] if min(y, H - 1 - y) < len(CORNER) else 0
        for x in range(cut, W - cut):
            px[x, y] = BODY
    # lit from the top left, as the game's panels are; then the outline
    for y in range(H):
        xs = [x for x in range(W) if px[x, y][3]]
        px[xs[0] + 1, y] = BODY_LIGHT
        px[xs[-1] - 1, y] = BODY_DARK
    for x in range(W):
        ys = [y for y in range(H) if px[x, y][3]]
        px[x, ys[0] + 1] = BODY_LIGHT
        px[x, ys[-1] - 1] = BODY_DARK
    for y in range(H):
        for x in range(W):
            if px[x, y][3] and any(not (0 <= i < W and 0 <= j < H) or not px[i, j][3]
                                   for i, j in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))):
                px[x, y] = K
    # the lens at the top end
    rect(px, W // 2 - 8, 0, W // 2 + 8, 4, K)
    rect(px, W // 2 - 7, 1, W // 2 + 7, 3, LENS)
    rect(px, W // 2 - 6, 1, W // 2 - 2, 2, LENS_LIGHT)
    # the display: outline, slate lip, glass
    well(px, LCD, LIP)
    rect(px, LCD[0] + 2, LCD[1] + 2, LCD[0] + LCD[2] - 2, LCD[1] + LCD[3] - 2, GLASS)
    well(px, LIST, WELL)
    well(px, FIELD, WELL)
    # grip grooves at the lower end
    for y in (189, 192, 195):
        rect(px, W // 2 - 12, y, W // 2 + 12, y + 1, BODY_DARK)
        rect(px, W // 2 - 12, y + 1, W // 2 + 12, y + 2, BODY_LIGHT)
    return im


def icons():
    im = Image.new('RGBA', (ICON * len(ICONS), ICON), CLEAR)
    px = im.load()
    for n, (_, rows) in enumerate(ICONS):
        for j, row in enumerate(rows):
            for i, ch in enumerate(row):
                if ch != '.':
                    px[n * ICON + i, j] = KEY[ch]
    return im


def hud():
    im = Image.new('RGBA', (len(HUD[0]), len(HUD)), CLEAR)
    px = im.load()
    for j, row in enumerate(HUD):
        for i, ch in enumerate(row):
            if ch == '#':
                px[i, j] = WHITE
    return im


def main():
    images = {'remote.png': body(), 'remote_icons.png': icons(), 'remote_hud.png': hud()}
    if '--check' in sys.argv:
        stale = [n for n, im in images.items()
                 if not os.path.exists(os.path.join(HERE, n)) or Image.open(os.path.join(HERE, n)).convert('RGBA').tobytes() != im.tobytes()]
        if stale:
            sys.exit('stale art (run art/make_remote.py): ' + ', '.join(stale))
    else:
        for n, im in images.items():
            im.save(os.path.join(HERE, n))
    print(f'remote {W}x{H}, display at {LCD[:2]}, list at {LIST[:2]} with {ROWS} rows, {len(ICONS)} icons')


if __name__ == '__main__':
    main()
