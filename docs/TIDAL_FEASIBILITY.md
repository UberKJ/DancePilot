# TIDAL Feasibility Guardrails

## Status

Version 0.6.2b repairs the public-API catalog contract and adds documented playlist discovery. TIDAL is now the active provider-integration milestone, with the goal of matching Spotify's DancePilot experience wherever supported and approved capabilities allow. The Live Event source entry remains a connection and catalog handoff only until an approved playback route is confirmed.

Catalog API access is disabled by default, while the selector entry remains visible. When explicitly enabled and authorized, catalog content remains inside the Experimental TIDAL Catalog section. Catalog, library, playlist, and shared provider-model work may now proceed; deck, queue, transition, and playback work remains approval-gated.

Implemented backend components:

- OAuth authorization code flow with PKCE and state validation
- System-browser authorization with an exact configured loopback redirect URI
- Access-token refresh and logout/token deletion
- Windows user-scoped encrypted token storage under the DancePilot local application data folder
- Mock-testable catalog operations for track search, track lookup, album search, and artist search
- Public API health check using the documented known-album request
- JSON:API relationship paging and two-stage track, album, artist, and playlist hydration
- My Collection and owned-playlist discovery when the required scopes are granted
- Playlist-item paging with music-track hydration, video skipping, and original order restoration
- TIDAL-specific metadata DTOs and API error mapping
- Bounded retries for temporary failures and rate limits only
- One refresh-and-retry attempt after an HTTP 401
- Sanitized request diagnostics written to `%LOCALAPPDATA%\DancePilot\startup.log`

This status does not constitute approval for production or public-event use.

Version 0.6.2b operator features:

- A persistent TIDAL choice in the Live Event source selector
- A connection-status card with Connect, Disconnect, and Open Catalog actions only
- Connect and disconnect through the system browser and OAuth PKCE backend
- Visible connection status and detailed operation errors
- Isolated track, album, and artist catalog search
- Session-only result collections with selected-item details
- Official TIDAL links for displayed catalog items where available
- TIDAL attribution beside catalog content
- Granted-scope display, API health check, reconnect, and sanitized diagnostic copy
- Distinct status for API availability, missing playlist scope, playlist availability, empty search, and detailed API failure

Playlist collection loading uses the documented `userCollectionPlaylists/{id}/relationships/items` resource. DancePilot labels collection/favorite playlists separately from playlists returned by the documented `filter[owners.id]=me` query. Playlist content remains session-only and is never added to DancePilot playlists, decks, queues, playback, or AI workflows.

## Public API Contract

DancePilot uses only `https://openapi.tidal.com/v2/` and the current public TIDAL OpenAPI reference.

- Health: `GET /albums/59727856?countryCode={countryCode}`
- Search: `GET /searchResults/{escapedQuery}` followed by documented relationship and collection resources
- Search hydration: `/tracks`, `/albums`, and `/artists` with `filter[id]` and supported `include` values
- Playlist collection: `GET /userCollectionPlaylists/{id}/relationships/items`
- Owned playlists: `GET /playlists?filter[owners.id]=me`
- Playlist items: `GET /playlists/{playlistId}/relationships/items`

The authorization request asks for `user.read`, `search.read`, `collection.read`, and `playlists.read`. Granted scopes come from the token response and are preserved in encrypted token storage. The documented token-response `user_id` is used as the collection identifier when present; otherwise the public API's documented `me` alias is used.

Search requires `user.read` and `search.read`. The playlist collection requires `user.read` and `collection.read`; playlist hydration and owned-playlist queries also require `playlists.read`. Missing scopes prevent the affected request and produce an exact reconnect instruction. Refreshing an old token is not treated as granting newly enabled scopes.

Every failed public API request records the operation, method, sanitized URI, status, content type, JSON:API error fields, retry delay, and granted scopes. Access tokens, refresh tokens, authorization codes, PKCE verifiers, and client secrets are never included.

## Configuration

The existing Settings dialog contains an isolated **TIDAL EXPERIMENT** section with:

- Experimental catalog toggle, off by default
- Client ID
- Redirect URI
- Two-letter country code

The default redirect URI is `http://127.0.0.1:8889/callback`. Register that exact URI in the TIDAL Developer Dashboard and use the same value in DancePilot. If it must change, the configured host, fixed port, and callback path must be updated together in both places. Only an HTTP loopback URI with an explicit port and non-root callback path is accepted.

DancePilot does not accept or store a TIDAL Client Secret. Tokens are stored with Windows current-user data protection and token values must never be logged.

## Experimental Scope

TIDAL catalog and API research may be evaluated only in an isolated experimental area. The Live Event selector may expose an experimental connection and catalog-navigation handoff, but catalog results must remain separate from Spotify and Local result lists and from decks, queues, transitions, playback, and AI workflows.

Research does not imply approval to ship a TIDAL integration.

The implemented service is catalog-only. Catalog metadata remains in TIDAL-specific models and is not merged into Spotify or Local result lists.

## Playback Boundary

DancePilot must not implement TIDAL playback through custom audio handling. Only official, unmodified TIDAL playback modules may be considered.

The following are explicitly prohibited:

- TIDAL crossfade
- TIDAL overlap
- TIDAL mixing
- TIDAL-to-local audio blending
- TIDAL-to-Spotify audio blending
- Audio capture
- Downloading
- Ripping
- Waveform or audio analysis

## AI Data Boundary

DancePilot must not pass TIDAL metadata, artwork, playlists, or playback data to an AI service. TIDAL content must not be added to AI-assisted search, recommendations, playlist generation, analysis, or other AI workflows.

## Live Event Boundary

The Live Event source selector may show TIDAL for connection status and navigation to the isolated catalog. This handoff must always remain selectable, including while disconnected, and must clearly state: "TIDAL catalog access is available. DancePilot deck playback is not enabled."

The source card must not display catalog results, Play Now, deck actions, queue actions, transitions, faders, or playback controls. It does not authorize public-event use or multi-provider playback. Written confirmation that TIDAL has approved the intended public-event and multi-provider use is required before reconsidering any of those capabilities.

## Initial UI Boundary

Any initial TIDAL UI must:

- Remain isolated from Spotify and local result lists.
- Use the Live Event source entry only as a connection and catalog-navigation handoff.
- Include all required TIDAL attribution.
- Clearly identify itself as experimental.

Version 0.6.2b keeps the TIDAL source-selector handoff and reuses the isolated Experimental TIDAL Catalog section under Library Manager. It does not add a deck control, queue action, transition action, fader, mixed result list, or playback control.

## Required Implementation Phases

TIDAL work must proceed in this order:

1. Documentation and feasibility.
2. Authorization experiment.
3. Catalog experiment.
4. Written approval decision.
5. Only then reconsider public-event and playback integration.

Each experiment requires its own scoped decision. Completion of an earlier phase does not authorize a later phase.

## Current Decision

The authorization, catalog-search, and scoped playlist integration, including its Library Manager UI and Live Event navigation handoff, is implemented for feasibility testing and hardening. Finishing TIDAL and achieving provider parity wherever capabilities permit is the current project priority. Selecting TIDAL does not yet make it a playable provider.

This implementation does not authorize or provide TIDAL playback, public-event use, mixing, deck or queue support, transitions, provider blending, downloading, audio analysis, or AI use. Written TIDAL approval and an official supported integration route are still required before enabling those capabilities. Local Library Manager feature expansion resumes after the active TIDAL provider milestone.
