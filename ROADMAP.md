# DancePilot Roadmap

## Current Milestone

Finish the TIDAL integration and make online-provider workflows consistent wherever provider capabilities allow.

TIDAL is the active provider milestone. The target is for Spotify and TIDAL to share the same browsing, playlist, queue, deck, and player experience whenever the provider's supported and approved APIs permit it. Provider-specific limitations must be represented as capabilities rather than separate, inconsistent workflows.

## Core Complete or Mostly Complete

- [x] WinUI 3 desktop application foundation
- [x] Source-aware queue items
- [x] Deck A and Deck B queue structure
- [x] Persistent local music library index
- [x] Local track file paths saved for playback
- [x] Local search from saved library index
- [x] Local queued-track playback from saved LocalPath
- [x] Deck randomize separated from source playlist randomize
- [x] Full playlist loading no longer intentionally capped at 24 songs
- [x] Deck UI state/highlight pass
- [x] Safer transition behavior for one-deck and two-deck use
- [x] Repository cleanup for generated build artifacts

## Supporting Sprint: Operator Experience Pass

- [ ] Verify deck fader/level controls have real behavior or are clearly labeled as future/limited
- [ ] Improve current/next track readability from a distance
- [ ] Improve bottom transport wording so it is source-neutral where possible
- [ ] Confirm transition status messages are clear during live use
- [ ] Confirm queue counts and deck state remain accurate during transitions

## Active Major Milestone: TIDAL Provider Integration

Already implemented in the current working milestone:

- [x] OAuth authorization code flow with PKCE
- [x] Encrypted token storage, refresh, and disconnect
- [x] Public catalog health check
- [x] Track, album, and artist search and hydration
- [x] My Collection and owned-playlist discovery
- [x] Playlist-track paging with music-track order preserved
- [x] Sanitized request diagnostics and detailed error mapping
- [x] Isolated TIDAL catalog workspace and Live Event source handoff
- [x] Keep TIDAL out of playback, decks, queues, provider blending, and AI until the required capability and approval gates are satisfied

Next implementation steps:

- [ ] Complete real-account authorization and catalog smoke testing after the development-environment upgrade
- [ ] Harden catalog, playlist, artwork, empty-state, reconnect, and rate-limit behavior from operator testing
- [ ] Introduce a shared provider capability contract for search, library, playlists, queue, deck, and playback actions
- [ ] Present Spotify and TIDAL through consistent online-source browsing and playlist UI where their capabilities match
- [ ] Map TIDAL catalog tracks into provider-neutral display and selection models without merging or persisting restricted TIDAL data incorrectly
- [ ] Confirm the approved TIDAL partner/playback route for DancePilot and document the decision
- [ ] If approved, integrate only the official, unmodified TIDAL playback route and add TIDAL queue/deck/player support through the shared provider contract
- [ ] Run provider-parity tests plus manual authorization, catalog, playlist, queue, deck, transition, and player tests for every enabled capability

Full TIDAL DJ playback is an approval-gated part of this milestone. A TIDAL DJ subscription enables playback through approved DJ integrations; it does not by itself expose unrestricted playback to a new application through the public catalog API.

See `docs/TIDAL_FEASIBILITY.md` and `docs/PROVIDER_MODEL.md`.

## Following Major Milestone: Library Manager

Library Manager is the planning workspace for DancePilot.

The Local Library Manager remains the planning workspace and resumes as the next major milestone after the TIDAL provider pass.

Live Event remains focused on decks, queues, transitions, and playback.
Library Manager supports planning and preparation.

Planned sections:

- Music Library
- Playlist Builder
- Event Templates
- Collections
- Library Health
- Import Center
- AI Playlist Assistant

See `docs/LIBRARY_MANAGER.md`.

## Completed Sprint: Library Manager Shell

- [x] Add a Library Manager navigation entry or workspace shell
- [x] Show current local library data in a planning-focused view
- [x] Keep Live Event workflow unchanged
- [x] Add placeholder sections for Playlist Builder, Event Templates, Collections, Library Health, Import Center, and AI Assistant
- [x] Complete the shell before beginning the later TIDAL integration pass

## Backlog: Playlist Builder

- [ ] Create DancePilot playlist
- [ ] Rename playlist
- [ ] Delete playlist
- [ ] Add local songs to playlist
- [ ] Remove songs from playlist
- [ ] Reorder playlist songs
- [ ] Save playlist
- [ ] Load playlist to Deck A
- [ ] Load playlist to Deck B
- [ ] Show estimated duration
- [ ] Show song count

## Backlog: Event Templates

- [ ] Create event template
- [ ] Add event sections
- [ ] Attach playlists or collections to sections
- [ ] Save reusable templates
- [ ] Load event template into decks/queue

## Backlog: Local Library Improvements

- [ ] Improve scan progress display for large libraries
- [ ] Add rescan/cancel behavior if not already reliable
- [ ] Detect missing local files and mark clearly
- [ ] Add local library stats view
- [ ] Improve duplicate detection
- [ ] Add optional folder exclusions
- [ ] Improve album-art fallback display

## Future Expansion

Only after core deck workflow and local library behavior are stable.

- [ ] Provider capability model
- [ ] Plex investigation
- [ ] Jellyfin investigation
- [ ] Other provider investigation
- [ ] Smarter event recommendations

See `docs/PROVIDER_MODEL.md`.

## TIDAL Feasibility and Approval Gates

TIDAL catalog integration is the active provider milestone. Live Event playback remains approval-gated; until that gate is satisfied, its Live Event source entry is limited to connection status and navigation to the catalog.

- [x] Document TIDAL feasibility guardrails
- [x] Evaluate authorization in an isolated experiment
- [x] Evaluate catalog access in an isolated experiment
- [x] Add an isolated Live Event source-selector handoff with no playback actions
- [x] Repair public API health, JSON:API catalog search, and relationship hydration
- [x] Add scope-gated My Collection, owned-playlist, and playlist-track loading
- [x] Record the project owner's direction that finishing TIDAL and provider parity are the next product priority
- [ ] Obtain and record TIDAL's approval for DancePilot's intended public-event, multi-provider, and DJ playback use
- [ ] Only then enable TIDAL deck, queue, transition, or playback capabilities through an approved official integration

TIDAL must not be added to decks, queues, playback, mixed provider result lists, or AI workflows during feasibility work. See `docs/TIDAL_FEASIBILITY.md`.

Local Library Manager resumes after the active TIDAL provider milestone.

## Repository and Maintenance

- [x] Ignore bin, obj, and publish folders
- [x] Keep generated build artifacts out of Git
- [ ] Consider GitHub Releases for packaged builds
- [ ] Consider a basic CI build/test workflow
- [ ] Keep future commits focused by sprint type

## Project Documents

- `docs/OPERATOR_WORKFLOW.md`
- `docs/LIBRARY_MANAGER.md`
- `docs/PROVIDER_MODEL.md`
- `docs/TIDAL_FEASIBILITY.md`
- `docs/UI_GUIDELINES.md`

## Development Rules

1. Work on one layer at a time.
2. Do not mix playback fixes with UI polish unless required.
3. Do not add providers during stabilization.
4. Do not commit generated build outputs.
5. Keep Codex prompts narrow.
6. Build and test after each focused change.
7. Manual event workflow testing matters as much as automated tests.
8. Do not move resources and change bindings in the same UI task.

## Manual Test Note Format

```text
What I clicked:
What I expected:
What happened:
Was audio playing:
Deck A/B state:
Source:
```
