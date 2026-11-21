using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using ReworkedCopium.Player;
using ShorelandsBillboard.Core;
using UnityEngine;

namespace ShorelandsBillboard
{
    // A billboard on the hedge line north of the town pond that shows a Twitch channel's live stream. Client only: scene
    // objects, libVLC in the process or an ffmpeg child process for decoding, Twitch's public playback endpoints.
    [BepInPlugin("com.zerfl.shorelands.billboard", "ShorelandsBillboard", BuildInfo.Version)]
    public class BillboardPlugin : BaseUnityPlugin
    {
        static readonly Vector2 Place = new Vector2(Layout.DefaultX, Layout.DefaultY);
        const float Near = 5f;
        const int TownTries = 10, MaxFailures = 3, StreamLogLines = 20;
        const double NoPictureAfter = 20;

        ConfigEntry<bool> enabledCfg, mute, sharp;
        ConfigEntry<string> channel, ffmpegPath, quality, backend;
        ConfigEntry<float> volume, range, offsetX, offsetY;

        World world; Stream stream; Controller controller = new Controller();
        AudioClip clip; volatile Decoder audioFrom;
        GameObject listener;
        bool playing, vlcUsed, vlcFailed; double clockStart, lastFrameAt, buildRetryAt, shownTime, soundTime; long shown;
        float distance, appliedVolume, appliedPan; volatile float gainLeft, gainRight; float[] stereo = new float[0]; string decoderUsed, lastStatus;
        Collider2D body;
        readonly AudioClock audioClock = new AudioClock(); long lastPull; bool bySound;
        double listenerCheckAt, startedAt, builtAt, configCheckAt, sharpCheckAt; int builtForHeight;
        // fail safe: what went wrong is logged once, then the billboard stays off
        bool dead, townMissing, audioChanged, cameraHookOff; int failures, townTries, streamLogLines;
        string lastOutcome; volatile bool soundHeard; bool soundReported;
        DateTime configSeen;

        void Awake()
        {
            enabledCfg = Config.Bind("Billboard", "Enabled", true, "Show the billboard in town.");
            channel = Config.Bind("Billboard", "Channel", "dunkorslam", "The Twitch channel to show: the name from its address, twitch.tv/NAME (letters, digits, underscore).");
            quality = Config.Bind("Billboard", "Quality", "auto", new ConfigDescription("Stream quality. auto: just enough for the billboard's size on your screen (360p on a 1080p screen). Or a picture height; higher costs more bandwidth and CPU and rarely looks better at this size.", new AcceptableValueList<string>("auto", "160", "360", "480", "720")));
            sharp = Config.Bind("Billboard", "SharpPicture", true, "true: the picture is drawn at your screen's resolution, so text in the stream can be read. false: the picture is drawn in the game's pixel-art resolution (184x104 pixels).");
            volume = Config.Bind("Sound", "Volume", 0.6f, new ConfigDescription("Volume of the stream's sound when you stand at the billboard. The game's master volume applies on top.", new AcceptableValueRange<float>(0f, 1f)));
            mute = Config.Bind("Sound", "Mute", false, "true: no stream sound.");
            range = Config.Bind("Sound", "Range", 18f, new ConfigDescription("Distance from the billboard at which its sound has faded to silence, in world units (a hedge bush is 1 unit wide). Full volume within 5 units. Further away than this the stream is paused.", new AcceptableValueRange<float>(6f, 40f)));
            offsetX = Config.Bind("Position", "OffsetX", 0f, new ConfigDescription("Moves the billboard east of its place at the town pond, in world units (negative: west). Only hedge bushes are cleared from under it, so pick a free spot.", new AcceptableValueRange<float>(-40f, 40f)));
            offsetY = Config.Bind("Position", "OffsetY", 0f, new ConfigDescription("Moves the billboard north, in world units (negative: south).", new AcceptableValueRange<float>(-40f, 40f)));
            backend = Config.Bind("Decoder", "Backend", "auto", new ConfigDescription("What decodes the video. auto: the libVLC that comes with this mod, or ffmpeg if libVLC cannot be used. vlc: only libVLC. ffmpeg: only an ffmpeg.exe on this PC.", new AcceptableValueList<string>("auto", "vlc", "ffmpeg")));
            ffmpegPath = Config.Bind("Decoder", "FfmpegPath", "", "Full path of the ffmpeg.exe to use. Empty: the one found in PATH, in winget's install folders or in this mod's folder.");
            Config.SettingChanged += (s, e) => { if (e.ChangedSetting.Definition.Section != "Sound") Teardown(); };   // sound settings are read every frame
            configSeen = ConfigTime();
            Application.onBeforeRender += KeepWorldUiCamera;
            AudioSettings.OnAudioConfigurationChanged += OnAudioChanged;
            Logger.LogInfo($"version {BuildInfo.Version}, channel twitch.tv/{channel.Value}");
        }

