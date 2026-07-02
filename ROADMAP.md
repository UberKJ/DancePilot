# DancePilot Roadmap

## Current Milestone

Stabilize deck workflow, transitions, and operator confidence.

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

## Current Sprint: Operator Experience Pass

- [ ] Verify deck fader/level controls have real behavior or are clearly labeled as future/limited
- [ ] Improve current/next track readability from a distance
- [ ] Improve bottom transport wording so it is source-neutral where possible
- [ ] Confirm transition status messages are clear during live use
- [ ] Confirm queue counts and deck state remain accurate during transitions

## Next Major Milestone: Library Manager

Library Manager is the planning workspace for DancePilot.

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

## Next Sprint: Library Manager Shell

- [ ] Add a Library Manager navigation entry or workspace shell
- [ ] Show current local library data in a planning-focused view
- [ ] Keep Live Event workflow unchanged
- [ ] Add placeholder sections for Playlist Builder, Event Templates, Collections, Library Health, Import Center, and AI Assistant
- [ ] Avoid adding provider integrations in this sprint

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
- [ ] Tidal investigation
- [ ] Other provider investigation
- [ ] Smarter event recommendations

See `docs/PROVIDER_MODEL.md`.

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
