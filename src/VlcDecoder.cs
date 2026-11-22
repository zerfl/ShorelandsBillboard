using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using LibVLCSharp.Shared;
using ShorelandsBillboard.Core;

namespace ShorelandsBillboard
{
    // libVLC inside the game process: the stream goes in through a media callback, pictures come
    // back through the video memory callbacks at the moment libVLC would show them, sound through the audio
    // callback ahead of the date it is to be heard. Nothing here may be touched unless LibVLCSharp.dll is
    // installed (Backend.VlcInstalled).
    public sealed class VlcDecoder : Decoder
    {
        const int MaxFrames = 32;

        // GPU decoding into memory callbacks costs more than it saves
        const string Hardware = "none";

        static LibVLC vlc;
        static readonly object init = new object();
        static readonly List<Thread> closers = new List<Thread>();

        readonly string dir;
        readonly DatedSound sound;
        readonly Feed feed = new Feed();
        readonly List<Frame> all = new List<Frame>(); // a picture's id for libVLC is its index + 1
        readonly object life = new object();
        readonly MediaPlayer.LibVLCVideoLockCb lockCb;
        readonly MediaPlayer.LibVLCVideoDisplayCb displayCb;
        readonly MediaPlayer.LibVLCAudioPlayCb playCb;
        readonly MediaPlayer.LibVLCAudioFlushCb flushCb;
        Media media;
        MediaPlayer player;
        Frame inFlight;
        short[] pcm = new short[4096];
        float[] samples = new float[4096];
        int closing;
        long dropped,
            lastEarly,
            lastShown;
        ulong lastSum;
        bool begun;

        public VlcDecoder(string dir, int width, int height, int sampleRate)
            : base(width, height, sampleRate)
        {
            this.dir = dir;
            sound = new DatedSound(sampleRate, 8);
            lockCb = OnLock;
            displayCb = OnDisplay;
            playCb = OnPlay;
            flushCb = (d, pts) => sound.Clear();
        }

        public override string Name => "libVLC";
        public override bool Rgba => true;
        public override bool TopDown => true;
        public override int StartFrames => 1;
        public override double SoundSeconds => sound.HandedOut / (double)SampleRate;

        public override double TimeOfSound(double seconds) =>
            sound.DateAt((long)(seconds * SampleRate)) is long d ? d / 1e6 : double.NaN;

        public override double Now => vlc != null ? vlc.Clock / 1e6 : double.NaN;
        public override string Stats =>
            $"soundEarly={lastEarly / 1e6:F2}s soundBuffered={sound.Buffered:F2}s dropped={dropped} closing={Closing}";

        public override void ReadSound(float[] dst)
        {
            if (vlc == null)
                Array.Clear(dst, 0, dst.Length);
            else
                sound.Read(dst, vlc.Clock);
        }

        public override void Start()
        {
            lock (life)
            {
                if (stop)
                    return;
                lock (init)
                {
                    if (vlc == null)
                    {
                        LibVLCSharp.Shared.Core.Initialize(dir);
                        var v = new LibVLC(
                            "--ignore-config",
                            "--no-video-title-show",
                            "--no-osd",
                            "--no-spu",
                            "--no-sub-autodetect-file",
                            "--no-stats",
                            "--no-snapshot-preview",
                            "--no-disable-screensaver",
                            "--no-audio-time-stretch"
                        );
                        v.Log += OnLog;
                        vlc = v;
                    }
                }
                media = new Media(
                    vlc,
                    feed,
                    Container == Container.Fmp4 ? ":demux=mp4" : ":demux=ts",
                    ":avcodec-hw=" + Hardware
                );
                player = new MediaPlayer(media);
                player.SetVideoFormat("RGBA", (uint)Width, (uint)Height, (uint)Width * 4);
                player.SetVideoCallbacks(lockCb, null, displayCb);
                player.SetAudioFormat("S16N", (uint)SampleRate, 2);
                player.SetAudioCallbacks(playCb, null, null, flushCb, null);
                player.EncounteredError += (s, e) =>
                {
                    if (!stop)
                        Events.Enqueue((StreamEvent.Failed, "libVLC could not play the stream: " + LastErrors()));
                };
                player.EndReached += (s, e) =>
                {
                    if (!stop)
                        Events.Enqueue((StreamEvent.Ended, "decoder stopped"));
                };
                if (!player.Play())
                    throw new InvalidOperationException("libVLC did not start");
            }
        }

        public override void Write(byte[] buf, int n)
        {
            // libVLC's MP4 demuxer reads an input that can seek to its end, looking for fragments, when the
            // movie's duration is 0, as a live stream's init section has it. With any other it starts at the
            // first fragment. The feed says it can seek (see below), so the duration goes in as "not known".
            if (Container == Container.Fmp4 && !begun)
            {
                begun = true;
                var init = Mp4.WithUnknownDuration(buf, n);
                if (init != null)
                    buf = init;
            }
            feed.Write(buf, n);
        }

        // libVLC's stop waits for its threads, which may be inside one of the callbacks here; it runs on a
        // thread of its own, never on a libVLC thread, and the callbacks never wait once `stop` is set.
        public override void Stop()
        {
            stop = true;
            feed.End();
            if (Interlocked.Exchange(ref closing, 1) != 0)
                return;
            var t = new Thread(Close) { IsBackground = true, Name = "billboard-vlc-close" };
            lock (closers)
            {
                closers.RemoveAll(c => !c.IsAlive);
                closers.Add(t);
            }
            t.Start();
        }

