# DancePilot Library Manager

## Purpose

Library Manager is the planning and preparation workspace for DancePilot. It supports organizing local music, building playlists, preparing event templates, checking library health, and later using AI-assisted playlist suggestions.

Library Manager should not clutter the live event interface. Live Event remains focused on decks, queues, transitions, and playback.

## Guiding Idea

DancePilot should help the operator prepare events before showtime.

The live app answers: what should play now?

Library Manager answers: what should I prepare for the event?

## Main Sections

### Music Library

A searchable catalog of saved music.

Initial focus:

- Local songs
- Album art
- Artists
- Albums
- Genres if available
- Years if available
- Folders
- Search and filters

Future sources:

- External drives
- Network shares
- Plex
- Jellyfin
- Other providers

### Playlist Builder

DancePilot playlists belong to DancePilot, not to a provider.

Initial features:

- Create playlist
- Rename playlist
- Delete playlist
- Add local songs
- Remove songs
- Reorder songs
- Save playlist
- Load playlist to Deck A or Deck B
- Estimated duration
- Song count

Future features:

- Duplicate playlist
- Import M3U
- Import CSV
- Export playlist
- Smart ordering
- AI suggestions

### Event Templates

Reusable plans for common event types.

Examples:

- Friday Night Dance
- Cornhole Tournament
- Wedding Reception
- RV Park Social
- Dinner Background Music
- Holiday Event

A template may include:

- Event name
- Duration
- Sections
- Playlist references
- Notes
- Suggested energy progression

### Collections

Collections describe groups of music. Playlists describe event order.

Examples:

- Country Dance
- Classic Rock
- Line Dance
- Slow Songs
- Christmas
- 4th of July
- Background Music
- Favorites

### Library Health

Tools for keeping the library usable.

Initial health checks:

- Duplicate songs
- Missing files
- Broken paths
- Missing artwork
- Missing artist/title metadata
- Unsupported files
- Recently added songs

### Import Center

Tools for adding music to DancePilot.

Initial tools:

- Add folder
- Rescan library
- Import playlist
- Import M3U
- Import CSV

Future tools:

- Watch folder
- Import ZIP
- Import purchased downloads
- Import from Plex
- Import from Jellyfin

### AI Playlist Assistant

AI should help build and improve playlists using the user's existing library first.

Example requests:

- Build a three-hour country dance playlist.
- Build a cornhole playlist.
- Create a wedding warm-up set.
- Keep energy changes gradual.
- Avoid repeating artists too closely.
- Prefer local music.
- Suggest missing songs to acquire or stream.

AI suggestions should be reviewable before changing saved playlists.

### Experimental TIDAL Catalog

An isolated, disabled-by-default feasibility workspace for TIDAL authorization and catalog browsing.

Current scope:

- Configure Client ID, country code, and the default exact loopback redirect URI `http://127.0.0.1:8889/callback`
- Connect and disconnect with OAuth authorization code and PKCE
- Search TIDAL tracks, albums, and artists
- Check the documented public catalog API before diagnosing search
- Show granted OAuth scopes and exact missing-scope guidance
- Inspect session-only metadata and artwork
- Open supported items on the official TIDAL service
- Load My Collection playlists and separately label playlists owned by the account when `user.read`, `collection.read`, and `playlists.read` are granted
- Load all playlist-item pages, skip videos, and retain music-track order
- Copy the latest sanitized request diagnostic

TIDAL content remains separate from Local Music, Spotify, DancePilot playlists, AI workflows, decks, queues, transitions, and playback. The Live Event TIDAL source card only opens this isolated catalog or manages its connection.

## Development Order

1. Complete the Library Manager navigation shell and local Music Library view.
2. Finish the active TIDAL provider milestone and shared provider capability model.
3. Add saved DancePilot playlists.
4. Add Playlist Builder basic CRUD.
5. Add load playlist to Deck A/Deck B.
6. Add Event Templates.
7. Add Collections.
8. Add Library Health.
9. Add AI Playlist Assistant for eligible non-TIDAL content only.
10. Add Import Center improvements.

## Rules

- Do not break Live Event workflow.
- Do not move playback logic into Library Manager.
- Keep online providers behind a shared capability model so their Library Manager experiences remain consistent where supported.
- Keep playlist data source-aware but user-facing workflow song-first.
- Build and test after each focused section.
