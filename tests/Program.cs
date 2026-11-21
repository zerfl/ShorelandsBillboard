using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;
using ShorelandsBillboard.Core;

static class Program
{
    static int failed;
    static string fixtures, art;

    static void Check(bool cond, string name)
    {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
        if (!cond) failed++;
    }

    static string Fixture(string name) => File.ReadAllText(Path.Combine(fixtures, name));

    static int Main()
    {
        fixtures = (string)AppContext.GetData("ShorelandsBillboard.Tests.Fixtures");
        art = (string)AppContext.GetData("ShorelandsBillboard.Tests.Art");
        TwitchTests(); MasterTests(); MediaTests(); ControllerTests(); LayoutTests(); SoundTests(); DatedSoundTests(); FfmpegTests();
        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }

    static void TwitchTests()
    {
        var req = JObject.Parse(Twitch.TokenRequest("dunkorslam"));
        Check((string)req["variables"]["login"] == "dunkorslam" && (bool)req["variables"]["isLive"] && (string)req["operationName"] == "PlaybackAccessToken"
            && ((string)req["extensions"]["persistedQuery"]["sha256Hash"]).Length == 64, "token request: persisted query for the channel");

        var live = Twitch.ParseToken(Fixture("token-live.json"));
        Check(live.Kind == Twitch.TokenKind.Token && live.Signature.Length == 40 && (string)JObject.Parse(live.Value)["channel"] == "dunkorslam", "recorded token response: signature and token for the channel");
        Check(Twitch.ParseToken(Fixture("token-nochannel.json")).Kind == Twitch.TokenKind.NoChannel, "recorded response for an unknown channel: no channel");
        var e1 = Twitch.ParseToken("{\"errors\":[{\"message\":\"service timeout\"}]}");
        var e2 = Twitch.ParseToken("{\"error\":\"Unauthorized\",\"status\":401,\"message\":\"bad client\"}");
        Check(e1.Kind == Twitch.TokenKind.Error && e1.Error == "service timeout" && e2.Kind == Twitch.TokenKind.Error && e2.Error.Contains("bad client"), "GQL error shapes are errors with their message");
        Check(Twitch.ParseToken("<html>").Kind == Twitch.TokenKind.Error, "a non-JSON answer is an error, not an exception");

        var url = new Uri(Twitch.UsherUrl("dunkorslam", live, 123));
        var query = url.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Check(url.AbsolutePath == "/api/v2/channel/hls/dunkorslam.m3u8" && query["token"] == live.Value && query["sig"] == live.Signature && query["supported_codecs"] == "h264",
            "usher URL carries the token unchanged after unescaping");
        Check(Twitch.UsherError(Fixture("usher-offline.json")) == "Can not find channel" && Twitch.UsherError("twirp error") == null, "recorded offline answer from usher: its error text");
        Check(Twitch.ValidChannel("dunkorslam") && Twitch.ValidChannel("a_b9") && !Twitch.ValidChannel("") && !Twitch.ValidChannel("a/b") && !Twitch.ValidChannel("x?y=1") && !Twitch.ValidChannel("Dunk"),
            "channel names that could alter the request are refused");
    }

    static void MasterTests()
    {
        var v = Hls.ParseMaster(Fixture("master-live.m3u8"));
        Check(v.Count == 6 && v.Select(x => x.Name).SequenceEqual(new[] { "1080p60", "720p60", "480p30", "360p30", "160p30", "audio_only" }), "recorded master playlist: six variants");
        Check(v[0].Source && v.Skip(1).All(x => !x.Source) && v[3].Width == 640 && v[3].Height == 360 && v[3].Fps == 30 && v[3].Url.StartsWith("https://") && v[5].Height == 0,
            "variant fields: source flag, size, frame rate, URL; audio_only has no size");
        Check(Hls.Select(v, 312).Name == "360p30" && Hls.Select(v, 104).Name == "160p30" && Hls.Select(v, 360).Name == "360p30" && Hls.Select(v, 361).Name == "480p30",
            "selection: the smallest transcode that covers the wanted height");
        Check(Hls.Select(v, 2000).Name == "720p60", "selection: nothing covers the height -> the largest transcode, never the source");
        Check(Hls.Select(v.Where(x => x.Source || x.Height == 0).ToList(), 312).Name == "1080p60", "selection: a channel without transcodes plays its source");
        Check(Hls.Select(v.Where(x => x.Height == 0).ToList(), 312) == null, "selection: audio only -> nothing");
    }

