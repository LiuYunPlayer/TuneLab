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
- [x] README rendered
- [ ] Voice bank configured

## Parameters

| Parameter | Meaning              | Default |
| --------- | -------------------- | ------- |
| speed     | Playback speed       | 1.0     |
| pitch     | Global pitch offset  | 0       |
| breath    | Breathiness amount   | 0.3     |

## Notes

> This is a blockquote. The README content is entirely author-defined —
> the host only discovers the file, picks the language variant, and renders it.

```csharp
var engine = new TestVoiceEngine();
engine.Synthesize(part);
```

See [the parameters section](#parameters) above for tuning tips.
