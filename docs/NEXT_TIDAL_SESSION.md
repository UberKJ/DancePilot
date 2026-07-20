# Next DancePilot Coding Session: TIDAL Catalog Reliability

## Session Objective

Make real-account TIDAL search, My Playlists, and playlist-track loading work reliably before expanding TIDAL into shared queue, deck, or playback surfaces.

## Before Coding

Using the connected TIDAL account:

1. Click **CHECK TIDAL API**.
2. Search for one common artist or track.
3. Click **LOAD MY PLAYLISTS**.
4. If playlists appear, select one and click **LOAD SELECTED PLAYLIST TRACKS**.
5. After each failure, click **COPY DIAGNOSTIC** and retain the sanitized text.

Do not share access tokens, refresh tokens, authorization codes, client secrets, or passwords.

## Task 1: Reproduce and Classify

Scope: diagnostics only; no UI redesign.

- Capture the exact status, endpoint, response content type, error code, and granted scopes for search and playlist failures.
- Determine whether each failure is authorization, endpoint/query shape, JSON:API relationship mapping, pagination, or UI state.
- Add a failing regression test for every confirmed code defect before fixing it.

Stop when the failures are explained by evidence. Do not guess at alternative endpoints.

## Task 2: Repair TIDAL Services

Scope: `DancePilot.Services/Tidal` and focused tests only.

- Correct public API requests using the current documented TIDAL contract.
- Repair JSON:API mapping, relationship hydration, pagination, artwork URLs, or scope handling only where diagnostics prove it is necessary.
- Keep retries bounded and diagnostics sanitized.
- Keep TIDAL data session-only unless storage is explicitly approved.

Completion check:

- API health succeeds.
- Search returns tracks, albums, and artists.
- My Collection and owned playlists load.
- Selected playlist tracks load in their original order.
- Focused tests pass.

## Task 3: Integrate the Existing TIDAL UI

Scope: TIDAL view model and TIDAL Library Manager section only.

- Correct busy, connected, empty, missing-scope, failure, and reconnect states.
- Show artwork with a safe fallback.
- Keep search results, playlists, and selected-item details visually consistent.
- Keep TIDAL tracks non-playable in DancePilot.
- Do not add deck, queue, transition, crossfade, download, capture, or AI actions.

Completion check:

- Every TIDAL button has a clear enabled/disabled state.
- Every operation produces a useful operator message.
- Empty results look different from API failures.
- Disconnect clears session content and encrypted tokens.

## Task 4: Validate and Checkpoint

- Run the complete test suite.
- Build the full solution in Debug x64.
- Perform the real-account workflow again.
- Record remaining failures with sanitized diagnostics.
- Keep this checkpoint separate from later provider-neutral queue/player work.

## Deferred to Later Sessions

1. Shared provider browser components for Local, Spotify, TIDAL, and future providers.
2. Provider-neutral playlist and queue adapters.
3. Provider-neutral player and output-device abstractions.
4. TIDAL deck/playback support only through an officially supported and approved route.
5. Broader Library Manager visual polish.

## Ready-to-Use Codex Prompt

```text
Continue DancePilot's TIDAL catalog reliability milestone.

Read docs/NEXT_TIDAL_SESSION.md and follow its task order. Start by inspecting the latest sanitized TIDAL diagnostics from the real connected account. Reproduce each confirmed failure in a focused automated test, then repair only the TIDAL service or mapping layer responsible.

Required live workflows:
- Check API health
- Search tracks, albums, and artists
- Load My Collection and owned playlists
- Load all tracks from a selected playlist in order

After the service layer is reliable, fix only the existing TIDAL UI states and artwork behavior. Keep TIDAL TrackDisplayItem instances non-playable. Do not add unofficial playback, deck or queue actions, provider blending, downloading, capture, audio analysis, or AI use.

Run the complete test suite and build DancePilot.sln in Debug x64. Preserve all existing user changes and report any live-account step that still requires operator interaction.
```
