"""Draws the billboard's sprites in the game's pixel-art style (32 px per unit, its wood, cloth and stone
colours, 1 px black outlines, the stalls' posts, trims and awning) and writes them next to this script:

    frame.png   the whole structure, the screen area painted as a switched-off screen
    bezel.png   same canvas, only the ring that lies over the picture's edge
    shadow.png  ground shadow mask (white, like the trees' shadow sprites)
    plate.png   name plate at its largest width; the plugin cuts it to the name's length
    font.png    5x7 capitals, digits and underscore for the name plate, 6 px per glyph

    python art/make_billboard.py [--check]      (needs Pillow)

--check fails if the committed PNGs differ from what the script draws. The numbers below are mirrored in
src/Core/Layout.cs; the tests compare the PNGs against Layout.
"""
import os
import sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))

# the game's colours (koi_stall, shop_cart_80x96, sound_statue)
K = (0, 0, 0, 255)
LIGHT, TRIM, WOOD, SHADE, EDGE, DARK = [(*bytes.fromhex(h), 255) for h in
                                        ('dbb785', 'b88d61', '9d714b', '82583f', '8a5338', '75442f')]
RED, RED_DARK = (0xa6, 0x20, 0x20, 255), (0x7f, 0x15, 0x1c, 255)
STONE_LIGHT, STONE, STONE_DARK = (0xac, 0xaa, 0xaa, 255), (0x76, 0x74, 0x74, 255), (0x4f, 0x4b, 0x4b, 255)
SLATE = (0x26, 0x28, 0x37, 255)
GLASS, GLASS_LIGHT, CAP = (0xa4, 0xaf, 0xb6, 255), (0xce, 0xd5, 0xda, 255), (0x7a, 0x87, 0x93, 255)
WHITE = (255, 255, 255, 255)
DARKEST = (0x5d, 0x33, 0x25, 255)
SCREEN_OFF = (9, 10, 18, 255)
CLEAR = (0, 0, 0, 0)

W = 210
PICTURE_W, PICTURE_H = 184, 104
OVERLAP = 2                       # bezel pixels lying over each picture edge
POST_W, POST_X = 8, 2
BALL, LINTEL, VALANCE, TRIM_H, SKIRT, FEET = 7, 7, 9, 7, 16, 4
PICTURE_X = (W - PICTURE_W) // 2
PICTURE_Y = BALL + LINTEL + VALANCE + 1                  # rows from the top
H = PICTURE_Y + PICTURE_H + TRIM_H + SKIRT + FEET       # the trim's outline closes the bezel
PLATE_H = 11                      # name plate: 7 px letters, 1 px margin, outline
PLATE_PAD = 3                     # outline and margin left and right of the letters
MAX_NAME = 25                     # Twitch login length
SHADOW_W, SHADOW_H = W + 8, 14
LANTERN_X, LANTERN_Y = 6, 33      # where the game's lit lantern sprite (lamp2, 16 px, centred) is put; mirrored on the right

FONT = {
    'A': '01110 10001 10001 11111 10001 10001 10001', 'B': '11110 10001 10001 11110 10001 10001 11110',
    'C': '01110 10001 10000 10000 10000 10001 01110', 'D': '11110 10001 10001 10001 10001 10001 11110',
    'E': '11111 10000 10000 11110 10000 10000 11111', 'F': '11111 10000 10000 11110 10000 10000 10000',
    'G': '01110 10001 10000 10111 10001 10001 01111', 'H': '10001 10001 10001 11111 10001 10001 10001',
    'I': '01110 00100 00100 00100 00100 00100 01110', 'J': '00111 00010 00010 00010 00010 10010 01100',
    'K': '10001 10010 10100 11000 10100 10010 10001', 'L': '10000 10000 10000 10000 10000 10000 11111',
    'M': '10001 11011 10101 10101 10001 10001 10001', 'N': '10001 11001 10101 10011 10001 10001 10001',
    'O': '01110 10001 10001 10001 10001 10001 01110', 'P': '11110 10001 10001 11110 10000 10000 10000',
    'Q': '01110 10001 10001 10001 10101 10010 01101', 'R': '11110 10001 10001 11110 10100 10010 10001',
    'S': '01111 10000 10000 01110 00001 00001 11110', 'T': '11111 00100 00100 00100 00100 00100 00100',
    'U': '10001 10001 10001 10001 10001 10001 01110', 'V': '10001 10001 10001 10001 10001 01010 00100',
    'W': '10001 10001 10001 10101 10101 11011 10001', 'X': '10001 10001 01010 00100 01010 10001 10001',
    'Y': '10001 10001 01010 00100 00100 00100 00100', 'Z': '11111 00001 00010 00100 01000 10000 11111',
    '0': '01110 10001 10011 10101 11001 10001 01110', '1': '00100 01100 00100 00100 00100 00100 01110',
    '2': '01110 10001 00001 00110 01000 10000 11111', '3': '11110 00001 00001 01110 00001 00001 11110',
    '4': '00010 00110 01010 10010 11111 00010 00010', '5': '11111 10000 11110 00001 00001 10001 01110',
    '6': '00110 01000 10000 11110 10001 10001 01110', '7': '11111 00001 00010 00100 01000 01000 01000',
    '8': '01110 10001 10001 01110 10001 10001 01110', '9': '01110 10001 10001 01111 00001 00010 01100',
    '_': '00000 00000 00000 00000 00000 00000 11111',
}
GLYPHS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_'