    static void MediaTests()
    {
        string a = Fixture("media-live.m3u8"), b = Fixture("media-live-next.m3u8");
        var pa = Hls.ParseMedia(a); var pb = Hls.ParseMedia(b);
        Check(pa.Segments.Count == 14 && pa.Segments[0].Seq == 4154 && pa.Segments.All(s => s.Duration == 2.0 && !s.Ad && s.Url.StartsWith("https://")) && !pa.Ended && pa.TargetDuration == 6,
            "recorded media playlist: 14 live segments from sequence 4154, prefetch entries left out");
        Check(pb.Segments[0].Seq == 4156, "the poll 5 s later starts two segments on");

        var t = new SegmentTracker(3);
        var first = t.Next(pa);
        Check(first.Select(s => s.Seq).SequenceEqual(new long[] { 4165, 4166, 4167 }), "tracker starts three segments behind the live edge");
        Check(t.Next(pb).Select(s => s.Seq).SequenceEqual(new long[] { 4168, 4169 }), "then only newer segments");
        Check(t.Next(pb).Count == 0 && t.Next(pa).Count == 0, "an older or repeated playlist yields nothing");

        // an ad break as Twitch marks it (streamlink: DATERANGE class twitch-stitched-ad / id stitched-ad-*),
        // laid over the last two recorded segments
        var dates = a.Split('\n').Where(l => l.StartsWith("#EXT-X-PROGRAM-DATE-TIME:")).Select(l => l.Substring(25).Trim()).ToList();
        string range = $"#EXT-X-DATERANGE:ID=\"stitched-ad-1\",CLASS=\"twitch-stitched-ad\",START-DATE=\"{dates[dates.Count - 2]}\",DURATION=30.000,X-TV-TWITCH-AD-ROLL-TYPE=\"MIDROLL\"\n";
        var ads = Hls.ParseMedia(a.Replace("#EXT-X-PROGRAM-DATE-TIME:" + dates[0], range + "#EXT-X-PROGRAM-DATE-TIME:" + dates[0]));
        Check(ads.Segments.Count(s => s.Ad) == 2 && ads.Segments.Skip(12).All(s => s.Ad), "segments dated inside a stitched-ad range are ads");
        var t2 = new SegmentTracker(3);
        var played = t2.Next(ads);
        Check(played.Select(s => s.Seq).SequenceEqual(new long[] { 4165 }) && t2.InAd, "ad segments are not played and the tracker reports the break");
        var byId = Hls.ParseMedia(a.Replace("#EXT-X-PROGRAM-DATE-TIME:" + dates[0], range.Replace("CLASS=\"twitch-stitched-ad\",", "") + "#EXT-X-PROGRAM-DATE-TIME:" + dates[0]));
        Check(byId.Segments.Count(s => s.Ad) == 2, "a range is also an ad by its id alone");
        var titled = Hls.ParseMedia(a.Replace("#EXTINF:2.000,live", "#EXTINF:2.000,Amazon|12345"));
        Check(titled.Segments.All(s => s.Ad), "segments titled Amazon are ads");
        var t3 = new SegmentTracker(3);
        t3.Next(titled);
        Check(t3.InAd && t3.Next(Hls.ParseMedia(a.Replace("#EXT-X-MEDIA-SEQUENCE:4154", "#EXT-X-MEDIA-SEQUENCE:4168"))).Count == 14 && !t3.InAd, "after the break every missed live segment plays");
        Check(Hls.ParseMedia(a + "#EXT-X-ENDLIST\n").Ended, "ENDLIST ends the stream");
    }

