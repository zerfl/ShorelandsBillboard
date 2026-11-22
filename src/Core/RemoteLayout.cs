namespace ShorelandsBillboard.Core
{
    // The remote's geometry in UI pixels of the game's 640x360 canvas, from the top-left of the body art.
    // The numbers mirror art/make_remote.py; the tests compare them with the PNGs.
    public static class RemoteLayout
    {
        public struct Box
        {
            public int X,
                Y,
                W,
                H;

            public Box(int x, int y, int w, int h)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
            }

            public int Right => X + W;
            public int Bottom => Y + H;

            public bool Overlaps(Box o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

            public Box Inset(int d) => new Box(X + d, Y + d, W - 2 * d, H - 2 * d);
        }

        public const int W = 112,
            H = 200;
        public const int Left = 6,
            Right = W - Left; // every control lies between these
        public const int ButtonH = 14,
            RockerW = 26;
        public const int TextH = 10; // a line of the game's font at size 9

        public static readonly Box Power = new Box(Left, 7, 18, ButtonH);
        public static readonly Box Close = new Box(Right - 11, 8, 11, 11);
        public static readonly Box Lcd = new Box(Left, 24, Right - Left, 28); // outline, lip, then the glass
        public const int LcdEdge = 2,
            LcdPad = 2;
        public static Box LcdGlass => Lcd.Inset(LcdEdge);

        public static Box LcdLine(int n) =>
            new Box(
                Lcd.X + LcdEdge + LcdPad,
                Lcd.Y + LcdEdge + LcdPad + n * TextH,
                Lcd.W - 2 * (LcdEdge + LcdPad),
                TextH
            );

        public static readonly Box VolUp = new Box(Left, 57, RockerW, ButtonH);
        public static readonly Box VolDown = new Box(Left, 83, RockerW, ButtonH);
        public static readonly Box ChUp = new Box(Right - RockerW, 57, RockerW, ButtonH);
        public static readonly Box ChDown = new Box(Right - RockerW, 83, RockerW, ButtonH);
        public static readonly Box Mute = new Box((W - RockerW) / 2, 70, RockerW, ButtonH);
        public const int CaptionY = 72; // "VOL" and "CH" between the rocker halves

        // the channel list: a well with rows that scroll, a thin scroll mark on the right
        public const int Rows = 4,
            RowH = 14,
            RowGap = 2,
            RowW = 93,
            ScrollW = 2;
        public static readonly Box List = new Box(Left, 102, Right - Left, Rows * RowH + (Rows - 1) * RowGap + 4);
        public static Box ListView => List.Inset(2); // outline and one pixel of margin

        public static int ContentHeight(int rows) => rows <= 0 ? 0 : rows * RowH + (rows - 1) * RowGap;

        public static readonly Box Scroll = new Box(List.X + 2 + RowW + 1, List.Y + 2, ScrollW, List.H - 4);

        // in a row
        public const int DotX = 2,
            DotY = 3,
            NameX = 11,
            SmallButton = 10;
        public const int NameY = 2;

        // a list that fits has no scroll mark, and its rows take the whole width
        public static int RowWidth(int rows) => ContentHeight(rows) > ListView.H ? RowW : ListView.W;

        public static Box RowRemove(int rowWidth) => new Box(rowWidth - 2 - SmallButton, 2, SmallButton, SmallButton);

        public static Box RowTop(int rowWidth) =>
            new Box(RowRemove(rowWidth).X - 1 - SmallButton, 2, SmallButton, SmallButton);

        public static readonly Box Add = new Box(Right - 16, 172, 16, ButtonH);
        public static readonly Box Field = new Box(Left, 172, Add.X - 3 - Left, ButtonH);

        // remote_icons.png: 8x8 cells in this order
        public const int Icon = 8;

        public enum Icons
        {
            Power,
            Plus,
            Minus,
            Up,
            Down,
            Sound,
            Muted,
            Top,
            Cross,
            Live,
            NotLive,
        }

        public const int IconCount = 11;

        // remote_hud.png: the picture on the button next to the game's menu button
        public const int HudW = 12,
            HudH = 10,
            HudGap = 3;

        // How far the list is scrolled so that a row is in view; rows and offsets in whole pixels.
        public static int ScrollTo(int offset, int row, int rows)
        {
            int top = row * (RowH + RowGap),
                view = ListView.H;
            if (top < offset)
                offset = top;
            if (top + RowH > offset + view)
                offset = top + RowH - view;
            return ClampScroll(offset, rows);
        }

        public static int ClampScroll(int offset, int rows) =>
            System.Math.Max(0, System.Math.Min(offset, ContentHeight(rows) - ListView.H));

        // The scroll mark: its top and height inside Scroll, or height 0 when everything fits.
        public static void ScrollMark(int offset, int rows, out int y, out int h)
        {
            int content = ContentHeight(rows),
                view = ListView.H;
            if (content <= view)
            {
                y = 0;
                h = 0;
                return;
            }
            h = System.Math.Max(6, view * view / content);
            y = (int)System.Math.Round((double)offset / (content - view) * (view - h));
        }
    }
}
