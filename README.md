# DancePilot

DancePilot is a WinUI 3 desktop foundation for dance-event DJ workflows. It includes a Spotify metadata importer plus a legal Spotify playback-control layer. DancePilot does not download, rip, record, copy, modify, separate, analyze, or bypass Spotify audio. Spotify audio playback remains inside Spotify-approved devices and clients.

## Location

`C:\Users\elkah\Documents\DancePilot`

## Projects

- `DancePilot.UI` - WinUI 3 dashboard, Spotify connector, Sound / Playback Settings, playlist player, and queue controls.
- `DancePilot.Core` - domain models, source fields, Spotify DTOs, playback settings, device, state, and queue models.
- `DancePilot.Services` - mock data, Spotify OAuth PKCE, encrypted token storage, metadata API, device manager, player service, playlist importer, and playback coordinator.
- `DancePilot.Data` - SQLite connection factory, migrations, settings, queue, playback history, and Spotify import repositories.
- `DancePilot.Tests` - xUnit tests for PKCE, import mapping, playback payloads, and playback database tables.

## Build And Run

```powershell
cd C:\Users\elkah\Documents\DancePilot
dotnet restore .\DancePilot.sln
dotnet build .\DancePilot.sln -c Debug -p:Platform=x64
dotnet run --project .\DancePilot.UI\DancePilot.UI.csproj -c Debug -p:Platform=x64
```

If DancePilot is already open, close it before running `dotnet run`. That command rebuilds first, and Windows locks the app DLLs while the app is running.

Safer local launch:

```powershell
cd C:\Users\elkah\Documents\DancePilot
powershell -ExecutionPolicy Bypass -File .\Run-DancePilot.ps1
```

Stop any running copies:

```powershell
cd C:\Users\elkah\Documents\DancePilot
powershell -ExecutionPolicy Bypass -File .\Stop-DancePilot.ps1
```

Run tests:

```powershell
dotnet test .\DancePilot.Tests\DancePilot.Tests.csproj -c Debug
```

## Spotify Developer Setup

1. Open the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard).
2. Create an app for DancePilot.
3. Copy the app's Client ID.
4. Register this exact Redirect URI:

```text
http://127.0.0.1:8888/callback
```

5. Do not put a client secret into DancePilot. This desktop app uses Authorization Code with PKCE.

Official Spotify references:

- [Spotify Web API OpenAPI Schema](https://developer.spotify.com/reference/web-api/open-api-schema.yaml)
- [Authorization Code with PKCE Flow](https://developer.spotify.com/documentation/web-api/tutorials/code-pkce-flow)
- [Refresh Tokens](https://developer.spotify.com/documentation/web-api/tutorials/refreshing-tokens)
- [Redirect URI Requirements](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri)
- [Scopes](https://developer.spotify.com/documentation/web-api/concepts/scopes)
- [Available Devices](https://developer.spotify.com/documentation/web-api/reference/get-a-users-available-devices)
- [Transfer Playback](https://developer.spotify.com/documentation/web-api/reference/transfer-a-users-playback)
- [Start/Resume Playback](https://developer.spotify.com/documentation/web-api/reference/start-a-users-playback)
- [Playback State](https://developer.spotify.com/documentation/web-api/reference/get-information-about-the-users-current-playback)
- [Web Playback SDK](https://developer.spotify.com/documentation/web-playback-sdk) - browser/WebView playback path, not the native DancePilot path
- [Rate Limits](https://developer.spotify.com/documentation/web-api/concepts/rate-limits)

## Desktop Spotify Integration Choice

DancePilot is a native Windows app, not a web app. Spotify does not provide a native Windows playback SDK like its Android and iOS SDKs, so DancePilot uses the supported desktop pattern:

- Authorization Code with PKCE opens the system browser and returns to `http://127.0.0.1:8888/callback`.
- Spotify Web API over HTTPS handles profile, playlists, playlist items, search, metadata, devices, and allowed playback-control commands.
- Spotify Connect controls an available Spotify playback device. Premium and an active/available device are required for playback commands.
- Open in Spotify App is the fallback when Web API device control is unavailable or refused.

DancePilot does not use the Spotify Web Playback SDK as a native WinUI player. That SDK is JavaScript/browser-based and would require a future WebView2-hosted browser player, not normal WinUI audio playback.

## Required Spotify Scopes

DancePilot metadata login requests only:

- `user-read-private`
- `playlist-read-private`
- `playlist-read-collaborative`

When you use Spotify Connect playback controls, DancePilot sends you through PKCE authorization again for:

- `user-read-playback-state`
- `user-modify-playback-state`
- `user-read-currently-playing`

Spotify Web API playback control requires Spotify Premium and an available Spotify playback device.

## How To Test Login

1. Launch DancePilot.
2. Scroll to `SPOTIFY CONNECTOR`.
3. Paste your Spotify Client ID.
4. Confirm the Redirect URI is `http://127.0.0.1:8888/callback`.
5. Click `LOGIN`.
6. Approve the request in the browser.
7. Return to DancePilot and load/search playlists.
8. Click `REFRESH` in Sound / Playback Settings only when you want Spotify Connect playback controls; DancePilot will request the playback scopes then.

Access and refresh tokens are encrypted with Windows Data Protection API for the current Windows user and stored under `%LOCALAPPDATA%\DancePilot`.

## How To Import And Play A Playlist

1. Login to Spotify.
2. Click `LOAD` to list Spotify playlists.
3. Select a playlist. DancePilot loads its tracks automatically; `PREVIEW` reloads the selected playlist on demand.
4. In `SPOTIFY PLAYLIST PLAYER`, click `LOAD LOCAL`.
5. Select the imported playlist and click `TRACKS`.
6. Open Spotify on a phone, desktop app, browser, or compatible device.
7. Click `REFRESH` under Sound / Playback Settings.
8. Select a Spotify device and click `TRANSFER`.
9. Use `PLAY LIST`, `PLAY SELECTED`, `ADD SELECTED TO QUEUE`, or double-click a track to add it to the active deck and start it.

## Playback Controls

Implemented:

- List Spotify playback devices.
- Transfer playback to a selected Spotify Connect device.
- Start a Spotify playlist by context URI.
- Play a selected Spotify track URI.
- Pause, resume, next, previous, seek, and set volume where Spotify/device supports it.
- Display current Spotify track, playback status, output device, time remaining, and progress.
- Maintain a local DancePilot queue in SQLite.
- Log started Spotify tracks to playback history.
- Emergency Pause disables Spotify Autopilot and sends a pause command.
- Playlist preview/import uses `/playlists/{playlist_id}/items`.
- Spotify search uses the OpenAPI schema maximum of 10 results per request.
- Spotify HTTP 429 responses are retried with `Retry-After` or exponential backoff.

Playback modes:

- `Spotify Web API / Spotify Connect` - implemented through Spotify Web API playback endpoints.
- `Open in Spotify App` - opens Spotify links without controlling a device.
- `Local Music Files` - plays music files from this Windows PC.

## Autopilot

First simple version:

- If Spotify Autopilot is ON and there is a pending DancePilot queue item, DancePilot starts the next queued Spotify URI when the current Spotify track is within the configured seconds from ending.
- Default timing is 8 seconds before end.
- No crossfade is implemented. Spotify transitions are controlled by Spotify settings and the selected Spotify device.

## Database

The `songs` table includes Spotify metadata fields:

- `source TEXT DEFAULT 'local'`
- `external_id TEXT`
- `external_uri TEXT`
- `external_url TEXT`
- `album TEXT`
- `duration_ms INTEGER`
- `popularity INTEGER`
- `imported_from_playlist_id TEXT`
- `last_synced_at TEXT`
- `likely_local_match_song_id INTEGER`

Spotify and playback tables:

- `spotify_playlists`
- `spotify_playlist_tracks`
- `app_settings`
- `playback_history`
- `queue`

Stored settings:

- `spotify_selected_device_id`
- `spotify_selected_device_name`
- `spotify_playback_mode`
- `spotify_autopilot_enabled`
- `spotify_autoplay_seconds_before_end`
- `spotify_default_volume`

## Testing Checklist

- Build the solution with `dotnet build .\DancePilot.sln -c Debug -p:Platform=x64`.
- Run `dotnet test .\DancePilot.Tests\DancePilot.Tests.csproj -c Debug`.
- Login with a Spotify account that has Premium.
- Refresh devices with Spotify open on at least one device.
- Transfer playback to the selected device.
- Import a playlist and load it in the Spotify Playlist Player.
- Play the playlist.
- Play from a selected track.
- Pause/resume, next/previous, seek, and set volume.
- Add a track to the DancePilot queue.
- Enable Spotify Autopilot and confirm it starts a queued track near the configured end threshold.
- Use Emergency Pause and confirm Autopilot turns off.

## Known Limitations

- Spotify Premium is required for Web API playback control.
- DancePilot does not mix, crossfade, capture, inspect, or process Spotify audio.
- Spotify BPM/key/audio-analysis data is not faked. Imported Spotify tracks may need manually assigned DancePilot BPM, key, tags, and energy.
- Spotify local files and unavailable playlist tracks are skipped during import.
- The Spotify Web Playback SDK is not implemented as a native WinUI playback layer. A future WebView2 player could be evaluated separately.
- Spotify volume support depends on the selected device.
- Rate-limited Spotify responses are retried a few times using `Retry-After` or exponential backoff, then surfaced to the user if Spotify still returns HTTP 429.
- The Spotify callback port `8888` must be free during login.
- The current imported-playlist SQLite feature stores Spotify metadata from the original project requirement. If strict immediate-use-only caching is required, convert this to a session cache or store only Spotify IDs plus user-authored DancePilot fields.