    static void ControllerTests()
    {
        var c = new Controller();
        Check(c.Tick(0, false) == Command.None && c.Mode == Mode.Idle && c.Status == "STANDBY", "nobody near: idle");
        Check(c.Tick(1, true) == Command.Start && c.Status == "CONNECTING" && c.Tick(2, true) == Command.None, "someone near: one start");
        c.OnPicture();
        Check(c.Mode == Mode.Playing && c.Status == null, "first picture: playing, no status text");
        c.OnAd(true); bool ad = c.Status == "AD BREAK"; c.OnAd(false);
        Check(ad && c.Mode == Mode.Playing, "ad break and back");
        Check(c.Tick(10, false) == Command.None && c.Tick(19.9, false) == Command.None && c.Mode == Mode.Playing, "walking away keeps it running for 10 s");
        Check(c.Tick(15, true) == Command.None && c.Tick(16, false) == Command.None && c.Tick(25.9, false) == Command.None, "coming back restarts the 10 s");
        Check(c.Tick(26, false) == Command.Stop && c.Mode == Mode.Idle && c.Tick(27, false) == Command.None, "then one stop");
        Check(c.Tick(30, true) == Command.Start, "back in range: start again");

        c.OnOffline(30);
        Check(c.Status == "OFFLINE" && c.Tick(89.9, true) == Command.None && c.Tick(90, true) == Command.Start, "offline: rechecked after 60 s");
        Check(c.Mode == Mode.Connecting && c.Status == "OFFLINE", "the recheck keeps OFFLINE on the screen");
        c.OnOffline(90);
        Check(c.Tick(95, false) == Command.None && c.Tick(105, false) == Command.None && c.Mode == Mode.Idle && c.Tick(106, true) == Command.Start,
            "offline and nobody near: idle without a stop, immediate start on return");

        c.OnFailed(106, null);
        Check(c.Status == "NO SIGNAL" && c.Tick(110.9, true) == Command.None && c.Tick(111, true) == Command.Start, "failure: retry after 5 s");
        Check(c.Mode == Mode.Connecting && c.Status == "NO SIGNAL", "the retry keeps NO SIGNAL on the screen");
        c.OnFailed(111, "NO FFMPEG");
        Check(c.Status == "NO FFMPEG" && c.Tick(120.9, true) == Command.None && c.Tick(121, true) == Command.Start, "second failure: 10 s, with its own text");
        for (int i = 0; i < 6; i++) { c.OnFailed(1000, null); c.Tick(2000 + i, true); }
        c.OnFailed(3000, null);
        Check(c.Tick(3059.9, true) == Command.None && c.Tick(3060, true) == Command.Start, "retry delay stops growing at 60 s");
        c.OnPicture(); c.OnFailed(4000, null);
        Check(c.Tick(4005, true) == Command.Start, "a picture resets the retry delay");
        Check(c.Status == "NO SIGNAL" && c.Tick(4010, false) == Command.None && c.Tick(4020, false) == Command.Stop && c.Tick(4021, true) == Command.Start && c.Status == "CONNECTING",
            "after standby a start says CONNECTING again");
        c.OnPicture(); c.OnEnded(5000);
        Check(c.Status == "OFFLINE" && c.Tick(5000, true) == Command.Start, "stream ended: looks again at once");

        var late = new Controller();
        late.Tick(0, true); late.Tick(1, false); late.Tick(11, false);
        late.OnPicture(); late.OnAd(true); late.OnOffline(12); late.OnFailed(12, null);
        Check(late.Mode == Mode.Idle, "reports from a stopped pipeline change nothing");
    }

