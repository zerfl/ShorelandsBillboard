using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using ShorelandsBillboard.Core;

namespace ShorelandsBillboard
{
    public sealed class Frame
    {
        public byte[] Data; public double Time; public GCHandle Pin; public int Id;
    }

    // Turns the MPEG-TS bytes of a stream into pictures of a fixed size and stereo float sound. Pictures
    // carry a time; the sound's playing position translates to the same time base, so the picture can
    // follow the sound.
    public abstract class Decoder
    {
        public readonly int Width, Height, SampleRate;
        public readonly ConcurrentQueue<(StreamEvent kind, string text)> Events = new ConcurrentQueue<(StreamEvent, string)>();
        public long FramesDecoded;
        protected volatile bool stop;
        protected readonly Queue<Frame> frames = new Queue<Frame>();
        protected readonly Stack<Frame> pool = new Stack<Frame>();

        protected Decoder(int width, int height, int sampleRate)
        {
            Width = width; Height = height; SampleRate = sampleRate;
        }

        public abstract string Name { get; }
        public abstract bool Rgba { get; }       // else RGB
        public abstract bool TopDown { get; }
        public abstract int StartFrames { get; } // pictures to hold before the first is shown
        public abstract void Start();            // stream thread
        public abstract void Write(byte[] buf, int n);
        public abstract void Stop();
        public abstract void ReadSound(float[] dst);   // audio thread
        public abstract double SoundSeconds { get; }   // sound handed to the engine
        // Picture time for a playing position on SoundSeconds; NaN while there is none.
        public abstract double TimeOfSound(double seconds);
        // Picture time now, for a decoder that paces its own output; else NaN.
        public virtual double Now => double.NaN;
        public virtual string Stats => "";

        public int Buffered { get { lock (frames) return frames.Count; } }

        // The newest picture due at `time`; older ones are dropped.
        public Frame Take(double time)
        {
            lock (frames)
            {
                Frame due = null;
                while (frames.Count > 0 && frames.Peek().Time <= time)
                {
                    if (due != null) pool.Push(due);
                    due = frames.Dequeue();
                }
                if (due != null) Monitor.PulseAll(frames);
                return due;
            }
        }

        public void Recycle(Frame f) { lock (frames) pool.Push(f); }

        protected void Log(string s) => Events.Enqueue((StreamEvent.Log, s));

        protected void Spawn(Action body, string name)
        {
            new Thread(() =>
            {
                try { body(); }
                catch (Exception e) { if (!stop) Events.Enqueue((StreamEvent.Failed, name + ": " + e.Message)); }
            }) { IsBackground = true, Name = "billboard-" + name }.Start();
        }
    }

    // An ffmpeg child process: transport stream into stdin, raw RGB frames from stdout, float samples from a
    // loopback socket. Frames and samples both count from the stream start.
    public sealed class FfmpegDecoder : Decoder
    {
        public const int Fps = 30;
        readonly string exe; readonly SampleRing audio;
        Process process; TcpListener listener; System.IO.Stream stdin;

        public FfmpegDecoder(string exe, int width, int height, int sampleRate) : base(width, height, sampleRate)
        {
            this.exe = exe;
            audio = new SampleRing(sampleRate * 2 * 4);   // 4 s stereo: ffmpeg delivers sound ahead of the picture
        }

        public override string Name => exe;
        public override bool Rgba => false;
        public override bool TopDown => false;
        public override int StartFrames => 8;
        public override void ReadSound(float[] dst) => audio.Read(dst);
        public override double SoundSeconds => audio.ReadPosition / (2.0 * SampleRate);
        public override double TimeOfSound(double seconds) => seconds;
        public override string Stats => $"soundAhead={audio.Written / (2.0 * SampleRate) - FramesDecoded / (double)Fps:F2}s";

        public override void Start()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string args = "-hide_banner -loglevel warning -probesize 400k -analyzeduration 1000000 -f mpegts -i pipe:0"
                + $" -map 0:v:0 -vf fps={Fps},scale={Width}:{Height}:flags=area,vflip -pix_fmt rgb24 -fps_mode passthrough -f rawvideo pipe:1"
                + $" -map 0:a:0 -af aresample=async=1 -ac 2 -ar {SampleRate} -f f32le tcp://127.0.0.1:{port}";
            process = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            stdin = process.StandardInput.BaseStream;
            Spawn(ReadVideo, "video"); Spawn(ReadAudio, "audio"); Spawn(ReadErrors, "ffmpeg");
        }

        public override void Write(byte[] buf, int n) { stdin.Write(buf, 0, n); stdin.Flush(); }

        public override void Stop()
        {
            stop = true;
            try { listener?.Stop(); } catch { }
            try { if (process != null && !process.HasExited) process.Kill(); } catch { }
            lock (frames) Monitor.PulseAll(frames);
        }

        static bool Fill(System.IO.Stream s, byte[] buf, int n)
        {
            int got = 0;
            while (got < n) { int r = s.Read(buf, got, n - got); if (r <= 0) return false; got += r; }
            return true;
        }

        void ReadVideo()
        {
            var s = process.StandardOutput.BaseStream;
            int size = Width * Height * 3;
            while (!stop)
            {
                Frame f;
                lock (frames) f = pool.Count > 0 ? pool.Pop() : new Frame { Data = new byte[size] };
                if (!Fill(s, f.Data, size)) break;
                f.Time = FramesDecoded / (double)Fps;
                lock (frames)
                {
                    while (frames.Count >= StartFrames && !stop) Monitor.Wait(frames, 200);
                    frames.Enqueue(f);
                }
                Interlocked.Increment(ref FramesDecoded);
            }
            if (!stop) Events.Enqueue((StreamEvent.Ended, "decoder stopped"));
        }

        void ReadAudio()
        {
            using (var client = listener.AcceptTcpClient())
            {
                var s = client.GetStream();
                var bytes = new byte[16384]; var samples = new float[bytes.Length / 4];
                int have = 0;
                while (!stop)
                {
                    int r = s.Read(bytes, have, bytes.Length - have);
                    if (r <= 0) break;
                    have += r;
                    int n = have & ~7;   // whole stereo samples
                    Buffer.BlockCopy(bytes, 0, samples, 0, n);
                    audio.Write(samples, n / 4);
                    Buffer.BlockCopy(bytes, n, bytes, 0, have - n);
                    have -= n;
                }
            }
        }

        void ReadErrors()
        {
            string line;
            while ((line = process.StandardError.ReadLine()) != null) if (!stop) Log("ffmpeg: " + line);
        }
    }
}
