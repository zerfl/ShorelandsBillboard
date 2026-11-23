using System;

namespace ShorelandsBillboard.Core
{
    // Billboard geometry in art pixels (32 per world unit). The numbers mirror art/make_billboard.py; the
    // tests compare them with the PNGs. Origin: the middle of the ground line the object sorts by, SortLift
    // above the art's lower edge (the game's benches and plant boxes sit 7 px below their sort line too).
    public static class Layout
    {
        public const int Ppu = 32;
        public const int FrameW = 210,
            FrameH = 155;
        public const int ScreenW = 184,
            ScreenH = 104; // the picture; 16:9 within half a pixel
        public const int PictureX = 13,
            PictureTop = 24; // in the frame image, from its top-left
        public const int Overlap = 2; // bezel pixels over each picture edge
        public const int SortLift = 7;
        public const int PlateTop = 138,
            PlateH = 11,
            PlatePad = 3,
            GlyphW = 6,
            GlyphH = 7,
            MaxName = 25;
        public const string Glyphs = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
        public const int ShadowW = FrameW + 8,
            ShadowH = 14;
        public const int LanternX = 6,
            LanternTop = 33; // in the frame image; mirrored on the right

        // Default place: the hedge line north of the town pond, east of the sign at x -63..-62.
        public const float DefaultX = -58.5f,
            DefaultY = 6.21875f;

        // The hedge the base stands in, as it stands in game 0.69g: seven bushes 1 unit apart on one line. The
        // billboard is only built where every one of them is found.
        public const float HedgeFirstX = -63.5f,
            HedgeY = 6.5f;
        public const int HedgeBushes = 7;

        public static bool IsHedge(System.Collections.Generic.IEnumerable<(float x, float y)> bushCentres)
        {
            int found = 0;
            foreach (var (x, y) in bushCentres)
            {
                float along = x - HedgeFirstX;
                int n = (int)Math.Round(along);
                if (n < 0 || n >= HedgeBushes || Math.Abs(along - n) > 0.1f || Math.Abs(y - HedgeY) > 0.1f)
                    continue;
                found |= 1 << n;
            }
            return found == (1 << HedgeBushes) - 1;
        }

        // Measured in game: the player's body box reaches 0.25 above its position, its sprite 0.92.
        public const float PlayerBodyTop = 0.25f,
            PlayerSpriteTop = 0.92f;

        public struct Rect
        {
            public float X,
                Y,
                W,
                H;
            public float Right => X + W;
            public float Top => Y + H;

            public bool Contains(float x, float y) => x > X && x < Right && y > Y && y < Top;

            public bool Overlaps(Rect o) => X < o.Right && o.X < Right && Y < o.Top && o.Y < Top;

            public Rect At(float x, float y) =>
                new Rect
                {
                    X = X + x,
                    Y = Y + y,
                    W = W,
                    H = H,
                };
        }

        static Rect Px(int x, int y, int w, int h) =>
            new Rect
            {
                X = x / (float)Ppu,
                Y = y / (float)Ppu,
                W = w / (float)Ppu,
                H = h / (float)Ppu,
            };

        // lower-left corners relative to the origin
        public static Rect Frame => Px(-FrameW / 2, -SortLift, FrameW, FrameH);
        public static Rect Screen =>
            Px(-FrameW / 2 + PictureX, -SortLift + FrameH - PictureTop - ScreenH, ScreenW, ScreenH);
        public static Rect Opening =>
            Px(
                -FrameW / 2 + PictureX + Overlap,
                -SortLift + FrameH - PictureTop - ScreenH + Overlap,
                ScreenW - 2 * Overlap,
                ScreenH - 2 * Overlap
            );
        public static Rect Shadow => Px(-ShadowW / 2, -SortLift - ShadowH / 2 + 2, ShadowW, ShadowH);

        // The base blocks like the hedge it replaces: a strip as deep as the game's bush and bench colliders,
        // starting 1 px above the sort line. One pixel wider on the left so nobody slips between it and the
        // next bush.
        public static Rect Collider => Px(-FrameW / 2 - 1, 1, FrameW + 1, 10);

        // Where the game's lit lantern sprite goes: over the unlit bulbs drawn on the posts.
        public static (float x, float y) Lantern(bool right) =>
            (
                (right ? FrameW / 2 - LanternX : -FrameW / 2 + LanternX) / (float)Ppu,
                (-SortLift + FrameH - LanternTop) / (float)Ppu
            );

        public static int PlateWidth(int letters) =>
            GlyphW * Math.Min(Math.Max(letters, 1), MaxName) - 1 + 2 * PlatePad;

        public static Rect Plate(int letters)
        {
            int w = PlateWidth(letters);
            return Px(-(w / 2), -SortLift + FrameH - PlateTop - PlateH, w, PlateH);
        }

