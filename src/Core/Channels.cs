using System;
using System.Collections.Generic;

namespace ShorelandsBillboard.Core
{
    public enum AddResult
    {
        Added,
        Invalid,
        Duplicate,
        Full,
    }

    // The channel list of the remote. In the settings file it is one comma-separated value; the first entry
    // is the default, the channel the billboard starts on.
    public static class Channels
    {
        public const int Max = 20;
        public const string Shipped = "dunkorslam";

        // What someone types or pastes ("Name", "@name", "https://www.twitch.tv/name?x=1") as a channel
        // name, or null.
        public static string Clean(string typed)
        {
            string t = (typed ?? "").Trim();
            int at = t.IndexOf("twitch.tv/", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
                t = t.Substring(at + "twitch.tv/".Length);
            int end = t.IndexOfAny(new[] { '/', '?', '#' });
            if (end >= 0)
                t = t.Substring(0, end);
            t = t.TrimStart('@').ToLowerInvariant();
            return Twitch.ValidChannel(t) ? t : null;
        }

        // Entries that are not channel names and repeats are dropped; no more than Max.
        public static List<string> Parse(string text)
        {
            var list = new List<string>();
            foreach (string part in (text ?? "").Split(',', ';'))
            {
                string name = Clean(part);
                if (name != null && !list.Contains(name) && list.Count < Max)
                    list.Add(name);
            }
            return list;
        }

        public static string Format(IList<string> list) => string.Join(", ", list);

        // Whether a settings file already has the list (version 1.0.0 wrote only Channel).
        public static bool HasListKey(string cfgText)
        {
            bool inSection = false;
            foreach (string raw in (cfgText ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("["))
                    inSection = line.StartsWith("[Billboard]", StringComparison.OrdinalIgnoreCase);
                else if (inSection && !line.StartsWith("#"))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && line.Substring(0, eq).Trim() == "Channels")
                        return true;
                }
            }
            return false;
        }

        // At game start. A file from 1.0.0 keeps its channel as the default, with the shipped one after it.
        // Otherwise the list decides, and the billboard starts on its first entry.
        public static List<string> Startup(string cfgText, string listValue, string channelValue, out string current)
        {
            string channel = Clean(channelValue);
            List<string> list;
            if (HasListKey(cfgText))
                list = Parse(listValue);
            else
            {
                list = new List<string>();
                if (channel != null)
                    list.Add(channel);
                if (!list.Contains(Shipped))
                    list.Add(Shipped);
            }
            if (list.Count == 0)
                list.Add(channel ?? Shipped);
            current = list[0];
            return list;
        }

        // While the game runs, after the settings were read again: the list is never empty, and a channel
        // set by hand that is not in it joins at the end.
        public static List<string> Reconcile(string listValue, string channelValue)
        {
            var list = Parse(listValue);
            string channel = Clean(channelValue);
            if (channel != null && !list.Contains(channel) && list.Count < Max)
                list.Add(channel);
            if (list.Count == 0)
                list.Add(Shipped);
            return list;
        }

        public static AddResult Add(List<string> list, string typed, out string name)
        {
            name = Clean(typed);
            if (name == null)
                return AddResult.Invalid;
            if (list.Contains(name))
                return AddResult.Duplicate;
            if (list.Count >= Max)
                return AddResult.Full;
            list.Add(name);
            return AddResult.Added;
        }

        // The last entry cannot be removed. Removing the channel that is on moves on to the one that takes
        // its place, or to the new last one.
        public static bool Remove(List<string> list, string name, ref string current)
        {
            int i = list.IndexOf(name);
            if (i < 0 || list.Count <= 1)
                return false;
            list.RemoveAt(i);
            if (current == name)
                current = list[Math.Min(i, list.Count - 1)];
            return true;
        }

        public static bool MoveToTop(List<string> list, string name)
        {
            int i = list.IndexOf(name);
            if (i <= 0)
                return false;
            list.RemoveAt(i);
            list.Insert(0, name);
            return true;
        }

        // The next or previous entry, around the ends. From a channel that is not in the list: the first
        // going forward, the last going back.
        public static string Step(IList<string> list, string current, int step)
        {
            if (list.Count == 0)
                return current;
            int i = list.IndexOf(current);
            if (i < 0)
                return step > 0 ? list[0] : list[list.Count - 1];
            return list[((i + step) % list.Count + list.Count) % list.Count];
        }

        // Volume buttons: tenths, without the float's drift.
        public static float StepVolume(float volume, int step) =>
            (float)Math.Max(0, Math.Min(10, Math.Round(volume * 10) + step)) / 10f;
    }

    // Channel buttons pressed in a row connect once: the choice applies when no further press followed.
    public sealed class Tuner
    {
        public const double Delay = 0.4;
        double at;

        public string Pending { get; private set; }

        public void Request(string channel, double now)
        {
            Pending = channel;
            at = now + Delay;
        }

        public void Cancel() => Pending = null;

        // The channel to switch to now, or null.
        public string Due(double now, string current)
        {
            if (Pending == null || now < at)
                return null;
            string p = Pending;
            Pending = null;
            return p == current ? null : p;
        }
    }
}
