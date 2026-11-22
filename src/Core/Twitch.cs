using System;
using Newtonsoft.Json.Linq;

namespace ShorelandsBillboard.Core
{
    // Twitch's public playback path, as streamlink's twitch plugin does it (2026-11): an anonymous
    // PlaybackAccessToken from GQL, then the usher master playlist signed with it.
    public static class Twitch
    {
        public const string GqlUrl = "https://gql.twitch.tv/gql";
        public const string ClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko"; // the web player's public id
        public const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:140.0) Gecko/20100101 Firefox/140.0";
        public const string Referer = "https://player.twitch.tv";
        const string TokenQueryHash = "ed230aa1e33e07eebb8928504583da78a5173989fadfb1ac94be06a04f3cdbe9";

        public enum TokenKind
        {
            Token,
            NoChannel,
            Error,
        }

        public sealed class Token
        {
            public TokenKind Kind;
            public string Signature,
                Value,
                Error;
        }

        public static bool ValidChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel) || channel.Length > 25)
                return false;
            foreach (char c in channel)
                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_'))
                    return false;
            return true;
        }

        public static string TokenRequest(string channel)
        {
            return new JObject
            {
                ["operationName"] = "PlaybackAccessToken",
                ["extensions"] = new JObject
                {
                    ["persistedQuery"] = new JObject { ["version"] = 1, ["sha256Hash"] = TokenQueryHash },
                },
                ["variables"] = new JObject
                {
                    ["isLive"] = true,
                    ["login"] = channel,
                    ["isVod"] = false,
                    ["vodID"] = "",
                    ["playerType"] = "embed",
                    ["platform"] = "site",
                },
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static Token ParseToken(string json)
        {
            try
            {
                var o = JObject.Parse(json);
                if (o["errors"] is JArray errs && errs.Count > 0)
                    return new Token { Kind = TokenKind.Error, Error = (string)errs[0]["message"] };
                if (o["error"] != null)
                    return new Token
                    {
                        Kind = TokenKind.Error,
                        Error = (string)o["error"] + ": " + (string)o["message"],
                    };
                var data = o["data"] as JObject;
                if (data == null)
                    return new Token { Kind = TokenKind.Error, Error = "no data" };
                var t = data["streamPlaybackAccessToken"];
                if (t == null || t.Type == JTokenType.Null)
                    return new Token { Kind = TokenKind.NoChannel };
                return new Token
                {
                    Kind = TokenKind.Token,
                    Signature = (string)t["signature"],
                    Value = (string)t["value"],
                };
            }
            catch (Exception e)
            {
                return new Token { Kind = TokenKind.Error, Error = "bad token response: " + e.Message };
            }
        }

        // One request for many channels: which of them are live.
        public static string LiveRequest(System.Collections.Generic.IEnumerable<string> channels)
        {
            return new JObject
            {
                ["query"] = "query($logins:[String!]){users(logins:$logins){login stream{id}}}",
                ["variables"] = new JObject { ["logins"] = new JArray(channels) },
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        // Channel -> live. A channel Twitch does not know is missing; null: no usable answer.
        public static System.Collections.Generic.Dictionary<string, bool> ParseLive(string json)
        {
            try
            {
                if (!(JObject.Parse(json)["data"]?["users"] is JArray users))
                    return null;
                var live = new System.Collections.Generic.Dictionary<string, bool>();
                foreach (var u in users)
                    if (u is JObject o && (string)o["login"] is string login)
                        live[login.ToLowerInvariant()] = o["stream"] != null && o["stream"].Type != JTokenType.Null;
                return live;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string UsherUrl(string channel, Token token, int p)
        {
            return "https://usher.ttvnw.net/api/v2/channel/hls/"
                + channel
                + ".m3u8"
                + "?platform=web&p="
                + p
                + "&allow_source=true&allow_audio_only=true&playlist_include_framerate=true"
                + "&multigroup_video=true&supported_codecs=h264"
                + "&sig="
                + Uri.EscapeDataString(token.Signature)
                + "&token="
                + Uri.EscapeDataString(token.Value);
        }

        // usher answers 404 with [{"error":"Can not find channel"}] or a transcode-not-found text while a channel is offline
        public static string UsherError(string body)
        {
            try
            {
                return (string)JArray.Parse(body)[0]["error"];
            }
            catch
            {
                return null;
            }
        }
    }
}