    static void LayoutTests()
    {
        var s = Layout.Screen; var f = Layout.Frame; var open = Layout.Opening; var col = Layout.Collider;
        bool grid = new[] { s, f, open, col, Layout.Shadow, Layout.Plate(10), Layout.Plate(7) }.All(q => new[] { q.X, q.Y, q.W, q.H }.All(v => v * 32 == Math.Round(v * 32)));
        Check(grid, "every part lies on the art pixel grid");
        Check(Math.Abs(s.W / s.H - 16f / 9f) < 0.01f, "screen is 16:9 within 1%");
        Check(Layout.Snap(6.23f) == 6.21875f && Layout.Snap(-58.7f) == -58.6875f, "position snaps to 1/32");

        // the art against the layout
        var frame = Png.Read(Path.Combine(art, "frame.png")); var bezel = Png.Read(Path.Combine(art, "bezel.png"));
        Check(frame.W == Layout.FrameW && frame.H == Layout.FrameH && bezel.W == frame.W && bezel.H == frame.H, "frame.png and bezel.png have the layout's size");
        int px = Layout.PictureX, py = Layout.PictureTop, o = Layout.Overlap;
        bool ring = true, line = true, clear = true, backed = true;
        for (int y = py - 1; y <= py + Layout.ScreenH; y++)
            for (int x = px - 1; x <= px + Layout.ScreenW; x++)
            {
                bool inPicture = x >= px && x < px + Layout.ScreenW && y >= py && y < py + Layout.ScreenH;
                int edge = Math.Min(Math.Min(x - px, px + Layout.ScreenW - 1 - x), Math.Min(y - py, py + Layout.ScreenH - 1 - y));
                if (!inPicture) line &= bezel.A(x, y) == 255;
                else if (edge < o) ring &= bezel.A(x, y) == 255;
                else if (edge >= o + 2) clear &= bezel.A(x, y) == 0;
                backed &= frame.A(x, y) == 255;
            }
        Check(line, "bezel: a closed line around the picture, so nothing shows between frame and picture");
        Check(ring, "bezel: covers the picture's outer " + o + " px on every side");
        Check(clear, "bezel: clear inside, from 2 px beyond the overlap (corner chamfers)");
        Check(backed, "frame: opaque behind the whole picture and its outline");
        Check(open.X == s.X + o / 32f && open.Top == s.Top - o / 32f && open.W == s.W - 2 * o / 32f, "the opening is the picture less the overlap");
        Check(Enumerable.Range(0, frame.W).Any(x => frame.A(x, frame.H - 1) == 255) && f.Y == -Layout.SortLift / 32f, "the art stands on its lowest row, SortLift below the sort line");
        var plate = Png.Read(Path.Combine(art, "plate.png")); var font = Png.Read(Path.Combine(art, "font.png")); var shadow = Png.Read(Path.Combine(art, "shadow.png"));
        Check(plate.W == Layout.PlateWidth(Layout.MaxName) && plate.H == Layout.PlateH && font.W == Layout.Glyphs.Length * Layout.GlyphW && font.H == Layout.GlyphH,
            "plate.png fits the longest name, font.png holds every glyph");
        Check(shadow.W == Layout.ShadowW && shadow.H == Layout.ShadowH, "shadow.png has the layout's size");
        bool plateOnSkirt = true;
        for (int x = (Layout.FrameW - plate.W) / 2; x < (Layout.FrameW + plate.W) / 2; x++)
            for (int y = Layout.PlateTop - 1; y <= Layout.PlateTop + Layout.PlateH; y++) plateOnSkirt &= frame.A(x, y) == 255 && y > py + Layout.ScreenH && y < frame.H - 1;
        Check(plateOnSkirt, "the longest name plate lies on the base board, below the picture");
        Check(Layout.PlateText("dunkorslam") == "DUNKORSLAM" && Layout.PlateText("a_b9") == "A_B9" && Layout.PlateText("x-y z") == "XYZ" && Layout.PlateText(new string('a', 40)).Length == Layout.MaxName && Layout.PlateText(null) == "",
            "plate text: capitals, unknown characters dropped, cut to the plate");
        Check(Layout.PlateWidth(10) == 65 && Layout.Plate(10).X == -32 / 32f && Layout.Plate(10).Y == (Layout.FrameH - Layout.PlateTop - Layout.PlateH - Layout.SortLift) / 32f, "plate: 6 px per letter less the last gap, 3 px ends, centred");

        // at its place in town (positions as they are in game 0.69g)
        float X = Layout.DefaultX, Y = Layout.DefaultY;
        var wall = col.At(X, Y); var at = f.At(X, Y);
        // every bush centre near the hedge: the seven of the hedge, the three behind the sign, the shore
        var town = new[] { -63.5f, -62.5f, -61.5f, -60.5f, -59.5f, -58.5f, -57.5f }.Select(x => (x, 6.5f))
            .Concat(new[] { (-63.5f, 8.5f), (-63.5f, 9.5f), (-54.5f, 6.53f), (-68.5f, 6.5f), (-56.5f, 5.5f) }).ToList();
        Check(Layout.IsHedge(town), "the town has the hedge the billboard is made for");
        Check(!Layout.IsHedge(town.Where(b => b.Item1 != -60.5f)) && !Layout.IsHedge(town.Select(b => (b.Item1 + 1f, b.Item2))) && !Layout.IsHedge(town.Select(b => (b.Item1, b.Item2 - 0.5f)))
            && !Layout.IsHedge(new (float, float)[0]),
            "a hedge with a bush gone, moved along by one, or moved off its line is not that hedge");
        Check(new[] { -61.5f, -60.5f, -59.5f, -58.5f, -57.5f }.All(x => Layout.HidesDecor(X, Y, x, 6.5f)), "the five hedge bushes under the base are hidden");
        Check(!Layout.HidesDecor(X, Y, -62.5f, 6.5f) && !Layout.HidesDecor(X, Y, -57.5f, 5.5f) && !Layout.HidesDecor(X, Y, -59.47f, 10.5f) && !Layout.HidesDecor(X, Y, -54.5f, 6.53f),
            "the bush next to it, the plant boxes in front, the bush behind the screen and the one at the shore stay");
        Check(new[] { -61.76f, -57.24f }.All(x => wall.X <= x && wall.Right >= x) && wall.Y <= 6.25f && wall.Top >= 6.48f, "the collider covers the hidden bushes' colliders (x -61.76..-57.24, y 6.24..6.48 less a rounding pixel)");
        Check(wall.X - (-62.24f) < 0.47f, "no player-wide gap (0.47) to the collider of the bush left of it");
        Check(at.X >= -62f && at.Right <= -55f, "clear of the sign at x -63..-62; ends 1.2 units before the shore at x -53.8");
        float head = wall.Y - Layout.PlayerBodyTop + Layout.PlayerSpriteTop;
        Check(head <= open.At(X, Y).Y + 1 / 32f, "a player stopped at the base from the south reaches at most 1 px into the picture");
        var stuck = new Layout.Rect { X = -60.75f, Y = 6.2f, W = 0.47f, H = 0.3f }; var free = new Layout.Rect { X = -60.75f, Y = 5.69f, W = 0.47f, H = 0.3f };
        Check(wall.Overlaps(stuck) && !wall.Overlaps(free) && !wall.Overlaps(new Layout.Rect { X = -62.3f, Y = 6.3f, W = 0.47f, H = 0.3f }), "overlap test: a player inside the base, in front of it, left of it");
        Check(Layout.OrderForY(wall.Y - Layout.PlayerBodyTop) > Layout.OrderForY(Y) && Layout.OrderForY(wall.Top + 0.05f) < Layout.OrderForY(Y), "a player stopped in front sorts over the frame, one behind the base under it");
        var lamp = Layout.Lantern(false);
        bool bulbs = new[] { Layout.LanternX, Layout.FrameW - Layout.LanternX }.All(x => frame.Rgb(x - 1, Layout.LanternTop) == 0xa4afb6 && frame.Rgb(x, Layout.LanternTop) == 0xa4afb6 && frame.Rgb(x - 3, Layout.LanternTop) == 0 && frame.Rgb(x + 2, Layout.LanternTop) == 0);
        Check(bulbs && lamp.x == -Layout.Lantern(true).x && lamp.x * 32 == Layout.LanternX - Layout.FrameW / 2 && lamp.y * 32 == Layout.FrameH - Layout.LanternTop - Layout.SortLift,
            "the lit lantern sprites sit on the bulbs drawn on the posts (glass 4 px wide between outlines)");

        Layout.DisplaySize(1080, out int w, out int h); Layout.DisplaySize(1440, out int w2, out int h2); Layout.DisplaySize(200, out int w3, out int h3);
        Check(w == 552 && h == 312 && w2 == 736 && h2 == 416 && w3 == 184 && h3 == 104, "picture size in window pixels: 3x at 1080p, 4x at 1440p, never below the art size");

        // against the game's own sorter
        var managed = (string)AppContext.GetData("ShorelandsBillboard.Tests.Managed");
        AssemblyLoadContext.Default.Resolving += (ctx, name) => { var p = Path.Combine(managed, name.Name + ".dll"); return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null; };
        var sorter = Assembly.LoadFrom(Path.Combine(managed, "Game.dll")).GetType("ReworkedCopium.YPositionSorter").GetMethod("OrderForY");
        bool same = true;
        for (float y = -20; y <= 20; y += 0.03125f) same &= (int)sorter.Invoke(null, new object[] { y }) == Layout.OrderForY(y);
        Check(same, "sorting order equals YPositionSorter.OrderForY for every grid position in town");
    }

