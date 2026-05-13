# ytdlp-jelly
Plugin that installs yt-dlp into Jellyfin

## Features

- Downloads the `yt-dlp` binary automatically into Jellyfin program data.
- Supports update modes from plugin configuration:
  - `Disabled`
  - `Latest` (runs `yt-dlp -U` on schedule)
  - `Specific version` (downloads exact tagged release)
- Configurable update interval in hours from the plugin configuration page.