        // The name as the plate shows it: capitals, what the font lacks dropped, cut to the plate's length.
        public static string PlateText(string channel)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (channel ?? "").ToUpperInvariant())
                if (Glyphs.IndexOf(c) >= 0 && sb.Length < MaxName)
                    sb.Append(c);
            return sb.ToString();
        }

        // Scenery the base stands on: a decoration centred in the strip from the art's lower edge to the top
        // of the name plate's board (the hedge bushes are 1 unit tall and sit on the same line).
        public static bool HidesDecor(float originX, float originY, float centreX, float centreY)
        {
            var f = Frame.At(originX, originY);
            return centreX > f.X && centreX < f.Right && centreY > f.Y && centreY < Screen.At(originX, originY).Y;
        }

        // The object's origin snapped to the art pixel grid, so every part lands on whole pixels.
        public static float Snap(float v) => (float)Math.Round(v * Ppu) / Ppu;

        // YPositionSorter.OrderForY: lower on screen draws in front
        public static int OrderForY(float y) => (int)Math.Round(-y * 100f);

        // Window pixels per art pixel for a camera showing `orthographicSize` world units above and below centre.
        public static double PixelScale(int windowHeight, float orthographicSize) =>
            orthographicSize > 0 ? windowHeight / (2.0 * orthographicSize * Ppu) : 1.0;

        // The picture's size in window pixels.
        public static void DisplaySize(double pixelScale, out int w, out int h)
        {
            double scale = Math.Max(1.0, pixelScale);
            w = (int)Math.Round(ScreenW * scale);
            h = (int)Math.Round(ScreenH * scale);
        }
    }

    public static class Sound
    {
        // Full volume up to `near` from the screen, silent from `far` on, squared fade between.
        public static float Falloff(float distance, float near, float far)
        {
            if (distance <= near)
                return 1f;
            if (distance >= far || far <= near)
                return 0f;
            float t = (far - distance) / (far - near);
            return t * t;
        }

        // Stereo position of the screen relative to the listener, at most 0.6 to either side.
        public static float Pan(float dx, float far) => far <= 0 ? 0 : Math.Max(-0.6f, Math.Min(0.6f, dx / far * 1.2f));

        // Channel gains: the pan takes from the far side only.
        public static void Gains(float volume, float pan, out float left, out float right)
        {
            left = volume * (pan > 0 ? 1 - pan : 1);
            right = volume * (pan < 0 ? 1 + pan : 1);
        }
    }

    // Interleaved stereo samples from the decoder thread to the audio thread. Positions count from the
    // stream start, so the picture can follow the sound. A full buffer drops the oldest.
    public sealed class SampleRing
    {
        readonly float[] buf;
        long written,
            read;
        readonly object gate = new object();

        public SampleRing(int capacity)
        {
            buf = new float[capacity & ~1];
        }

        public long Written
        {
            get
            {
                lock (gate)
                    return written;
            }
        }
        public long ReadPosition
        {
            get
            {
                lock (gate)
                    return read;
            }
        }

        public void Write(float[] src, int n)
        {
            lock (gate)
            {
                int skip = Math.Max(0, n - buf.Length);
                written += skip;
                for (int left = n - skip, from = skip; left > 0; )
                {
                    int at = (int)(written % buf.Length),
                        run = Math.Min(left, buf.Length - at);
                    Array.Copy(src, from, buf, at, run);
                    written += run;
                    from += run;
                    left -= run;
                }
                if (written - read > buf.Length)
                    read = written - buf.Length;
            }
        }

        // Fills dst; what the buffer cannot supply is silence.
        public int Read(float[] dst)
        {
            int n = Read(dst, 0, dst.Length);
            Array.Clear(dst, n, dst.Length - n);
            return n;
        }

        public int Read(float[] dst, int offset, int count)
        {
            lock (gate)
            {
                int n = (int)Math.Min(count, written - read);
                for (int done = 0; done < n; )
                {
                    int at = (int)(read % buf.Length),
                        run = Math.Min(n - done, buf.Length - at);
                    Array.Copy(buf, at, dst, offset + done, run);
                    read += run;
                    done += run;
                }
                return n;
            }
        }

        public void Skip(long n)
        {
            lock (gate)
                read = n >= written - read ? written : read + n;
        }
    }

    // Where in the stream the sound is, for the picture to follow. The engine takes sound a block at a time,
    // so what it has taken runs ahead of the device clock by up to a block, and by more whenever it takes
    // several at once. The lowest lead within the last seconds is taken as the playing position.
    public sealed class AudioClock
    {
        public const double Window = 1.5;
        readonly double[] at = new double[512],
            lead = new double[512];
        int next,
            count;

        public void Reset()
        {
            next = count = 0;
        }

        public double Position(double readSeconds, double clockSeconds)
        {
            at[next] = clockSeconds;
            lead[next] = readSeconds - clockSeconds;
            next = (next + 1) % at.Length;
            if (count < at.Length)
                count++;
            double min = double.MaxValue;
            for (int i = 0; i < count; i++)
                if (clockSeconds - at[i] <= Window && lead[i] < min)
                    min = lead[i];
            return clockSeconds + min;
        }
    }
}