    static void SoundTests()
    {
        Check(Sound.Falloff(0, 5, 18) == 1 && Sound.Falloff(5, 5, 18) == 1 && Sound.Falloff(18, 5, 18) == 0 && Sound.Falloff(40, 5, 18) == 0, "falloff: full inside near, silent from far on");
        bool falling = true;
        for (float d = 5; d < 18; d += 0.5f) falling &= Sound.Falloff(d + 0.5f, 5, 18) < Sound.Falloff(d, 5, 18);
        Check(falling && Math.Abs(Sound.Falloff(11.5f, 5, 18) - 0.25f) < 1e-6, "falloff: strictly falling, a quarter at half way");
        Check(Sound.Falloff(3, 5, 5) == 1 && Sound.Falloff(6, 5, 5) == 0 && Sound.Falloff(6, 5, 2) == 0, "falloff: a range inside the near distance has no fade");
        Check(Sound.Pan(0, 18) == 0 && Sound.Pan(100, 18) == 0.6f && Sound.Pan(-100, 18) == -0.6f && Sound.Pan(3, 18) > 0 && Sound.Pan(3, 0) == 0, "pan follows the side the screen is on, capped at 0.6");
        Sound.Gains(0.6f, 0, out float gl, out float gr); Sound.Gains(0.6f, 0.5f, out float pl, out float pr); Sound.Gains(0.6f, -0.5f, out float ql, out float qr);
        Check(gl == 0.6f && gr == 0.6f && pl == 0.3f && pr == 0.6f && ql == 0.6f && qr == 0.3f, "gains: the pan takes from the far channel only");

        var ring = new SampleRing(8);
        ring.Write(new float[] { 1, 2, 3, 4, 5, 6 }, 6);
        var o = new float[4];
        Check(ring.Read(o) == 4 && o.SequenceEqual(new float[] { 1, 2, 3, 4 }) && ring.ReadPosition == 4 && ring.Written == 6, "ring: first in, first out, positions count from the stream start");
        ring.Write(new float[] { 7, 8, 9, 10 }, 4);
        var o2 = new float[8];
        Check(ring.Read(o2) == 6 && o2.SequenceEqual(new float[] { 5, 6, 7, 8, 9, 10, 0, 0 }), "ring: wraps, and a short read ends in silence");
        ring.Write(new float[] { 1, 2, 3, 4, 5, 6 }, 6); ring.Write(new float[] { 7, 8, 9, 10 }, 4);
        var o3 = new float[8];
        Check(ring.ReadPosition == 12 && ring.Read(o3) == 8 && o3.SequenceEqual(new float[] { 3, 4, 5, 6, 7, 8, 9, 10 }), "ring: overflow drops the oldest stereo pair, left stays left");
        ring.Write(Enumerable.Range(1, 12).Select(i => (float)i).ToArray(), 12);
        Check(ring.Read(o3) == 8 && o3[0] == 5 && o3[7] == 12 && ring.ReadPosition == 32, "ring: a write larger than the ring keeps its newest part");

        // a pull pattern with bursts: 0.34 s at a time, speakers 0.1 s behind the last burst's start
        var clock = new AudioClock();
        double worst = 0, read = 0;
        for (double t = 0; t < 10; t += 1 / 120.0)
        {
            double playing = t;                       // stream position at the speakers
            while (read < playing + 0.1) read += 0.34;
            double p = clock.Position(read, t + 50);  // device clock has its own origin
            if (t > 1) worst = Math.Max(worst, Math.Abs(p - playing - 0.1));
        }
        Check(worst < 0.01, "audio clock: bursty reads give a steady position (the emptiest moment of the engine's buffer)");
        double stalledMax = 0, stalledEnd = 0;
        for (double t = 60; t < 62; t += 1 / 120.0) { stalledEnd = clock.Position(read, t); stalledMax = Math.Max(stalledMax, stalledEnd); }
        Check(stalledMax <= read + 1e-9 && Math.Abs(stalledEnd - read) < 1e-9, "audio clock: when the sound starves, the position runs up to what was read and stops there");
        double jumped = 0, last = 0;
        for (double t = 62; t < 65; t += 1 / 120.0) { last = read + 3 + (t - 62); jumped = clock.Position(last, t); }
        Check(Math.Abs(jumped - last) < 1e-9, "audio clock: after a forward jump (dropped sound) the position follows within the window");
    }

