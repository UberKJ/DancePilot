# DancePilot Provider Model

## Purpose

This document defines the long-term direction for music sources in DancePilot.

DancePilot should be song-first, not provider-first. Providers are locations where songs can be found or played.

## Provider Parity Principle

Online integrations should use the same DancePilot browsing, library, playlist, queue, deck, and player surfaces whenever their supported capabilities match. A provider should not receive a separate-feeling workflow merely because its API is different.

Parity does not mean pretending every provider has the same permissions. Each provider declares its capabilities, and DancePilot consistently enables, disables, or explains actions from that capability contract. Provider-specific code remains behind the contract; provider-specific legal, attribution, storage, and playback restrictions remain enforced.

## Current Providers

- Local Library
- Spotify
- TIDAL catalog, library, and playlist integration; playback remains approval-gated

## Future Providers

Possible future providers:

- Plex
- Jellyfin
- Apple Music
- Amazon Music
- YouTube Music if technically and legally feasible

TIDAL is the active provider-integration milestone. Its catalog, library, and playlist capabilities should move toward the shared provider experience now. Its Live Event playback capabilities remain gated by an approved official TIDAL integration route. See `TIDAL_FEASIBILITY.md` for the required legal, playback, UI, data, and approval boundaries.

## Provider Roles

A provider may support different capabilities.

Examples:

- Search catalog
- Read playlists
- Import metadata
- Provide album art
- Play through controlled playback
- Play through local app-managed audio
- Support seek
- Support pause/resume
- Support volume
- Support true crossfade

Not every provider supports every feature.

## Capability-Based Design

The UI and transition engine should ask what a provider can do instead of assuming all providers behave the same.

Example capabilities:

- CanSearch
- CanLoadPlaylists
- CanPlay
- CanSeek
- CanPause
- CanControlVolume
- CanCrossfade
- CanProvideArtwork
- CanProvideDuration
- RequiresExternalPlayer

## Legal Playback Boundary

DancePilot must not rip, download, capture, bypass, or process protected provider audio unless explicitly legal and allowed by provider terms.

For services like Spotify Connect, DancePilot controls playback legally but does not own the audio stream.

For TIDAL, DancePilot must not use custom audio handling. Only official, unmodified TIDAL playback modules may be considered. TIDAL crossfade, overlap, mixing, provider-to-provider audio blending, capture, downloading, ripping, waveform analysis, and audio analysis are prohibited.

TIDAL metadata, artwork, playlists, and playback data must not be sent to AI services.

The experimental TIDAL catalog uses only `https://openapi.tidal.com/v2/`. Catalog search resolves JSON:API relationship identifiers through documented collection resources. User playlist collection access is capability-gated by the exact granted scopes and remains session-only; it does not make TIDAL a playable provider.

## Local Provider

Local files are app-managed audio. DancePilot can support deeper control here:

- File path playback
- Album art from metadata/cache
- Faders
- Local transitions
- Local crossfade if multiple local players are supported

## Plex Provider Direction

Plex should initially be treated as a personal library source.

Possible first-stage features:

- Connect to Plex server
- Read music libraries
- Import metadata
- Import playlists
- Use Plex artwork
- Link Plex track to DancePilot song record

Playback control should be investigated separately.

## Spotify Provider Direction

Spotify remains a legal Spotify Connect playback-control provider.

DancePilot should not claim independent Spotify deck crossfading unless a legal supported playback model exists.

## Song-First UX

The operator should choose songs and events, not providers.

When multiple sources have the same song, DancePilot should eventually show availability and preferred source.

Example:

- Local FLAC
- Local MP3
- Plex
- Spotify

Future preferred source order could be configured by the user.

## Development Rule

Do not add a new provider until the provider capability model is stable enough to avoid duplicating provider-specific logic across the UI.

TIDAL research must remain isolated from Local and Spotify result lists. The Live Event selector may provide a TIDAL connection and catalog-navigation handoff, but TIDAL must not participate in decks, queues, transitions, faders, playback, mixed-provider results, or AI workflows. Those capabilities may be reconsidered only after authorization and catalog experiments and a written approval decision confirming TIDAL's approval of the intended public-event and multi-provider use.