        void OnDestroy()
        {
            Application.onBeforeRender -= KeepWorldUiCamera;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioChanged;
            Teardown();
        }

        void OnApplicationQuit() { Teardown(); if (vlcUsed) WaitForVlc(); }

        // The engine drops every playing source when the sound device changes.
        void OnAudioChanged(bool deviceWasChanged) => audioChanged = true;

        void Update()
        {
            if (dead) return;
            try { Step(); }
            catch (Exception e) { Fail(e); }
        }

        // A game update may have changed what the billboard builds on. Never an error per frame: a retry or two,
        // then off until the game restarts.
        void Fail(Exception e)
        {
            try { Teardown(); } catch (Exception) { world = null; stream = null; }
            bool gameChanged = e is TypeLoadException || e is MissingMemberException;
            if (gameChanged || ++failures >= MaxFailures)
            {
                dead = true;
                Logger.LogError($"the billboard is switched off until the game restarts{(gameChanged ? $" (it does not fit game version {Application.version})" : "")}: {e}");
                return;
            }
            buildRetryAt = Time.realtimeSinceStartupAsDouble + 10 * failures;
            Logger.LogWarning($"billboard failed, trying again in {10 * failures} s: {e}");
        }

        void Step()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            WatchConfig(now);
            var player = PlayerModel.LocalPlayer;
            if (!enabledCfg.Value || player == null)
            {
                if (world != null || stream != null) Teardown();
                townMissing = false; townTries = 0;   // looked for again at the next login
                return;
            }
            // the scene went away under it; the sound device changed; the sharp picture is decoded at the window's size
            if (world != null && (world.Root == null || audioChanged || world.Sharp && Screen.height != builtForHeight)) Teardown();
            audioChanged = false;
            // sharp is possible or not as the window's shape changes; the cameras settle a moment after a resize
            if (world != null && sharp.Value && now >= sharpCheckAt)
            {
                sharpCheckAt = now + 1;
                if (World.FindUiCamera(out _) != null != world.Sharp) { Teardown(); buildRetryAt = now + 0.5; }
            }
            if (world == null)
            {
                if (townMissing || now < buildRetryAt) return;
                if (!Build(now)) return;
            }
            if (failures > 0 && now - builtAt > 60) failures = 0;
            EnsureListener(now);
            if (!world.Blocking)
            {
                if (body == null) body = player.GetComponents<Collider2D>().FirstOrDefault(c => !c.isTrigger);
                if (body != null) world.Stand(body.bounds);
            }

            var toScreen = world.ScreenCentre - (Vector2)player.transform.position;
            distance = toScreen.magnitude;
            switch (controller.Tick(now, distance < range.Value + 2f))
            {
                case Command.Start: StartStream(now); break;
                case Command.Stop: StopStream(); break;
            }
            Drain(now);
            Present(now);

            // the engine does not apply the listener volume (the game's master volume) to a filter's output
            appliedVolume = mute.Value || !playing ? 0f : volume.Value * Sound.Falloff(distance, Near, range.Value) * Mathf.Clamp01(AudioListener.volume);
            appliedPan = Sound.Pan(toScreen.x, range.Value);
            Sound.Gains(appliedVolume, appliedPan, out float l, out float r);
            gainLeft = l; gainRight = r;
            if (soundHeard && !soundReported) { soundReported = true; Logger.LogInfo("sound is playing"); }