    // 1000 frames a second, so a frame is 1000 microseconds; a block's samples count up from `first`
    static float[] Block(int first, int frames)
    {
        var b = new float[frames * 2];
        for (int i = 0; i < frames; i++) b[2 * i] = b[2 * i + 1] = first + i;
        return b;
    }

    static float[] Take(DatedSound s, int frames, long now)
    {
        var o = new float[frames * 2]; s.Read(o, now);
        return Enumerable.Range(0, frames).Select(i => o[2 * i]).ToArray();
    }

    static void DatedSoundTests()
    {
        const long D = 5_000_000;
        var s = new DatedSound(1000, 8);
        s.Write(Block(1, 100), 100, D);
        Check(Take(s, 20, D - 100_000).All(v => v == 0) && s.DateAt(0) == null && s.HandedOut == 20, "dated sound: silence until the first sample is due, counted as handed out");
        Check(Take(s, 20, D).SequenceEqual(Block(1, 20).Where((v, i) => i % 2 == 0)) && s.DateAt(20) == D && s.DateAt(30) == D + 10_000, "then the samples, and their position has their date");

        s.Write(Block(101, 100), 100, D + 400_000);   // 0.3 s missing in the stream
        Check(Take(s, 80, D + 100_000).SequenceEqual(Block(21, 80).Where((v, i) => i % 2 == 0)), "what came before a gap plays out");
        Check(Take(s, 50, D + 150_000).All(v => v == 0) && Take(s, 50, D + 400_000)[0] == 101, "after a jump in the dates the next samples wait until they are due");
        Check(s.DateAt(170) == D + 400_000 && s.DateAt(169) == D + 149_000, "positions in the silence keep counting from the sound before it");

        var creep = new DatedSound(1000, 8);
        for (int i = 0; i < 30; i++) creep.Write(Block(1 + i * 100, 100), 100, D + i * 100_040L);   // dates 0.04% faster than the samples
        var all = Take(creep, 2500, D);
        long at2400 = creep.DateAt(2400).Value, truth = D + 24 * 100_040L;
        Check(all.SequenceEqual(Enumerable.Range(1, 2500).Select(i => (float)i)) && Math.Abs(at2400 - truth) <= 400, "dates that creep against the sample count: no silence, and the date stays within the last second's creep");

        var dry = new DatedSound(1000, 8);
        dry.Write(Block(1, 100), 100, D);
        var first = Take(dry, 150, D);
        dry.Write(Block(101, 100), 100, D + 100_000);
        Check(first[99] == 100 && first[100] == 0 && Take(dry, 100, D + 200_000)[0] == 101 && dry.DateAt(150) == D + 100_000, "sound that ran dry and went on later: the date follows the samples, not the count");

        var late = new DatedSound(1000, 8);
        late.Write(Block(1, 1000), 1000, D);
        Check(Take(late, 10, D + 700_000)[0] == 701 && late.DateAt(0) == D + 700_000, "sound found more than half a second late skips to what is due now");
        var bit = new DatedSound(1000, 8);
        bit.Write(Block(1, 1000), 1000, D);
        Check(Take(bit, 10, D + 200_000)[0] == 1 && bit.DateAt(0) == D, "less late than that, it plays from its start and the picture waits for it");

        s.Clear();
        s.Write(Block(1, 100), 100, D + 2_000_000);
        Check(Take(s, 10, D + 1_000_000).All(v => v == 0) && Take(s, 10, D + 2_000_000)[0] == 1, "after a flush the new sound waits for its date again");
    }

