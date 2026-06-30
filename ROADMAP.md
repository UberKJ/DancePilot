# DancePilot Roadmap

## Current Milestone

Stabilize deck workflow and polish the live event UI.

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
- [x] Repository cleanup for generated build artifacts

## Current Sprint: Deck UI State Pass

- [ ] Fix Deck B selected/highlight/glow state so it matches Deck A
- [ ] Show clear active deck state
- [ ] Show clear playing deck state
- [ ] Show clear paused/stopped state
- [ ] Make non-active deck visually dimmer without hiding important information
- [ ] Add or verify queue counts on each deck
- [ ] Add clear status text after deck actions
- [ ] Verify selected item, playing item, and active deck are visually distinct
- [ ] Confirm Deck A and Deck B use the same style and binding pattern

## Known Issues

- Deck A highlights correctly when selected.
- Deck B appears active logically but does not show the same selected highlight.

## Next Sprint: Deck Workflow Verification

- [ ] Load full playlist to Deck A
- [ ] Load full playlist to Deck B
- [ ] Randomize Deck A only
- [ ] Randomize Deck B only
- [ ] Confirm randomize does not pull from the wrong selected source playlist
- [ ] Confirm Append Playlist and Replace Deck are clear and separate
- [ ] Confirm currently playing item is not removed or skipped during randomize
- [ ] Confirm normal deck play and Play Now remain separate behaviors
- [ ] Confirm mixed source playback works end to end

## Backlog: Local Library Improvements

- [ ] Improve scan progress display for large libraries
- [ ] Add rescan/cancel behavior if not already reliable
- [ ] Detect missing local files and mark clearly
- [ ] Add local library stats view
- [ ] Improve duplicate detection
- [ ] Add optional folder exclusions
- [ ] Improve album-art fallback display

## Backlog: Event Workflow Features

- [ ] Save event/session queue sets
- [ ] Restore last event session
- [ ] Add event notes or venue profile
- [ ] Add request list
- [ ] Add simple history of played songs
- [ ] Add quick filters for slow, fast, country, rock, line dance, etc.

## Future Expansion

Only after core deck workflow and local library behavior are stable.

- [ ] Additional provider investigation
- [ ] Provider interface cleanup if needed
- [ ] Smarter event recommendations

## Repository and Maintenance

- [x] Ignore bin, obj, and publish folders
- [x] Keep generated build artifacts out of Git
- [ ] Consider GitHub Releases for packaged builds
- [ ] Consider a basic CI build/test workflow
- [ ] Keep future commits focused by sprint type

## Development Rules

1. Work on one layer at a time.
2. Do not mix playback fixes with UI polish unless required.
3. Do not add providers during stabilization.
4. Do not commit generated build outputs.
5. Keep Codex prompts narrow.
6. Build and test after each focused change.
7. Manual event workflow testing matters as much as automated tests.

## Manual Test Note Format

```text
What I clicked:
What I expected:
What happened:
Was audio playing:
Deck A/B state:
Source:
```