            string status = controller.Status ?? (now - lastFrameAt < 1.5 ? null : "BUFFERING");
            world.SetStatus(status, channel.Value);
            if (status != lastStatus) { Logger.LogInfo("screen: " + (status ?? "picture")); lastStatus = status; }
        }

        bool Build(double now)
        {
            var town = Town.Find();
            if (town == null)
            {
                // the scene may still be loading right after the login
                if (++townTries < TownTries) { buildRetryAt = now + 2; return false; }
                townMissing = true;
                Logger.LogWarning($"the hedge north of the town pond is not where this version of the mod knows it (game version {Application.version}); the billboard stays off");
                return false;
            }
            townTries = 0;
            world = new World(town, Place + new Vector2(offsetX.Value, offsetY.Value), sharp.Value, channel.Value);
            builtForHeight = Screen.height; builtAt = now; sharpCheckAt = now + 1;
            // a silent clip keeps the source playing; the sound itself goes in through the tap
            int rate = AudioSettings.outputSampleRate;
            if (rate > 0)
            {
                clip = AudioClip.Create("billboard", rate, 2, rate, false);
                world.Audio.clip = clip; world.Audio.volume = 1; world.Audio.Play();
                world.Audio.gameObject.AddComponent<SoundTap>().Fill = FillSound;
            }
            else world.Missing.Add("sound (no sound output)");
            Logger.LogInfo($"built at {world.Root.transform.position.x:F2}, {world.Root.transform.position.y:F2}" + (world.Missing.Count > 0 ? ", without " + string.Join(", ", world.Missing) : ""));
            return true;
        }

        // The cfg file is read again when it was saved, so a change made in an editor applies while the game runs.
        void WatchConfig(double now)
        {
            if (now < configCheckAt) return;
            configCheckAt = now + 2;
            var t = ConfigTime();
            if (t == configSeen) return;
            try { Config.Reload(); } catch (IOException) { return; }   // still being written: next time
            configSeen = ConfigTime();
        }

        DateTime ConfigTime()
        {
            try { return File.GetLastWriteTimeUtc(Config.ConfigFilePath); } catch (Exception) { return configSeen; }
        }

        // A stream outcome is logged when it changes, not at every retry.
        void Report(string outcome, bool warning)
        {
            if (outcome == lastOutcome) return;
            lastOutcome = outcome;
            if (warning) Logger.LogWarning(outcome); else Logger.LogInfo(outcome);
        }

        void StartStream(double now)
        {
            string name = channel.Value.Trim().ToLowerInvariant();
            if (!Twitch.ValidChannel(name)) { controller.OnFailed(now, "BAD CHANNEL"); Report($"'{channel.Value}' is not a Twitch channel name; set Billboard.Channel to the name from twitch.tv/NAME", true); return; }
            int w = Layout.ScreenW, h = Layout.ScreenH;
            if (world.Sharp) Layout.DisplaySize(Screen.height, out w, out h);
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            string dir = Path.GetDirectoryName(Info.Location);
            bool installed = Backend.VlcInstalled(dir);
            Decoder decoder;
            if (Backend.UseVlc(backend.Value, installed && !vlcFailed))
            {
                if (!installed)
                {
                    controller.OnFailed(now, "NO VLC");
                    Report("Decoder.Backend is vlc, but LibVLCSharp.dll and the libvlc folder are not in the mod's folder; install the mod again from its zip", true);
                    return;
                }
                decoder = NewVlc(dir, w, h, rate);
            }
            else
            {
                string exe = Ffmpeg.Find(ffmpegPath.Value, Environment.GetEnvironmentVariable("PATH"),
                    Environment.GetEnvironmentVariable("LOCALAPPDATA"), dir);
                if (exe == null)
                {
                    bool auto = backend.Value == "auto";
                    controller.OnFailed(now, auto ? "NO DECODER" : "NO FFMPEG");
                    Report(!string.IsNullOrWhiteSpace(ffmpegPath.Value) ? "Decoder.FfmpegPath does not exist: " + ffmpegPath.Value
                        : auto && !installed ? "no video decoder: LibVLCSharp.dll and the libvlc folder are not in the mod's folder (install the mod again from its zip), and no ffmpeg.exe was found either"
                        : auto ? "no video decoder: libVLC did not start and no ffmpeg.exe was found (winget install Gyan.FFmpeg, or set Decoder.FfmpegPath)"
                        : "Decoder.Backend is ffmpeg, but no ffmpeg.exe was found in PATH, winget's folders or the mod's folder (winget install Gyan.FFmpeg, or set Decoder.FfmpegPath)", true);
                    return;
                }
                decoder = new FfmpegDecoder(exe, w, h, rate);
            }
            if (decoder.Name != decoderUsed) { decoderUsed = decoder.Name; Logger.LogInfo("decoder: " + decoderUsed); }
            int.TryParse(quality.Value, out int minHeight);
            world.SetPictureSize(w, h, world.Sharp, decoder.Rgba, decoder.TopDown);
            stream = new Stream(name, minHeight, decoder);
            stream.Start();
            playing = false; startedAt = now; streamLogLines = 0; soundHeard = false; soundReported = false;
        }

