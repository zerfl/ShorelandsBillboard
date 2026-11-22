using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using ShorelandsBillboard.Core;

namespace ShorelandsBillboard
{
    public enum StreamEvent
    {
        Ad,
        AdOver,
        Offline,
        Failed,
        Ended,
        NoDecoder,
        Log,
    }

    // One playback attempt: resolves the channel, reads its HLS segments and hands them to the decoder.
    public sealed class Stream
    {
        public readonly Decoder Decoder;
        public ConcurrentQueue<(StreamEvent kind, string text)> Events => Decoder.Events;

        readonly string channel;
        readonly int quality;
        volatile bool stop;

        public Stream(string channel, int quality, Decoder decoder)
        {
            this.channel = channel;
            this.quality = quality;
            Decoder = decoder;
        }

        public void Start() => Spawn(Run, "hls");

        public void Stop()
        {
            stop = true;
            Decoder.Stop();
        }

        void Spawn(Action body, string name)
        {
            new Thread(() =>
            {
                try
                {
                    body();
                }
                catch (Exception e)
                {
                    if (!stop)
                        Events.Enqueue((StreamEvent.Failed, name + ": " + e.Message));
                }
            })
            {
                IsBackground = true,
                Name = "billboard-" + name,
            }.Start();
        }

        void Log(string s) => Events.Enqueue((StreamEvent.Log, s));

        void Run()
        {
            var token = Twitch.ParseToken(Http("POST", Twitch.GqlUrl, Twitch.TokenRequest(channel), out _));
            if (token.Kind == Twitch.TokenKind.NoChannel)
            {
                Events.Enqueue((StreamEvent.Offline, "no channel " + channel));
                return;
            }
            if (token.Kind == Twitch.TokenKind.Error)
            {
                Events.Enqueue((StreamEvent.Failed, "token: " + token.Error));
                return;
            }
            string master = Http(
                "GET",
                Twitch.UsherUrl(channel, token, new Random().Next(999999)),
                null,
                out int status
            );
            if (status == 404 || status == 403)
            {
                Events.Enqueue((StreamEvent.Offline, Twitch.UsherError(master) ?? "http " + status));
                return;
            }
            if (status != 200)
            {
                Events.Enqueue((StreamEvent.Failed, "usher http " + status));
                return;
            }
            var variants = Hls.ParseMaster(master);
            var variant = Hls.Select(variants, quality > 0 ? quality : Decoder.Height);
            if (variant == null)
            {
                Events.Enqueue((StreamEvent.Failed, "no video variant"));
                return;
            }
            Log(
                $"{channel}: {variants.Count} variants, playing {variant.Name} {variant.Width}x{variant.Height} as {Decoder.Width}x{Decoder.Height}"
            );

            var tracker = new SegmentTracker(3);
            bool ad = false,
                started = false,
                changed = false;
            int failures = 0;
            while (!stop)
            {
                string text = Http("GET", variant.Url, null, out status);
                if (status != 200)
                {
                    if (++failures >= 3)
                    {
                        Events.Enqueue((StreamEvent.Ended, "playlist http " + status));
                        break;
                    }
                    Thread.Sleep(1000);
                    continue;
                }
                failures = 0;
                var pl = Hls.ParseMedia(text);
                var segs = tracker.Next(pl);
                if (tracker.InAd != ad)
                {
                    ad = tracker.InAd;
                    Events.Enqueue((ad ? StreamEvent.Ad : StreamEvent.AdOver, null));
                }
                foreach (var s in segs)
                {
                    if (stop)
                        break;
                    // the decoder is told what it gets, so it starts with the first segment that plays
                    if (!started)
                    {
                        Decoder.Container = s.Container;
                        if (!StartDecoder())
                            return;
                        started = true;
                        if (s.Container == Container.Fmp4)
                            Log("the stream comes as fragmented MP4");
                    }
                    if (s.Container != Decoder.Container || !Play(s))
                    {
                        Events.Enqueue((StreamEvent.Ended, "the stream changed its format"));
                        changed = true;
                        break;
                    }
                }
                if (changed)
                    break;
                if (pl.Ended)
                {
                    Events.Enqueue((StreamEvent.Ended, "stream ended"));
                    break;
                }
                Thread.Sleep(segs.Count > 0 ? 500 : 1000);
            }
            Stop();
        }

