using System;

namespace ShorelandsBillboard.Core
{
    public enum Mode
    {
        Idle,
        Connecting,
        Playing,
        AdBreak,
        Offline,
        Failed,
    }

    public enum Command
    {
        None,
        Start,
        Stop,
    }

    // Decides when the stream pipeline runs. The plugin feeds it time, whether anyone can see or hear the
    // billboard, and what the pipeline reported; it answers with Start/Stop and the text for the screen.
    public sealed class Controller
    {
        public const double IdleDelay = 10,
            OfflineRecheck = 60,
            FirstRetry = 5,
            MaxRetry = 60;

        public Mode Mode { get; private set; } = Mode.Idle;
        public string Detail { get; private set; }
        bool running;
        string held; // held: what the screen said before a retry, kept until the retry has an answer
        double unwantedSince = -1,
            retryAt,
            retryDelay = FirstRetry;

        public string Status
        {
            get
            {
                switch (Mode)
                {
                    case Mode.Connecting:
                        return held ?? "CONNECTING";
                    case Mode.AdBreak:
                        return "AD BREAK";
                    case Mode.Offline:
                        return "OFFLINE";
                    case Mode.Failed:
                        return Detail ?? "NO SIGNAL";
                    case Mode.Idle:
                        return "STANDBY";
                    default:
                        return null;
                }
            }
        }

        public Command Tick(double now, bool wanted)
        {
            if (!wanted)
            {
                if (unwantedSince < 0)
                    unwantedSince = now;
                if (Mode != Mode.Idle && now - unwantedSince >= IdleDelay)
                {
                    Mode = Mode.Idle;
                    if (running)
                    {
                        running = false;
                        return Command.Stop;
                    }
                }
                return Command.None;
            }
            unwantedSince = -1;
            if (running)
                return Command.None;
            if ((Mode == Mode.Offline || Mode == Mode.Failed) && now < retryAt)
                return Command.None;
            held = Mode == Mode.Offline || Mode == Mode.Failed ? Status : null;
            Mode = Mode.Connecting;
            running = true;
            return Command.Start;
        }

        public void OnPicture()
        {
            if (running)
            {
                Mode = Mode.Playing;
                retryDelay = FirstRetry;
            }
        }

        public void OnAd(bool ad)
        {
            if (!running || Mode == Mode.Connecting && !ad)
                return;
            Mode = ad ? Mode.AdBreak : Mode.Playing;
        }

        public void OnOffline(double now)
        {
            if (running)
            {
                running = false;
                Mode = Mode.Offline;
                retryAt = now + OfflineRecheck;
            }
        }

        // detail: a short text for the screen, or null for "NO SIGNAL"
        public void OnFailed(double now, string detail)
        {
            if (!running)
                return;
            running = false;
            Mode = Mode.Failed;
            Detail = detail;
            retryAt = now + retryDelay;
            retryDelay = Math.Min(retryDelay * 2, MaxRetry);
        }

        // the stream ended or the playlist stopped answering: look again right away
        public void OnEnded(double now)
        {
            if (running)
            {
                running = false;
                Mode = Mode.Offline;
                retryAt = now;
            }
        }
    }
}
