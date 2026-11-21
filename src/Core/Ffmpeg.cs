using System;
using System.Collections.Generic;
using System.IO;

namespace ShorelandsBillboard.Core
{
    public static class Ffmpeg
    {
        // The configured file, else ffmpeg.exe from PATH as this process sees it, else winget's shim and
        // package folders (a PATH change made after the launcher started is not in the process's PATH),
        // else next to the plugin. Null when there is none.
        public static string Find(string configured, string path, string localAppData, string pluginDir)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
            foreach (var c in Candidates(path, localAppData, pluginDir)) if (File.Exists(c)) return c;
            return null;
        }

        static IEnumerable<string> Candidates(string path, string localAppData, string pluginDir)
        {
            foreach (var dir in (path ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string d = dir.Trim().Trim('"');
                if (d.Length > 0) yield return Path.Combine(d, "ffmpeg.exe");
            }
            if (!string.IsNullOrEmpty(localAppData))
            {
                string winget = Path.Combine(localAppData, "Microsoft", "WinGet");
                yield return Path.Combine(winget, "Links", "ffmpeg.exe");
                string packages = Path.Combine(winget, "Packages");
                if (Directory.Exists(packages))
                    foreach (var pkg in Directory.GetDirectories(packages, "Gyan.FFmpeg*"))
                        foreach (var build in Directory.GetDirectories(pkg))
                            yield return Path.Combine(build, "bin", "ffmpeg.exe");
            }
            if (!string.IsNullOrEmpty(pluginDir)) yield return Path.Combine(pluginDir, "ffmpeg.exe");
        }
    }

    public static class Backend
    {
        public static string VlcDir(string pluginDir) => Path.Combine(pluginDir, "libvlc");

        public static bool VlcInstalled(string pluginDir) =>
            File.Exists(Path.Combine(pluginDir, "LibVLCSharp.dll")) && File.Exists(Path.Combine(VlcDir(pluginDir), "libvlc.dll"))
            && File.Exists(Path.Combine(VlcDir(pluginDir), "libvlccore.dll"));

        public static bool UseVlc(string configured, bool usable) => configured == "vlc" || configured != "ffmpeg" && usable;
    }
}
