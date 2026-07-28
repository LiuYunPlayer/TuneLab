# Suite Voice

The **voice half** of a one-package-many-plugins test suite. A deliberately minimal engine: one
bank, no phonemes worth speaking of — its job is to prove that two capabilities in the same
package stay independent.

## What to check

- This package's detail window has **two tabs**; switching tabs swaps this text for the format one.
- Each tab carries its own type badge (`Voice` here, `Format` next door) — the header no longer
  repeats that set.
- The **Settings** button sits at the right end of the tab row and appears only on this tab, because
  only this entry implements `IExtensionSettings`. Switch to the format tab and it disappears.
- Both entries share the package header (icon / version / author / package description).

## Setup

1. Create a MidiPart on any track.
2. Set its sound source to **Suite Voice**.
3. Type lyrics and synthesize.