class Canvas:
    def __init__(self, w, h):
        self.im = Image.new('RGBA', (w, h), CLEAR)
        self.px = self.im.load()

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1):
            for x in range(x0, x1):
                self.px[x, y] = c

    def box(self, x0, y0, x1, y1, fill):
        self.rect(x0, y0, x1, y1, K)
        self.rect(x0 + 1, y0 + 1, x1 - 1, y1 - 1, fill)

    def rows(self, x, y, rows, key):
        for j, row in enumerate(rows):
            for i, ch in enumerate(row):
                if ch != '.':
                    self.px[x + i, y + j] = key[ch]


def post(c, x, top, bottom):
    """Stall post: highlight on the left, shade on the right, a stone shoe on the ground."""
    c.box(x, top, x + POST_W, bottom, WOOD)
    c.rect(x + 1, top + 1, x + 2, bottom - 1, LIGHT)
    c.rect(x + POST_W - 2, top + 1, x + POST_W - 1, bottom - 1, SHADE)
    c.rows(x - 1, bottom - FEET, ['0lmmmmmmd0', '0mmmmmmmd0', '0dddddddd0', '.00000000.'],
           {'0': K, 'l': STONE_LIGHT, 'm': STONE, 'd': STONE_DARK})


def ball(c, x, y):
    """The stalls' post finial, 10 wide."""
    c.rows(x, y, ['...0000...', '..011110..', '.01111110.', '0111111110', '0211111120', '0222222220', '.02222220.'],
           {'0': K, '1': LIGHT, '2': WOOD})


def lantern(c, x, y):
    """The tree house's unlit bulb; the game's lamp2 sprite centred on (x, y) glows over it at night."""
    c.rows(x - 3, y - 8, ['..00..', '.0000.', '.0cc0.', '.0000.', '.0gg0.', '.0gg0.', '0gwlg0', '0gllg0', '0gggg0', '0gggg0',
                          '.0000.'], {'0': K, 'c': CAP, 'g': GLASS, 'l': GLASS_LIGHT, 'w': WHITE})


def lintel(c, y):
    c.box(0, y, W, y + LINTEL, WOOD)
    c.rect(1, y + 1, W - 1, y + 2, TRIM)
    c.rect(1, y + LINTEL - 2, W - 1, y + LINTEL - 1, SHADE)
    for x in (1, W - 2):                                   # end grain
        c.rect(x, y + 1, x + 1, y + LINTEL - 1, LIGHT)


def valance(c, x0, x1, y):
    """The shop cart's awning as a strip: 3 flat rows, then scallops 16 px wide."""
    depth = [2, 3, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 4, 4, 3, 2]
    n = len(depth)
    start = x0 + ((x1 - x0) % n) // 2
    c.rect(x0, y, x1, y + 3, RED)
    c.rect(x0, y, x1, y + 1, RED_DARK)                     # in the lintel's shade
    for x in range(x0, x1):
        d = depth[(x - start) % n] if start <= x < start + (x1 - x0) // n * n else 2
        c.rect(x, y + 3, x + 1, y + 3 + d, RED)
        c.px[x, y + 2 + d] = RED_DARK
        c.px[x, y + 3 + d] = K


def trim(c, y):
    """The stalls' counter edge: plank ends over a lighter strip, 6 px per plank."""
    c.rect(0, y, W, y + TRIM_H, K)
    for x in range(1, W - 1):
        k = (x - 1) % 6
        c.px[x, y + 1] = DARK if k == 5 else WOOD
        c.px[x, y + 2] = DARK if k == 5 else LIGHT if k == 0 else TRIM
        c.px[x, y + 3] = DARK if k == 5 else TRIM if k == 0 else WOOD
        c.px[x, y + 4] = c.px[x, y + 3]
        c.px[x, y + 5] = K if k == 5 else TRIM