        // LibVLCSharp types only in methods that run when it is installed: the JIT resolves a method's types at once.
        [MethodImpl(MethodImplOptions.NoInlining)]
        Decoder NewVlc(string dir, int w, int h, int rate)
        {
            vlcUsed = true;
            return new VlcDecoder(Backend.VlcDir(dir), w, h, rate);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void WaitForVlc() => VlcDecoder.WaitClosed(3000);

        void StopStream()
        {
            audioFrom = null; playing = false;
            if (stream != null) { stream.Stop(); stream = null; }
        }

        void Drain(double now)
        {
            if (stream == null) return;
            while (stream != null && stream.Events.TryDequeue(out var e))
            {
                switch (e.kind)
                {
                    case StreamEvent.Log:
                        // ffmpeg and a bad connection can say the same thing every second
                        if (++streamLogLines <= StreamLogLines) Logger.LogInfo(e.text);
                        else if (streamLogLines == StreamLogLines + 1) Logger.LogInfo("further stream messages are left out until the next connection");
                        break;
                    case StreamEvent.Ad: controller.OnAd(true); Logger.LogInfo("ad break: the stream's ads are not shown"); break;
                    case StreamEvent.AdOver: controller.OnAd(false); break;
                    case StreamEvent.Offline: Report($"{channel.Value} is offline ({e.text}); checking every {Controller.OfflineRecheck} s", false); controller.OnOffline(now); StopStream(); break;
                    case StreamEvent.Ended: Logger.LogInfo("stream stopped: " + e.text); controller.OnEnded(now); StopStream(); break;
                    case StreamEvent.Failed: Report("stream failed, retrying: " + e.text, true); controller.OnFailed(now, null); StopStream(); break;
                    case StreamEvent.NoDecoder:
                        Report("decoder did not start: " + e.text, true);
                        if (stream.Decoder.Rgba && backend.Value == "auto") { vlcFailed = true; Logger.LogWarning("libVLC is left out until the game restarts; using ffmpeg"); }
                        controller.OnFailed(now, null); StopStream(); break;
                }
            }
        }

        void Present(double now)
        {
            if (stream == null) return;
            var d = stream.Decoder;
            if (!playing)
            {
                if (d.Buffered < d.StartFrames)
                {
                    // connected (or not) but nothing to show: not CONNECTING for ever
                    if (now - startedAt > NoPictureAfter && controller.Mode == Mode.Connecting)
                    {
                        Report($"no picture {NoPictureAfter} s after connecting, retrying", true);
                        controller.OnFailed(now, null); StopStream();
                    }
                    return;
                }
                playing = true; clockStart = now; shown = 0; shownTime = 0; lastFrameAt = now;
                audioClock.Reset(); audioFrom = d;
                controller.OnPicture();
                lastOutcome = null;
                Logger.LogInfo($"first picture {now - startedAt:F2} s after the start");
            }
            // The picture follows the sound while the engine pulls it; else the decoder's own pace or the wall clock.
            bySound = System.Diagnostics.Stopwatch.GetTimestamp() - System.Threading.Interlocked.Read(ref lastPull) < System.Diagnostics.Stopwatch.Frequency / 2;
            double t = bySound ? d.TimeOfSound(audioClock.Position(d.SoundSeconds, AudioSettings.dspTime)) : double.NaN;
            bySound &= !double.IsNaN(t);
            if (!bySound) t = d.Now;
            bool wall = double.IsNaN(t);
            if (wall) t = now - clockStart;
            clockStart = now - t; soundTime = t;
            var f = d.Take(t);
            if (f != null)
            {
                world.Show(f.Data); shownTime = f.Time; shown++; lastFrameAt = now;
                d.Recycle(f);
                return;
            }
            if (wall && d.Buffered == 0) clockStart = now - shownTime;   // starved: no catching up later
            if (controller.Mode == Mode.Playing && now - lastFrameAt > NoPictureAfter)
            {
                Report($"no picture for {NoPictureAfter} s, reconnecting", true);
                controller.OnEnded(now); StopStream();
            }
        }

        // The engine's mixer thread, for every block it is about to play. Volume and pan are applied here:
        // what the source's own do to a filter's output is the engine's business.
        void FillSound(float[] data, int channels)
        {
            System.Threading.Interlocked.Exchange(ref lastPull, System.Diagnostics.Stopwatch.GetTimestamp());
            var d = audioFrom;
            if (d == null) { Array.Clear(data, 0, data.Length); return; }
            float l = gainLeft, r = gainRight;
            if (channels == 2)
            {
                d.ReadSound(data);
                if (!soundHeard && (l > 0 || r > 0) && data.Length > 0 && (data[0] != 0 || data[data.Length / 2] != 0)) soundHeard = true;
                for (int i = 0; i < data.Length; i += 2) { data[i] *= l; data[i + 1] *= r; }
                return;
            }
            int frames = data.Length / channels;
            if (stereo.Length != frames * 2) stereo = new float[frames * 2];
            d.ReadSound(stereo);
            if (!soundHeard && (l > 0 || r > 0) && frames > 0 && (stereo[0] != 0 || stereo[frames] != 0)) soundHeard = true;
            Array.Clear(data, 0, data.Length);
            for (int i = 0; i < frames; i++)
            {
                if (channels == 1) data[i] = (stereo[2 * i] * l + stereo[2 * i + 1] * r) / 2;
                else { data[i * channels] = stereo[2 * i] * l; data[i * channels + 1] = stereo[2 * i + 1] * r; }
            }
        }

        sealed class SoundTap : MonoBehaviour
        {
            public Action<float[], int> Fill;
            void OnAudioFilterRead(float[] data, int channels) => Fill?.Invoke(data, channels);
        }

        // The game ships no AudioListener (it has no sound yet); without one nothing is audible. Should the
        // game bring its own, this one goes.
        void EnsureListener(double now)
        {
            if (now < listenerCheckAt) return;
            listenerCheckAt = now + 5;
            int listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
            if (listener == null && listeners == 0)
            {
                listener = new GameObject("BillboardListener");
                listener.transform.SetParent(world.Root.transform, false);
                listener.AddComponent<AudioListener>();
            }
            else if (listener != null && listeners > 1) { Destroy(listener); listener = null; }
        }

        // The sharp picture needs the game's world UI camera this frame, whatever switched it off.
        void KeepWorldUiCamera()
        {
            if (cameraHookOff || world == null || !world.Sharp) return;
            try
            {
                var worldUi = world.UiCamera; var main = Camera.main;
                if (worldUi == null || main == null) return;
                bool mainDrawsIt = (main.cullingMask & worldUi.cullingMask) != 0;
                if (!mainDrawsIt && !worldUi.enabled) worldUi.enabled = true;
            }
            catch (Exception e) { cameraHookOff = true; Logger.LogWarning("world UI camera check switched off: " + e.Message); }
        }

        void Teardown()
        {
            StopStream();
            if (world != null) { var w = world; world = null; listener = null; listenerCheckAt = 0; body = null; w.Destroy(); }
            if (clip != null) { Destroy(clip); clip = null; }
            controller = new Controller(); lastStatus = null; lastOutcome = null;
        }
    }
}
