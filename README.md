# ytdlp-jelly

A Jellyfin plugin that automatically downloads and keeps the [`yt-dlp`](https://github.com/yt-dlp/yt-dlp) binary up to date inside Jellyfin program data.

## Features

- Downloads the `yt-dlp` binary automatically into Jellyfin program data.
- Supports update modes from plugin configuration:
  - `Disabled`
  - `Latest` (runs `yt-dlp -U` on schedule)
  - `Specific version` (downloads exact tagged release)
- Configurable update interval in hours from the plugin configuration page.

## Installation

### Via plugin repository (recommended)

1. Open Jellyfin and go to **Dashboard → Plugins → Repositories**.
2. Click **Add** and enter the following repository URL:
   ```
   https://raw.githubusercontent.com/errordlien/ytdlp-jelly/refs/heads/main/manifest.json
   ```
3. Go to **Dashboard → Plugins → Catalog**, find **yt-dlp manager**, and click **Install**.
4. Restart Jellyfin when prompted.

### Manual installation

1. Download the latest `ytdlp-jelly_*.zip` from the [Releases](https://github.com/errordlien/ytdlp-jelly/releases) page.
2. Extract the ZIP and copy all files into your Jellyfin plugins directory:
   - **Linux:** `/var/lib/jellyfin/plugins/ytdlp-jelly/`
   - **Windows:** `%PROGRAMDATA%\Jellyfin\Server\plugins\ytdlp-jelly\`
3. Restart Jellyfin.

## Configuration

After installation, go to **Dashboard → Plugins → yt-dlp manager** to configure the plugin.

| Setting | Description |
|---|---|
| **Update Mode** | `Disabled` – do nothing; `Latest` – keep yt-dlp at the latest release; `Specific Version` – pin to an exact release tag (e.g. `2024.04.09`). |
| **Update Interval (hours)** | How often (in hours) the plugin checks for updates. Default is `24`. |
| **Specific Version** | The yt-dlp release tag to pin to when using *Specific Version* mode (e.g. `2024.04.09`). |
| **Cookies File Path** | Optional path used with `--cookies <file>` when you run yt-dlp manually for sites requiring authentication. |
| **Cookies From Browser** | Optional browser selector used with `--cookies-from-browser <browser>` (for example `firefox`, `chrome`, or `edge`). |
| **Additional yt-dlp Arguments** | Optional extra command-line switches to include in your own yt-dlp commands for `.strm` URL resolution. |

The plugin checks for updates in the background starting one minute after Jellyfin starts, then repeats on the configured interval.

## Binary location

Once installed and triggered, the `yt-dlp` binary is placed at:

| Platform | Path |
|---|---|
| Linux / macOS | `/var/lib/jellyfin/plugins/ytdlp-jelly/bin/yt-dlp` |
| Windows | `%PROGRAMDATA%\Jellyfin\Server\plugins\ytdlp-jelly\bin\yt-dlp.exe` |

> **Note:** The exact path depends on your `ProgramDataPath`. The path above assumes the default Jellyfin data directory.

## Shell / PATH export

After each download or update the plugin automatically tries to make `yt-dlp` available without a full path:

| Platform | What the plugin does |
|---|---|
| **Linux / macOS** | Creates a symlink `/usr/local/bin/yt-dlp` → the binary. Requires Jellyfin to be running with write access to `/usr/local/bin` (e.g. as root or via `sudo`). |
| **Windows** | Appends the binary directory to the machine-level `PATH` environment variable. Requires Jellyfin to be running with administrator privileges. |

If the plugin cannot write to the target location it logs a warning and falls back gracefully. In that case you can either:

- Run Jellyfin with the necessary privileges so the plugin can set up the symlink / PATH entry, **or**
- Add the binary directory to your PATH manually:

  ```bash
  # Linux / macOS — add to ~/.bashrc, ~/.zshrc, etc.
  export PATH="/var/lib/jellyfin/plugins/ytdlp-jelly/bin:$PATH"
  ```

  ```powershell
  # Windows PowerShell (run as administrator)
  [System.Environment]::SetEnvironmentVariable(
      "PATH",
      $env:PATH + ";$env:PROGRAMDATA\Jellyfin\Server\plugins\ytdlp-jelly\bin",
      "Machine"
  )
  ```

Once the PATH is set up you can simply call:

```bash
yt-dlp -g "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
```

## Using yt-dlp to stream videos in Jellyfin via .strm files

Jellyfin supports [`.strm` files](https://jellyfin.org/docs/general/server/media/external-files) — plain text files containing a single media URL that Jellyfin treats as a video. You can use the `yt-dlp` binary installed by this plugin to obtain a direct stream URL for any site yt-dlp supports (YouTube, Twitch VODs, etc.) and save it as a `.strm` file.

### Step 1 – Get the direct stream URL

Run `yt-dlp` with the `-g` flag to print the best available stream URL without downloading the file. If the plugin successfully exported `yt-dlp` to your PATH (see [Shell / PATH export](#shell--path-export) above) you can run it directly:

```bash
yt-dlp -g "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
```

If the binary is not yet on your PATH, use the full path instead:

```bash
# Linux / macOS
/var/lib/jellyfin/plugins/ytdlp-jelly/bin/yt-dlp -g "https://www.youtube.com/watch?v=dQw4w9WgXcQ"

# Windows (PowerShell)
& "$env:PROGRAMDATA\Jellyfin\Server\plugins\ytdlp-jelly\bin\yt-dlp.exe" -g "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
```

The command prints a direct HTTPS URL to the video stream.

If your source requires login/session cookies, use one of yt-dlp's cookie mechanisms:

```bash
# Use a cookies.txt / netscape-format cookie file
yt-dlp --cookies /path/to/cookies.txt -g "https://example.com/protected/video"

# Or extract cookies directly from a browser profile
yt-dlp --cookies-from-browser firefox -g "https://example.com/protected/video"
```

If you already have a `.strm` file containing the original page URL, feed that URL back into yt-dlp and add any required switches:

```bash
# Linux / macOS: parse URL from .strm and resolve direct stream URL
yt-dlp --cookies /path/to/cookies.txt --no-playlist -g "$(cat /path/to/video.strm)"

# Windows PowerShell: parse URL from .strm and resolve direct stream URL
yt-dlp --cookies-from-browser edge --no-playlist -g (Get-Content "C:\path\to\video.strm" -TotalCount 1)
```

### Step 2 – Create the .strm file

Create a plain text file with a `.strm` extension and paste the URL from step 1 as the only line:

```
# Example: RickRoll.strm
https://rr5---sn-xxx.googlevideo.com/videoplayback?...
```

Save the file in a folder you will add as a Jellyfin library, for example:

```
/media/youtube/Rick Astley - Never Gonna Give You Up (1987).strm
```

### Step 3 – Add the folder as a Jellyfin library

1. Go to **Dashboard → Libraries → Add Media Library**.
2. Choose **Movies** or **Shows** as the content type.
3. Set the folder path to the directory containing your `.strm` files.
4. Scan the library — Jellyfin will pick up the `.strm` files and display them as playable items.

> **Important:** Direct stream URLs from services like YouTube are time-limited and will expire (usually within a few hours). You will need to regenerate the URL with `yt-dlp -g` and update the `.strm` file each time the link expires. For long-lived libraries, consider using a local streaming proxy (such as [yt-dlp-web-ui](https://github.com/nicholasgasior/gsfmt) or a custom wrapper script) that calls `yt-dlp` on demand.