def skirt(c, x0, x1, y):
    c.box(x0, y - 1, x1, y + SKIRT, WOOD)
    c.rect(x0 + 1, y, x1 - 1, y + 2, SHADE)                # under the trim
    for x in range(x0 + 12, x1 - 6, 12):                   # board joints
        c.rect(x, y + 2, x + 1, y + SKIRT - 1, EDGE)


PLATE_Y = H - FEET - SKIRT + 2 + (SKIRT - 3 - PLATE_H) // 2   # the plate's top row in the frame


def plate():
    w = 6 * MAX_NAME - 1 + 2 * PLATE_PAD
    c = Canvas(w, PLATE_H)
    c.box(0, 0, w, PLATE_H, DARK)
    c.rect(1, 1, w - 1, 2, DARKEST)
    return c.im


def bezel_ring(c):
    """Black line around the picture, a slate lip over its edge, chamfered inner corners."""
    x0, y0, x1, y1 = PICTURE_X, PICTURE_Y, PICTURE_X + PICTURE_W, PICTURE_Y + PICTURE_H
    c.rect(x0 - 1, y0 - 1, x1 + 1, y0 + OVERLAP, SLATE)
    c.rect(x0 - 1, y1 - OVERLAP, x1 + 1, y1 + 1, SLATE)
    c.rect(x0 - 1, y0 - 1, x0 + OVERLAP, y1 + 1, SLATE)
    c.rect(x1 - OVERLAP, y0 - 1, x1 + 1, y1 + 1, SLATE)
    for x in range(x0 - 1, x1 + 1):
        c.px[x, y0 - 1] = c.px[x, y1] = K
    for y in range(y0 - 1, y1 + 1):
        c.px[x0 - 1, y] = c.px[x1, y] = K
    for dx, dy in ((0, 0), (1, 0), (0, 1)):
        for cx, sx in ((x0 + OVERLAP, 1), (x1 - OVERLAP - 1, -1)):
            for cy, sy in ((y0 + OVERLAP, 1), (y1 - OVERLAP - 1, -1)):
                c.px[cx + sx * dx, cy + sy * dy] = SLATE


def frame():
    c = Canvas(W, H)
    left, right = POST_X, W - POST_X - POST_W
    top = BALL + LINTEL
    inner0, inner1 = left + POST_W - 1, right + 1          # the posts' outlines double as the panel's
    # panel behind the picture: wood stiles, the switched-off screen
    c.box(inner0, top - 1, inner1, H - FEET - SKIRT - TRIM_H + 1, WOOD)
    c.rect(inner0 + 1, top, inner0 + 2, H - FEET - SKIRT - TRIM_H, SHADE)
    c.rect(PICTURE_X, PICTURE_Y, PICTURE_X + PICTURE_W, PICTURE_Y + PICTURE_H, SCREEN_OFF)
    skirt(c, inner0, inner1, H - FEET - SKIRT)
    for x in (left, right):
        post(c, x, top - 1, H)
    valance(c, inner0 + 1, inner1 - 1, top)
    lintel(c, BALL)
    trim(c, H - FEET - SKIRT - TRIM_H)
    for x in (left, right):
        ball(c, x - 1, 0)
    lantern(c, LANTERN_X, LANTERN_Y)
    lantern(c, W - LANTERN_X, LANTERN_Y)
    bezel_ring(c)
    return c.im


def bezel():
    c = Canvas(W, H)
    bezel_ring(c)
    return c.im


def shadow():
    """A flat ellipse under the base, soft-stepped like the tree shadows' outline."""
    c = Canvas(SHADOW_W, SHADOW_H)
    for y in range(SHADOW_H):
        t = abs((y + 0.5) / SHADOW_H * 2 - 1)
        inset = round(10 * t * t * t)
        c.rect(inset, y, SHADOW_W - inset, y + 1, (255, 255, 255, 255))
    return c.im


def font():
    c = Canvas(6 * len(GLYPHS), 7)
    for i, ch in enumerate(GLYPHS):
        c.rows(6 * i, 0, FONT[ch].split(), {'1': LIGHT, '0': CLEAR})
    return c.im


def main():
    images = {'frame.png': frame(), 'bezel.png': bezel(), 'shadow.png': shadow(), 'plate.png': plate(),
              'font.png': font()}
    stale = []
    for name, im in images.items():
        path = os.path.join(HERE, name)
        if '--check' in sys.argv:
            if not os.path.exists(path) or Image.open(path).convert('RGBA').tobytes() != im.tobytes():
                stale.append(name)
        else:
            im.save(path)
    if stale:
        sys.exit('out of date: ' + ', '.join(stale))
    print(f'frame {W}x{H}, picture at ({PICTURE_X}, {PICTURE_Y}) {PICTURE_W}x{PICTURE_H}, plate row {PLATE_Y}')


if __name__ == '__main__':
    main()
