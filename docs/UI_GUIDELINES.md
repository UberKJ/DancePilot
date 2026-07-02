# DancePilot UI Guidelines

## Purpose

This document defines the UI direction for DancePilot.

The interface should support live operation first. It should be clear from several feet away, avoid duplicate concepts, and make the current state obvious.

## Main Principles

1. Live Event UI should stay focused.
2. Library Manager UI can be deeper and more detailed.
3. Do not mix planning tools into the live deck view.
4. Do not use dead controls.
5. Build and run after every UI change.
6. Do not move resources and change bindings in the same task.

## Live Event UI Areas

- Top status/chrome
- Deck A
- Deck B
- Source browser
- Queue panel
- Transition panel
- Bottom transport/output bar

## Deck Visual Rules

Each deck must clearly show:

- Selected deck
- Playing deck
- Paused deck
- Queue count
- Current track
- Next track if available
- Deck fader/level

Deck A accent should remain visually distinct from Deck B.

## Transport Rules

Transport should show current playback and output.

It should avoid source-specific labels where possible.

Preferred labels:

- Current Track
- Output
- Progress
- Main Volume
- Previous
- Back 15
- Play/Pause
- Forward 15
- Next

## Volume and Fader Rules

- Main Volume appears in one place only.
- Deck controls are faders/levels, not master volume.
- Deck faders should not change main volume.
- Main volume should not change deck faders.

## Transition UI Rules

Transition mode labels should be understandable during a live event.

Preferred modes:

- Off
- Same Deck
- Alternate Decks
- Auto

Auto means use the other deck if ready, otherwise continue same deck.

## Theme and Style Rules

Future theme work should be done in small passes.

Safe order:

1. Create resource dictionary only.
2. Build and run.
3. Move text brushes only.
4. Build and run.
5. Move panel brushes only.
6. Build and run.
7. Move button styles only.
8. Build and run.

Never remove or rename a resource until its replacement exists and the app launches.

## Manual UI Testing

For every UI prompt:

1. Build.
2. Run.
3. Test only the changed section.
4. Check for XAML startup errors.
5. Commit only after launch is confirmed.
