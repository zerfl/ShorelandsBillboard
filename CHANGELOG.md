# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), the version numbers
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-21

First release. Tested with Shorelands Playtest 0.69g and BepInEx 5.4.23.6.

### Added

- A billboard at the town pond that plays a Twitch channel's live stream, with sound that fades with distance
  and follows the game's master volume.
- Settings for the channel, the stream quality, volume, mute, the sound's range and the billboard's position.
  The settings file is read again when it is saved, so changes apply while the game runs.
- A sharp picture at the screen's resolution in 16:9 windows; the game's pixel-art resolution in other
  windows or by choice.
- libVLC 3.0.24 in the package as the video decoder; an ffmpeg on the PC as the second choice.
- The stream is paused while you are away from the billboard. The screen says what is going on (`STANDBY`,
  `OFFLINE`, `AD BREAK`, `NO SIGNAL` and others), and the mod reconnects by itself.
- Twitch's advertising inserted into a stream is not played.
- The billboard is only built when the hedge it stands in is found where it is in 0.69g; otherwise it stays
  off and says so once in the log.
