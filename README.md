# Shorelands Billboard

A billboard in the town of the game Shorelands that plays a Twitch channel's live stream, with sound that gets quieter as you walk away. Out of the box it shows
[twitch.tv/dunkorslam](https://www.twitch.tv/dunkorslam), the channel of the game's developer.

![The billboard at the town pond, playing a live stream](docs/billboard.png)

It is a mod for your own game only. Nothing is sent to the game server, and other players only see the
billboard if they have installed the mod themselves.

This is a fan-made mod. It is not made or supported by the game's developer; if something is wrong with it,
do not report it to the game.

## What you need

- **Shorelands on Windows (64-bit).** The mod was tested with Playtest 0.69g. It expects the hedge north of
  the town pond where it is in that version; if a game update moves or removes that hedge, the billboard stays
  off until the mod is updated (see "Troubleshooting").
- **BepInEx 5.4.23.6, x64** (the mod loader). Download `BepInEx_win_x64_5.4.23.6.zip` from
  <https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6>. The mod is a BepInEx 5 plugin and was not tried
  with any other version.

Nothing else. The video decoder (libVLC) comes with the mod.

## Install

1. Close the game.
2. Open the game's folder: in Steam, right-click the game, then *Manage*, *Browse local files*. It is the
   folder that holds `Shorelands.exe`.
3. If you do not have BepInEx yet: unzip `BepInEx_win_x64_5.4.23.6.zip` into that folder, so that
   `winhttp.dll` and the `BepInEx` folder lie next to `Shorelands.exe`. Start the game once and close it
   again; BepInEx then creates its folders.
4. Download `ShorelandsBillboard-1.1.1.zip` from the
   [releases page](https://github.com/zerfl/ShorelandsBillboard/releases) and unzip it into the same folder. You should now have
   `BepInEx\plugins\ShorelandsBillboard\ShorelandsBillboard.dll`.
5. Start the game and log in.

## Using it

The billboard stands in town on the north side of the pond, behind the two benches. Stand between the benches
for the best view.

- The sound is at full volume near the billboard and fades to silence about three billboard widths away (you
  can change the volume and the distance). It comes a little more from the side the billboard is on.
- When you are further away than that, the stream is paused and the screen shows `STANDBY`. It starts
  again by itself when you come back, about a second later.
- You cannot walk through the billboard's base; walk around its right end to get behind it.
- The game's master volume also turns the billboard's sound up and down.

## The remote

![The remote next to the billboard; its button is the one left of the menu button, top right](docs/remote.png)

The button with the little television, left of the game's menu button in the top right corner, opens the
remote. The same button, `Esc` or the remote's own close button closes it. You can drag it to another place
on the screen, and it works wherever you are in the world: away from the billboard its display says
`STANDBY`, and what you chose is on when you come back.

| On the remote | What it does |
| --- | --- |
| Power (red, top left) | Switches the billboard off and on (`[Billboard] Enabled`). |
| Display | The channel and what it is doing: `LIVE`, `OFFLINE`, `AD BREAK`, `STANDBY`, `OFF` and the other texts of the billboard's screen (see "Troubleshooting"). On the right its place in the list, and `MUTE` while muted. |
| `VOL` + and - | Volume up and down by a tenth (`[Sound] Volume`). |
| Speaker (middle) | Mute on and off (`[Sound] Mute`). |
| `CH` up and down | The previous and the next channel of the list. After the last one comes the first again. |
| A row of the list | Switches to that channel. The one that is on is written in yellow; a red dot marks the channels that are live right now. The mouse wheel scrolls a long list. |
| The two small buttons on the row under the pointer | The arrow makes that channel the default: it moves to the top of the list. The cross removes it from the list (the last channel cannot be removed). |
| `add channel` and + | Type a channel's name, or paste its address, and press Enter or +. The list holds up to 20 channels. |

The first channel of the list is the default: the one the billboard shows when you start the game.
Switching channels with the remote is for now only, so an evening on another channel does not change what
you see the next day; to change that, make another channel the default.

While you type a name, the keys go to the name field and not to the game. Everything the remote changes is
written to the settings file at once, and what you change in the file shows on the remote within a few
seconds.

## Settings

Start the game once with the mod; it then writes `BepInEx\config\com.zerfl.shorelands.billboard.cfg`. Open
that file in a text editor (Notepad will do). You can change it while the game runs: the mod reads the file again
within a few seconds of saving it.

| Setting | Default | What it does |
| --- | --- | --- |
| `[Billboard] Enabled` | `true` | `false` removes the billboard. |
| `[Billboard] Channels` | `dunkorslam` | The remote's channel list, separated by commas (see below). The billboard starts on the first one. |
| `[Billboard] Channel` | `dunkorslam` | The channel that is on now. The remote changes it. At every game start it is set to the first entry of `Channels`. |
| `[Billboard] Quality` | `auto` | `auto` picks just enough for the billboard's size on your screen (360p on a 1080p screen). Or `160`, `360`, `480`, `720`: at least that picture height. Higher costs more bandwidth and processor time and rarely looks better at this size. |
| `[Billboard] SharpPicture` | `true` | `true`: the picture is drawn at your screen's resolution, so text in the stream can be read. `false`: it is drawn in the game's pixel-art resolution (184x104 pixels). |
| `[Sound] Volume` | `0.6` | Volume next to the billboard, from `0` to `1`. |
| `[Sound] Mute` | `false` | `true`: picture only. |
| `[Sound] Range` | `18` | Distance at which the sound has faded to silence, from `6` to `40` (a hedge bush is 1 wide). Further away than this the stream is paused. |
| `[Position] OffsetX`, `OffsetY` | `0` | Move the billboard east and north (negative: west and south), from `-40` to `40`. Only hedge bushes are cleared from under it, so pick a free spot. |
| `[Decoder] Backend` | `auto` | What decodes the video. `auto`: the libVLC that comes with the mod, or ffmpeg if libVLC cannot be used. `vlc`: only libVLC. `ffmpeg`: only an ffmpeg on your PC. |
| `[Decoder] FfmpegPath` | empty | Full path of the `ffmpeg.exe` to use. Empty: the one found in `PATH`, in winget's install folders or in the mod's folder. |
| `[Remote] Enabled` | `true` | `false`: no button next to the menu button and no remote. |
| `[Remote] Key` | empty | A key that opens and closes the remote, for example `F8` or `R`. Empty: no key. |

### Showing another channel

The easy way is the remote (see "The remote"). In the file:

- `Channels` is the list: the names from the channels' addresses, separated by commas. For
  `https://www.twitch.tv/somechannel` the name is `somechannel`. Example:
  `Channels = dunkorslam, somechannel, anotherchannel`. The first one is the default. Entries that are not
  channel names (anything other than letters, digits and underscores) and repeats are ignored.
- `Channel` switches the billboard right away, as the remote does: `Channel = somechannel`. The name plate
  under the screen changes with it. A channel set here that is not in the list is added to its end. A name
  that is not a channel name is refused (`BAD CHANNEL`). Because the game starts on the first entry of
  `Channels`, a channel you want to keep belongs there.

If you used version 1.0.0: the channel you had set is now the first entry of `Channels`, so the billboard
shows what it showed before.

## Troubleshooting

What the screen says:

| Screen | Meaning | What to do |
| --- | --- | --- |
| `CONNECTING` | The stream is being looked up. | Wait; it takes a second or two. |
| `BUFFERING` | The stream stalled. | Wait. If it does not come back within 20 seconds the mod reconnects by itself. |
| `OFFLINE` | The channel is not live (or does not exist). | Nothing: the mod checks again every minute and starts when the channel goes live. |
| `AD BREAK` | Twitch is running ads on the channel. The mod does not show them. | Wait until the break is over. |
| `NO SIGNAL` | Twitch could not be reached, or the stream could not be played. | Check your internet connection and whether a firewall blocks `Shorelands.exe`. The mod tries again by itself, at first after 5 seconds, then at longer intervals up to a minute. |
| `STANDBY` | You are too far away; the stream is paused. | Walk to the billboard. |
| `BAD CHANNEL` | `Channel` is not a Twitch channel name. | Pick a channel on the remote, or fix it in the settings file. |
| `NO DECODER` | The mod's `libvlc` folder or `LibVLCSharp.dll` is missing and there is no ffmpeg on the PC either. | Unzip the mod again so that all of its files are there. |
| `NO VLC` | `Backend = vlc`, but the mod's `libvlc` folder or `LibVLCSharp.dll` is missing. | Unzip the mod again, or set `Backend = auto`. |
| `NO FFMPEG` | `Backend = ffmpeg`, but no `ffmpeg.exe` was found. | Install ffmpeg (`winget install Gyan.FFmpeg`), set `FfmpegPath`, or set `Backend = auto`. |

Other things:

- **There is no button for the remote.** `[Remote] Enabled` may be `false`. Otherwise look into
  `BepInEx\LogOutput.log` for `no remote` or `the remote is switched off`: a game update changed the part of
  the screen the button belongs to, and a newer version of the mod is needed. The billboard itself keeps
  working.
- **There is no billboard at all.** Look into `BepInEx\LogOutput.log` in the game folder for lines with
  `ShorelandsBillboard`.
  - No such line: the mod was not loaded. Check that BepInEx is installed (the log file exists and starts with
    `BepInEx 5.4.23.6`) and that the mod's folder is in `BepInEx\plugins`.
  - `the hedge north of the town pond is not where this version of the mod knows it`: a game update moved or
    removed the hedge the billboard stands in. The mod stays off rather than put the billboard in a wrong
    place; look for a newer version of the mod.
  - `the billboard is switched off until the game restarts`: something the mod relies on changed or failed. The
    lines before it say what; a newer version of the mod is needed if it happens every time.
- **Picture, but no sound.** Check `Mute` and `Volume` in the settings, the game's master volume, and that
  you are near the billboard.
- **The picture is blocky.** In a window that is not 16:9 (for example an ultrawide screen or 1280x1024) the
  mod always draws the picture in pixel-art resolution, as with `SharpPicture = false`.
- For anything else, the lines with `ShorelandsBillboard` in `BepInEx\LogOutput.log` say what happened.

## Uninstall

Close the game and delete the folder `BepInEx\plugins\ShorelandsBillboard`. To remove the settings too, delete
`BepInEx\config\com.zerfl.shorelands.billboard.cfg`. The mod changes no file of the game.

## Privacy and network

While you are near the billboard, the mod fetches the stream the way a web browser does when it shows a Twitch
channel without being logged in. It connects to:

- `gql.twitch.tv` and `usher.ttvnw.net` (Twitch), to look up the channel's stream;
- the video servers under `ttvnw.net` that Twitch names for that stream, to download it.

It sends the name of the channel and what every web request carries (your IP address, a generic browser
identification). It sends no Twitch account, no login, no cookies, no device or player identification and
nothing about your game or your PC. Twitch sees an anonymous viewer of the channel. The mod contacts no other
server, and nothing goes to the Shorelands game server.

While the remote is open, the mod also asks `gql.twitch.tv` which channels of your list are live, to mark
them with a red dot: one request that carries the list's channel names, when the remote opens and then at
most once a minute. With the remote closed, or with `[Remote] Enabled = false`, this request is not made.

Twitch's advertising inserted into a stream is not played; the screen shows `AD BREAK` for its duration.

The stream is paused, and nothing is downloaded, while you are away from the billboard, while you are in the
main menu, and when `Enabled = false`.

## Known limitations

- Players without the mod do not see the billboard, and for them its base is not an obstacle. You may see
  them walk through it and through the place of the five hedge bushes the billboard replaces.
- Name tags and speech bubbles are drawn over the picture: your own name tag covers a little of the picture's
  lower edge when you stand right at the base.
- The sharp picture needs a 16:9 window; other shapes get the pixel-art picture (see "Troubleshooting").
- The stream runs some seconds behind the live broadcast; Twitch's low-latency mode is not used.
- The remote is used with the mouse; there is no gamepad control for it.
- Windows only.

## Licences

- The mod's own code and pictures are under the [MIT License](LICENSE).
- The package also contains libVLC 3.0.24 and LibVLCSharp 3.10.2, both under the GNU Lesser General Public
  License 2.1 or later. They are VideoLAN's unmodified builds, kept as separate files that you can replace
  with your own. Their source code is at VideoLAN:
  [vlc-3.0.24.tar.xz](https://download.videolan.org/pub/videolan/vlc/3.0.24/vlc-3.0.24.tar.xz) and
  [LibVLCSharp, tag 3.10.2](https://code.videolan.org/videolan/LibVLCSharp/-/tags/3.10.2).
  [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) has the exact versions and package checksums and says
  what else is inside them; the licence text is in [licenses/LGPL-2.1.txt](licenses/LGPL-2.1.txt).

Shorelands belongs to its developer. Twitch is a trademark of Twitch Interactive, Inc.; this mod is not
affiliated with Twitch.

## Building from source

You need:

- Windows (64-bit) with the [.NET SDK](https://dotnet.microsoft.com/download) 10 or newer;
- the game with BepInEx 5.4.23.6 installed in it (steps 1 to 3 of "Install"). The build compiles against the
  game's and BepInEx's own assemblies, which are not in this repository.

In the commands below, put your game folder (the one that holds `Shorelands.exe`) in place of the path.
Instead of passing it every time, you can set the environment variable `SHORELANDS_DIR` to it. Run them in
the repository's folder, in PowerShell or the command prompt.

Build the plugin:

```
dotnet build src -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Shorelands Playtest"
```

The result is `src\bin\Release\ShorelandsBillboard.dll`.

Run the tests:

```
dotnet run --project tests -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Shorelands Playtest"
```

They print one line for each check and `ALL PASS` at the end. The tests are run locally; there is no build
server, because building needs the game's assemblies.

Make the package:

```
powershell -ExecutionPolicy Bypass -File tools\release.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Shorelands Playtest"
```

This downloads the pinned libVLC and LibVLCSharp packages from nuget.org (checksums checked), runs the tests,
builds the plugin, checks what goes into the package and writes `dist\ShorelandsBillboard-<version>.zip`,
ready to be unzipped into the game folder.

The pictures in `art\` are drawn by `art\make_billboard.py` (the billboard) and `art\make_remote.py` (the
remote), Python with Pillow; run them after changing them. Changes between versions: [CHANGELOG.md](CHANGELOG.md).