        void Close()
        {
            lock (life)
            {
                if (player != null)
                {
                    player.Stop();
                    player.Dispose();
                    media.Dispose();
                    feed.Dispose();
                }
                lock (frames)
                {
                    foreach (var f in all)
                        f.Pin.Free();
                    all.Clear();
                    frames.Clear();
                    pool.Clear();
                }
            }
        }

        static int Closing
        {
            get
            {
                lock (closers)
                    return closers.FindAll(c => c.IsAlive).Count;
            }
        }

        // Quitting with libVLC threads still calling into managed code crashes the runtime's shutdown.
        public static void WaitClosed(int milliseconds)
        {
            Thread[] wait;
            lock (closers)
                wait = closers.ToArray();
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            foreach (var t in wait)
                t.Join(Math.Max(0, (int)(until - DateTime.UtcNow).TotalMilliseconds));
        }

        IntPtr OnLock(IntPtr opaque, IntPtr planes)
        {
            Frame f;
            lock (frames)
            {
                f = inFlight; // prepared but never shown
                if (f == null)
                {
                    if (pool.Count > 0)
                        f = pool.Pop();
                    else if (all.Count >= MaxFrames && frames.Count > 0)
                    {
                        f = frames.Dequeue();
                        dropped++;
                    }
                    else
                    {
                        f = new Frame { Data = new byte[Width * Height * 4], Id = all.Count + 1 };
                        f.Pin = GCHandle.Alloc(f.Data, GCHandleType.Pinned);
                        all.Add(f);
                    }
                    inFlight = f;
                }
            }
            Marshal.WriteIntPtr(planes, f.Pin.AddrOfPinnedObject());
            return (IntPtr)f.Id;
        }

        void OnDisplay(IntPtr opaque, IntPtr picture)
        {
            lock (frames)
            {
                var f = inFlight;
                if (f == null || f.Id != (int)picture)
                    return;
                inFlight = null;
                long now = vlc.Clock;
                // While its input is dry libVLC shows its last picture again every 80 ms; that is no new picture.
                ulong sum = Sample(f.Data);
                bool again = sum == lastSum && now - lastShown >= 70_000;
                lastSum = sum;
                lastShown = now;
                if (stop || again)
                {
                    pool.Push(f);
                    return;
                }
                f.Time = now / 1e6;
                frames.Enqueue(f);
            }
            Interlocked.Increment(ref FramesDecoded);
        }

        static ulong Sample(byte[] data)
        {
            ulong h = 14695981039346656037;
            for (int i = 0; i < data.Length; i += 61)
                h = (h ^ data[i]) * 1099511628211;
            return h;
        }

        void OnPlay(IntPtr data, IntPtr buffer, uint count, long pts)
        {
            if (stop)
                return;
            int n = (int)count * 2;
            if (pcm.Length < n)
            {
                pcm = new short[n];
                samples = new float[n];
            }
            Marshal.Copy(buffer, pcm, 0, n);
            for (int i = 0; i < n; i++)
                samples[i] = pcm[i] * (1f / 32768f);
            lastEarly = pts - vlc.Clock;
            sound.Write(samples, (int)count, pts);
        }

        static void OnLog(object sender, LogEventArgs e)
        {
            // Errors are routine here (options of plugins that are not installed, hardware formats tried first),
            // so they are only shown when the stream fails.
            if (e.Level != LogLevel.Error)
                return;
            lock (recent)
            {
                if (recent.Count == 8)
                    recent.Dequeue();
                recent.Enqueue($"[{e.Module}] {e.Message}");
            }
        }

        static readonly Queue<string> recent = new Queue<string>();

        static string LastErrors()
        {
            lock (recent)
                return string.Join("; ", recent);
        }

        // The stream's bytes for libVLC's input thread, which waits here for more.
        sealed class Feed : MediaInput
        {
            const int MaxBytes = 8 << 20;
            readonly Queue<byte[]> chunks = new Queue<byte[]>();
            byte[] current;
            int at,
                queued;
            bool ended;

            // libVLC paces its reading by the stream's clock only for an input that says it can seek (the
            // callback access reports pace control exactly then); otherwise it takes everything at once and
            // derives its clock from the arrival of 2 s segments.
            public override bool Open(out ulong size)
            {
                size = ulong.MaxValue;
                return true;
            }

            public override bool Seek(ulong offset) => false;

            public override void Close() { }

            public override int Read(IntPtr buf, uint len)
            {
                lock (chunks)
                {
                    while (current == null)
                    {
                        if (ended)
                            return 0;
                        if (chunks.Count > 0)
                        {
                            current = chunks.Dequeue();
                            at = 0;
                        }
                        else
                            Monitor.Wait(chunks, 500);
                    }
                    int n = Math.Min((int)len, current.Length - at);
                    Marshal.Copy(current, at, buf, n);
                    at += n;
                    queued -= n;
                    if (at == current.Length)
                        current = null;
                    Monitor.PulseAll(chunks);
                    return n;
                }
            }

            public void Write(byte[] buf, int n)
            {
                var c = new byte[n];
                Buffer.BlockCopy(buf, 0, c, 0, n);
                lock (chunks)
                {
                    while (queued > MaxBytes && !ended)
                        Monitor.Wait(chunks, 500);
                    if (ended)
                        return;
                    chunks.Enqueue(c);
                    queued += n;
                    Monitor.PulseAll(chunks);
                }
            }

            public void End()
            {
                lock (chunks)
                {
                    ended = true;
                    Monitor.PulseAll(chunks);
                }
            }
        }
    }
}
