# DancePilot Operator Workflow

## Purpose

This document defines how DancePilot should behave for a live operator.

The live interface should stay simple, predictable, and safe during an event.

## Main Workspaces

### Live Event

Used during an event.

Includes:

- Deck A
- Deck B
- Current queue
- Source browser
- Playback transport
- Transition controls
- Output status

### Library Manager

Used before an event.

Includes:

- Music library
- Playlist builder
- Event templates
- Collections
- Import tools
- Library health
- AI playlist assistant

## Live Event Rules

1. Sources are for finding music.
2. Decks own queued songs after songs are added.
3. Deck controls act only on that deck.
4. Main transport controls current playback.
5. Main output volume exists in one place only.
6. Deck faders are deck levels, not master output volume.
7. Randomize acts on the current deck queue only.
8. Replace and Append must be separate actions.
9. Play Now is separate from Add to Deck.
10. Provider source should be visible but should not dominate the deck workflow.

## Source Browser

The Source Browser helps the operator find music.

Initial sources:

- Spotify
- TIDAL Experimental Catalog
- Local Library
- YouTube

Future sources:

- Plex
- Other providers

Source browser actions:

- Add selected song to Deck A
- Add selected song to Deck B
- Play Now
- Preview if supported later

For TIDAL, the source browser is a non-playable handoff. Its only Live Event actions are Connect, Disconnect, and Open Catalog. Search, supported playlist views, and official TIDAL links remain in the isolated Library Manager catalog. TIDAL does not expose Play Now, Add to Deck, queue, transition, fader, or playback actions.

## Deck Workflow

Each deck should show:

- Selected/active state
- Playing state
- Paused state
- Queue count
- Current song
- Artist
- Album art
- Next song if available
- Deck source for current item

Deck actions:

- Select deck
- Play/pause deck
- Randomize deck queue
- Clear deck queue
- Fader/level control

## Queue Workflow

Queue actions should be predictable:

- Move up
- Move down
- Remove
- Clear pending
- View Deck A queue
- View Deck B queue

Future queue actions:

- Play next
- Send to other deck
- Duplicate item
- Lock item
- Mark as do not remove

## Transition Workflow

Transition modes should be clear:

- Off
- Same Deck
- Alternate Decks
- Auto

Auto should prefer the other deck if ready, otherwise continue within the same deck.

Transitions must not stop playback when only one deck has songs.

## Volume and Fader Workflow

Main Output Volume:

- One true output volume.
- Lives in the bottom transport/status area.

Deck Faders:

- Relative deck levels.
- Do not change main output volume.
- Should affect local/app-managed playback when technically possible.

Spotify Connect limitations:

- Spotify Connect does not provide independent two-deck audio streams inside DancePilot.
- Spotify volume remains main output/device volume.

## Testing Format

Use this format for issue notes:

```text
What I clicked:
What I expected:
What happened:
Was audio playing:
Deck A/B state:
Source:
```
