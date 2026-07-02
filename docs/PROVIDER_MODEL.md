# DancePilot Provider Model

## Purpose

This document defines the long-term direction for music sources in DancePilot.

DancePilot should be song-first, not provider-first. Providers are locations where songs can be found or played.

## Current Providers

- Local Library
- Spotify

## Future Providers

Possible future providers:

- Plex
- Jellyfin
- Tidal
- Apple Music
- Amazon Music
- YouTube Music if technically and legally feasible

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
