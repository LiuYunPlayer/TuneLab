# V1 Test Voice

A **test voice engine** for exercising the extension detail page. This paragraph is here to
show *italic*, **bold**, ~~strikethrough~~ and `inline code` rendering, plus an auto-link:
https://example.com

![portrait](portrait.webp)

## Getting Started

Before this voice can be used, a little setup is needed:

1. Open **Settings → Extensions** and pick a voice bank.
2. Create a new *voice part* on any track.
3. Type lyrics — each note gets its phonemes.

- [x] Manifest loaded
- [x] Introduction rendered
- [ ] Voice bank configured

## Parameters

| Parameter | Meaning              | Default |
| --------- | -------------------- | ------- |
| speed     | Playback speed       | 1.0     |
| pitch     | Global pitch offset  | 0       |
| breath    | Breathiness amount   | 0.3     |

## Notes

> This is a blockquote. The introduction content is entirely author-defined — the host only
> resolves the path declared in the manifest (per-language via localizations) and renders it.

```csharp
var engine = new TestVoiceEngine();
engine.Synthesize(part);
```

See [the parameters section](#parameters) above for tuning tips.

## Canvas fields

These note fields are drawn by the extension itself; their values are plain JSON:

- **Stretch Curve** (`stretch`, also on the core phoneme as `stretch`): an array of exactly 5 numbers, each in 0..1, for evenly spaced points from the start to the end of the note. The default `[0, 0.25, 0.5, 0.75, 1]` is linear. Missing or out-of-range entries are treated as linear at that point.
- **Canvas Images** (`image_demo`): one number, the star's rotation in degrees (any value; 0 = upright).
