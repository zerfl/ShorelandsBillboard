using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShorelandsBillboard.Core
{
    public sealed class Variant
    {
        public string Name,
            Url;
        public int Width,
            Height,
            Bandwidth;
        public double Fps;
        public bool Source;
    }

    public enum Container
    {
        Ts,
        Fmp4,
    }

    // What a fragmented MP4 segment cannot be read without (EXT-X-MAP). Length < 0: the whole resource.
    public sealed class InitSection
    {
        public string Url;
        public long Offset,
            Length = -1;

        public bool Same(InitSection o) => o != null && o.Url == Url && o.Offset == Offset && o.Length == Length;
    }

    public sealed class Segment
    {
        public long Seq;
        public string Url;
        public double Duration;
        public bool Ad,
            Discontinuity;
        public InitSection Map; // the last one above the segment; none: MPEG-TS
        public Container Container => Map != null ? Container.Fmp4 : Container.Ts;
    }

    public sealed class MediaPlaylist
    {
        public readonly List<Segment> Segments = new List<Segment>();
        public double TargetDuration = 2;
        public bool Ended;
    }

    public static class Hls
    {
        static Dictionary<string, string> Attributes(string s)
        {
            var d = new Dictionary<string, string>();
            int i = 0;
            while (i < s.Length)
            {
                int eq = s.IndexOf('=', i);
                if (eq < 0)
                    break;
                string key = s.Substring(i, eq - i).Trim();
                string val;
                i = eq + 1;
                if (i < s.Length && s[i] == '"')
                {
                    int end = s.IndexOf('"', i + 1);
                    if (end < 0)
                        end = s.Length;
                    val = s.Substring(i + 1, end - i - 1);
                    i = s.IndexOf(',', end);
                    i = i < 0 ? s.Length : i + 1;
                }
                else
                {
                    int end = s.IndexOf(',', i);
                    if (end < 0)
                        end = s.Length;
                    val = s.Substring(i, end - i);
                    i = end + 1;
                }
                d[key] = val;
            }
            return d;
        }

        static string[] Lines(string text) => text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        public static List<Variant> ParseMaster(string text)
        {
            var list = new List<Variant>();
            Dictionary<string, string> inf = null;
            foreach (var raw in Lines(text))
            {
                var line = raw.Trim();
                if (line.StartsWith("#EXT-X-STREAM-INF:"))
                {
                    inf = Attributes(line.Substring(18));
                    continue;
                }
                if (line.StartsWith("#") || inf == null)
                    continue;
                var v = new Variant { Url = line };
                inf.TryGetValue("STABLE-VARIANT-ID", out v.Name);
                if (v.Name == null)
                    inf.TryGetValue("VIDEO", out v.Name);
                if (inf.TryGetValue("RESOLUTION", out var res))
                {
                    var p = res.Split('x');
                    if (p.Length == 2)
                    {
                        int.TryParse(p[0], out v.Width);
                        int.TryParse(p[1], out v.Height);
                    }
                }
                if (inf.TryGetValue("BANDWIDTH", out var bw))
                    int.TryParse(bw, out v.Bandwidth);
                if (inf.TryGetValue("FRAME-RATE", out var fr))
                    double.TryParse(fr, NumberStyles.Float, CultureInfo.InvariantCulture, out v.Fps);
                v.Source = inf.TryGetValue("IVS-VARIANT-SOURCE", out var src) ? src == "source" : v.Name == "chunked";
                list.Add(v);
                inf = null;
            }
            return list;
        }

        // The smallest transcode that is at least `height` tall (the picture is only scaled down), else the
        // largest transcode; the source quality only when the channel has no transcodes.
        public static Variant Select(List<Variant> variants, int height)
        {
            Variant best = null,
                largest = null,
                source = null;
            foreach (var v in variants)
            {
                if (v.Height <= 0)
                    continue; // audio_only
                if (v.Source)
                {
                    if (source == null)
                        source = v;
                    continue;
                }
                if (largest == null || v.Height > largest.Height)
                    largest = v;
                if (v.Height >= height && (best == null || v.Height < best.Height))
                    best = v;
            }
            return best ?? largest ?? source;
        }

        public static MediaPlaylist ParseMedia(string text)
        {
            var pl = new MediaPlaylist();
            var ads = new List<(DateTime start, DateTime end)>();
            long seq = 0;
            double duration = 0;
            string title = null;
            DateTime? date = null;
            InitSection map = null;
            bool discontinuity = false;
            foreach (var raw in Lines(text))
            {
                var line = raw.Trim();
                if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:"))
                    long.TryParse(line.Substring(22), out seq);
                else if (line.StartsWith("#EXT-X-TARGETDURATION:"))
                    double.TryParse(
                        line.Substring(22),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out pl.TargetDuration
                    );
                else if (line.StartsWith("#EXT-X-ENDLIST"))
                    pl.Ended = true;
                else if (line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:"))
                    date = ParseDate(line.Substring(25));
                else if (line.StartsWith("#EXT-X-DISCONTINUITY") && !line.StartsWith("#EXT-X-DISCONTINUITY-SEQUENCE"))
                    discontinuity = true;
                else if (line.StartsWith("#EXT-X-MAP:"))
                {
                    var a = Attributes(line.Substring(11));
                    if (!a.TryGetValue("URI", out var uri) || uri.Length == 0)
                        continue;
                    map = new InitSection { Url = uri };
                    // BYTERANGE="<length>[@<offset>]"
                    if (a.TryGetValue("BYTERANGE", out var range))
                    {
                        var p = range.Split('@');
                        if (long.TryParse(p[0], out long length) && length > 0)
                        {
                            map.Length = length;
                            if (p.Length > 1)
                                long.TryParse(p[1], out map.Offset);
                        }
                    }
                }
                else if (line.StartsWith("#EXT-X-DATERANGE:"))
                {
                    var a = Attributes(line.Substring(17));
                    a.TryGetValue("CLASS", out var cls);
                    a.TryGetValue("ID", out var id);
                    if (cls != "twitch-stitched-ad" && !(id ?? "").StartsWith("stitched-ad-"))
                        continue;
                    if (!a.TryGetValue("START-DATE", out var sd) || !(ParseDate(sd) is DateTime start))
                        continue;
                    DateTime end = DateTime.MaxValue;
                    if (a.TryGetValue("END-DATE", out var ed) && ParseDate(ed) is DateTime e2)
                        end = e2;
                    else if (
                        a.TryGetValue("DURATION", out var du)
                        && double.TryParse(du, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs)
                    )
                        end = start.AddSeconds(secs);
                    ads.Add((start, end));
                }
                else if (line.StartsWith("#EXTINF:"))
                {
                    var rest = line.Substring(8);
                    int comma = rest.IndexOf(',');
                    double.TryParse(
                        comma < 0 ? rest : rest.Substring(0, comma),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out duration
                    );
                    title = comma < 0 ? null : rest.Substring(comma + 1);
                }
                else if (!line.StartsWith("#"))
                {
                    bool ad = title != null && title.Contains("Amazon");
                    if (!ad && date is DateTime d)
                        foreach (var r in ads)
                            if (d >= r.start && d < r.end)
                            {
                                ad = true;
                                break;
                            }
                    pl.Segments.Add(
                        new Segment
                        {
                            Seq = seq++,
                            Url = line,
                            Duration = duration,
                            Ad = ad,
                            Map = map,
                            Discontinuity = discontinuity,
                        }
                    );
                    if (date is DateTime dd)
                        date = dd.AddSeconds(duration);
                    title = null;
                    discontinuity = false;
                }
            }
            return pl;
        }

        static DateTime? ParseDate(string s)
        {
            return DateTime.TryParse(
                s,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var d
            )
                ? d
                : (DateTime?)null;
        }
    }

    // Which segments of each playlist poll to play: the newest `startSegments` at first, then every newer
    // one, ads left out (streamlink's rule; the stream holds until the break is over).
    public sealed class SegmentTracker
    {
        readonly int startSegments;
        long last = -1;
        public bool InAd { get; private set; }

        public SegmentTracker(int startSegments)
        {
            this.startSegments = startSegments;
        }

        public List<Segment> Next(MediaPlaylist pl)
        {
            var fresh = new List<Segment>();
            foreach (var s in pl.Segments)
                if (s.Seq > last)
                    fresh.Add(s);
            if (fresh.Count == 0)
                return fresh;
            if (last < 0 && fresh.Count > startSegments)
                fresh.RemoveRange(0, fresh.Count - startSegments);
            last = fresh[fresh.Count - 1].Seq;
            InAd = fresh[fresh.Count - 1].Ad;
            fresh.RemoveAll(s => s.Ad);
            return fresh;
        }
    }
}
