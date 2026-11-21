# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), the version numbers
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - Unreleased

Until now, changing the channel meant leaving the pond and opening a text file. The billboard has a remote
now, so you can stay on the bench.

### Added

- **A remote control.** Zap without getting up, or without even being in town. A new button left of the
  game's menu button (top right) opens it; the same button, `Esc` or the remote's own close button puts it
  away again. On it: power (billboard on and off), `CH` up and down (previous and next channel, going round
  at the ends of the list), `VOL` plus and minus (in steps of a tenth), mute, and a small display with the
  channel and what it is doing (`LIVE`, `OFFLINE`, `AD BREAK`, `OFF` and so on). It works from anywhere in
  the world: away from the billboard the display says `STANDBY`, and your choice is on when you walk back.
  `[Remote] Enabled = false` removes the button and the remote; `[Remote] Key` gives it a key of your own.
- **A channel list on the remote.** For the hours when your favourite streamer is not live to explain, with
  a detour or two, why the update is nearly ready. Click a channel to switch to it. Type a name into the
  field at the bottom (a pasted channel address works too) to add it, up to 20 channels. Point at a row for
  its two small buttons: one removes the channel, one makes it the default. The list is stored in the
  settings file as `[Billboard] Channels`; its first entry is the default, the channel the billboard starts
  on.
- **A red dot for who is live.** So you know whether the walk to the pond is worth it before you leave your
  koi. The remote's list marks the channels that are live right now: one request to Twitch for the whole
  list while the remote is open, at most once a minute.

### Changed

- **`[Billboard] Channel` now means "what is on right now".** The remote changes it, you still can by hand,
  and every game start sets it back to the first entry of `Channels`: one evening on another channel does
  not move your default. A settings file from 1.0.0 loses nothing: its channel becomes the first entry of
  the new list, with `dunkorslam` after it.

### Fixed

- **Long names no longer spill over.** A channel with a very long name wrapped onto a second line under the
  screen's status text (`OFFLINE` and friends). The `twitch.tv/` in front is now left out when the line
  does not fit, and the name is cut if it still does not.

## [1.0.0] - 2026-11-21

First release: the town gets a telly. Tested with Shorelands Playtest 0.69g and BepInEx 5.4.23.6.

### Added

- **A billboard at the town pond.** Watch your favourite streamer talk about the fish, at length, while you
  catch your next one. It plays a Twitch channel's live stream, with sound that fades as you walk away and
  follows the game's master volume.
- **Settings, for when he gets a bit loud.** The channel, the stream quality, volume, mute, the sound's
  range and the billboard's position. The settings file is read again when it is saved, so changes apply
  while the game runs.
- **A picture you can read.** In 16:9 windows the picture is sharp, at the screen's resolution; in other
  windows, or by choice, it is drawn in the game's pixel-art resolution.
- **No extra shopping.** libVLC 3.0.24 comes in the package as the video decoder; an ffmpeg on the PC is
  the second choice.
- **It minds its manners.** The stream is paused while you are away from the billboard. The screen says
  what is going on (`STANDBY`, `OFFLINE`, `AD BREAK`, `NO SIGNAL` and others), and the mod reconnects by
  itself.
- **No ad breaks at the pond.** Twitch's advertising inserted into a stream is not played.
- **It knows its place.** The billboard is only built when the hedge it stands in is found where it is in
  0.69g; otherwise it stays off and says so once in the log.
