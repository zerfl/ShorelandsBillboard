using System;
using System.Collections.Generic;

namespace ShorelandsBillboard.Core
{
    // Stereo sound that arrives ahead of the date (microseconds on the decoder's clock) at which it is to be
    // heard, as libVLC's audio callback delivers it. Samples are handed out in order; where the dates jump
    // (start, a gap in the stream, a stall) the next samples wait until they are due, and silence fills in.
    // Every frame handed out counts, silence too, so a position on that count has a date for the picture.
    public sealed class DatedSound
    {
        public const long Tolerance = 50_000, MaxLate = 500_000;

        readonly SampleRing ring; readonly int rate;
        readonly Queue<(long at, long date, bool jump)> marks = new Queue<(long, long, bool)>();   // ring positions
        readonly (long at, long date)[] heard = new (long, long)[32];   // positions on the handed-out count
        int heardNext, heardCount;
        long nextDate = long.MinValue, markedDate = long.MinValue, readDate, handedOut;
        bool silent = true;
        readonly object gate = new object();

        public DatedSound(int rate, double seconds) { this.rate = rate; ring = new SampleRing((int)(rate * seconds) * 2); }

        public long HandedOut { get { lock (gate) return handedOut; } }
        public double Buffered { get { lock (gate) return (ring.Written - ring.ReadPosition) / (2.0 * rate); } }

        long Micros(long frames) => frames * 1_000_000 / rate;
        long Frames(long micros) => micros * rate / 1_000_000;

        public void Write(float[] src, int frames, long date)
        {
            lock (gate)
            {
                bool jump = nextDate == long.MinValue || Math.Abs(date - nextDate) > Tolerance;
                // a mark per second keeps the dates exact when they creep against the sample count
                if (jump || date - markedDate >= 1_000_000) { marks.Enqueue((ring.Written / 2, date, jump)); markedDate = date; }
                ring.Write(src, frames * 2);
                nextDate = date + Micros(frames);
            }
        }

        public void Clear()
        {
            lock (gate) { ring.Skip(long.MaxValue); marks.Clear(); nextDate = markedDate = long.MinValue; silent = true; }
        }

        // audio thread; now: the decoder's clock
        public void Read(float[] dst, long now)
        {
            lock (gate)
            {
                int done = 0;
                while (done < dst.Length)
                {
                    long at = ring.ReadPosition / 2;
                    int want = dst.Length - done;
                    if (marks.Count > 0)
                    {
                        var m = marks.Peek();
                        if (m.at <= at)
                        {
                            if (m.jump && m.date > now) break;
                            marks.Dequeue();
                            readDate = m.date + Micros(at - m.at);
                            if (m.jump && now - m.date > MaxLate)
                            {
                                long until = marks.Count > 0 ? marks.Peek().at : ring.Written / 2;
                                long skip = Math.Max(0, Math.Min(Frames(now - m.date), until - at));
                                ring.Skip(skip * 2); readDate += Micros(skip);
                            }
                            Heard(handedOut + done / 2, readDate);
                            silent = false;
                            continue;
                        }
                        want = (int)Math.Min(want, (m.at - at) * 2);
                    }
                    int n = ring.Read(dst, done, want);
                    if (n > 0 && silent) { Heard(handedOut + done / 2, readDate); silent = false; }
                    done += n; readDate += Micros(n / 2);
                    if (n < want) break;
                }
                if (done < dst.Length) { Array.Clear(dst, done, dst.Length - done); silent = true; }
                handedOut += dst.Length / 2;
            }
        }

        void Heard(long at, long date)
        {
            heard[heardNext] = (at, date);
            heardNext = (heardNext + 1) % heard.Length; if (heardCount < heard.Length) heardCount++;
        }

        // The date of the sound at a position on the handed-out count; null before any sound was handed out.
        public long? DateAt(long position)
        {
            lock (gate)
            {
                if (heardCount == 0) return null;
                int oldest = (heardNext - heardCount + heard.Length) % heard.Length;
                var best = heard[oldest];
                for (int i = 1; i < heardCount; i++)
                {
                    var h = heard[(oldest + i) % heard.Length];
                    if (h.at <= position) best = h;
                }
                return best.date + (position - best.at) * 1_000_000 / rate;
            }
        }
    }
}