        InitSection map;
        byte[] init;
        long last = -1;
        bool cut;

        // False: the stream cannot go on in this decoder.
        bool Play(Segment s)
        {
            // segments left out for an ad break, missed or not arrived whole
            bool hole = cut || s.Discontinuity || last >= 0 && s.Seq != last + 1;
            last = s.Seq;
            cut = true;
            if (s.Map != null)
            {
                if (!s.Map.Same(map))
                {
                    var bytes = Download(s.Map.Url, s.Map.Offset, s.Map.Length);
                    if (bytes == null)
                        return true;
                    // a decoder reads one init section; another one (the broadcast was set up anew) needs a new decoder
                    if (init != null && !Same(bytes, init))
                        return false;
                    map = s.Map;
                    if (init == null)
                    {
                        init = bytes;
                        Decoder.Write(init, init.Length);
                        hole = false;
                    }
                }
                if (hole && Decoder.StartOver())
                    Decoder.Write(init, init.Length);
            }
            cut = !Pipe(s.Url);
            return true;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        bool StartDecoder()
        {
            try
            {
                Decoder.Start();
                return true;
            }
            catch (Exception e)
            {
                Events.Enqueue((StreamEvent.NoDecoder, e.Message));
                return false;
            }
        }

        static HttpWebRequest Request(string method, string url)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = method;
            r.UserAgent = Twitch.UserAgent;
            r.Timeout = 10000;
            r.ReadWriteTimeout = 10000;
            r.Referer = Twitch.Referer;
            r.Headers["Origin"] = Twitch.Referer;
            if (url == Twitch.GqlUrl)
                r.Headers["Client-ID"] = Twitch.ClientId;
            return r;
        }

        internal static string Http(string method, string url, string body, out int status)
        {
            var r = Request(method, url);
            if (body != null)
            {
                var b = Encoding.UTF8.GetBytes(body);
                r.ContentType = "application/json";
                using (var s = r.GetRequestStream())
                    s.Write(b, 0, b.Length);
            }
            HttpWebResponse resp;
            try
            {
                resp = (HttpWebResponse)r.GetResponse();
            }
            catch (WebException e) when (e.Response is HttpWebResponse er)
            {
                resp = er;
            }
            using (resp)
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                status = (int)resp.StatusCode;
                return reader.ReadToEnd();
            }
        }

        readonly byte[] copy = new byte[65536];

        // False: the segment did not arrive whole. Half a transport stream segment is played, the decoder
        // resyncs after the cut; half an MP4 segment would leave the demuxer inside a box, so none of it is.
        bool Pipe(string url)
        {
            if (Decoder.Container == Container.Fmp4)
            {
                var whole = Download(url, 0, -1);
                if (whole != null)
                    Decoder.Write(whole, whole.Length);
                return whole != null;
            }
            try
            {
                using (var resp = (HttpWebResponse)Request("GET", url).GetResponse())
                using (var s = resp.GetResponseStream())
                {
                    int n;
                    while ((n = s.Read(copy, 0, copy.Length)) > 0)
                    {
                        Decoder.Write(copy, n);
                    }
                }
                return true;
            }
            catch (WebException e)
            {
                Log("segment skipped: " + e.Message);
                return false;
            }
        }

        byte[] Download(string url, long offset, long length)
        {
            try
            {
                var r = Request("GET", url);
                if (length >= 0)
                    r.AddRange(offset, offset + length - 1);
                using (var resp = (HttpWebResponse)r.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var all = new MemoryStream())
                {
                    int n;
                    while ((n = s.Read(copy, 0, copy.Length)) > 0)
                        all.Write(copy, 0, n);
                    return all.ToArray();
                }
            }
            catch (Exception e) when (e is WebException || e is IOException)
            {
                Log("segment skipped: " + e.Message);
                return null;
            }
        }
    }
}
