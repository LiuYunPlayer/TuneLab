# V1 Test Format — Export

Writes `.tltest`, the same small JSON project format the import entry reads. Import a `.tltest`
file, export it again and re-import: tracks and notes (pos / dur / pitch / lyric) should survive
the round trip.

## What this entry is here to prove

**This is a separate entry from the import side**, with its own implementation class, its own
name, its own introduction — this page — and its own settings.

Open **Settings → Extensions**: this entry has one field, *Indent Output*, deciding whether the
written JSON is indented or squeezed onto one line. Open the exported file in a text editor to see
which it was. The import entry's field (*Fallback Track Name*) is unrelated and lives in its own
bucket; setting one never leaks into the other.

Note that the routing matrix has always treated the two directions separately — a different
package could take over just the export of `.tltest`. Declaring them as two entries simply makes
the manifest say what routing already assumed.
