# TIDAL Feasibility Guardrails

## Status

Version 0.5.0a documents feasibility boundaries only. TIDAL is an experimental provider candidate, not committed Live Event support.

The Local Library Manager remains the next implementation milestone. No TIDAL implementation should displace or expand that milestone.

## Experimental Scope

TIDAL catalog and API research may be evaluated only in an isolated experimental area. Experiments must remain separate from production provider lists, Live Event workflows, decks, queues, and AI workflows.

Research does not imply approval to ship a TIDAL integration.

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

TIDAL must not be exposed inside Live Event mode until the project owner confirms that TIDAL has approved the intended public-event and multi-provider use.

This written approval decision is required before reconsidering TIDAL for decks, queues, transitions, or any other Live Event surface.

## Initial UI Boundary

Any initial TIDAL UI must:

- Remain isolated from Spotify and local result lists.
- Remain outside Live Event mode.
- Include all required TIDAL attribution.
- Clearly identify itself as experimental.

## Required Implementation Phases

TIDAL work must proceed in this order:

1. Documentation and feasibility.
2. Authorization experiment.
3. Catalog experiment.
4. Written approval decision.
5. Only then reconsider Live Event integration.

Each experiment requires its own scoped decision. Completion of an earlier phase does not authorize a later phase.

## Current Decision

The current phase is documentation and feasibility only. This document does not authorize TIDAL authorization, catalog integration, playback, Live Event integration, deck or queue support, or AI use.
