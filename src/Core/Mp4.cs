using System;

namespace ShorelandsBillboard.Core
{
    public static class Mp4
    {
        // A copy of an init section whose movie duration (moov/mvhd) reads "not known", all ones, as the format
        // has it. A live stream's says 0. Null: no movie header found.
        public static byte[] WithUnknownDuration(byte[] init, int n)
        {
            if (!Find(init, 0, n, "moov", out int moov, out int moovEnd) || !Find(init, moov, moovEnd, "mvhd", out int at, out int end)) return null;
            // version, flags, two dates, time scale, duration; version 1 has 64-bit dates and duration
            bool wide = at < end && init[at] == 1;
            int duration = at + (wide ? 24 : 16), size = wide ? 8 : 4;
            if (duration + size > end) return null;
            var copy = new byte[n];
            Buffer.BlockCopy(init, 0, copy, 0, n);
            for (int i = 0; i < size; i++) copy[duration + i] = 0xFF;
            return copy;
        }

        // The payload of the first box of that type among the boxes in [from, to).
        static bool Find(byte[] b, int from, int to, string type, out int payload, out int end)
        {
            payload = end = 0;
            for (long at = from; at + 8 <= to;)
            {
                long size = (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]); int header = 8;
                if (size == 1)
                {
                    if (at + 16 > to) return false;
                    size = 0; for (int i = 8; i < 16; i++) size = size << 8 | b[at + i];
                    header = 16;
                }
                else if (size == 0) size = to - at;
                if (size < header || at + size > to) return false;
                if (b[at + 4] == type[0] && b[at + 5] == type[1] && b[at + 6] == type[2] && b[at + 7] == type[3])
                {
                    payload = (int)at + header; end = (int)(at + size);
                    return true;
                }
                at += size;
            }
            return false;
        }
    }
}