    static void FfmpegTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbtest-" + Guid.NewGuid().ToString("N"));
        string Make(params string[] parts) { var p = Path.Combine(new[] { root }.Concat(parts).ToArray()); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, ""); return p; }
        try
        {
            string own = Make("own", "my-ffmpeg.exe"), inPath = Make("bin2", "ffmpeg.exe"), plugin = Make("plugin", "ffmpeg.exe");
            string pkg = Make("local", "Microsoft", "WinGet", "Packages", "Gyan.FFmpeg_x", "ffmpeg-9.1-full_build", "bin", "ffmpeg.exe");
            string path = Path.Combine(root, "bin1") + ";\"" + Path.Combine(root, "bin2") + "\";;";
            string local = Path.Combine(root, "local"), pluginDir = Path.Combine(root, "plugin");
            Check(Ffmpeg.Find(own, path, local, pluginDir) == own, "ffmpeg: the configured file wins");
            Check(Ffmpeg.Find(Path.Combine(root, "missing.exe"), path, local, pluginDir) == null, "ffmpeg: a configured file that does not exist is not replaced by another");
            Check(Ffmpeg.Find("", path, local, pluginDir) == inPath, "ffmpeg: else the first PATH folder that has it (quoted entries too)");
            Check(Ffmpeg.Find("", Path.Combine(root, "bin1"), local, pluginDir) == pkg, "ffmpeg: else winget's package folder, whatever the version");
            string link = Make("local", "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            Check(Ffmpeg.Find("", null, local, pluginDir) == link, "ffmpeg: winget's shim before its package folder");
            Check(Ffmpeg.Find("", null, Path.Combine(root, "nolocal"), pluginDir) == plugin && Ffmpeg.Find("", null, null, null) == null, "ffmpeg: else the plugin folder, else none");
        }
        finally { Directory.Delete(root, true); }
    }
}
