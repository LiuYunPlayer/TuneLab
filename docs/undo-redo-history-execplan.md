# Add a navigable undo and redo history

This ExecPlan is a living document. The sections `Progress`, `Surprises & Discoveries`, `Decision Log`, and `Outcomes & Retrospective` must be kept up to date as work proceeds. At every stopping point, update `Progress`, and when this plan changes, append a short revision note at the bottom explaining what changed and why.

This document must be maintained in accordance with `docs/PLANS.md`. It is intentionally self-contained: a contributor should be able to implement the feature using only the current working tree and this file.

## Purpose / Big Picture

TuneLab already lets a user undo or redo one committed edit at a time, but it does not show the edits that have occurred since the project was opened and it cannot jump directly to an earlier or later point. After this work, the right sidebar will contain a History page. It will show the opened-project baseline followed by every committed project edit, identify the current state, and let the user click any retained state to move there. If the user moves backward and then makes a new edit, the superseded forward entries disappear; history remains a single line and does not retain alternate branches.

The feature applies to project edits that already participate in TuneLab's undo system, such as adding, deleting, moving, resizing, renaming, drawing parameters, changing properties, changing tempo, and running an editing script. View-only interactions such as selection, playhead movement, scrolling, zoom, playback, and opening a sidebar are not history entries. The export options currently stored as ordinary `Project` properties are also outside this plan because they do not participate in the existing command system. History lasts only for the current open project and is not serialized into `.tlp` files.

The user can see the feature working by making several different edits, opening History, clicking an earlier row, clicking a later row, and then branching from an earlier row with a new edit. The visible project data must follow the selected row, ordinary Ctrl+Z/Ctrl+Y must remain synchronized with the row selection, and the abandoned forward rows must vanish after the branch.

## Progress

- [x] (2026-07-22 02:41Z) Inspected the current document command stack, project lifecycle, undo/redo UI integration, sidebar architecture, and user-facing `Commit()` call sites.
- [x] (2026-07-22 02:41Z) Chose a linear command-history design and recorded the initial scope and decisions in this plan.
- [x] (2026-07-23 06:17Z) Replaced the two committed/redo stacks with an immutable-entry history list and cursor, added direct history navigation and branch truncation, and preserved one-step undo/redo, discard, and merge-notification behavior.
- [x] (2026-07-23 06:25Z) Gave the baseline, pending-command boundaries, committed entries, undo/redo destinations, cleared documents, and newly branched states monotonically allocated, collision-free `Head` values.
- [x] (2026-07-23 06:31Z) Added 12 focused data-layer tests for history creation, undo/redo and direct navigation, branching, clearing, invalid and blocked movement, discard behavior, notification batching, replay failure recovery, and state identity.
- [x] (2026-07-23 06:39Z) User ran the Milestone 1 focused test command: 21 passed, 0 failed, 0 skipped in 31 ms; no fixes were required.
- [x] (2026-07-23 06:44Z) Added `Commit(string description, string? detail = null)` to the hosting document API, normalized blank descriptions to `Edit Project`, and forwarded it through every direct `IDataObject` adapter.
- [x] (2026-07-23 07:49Z) Annotated all current user-facing project commit sites with canonical English action names, added useful script/track/part/property details where available, and distinguished creation from resize plus singular from batch edits where the interaction exposes that information.
- [x] (2026-07-23 07:49Z) Completed the static review and remaining parameterless-call audit: the 103 baseline project-edit calls are named, the four remaining parameterless calls under `TuneLab`/`TuneLab.GUI` are one compatibility forwarder and three independent settings-document commits, `git diff --check` reports no whitespace errors, and neither frozen ABI assembly nor any `PublicAPI` file is changed.
- [x] (2026-07-23 08:01Z) User reported that the Milestone 2 solution build, focused history tests, full `TuneLab.Tests`, and legacy compatibility tests all passed, and that the representative edit/undo smoke test showed no obvious abnormal behavior; no follow-up fix was required.
- [x] (2026-07-23 08:18Z) Implemented and integrated the full-height right-sidebar History page with an always-present baseline, translated action labels plus verbatim details, current-row selection, weaker forward rows, direct navigation, automatic current-row scrolling, branch reconciliation, and project-reset behavior.
- [x] (2026-07-23 08:32Z) User reran the Milestone 3 build after the namespace fixes and reported that it passed.
- [x] (2026-07-23 08:36Z) User rebuilt after the row hit-test fix and reported no remaining problem in the History sidebar smoke test; Milestone 3 build and live interaction validation are complete.
- [x] (2026-07-23 10:29Z) Added or reused all 64 History page, baseline, fallback, and canonical operation translations in every bundled translation file; the 15 non-English files each gained their 37 missing keys, the existing empty English file gained explicit source-language mappings, and a static audit found no missing or duplicate `[Menu]` keys.
- [x] (2026-07-23 11:13Z) Fixed `IDataValueController` so focus/blur and unchanged slider interactions detect data changes before closing their merge, discard the empty begin/end command pair, restore the saved-state and Undo/Redo status notification, and leave the next Ctrl+Z targeting the preceding real edit; added two focused regression tests.
- [x] (2026-07-23 11:31Z) Completed array-property history detail forwarding by carrying the detail supplied to `ArrayControllerBase.Bind()` through each `ElementRow` into scalar and nested `ElementWidget` bindings, so edits inside a labeled array retain the available parent-field context just as add/delete operations do.
- [x] Ask the user to run the required build and test commands manually, wait for the results, and resolve all reported failures.
- [x] Ask the user to perform the manual acceptance scenarios, wait for feedback, and record the observed result here.

## Surprises & Discoveries

- Observation: The original two-stack implementation already had the requested linear branch semantics. The list-and-cursor implementation can preserve them by truncating forward entries without replaying commands.
  Evidence: `TuneLab.Hosting.Foundation/Document/DataDocument.cs` still groups pending low-level commands into one `CompositeCommand`, and `Commit()` now removes entries after `HistoryPosition` before appending the new branch entry.

- Observation: Opening or creating a project already establishes the correct lifetime boundary for this feature.
  Evidence: `TuneLab/Data/ProjectDocument.cs` calls `Clear()` before attaching the newly created or deserialized `Project` in `SetProject()`.

- Observation: The original `Head` was a stack-depth value rather than a unique state identity. A saved state at depth N could be confused with a different state created by undoing to N-1 and committing a new edit.
  Evidence: The refactored `DataDocument.Head` now returns a stored token from a checked monotonic allocator, and `ProjectDocument.IsSaved` can continue comparing only `mLastSavedHead == Head` without branch-depth collisions.

- Observation: Low-level commands do not contain enough semantic information to produce useful user-facing descriptions. A `ModifyCommand` knows a before and after value but not whether the edit means “Move Notes”, “Change Lyric”, or “Set Voice”.
  Evidence: `TuneLab.Hosting.Foundation/Document/ICommand.cs` exposes only `Undo()` and `Redo()`, and the concrete commands in `DataProperty.cs`, `DataList.cs`, and related files are deliberately generic.

- Observation: The baseline contained exactly 107 parameterless application `Commit()` call sites. After annotation, only four remain under `TuneLab` and `TuneLab.GUI`; none commit project data.
  Evidence: `git grep -n -E "\.Commit\(\)" HEAD -- TuneLab TuneLab.GUI` reports 107 matches. `rg -n --glob '*.cs' "\.Commit\s*\(\s*\)" TuneLab TuneLab.GUI` now reports only `ForwardingDataObject.Commit()` plus the isolated extension-settings and Agent provider/settings document commits. The other 103 baseline sites now supply a canonical description directly or through the central script/property binding path.

- Observation: Retained commands already keep deleted objects alive for as long as they remain undoable, so a visible unlimited history does not introduce a new retention model. It does make the existing unbounded memory behavior an explicit product promise for the duration of an open project.
  Evidence: list removal commands capture the removed object, and committed `CompositeCommand` instances remain in `DataDocument.History` until the project changes or forward history is discarded.

- Observation: Validating a requested discard boundary before undoing is required once Heads become opaque identities, and it also fixes a pre-existing stale-Head failure mode.
  Evidence: the original `DiscardTo()` loop undid all pending commands when passed an unreachable Head. It now searches the saved before-state boundaries first and returns false without executing a command when the target is absent.

- Observation: The existing merge-notification counter can safely provide an outer, command-free scope for a multi-entry history jump.
  Evidence: `DataObject.ChangeNotifyFlag()` propagates nested counter changes through the current child tree, while the existing begin/end merge commands remain balanced inside that outer scope. `DataDocument.MoveToHistory()` therefore enters the direct scope only for jumps longer than one entry and closes it before publishing the single `StatusChanged` event.

- Observation: `DataDocument.StatusChanged` also fires for each low-level uncommitted preview command, even though neither the committed history nor its cursor changed.
  Evidence: `DataDocument.Push()` publishes `StatusChanged`, while `HistorySideBarContentProvider.OnDocumentStatusChanged()` now compares the rendered history count and cursor first and returns without scanning or rebuilding rows when both are unchanged.

- Observation: Importing both `TuneLab.GUI` and `Avalonia.Layout` makes the unqualified `HorizontalAlignment` name ambiguous, and an inherited control property can also hide the `VerticalAlignment` type name inside a control subclass.
  Evidence: The first user-run Milestone 3 build reported CS0104 and CS0176 in `HistorySideBarContentProvider.cs`; the fix fully qualifies both Avalonia alignment values and imports `Avalonia.Controls.Primitives` for `ScrollBarVisibility`.

- Observation: A History row's visual children must not participate in pointer hit testing when the row container owns the complete press/capture/release gesture.
  Evidence: User smoke testing found that clicking the blank part of a row navigated correctly while clicking its `TextBlock` did not. Marking the decorative row grid `IsHitTestVisible = false` makes every point in the row target the same `HistoryRow` input handler.

- Observation: The 15 non-English translation files already shared the same 27 reusable canonical `[Menu]` keys and therefore each lacked the same 37 History-specific keys; `en-US.toml` was intentionally empty because untranslated English source text is the runtime fallback.
  Evidence: The localization audit compared the 64 canonical History keys against each `[Menu]` table before editing, then repeated the comparison after editing and reported `required=64/64, duplicates=0` for all 16 files.

- Observation: A merge boundary is itself represented by commands, so comparing `Head` only after `EndMergeNotify()` cannot distinguish a real value edit from an empty focus/blur cycle.
  Evidence: `IDataValueController` previously captured its preview boundary after `BeginMergeNotify()`, then called `EndMergeNotify()` before comparing Heads. The end command always allocated another Head, causing an empty begin/end pair to be committed as `Edit Properties`.

- Observation: The array controller already stored the parent-field detail and every scalar or nested widget implementation could consume it, but the row-construction boundary silently dropped it.
  Evidence: `ArrayControllerBase.ReconcileRows()` created `ElementRow` without `mDetail`, and `ElementRow` called `ElementWidget.Create()` without its optional detail argument. Passing the value across those two calls activates the existing scalar, nested array/list, and extensible-object forwarding paths without changing their commit behavior.

## Decision Log

- Decision: Evolve the existing command log instead of taking a serialized project snapshot after every operation.
  Rationale: Existing commands already preserve object identity, issue the correct data events, and define the desired undo unit at `Commit()`. Full snapshots would duplicate large projects, force project-object replacement, reset transient editing relationships, and make every edit more expensive.
  Date/Author: 2026-07-22 / Codex

- Decision: Represent history as an ordered list plus a cursor, where cursor 0 is the opened-project baseline and cursor N is the state after the first N entries.
  Rationale: This directly models both undo and redo entries, makes arbitrary navigation simple, and implements branch replacement by removing entries at and after the cursor before appending a new commit.
  Date/Author: 2026-07-22 / Codex

- Decision: Keep history session-local and linear. Do not serialize it and do not preserve abandoned branches.
  Rationale: This is the requested behavior and matches the current clearing of redo commands on a new commit.
  Date/Author: 2026-07-22 / Codex

- Decision: Scope entries to committed project-data edits, not every UI interaction.
  Rationale: TuneLab's undo contract is rooted in `IDataObject`; selection, playhead, scroll, zoom, and playback are intentionally transient. Expanding history to those states would be a different feature and would make Ctrl+Z behavior surprising.
  Date/Author: 2026-07-22 / Codex

- Decision: Preserve the parameterless `Commit()` API as a compatibility fallback and add `Commit(string description, string? detail = null)` for named history entries.
  Rationale: Many generic data components depend on `IDataObject.Commit()`. The overload permits incremental annotation without breaking existing callers. Product-facing project edits must use the named overload by the end of this plan; the fallback description exists for defensive completeness.
  Date/Author: 2026-07-22 / Codex

- Decision: Store canonical English description keys in the hosting document layer and translate them only in the TuneLab UI.
  Rationale: `TuneLab.Hosting.Foundation` must not depend on the application-level translation system. Canonical strings such as `Move Notes` can reuse the existing `[Menu]` translations, while an optional raw detail can identify a script, track, part, parameter, or property where useful.
  Date/Author: 2026-07-22 / Codex

- Decision: Populate the existing empty `en-US.toml` with explicit identity mappings for the 64 History-related `[Menu]` keys while leaving unrelated English strings on the normal source-text fallback path.
  Rationale: The file is a bundled supported-language resource and this milestone requires every bundled translation file to contain the complete History vocabulary. Mapping each key to itself preserves the current English UI exactly while making the coverage audit uniform across all languages.
  Date/Author: 2026-07-23 / Codex

- Decision: Give each state a monotonically allocated `Head` within its `DataDocument`, including intermediate uncommitted states, and never derive a `Head` from list depth.
  Rationale: A state token must distinguish two different branches at the same depth. This also preserves the existing use of `Head` and `DiscardTo()` by drag and text-edit operations.
  Date/Author: 2026-07-22 / Codex

- Decision: Reserve the default `Head` value zero as an unissued sentinel, allocate real document states starting at one, and use checked integer increment.
  Rationale: Detached or empty UI helpers sometimes hold `default(Head)`, so keeping it distinct from every real document state prevents accidental matches. Checked overflow fails explicitly instead of silently reusing a prior token; an `int` remains ample for a single application session.
  Date/Author: 2026-07-23 / Codex

- Decision: Put History in the existing right sidebar as a full-height page.
  Rationale: The sidebar already has a tab rail, cached pages, and full-height content support. A full-height list needs its own scrolling and selection behavior, like the Agent and Script pages, rather than being wrapped in the property-card `ListView` used by simple providers.
  Date/Author: 2026-07-22 / Codex

- Decision: Render History as stable clickable rows inside the existing full-height scrolling infrastructure instead of maintaining a second selectable collection model.
  Rationale: Row state can be derived directly from `HistoryPosition`, so programmatic refresh never produces a selection-change callback. A click has exactly one navigation path through `MoveToHistory()`, and a rejected jump can restyle the rows from the actual cursor without committing or discarding another control's preview.
  Date/Author: 2026-07-23 / Codex

- Decision: A multi-step history jump emits `DataDocument.StatusChanged` once and coalesces settled data notifications across the replay when feasible with the existing merge-notification model.
  Rationale: Updating the title, menus, history list, property panels, and synthesis invalidation once per traversed entry would make long jumps unnecessarily expensive. The original commands still execute in order; only redundant observer refresh is batched.
  Date/Author: 2026-07-22 / Codex

- Decision: Do not modify `TuneLab.SDK`, `TuneLab.Foundation`, or either frozen `PublicAPI.Shipped.txt` file.
  Rationale: The document implementation lives in `TuneLab.Hosting.Foundation`, despite using the `TuneLab.Foundation` namespace. The plugin ABI assemblies are frozen and are unrelated to this host-only UI feature.
  Date/Author: 2026-07-22 / Codex

- Decision: Generic value bindings decide whether data changed before closing their merge, retain both the pre-merge and post-begin Heads, and remove an empty merge after it has been balanced.
  Rationale: The post-begin Head remains the correct rollback boundary for live previews, while the pre-merge Head is the only boundary that removes both notification commands. When the document was pushable at edit start, `Discard()` is safe because the balanced pair is the complete pending sequence and it also publishes the final status needed to clear the window's modified marker and restore Undo/Redo availability. If older pending commands existed, targeted `DiscardTo()` preserves them.
  Date/Author: 2026-07-23 / Codex

## Outcomes & Retrospective

Milestone 1 is implemented and validated. `DataDocument` now retains a linear read-only history, exposes its cursor, supports direct backward and forward navigation, truncates abandoned forward entries on a new commit, and coalesces settled data notifications during multi-entry jumps. Every baseline and edit boundary receives a never-reused Head; undo, redo, discard, clear, and branching restore or allocate the correct token. Twelve new focused tests plus the selected existing merge and linked-list undo tests passed in the user-run validation: 21 passed, 0 failed, 0 skipped. Milestone 2 is also implemented and validated: callers can commit a canonical description and optional detail through any hosting `IDataObject`, parameterless and blank-description commits retain the `Edit Project` fallback, and all 103 baseline project-edit call sites use named history entries. The user reported that the solution build, focused history tests including the two description cases, full application tests, and legacy compatibility tests all passed; representative creation, editing, multi-selection, and undo/redo smoke checks showed no obvious abnormal behavior. Milestone 3 is implemented and validated: the right sidebar has a cached full-height History page driven only by the document history and cursor, with clickable baseline and edit rows, current/forward styling, automatic scrolling, and reset/branch reconciliation. The initial build namespace errors and text-only click defect were fixed, and the user reported that the repeated build and History interaction smoke test showed no remaining problem. The Milestone 4 localization increment is implemented and statically audited across all 16 bundled languages; the final user-run regression commands plus cross-language, branching, script, project-reset, and long-jump acceptance scenarios remain.

The first post-localization review correction is implemented but not yet user-validated. Empty generic property edits no longer create a History row or change the retained saved Head, and the final status notification restores the visible saved/Undo state. Two new focused tests cover both a pure focus/blur cycle and a slider-style same-value change event. Array-property detail propagation remains the next source correction before final validation.

## Context and Orientation

TuneLab stores editable project data in a tree of `DataObject` instances. A leaf mutation creates an `ICommand`, immediately runs its `Redo()` method, and pushes it upward through the parent tree. The root is `DataDocument` in `TuneLab.Hosting.Foundation/Document/DataDocument.cs`. Commands made during one mouse gesture, text commit, property edit, or script execution accumulate in `mUncommitedCommands`; calling `Commit()` wraps them in a `CompositeCommand`. In this plan, a “history entry” means one such committed composite, not every internal property assignment made during the gesture.

`TuneLab.Hosting.Foundation/Document/DataObject.cs` implements the common delegation methods. `TuneLab.Hosting.Foundation/Document/IDataObject.cs` defines the interface used by application data types such as `IProject`, `ITrack`, `IPart`, and `INote`. Several adapters implement the interface by forwarding to another data object: `IDataObject.Wrapper` in the same interface file, `MultipleDataProperty<T>`, `MultipleDataPropertyObject`, and `MultipleDataPropertyArray` under `TuneLab.Hosting.Foundation/Property/`, plus `ForwardingDataObject` near the bottom of `TuneLab.GUI/GUI/Controllers/ArrayController.cs`. Any new `Commit` overload must be forwarded by all of them or the solution will not compile.

`Head`, defined in `TuneLab.Hosting.Foundation/Document/Head.cs`, is an opaque state token. Interactive operations capture a `Head`, repeatedly call `DiscardTo(capturedHead)` to undo only their uncommitted preview, recalculate the preview, and finally call `Commit()`. `ProjectDocument`, in `TuneLab/Data/ProjectDocument.cs`, also captures a `Head` when saving and uses equality to decide whether the window title should show the project as modified. The implementation must therefore preserve both uses while ensuring distinct branches never receive equal tokens.

`Editor`, in `TuneLab/UI/MainWindow/Editor/Editor.cs`, owns one `ProjectDocument` for its lifetime. It wires `StatusChanged` to the enabled state of the Undo and Redo menu items, exposes the current Ctrl+Z/Ctrl+Y actions, owns the right `SideBar`, and switches sidebar content according to `SideBarTab`. `SideTabBar.cs` creates the visible tab buttons, `SideBarTab.cs` defines their identities, `SideBar.cs` caches their content, and `TuneLab.GUI/GUI/Assets.cs` contains inline SVG icons. The new History page should follow this architecture rather than introduce another window.

Translations live in `TuneLab/Resources/Translations/*.toml`. English source text is the fallback language. Most edit verbs already exist under `[Menu]`, so history descriptions should use `description.Tr(TC.Menu)` and should reuse those keys. Add missing operation names to the `[Menu]` section of all 16 translation files, along with `History`, `Opened Project`, and `Edit Project`. A detail is user or plugin data and must be displayed verbatim after the translated action name; do not attempt to use it as a translation key.

The two plugin ABI assemblies, `TuneLab.SDK` and `TuneLab.Foundation`, are guarded by public API analyzers. This feature must not touch them. `TuneLab.Hosting.Foundation` is a host-internal assembly in architectural terms and is the correct place to evolve `DataDocument`, `DataObject`, and `IDataObject`.

The repository root `AGENTS.md` requires the user, not the coding agent, to perform builds and tests. An agent executing this plan must make the source changes, provide the exact commands below to the user, wait for the returned results, and then fix any reported failures. It must not run `dotnet build` or `dotnet test` itself.

## Plan of Work

### Milestone 1: Make document history addressable and state identities collision-free

At the end of this milestone, `DataDocument` will still support ordinary undo and redo, but it will also expose the complete linear history and a cursor, move to any cursor position, discard forward entries on a branch, and never confuse two states merely because they have the same depth. Automated data-layer tests will demonstrate these behaviors without depending on the Avalonia UI.

Create `TuneLab.Hosting.Foundation/Document/HistoryEntry.cs`. Define a public sealed `HistoryEntry` whose public surface is immutable and contains `Head State`, `string Description`, and `string? Detail`. Keep the replay command and the head before the edit internal so consumers cannot execute or mutate commands. Its internal constructor should receive the before head, after head, description, detail, and `ICommand`.

Refactor `TuneLab.Hosting.Foundation/Document/DataDocument.cs` from committed and redo stacks to `List<HistoryEntry> mHistory` plus `int mHistoryPosition`. Position 0 represents the baseline; position `mHistory.Count` represents the newest retained state. Expose a read-only view as `IReadOnlyList<HistoryEntry> History` and expose `int HistoryPosition`. `Undoable()` becomes true when there are no uncommitted commands and the position is greater than zero. `Redoable()` becomes true when there are no uncommitted commands and the position is less than the history count.

Add `public bool MoveToHistory(int position)`. Reject positions outside the inclusive range `0..History.Count`, and reject movement while uncommitted commands exist. Validate these conditions before changing data so an invalid request cannot partially modify the project. Moving backward must undo entries from `HistoryPosition - 1` down to the target. Moving forward must redo entries starting at `HistoryPosition` up to the target. Change the cursor only after each individual command succeeds. Emit `StatusChanged` once after the complete jump, including in a `finally` path if a later command throws after earlier entries succeeded, so observers see the valid cursor actually reached. Re-throw command exceptions rather than hiding data-layer failures.

Implement `Undo()` and `Redo()` through single-step internal helpers shared with `MoveToHistory()` so there is one source of truth. Preserve the current public return behavior: no available step returns false, and a successful step returns true. A normal one-step call still emits one `StatusChanged` event.

Replace count-derived heads with monotonic state tokens. Keep the public shape of `Head` unless implementation proves a wider counter is necessary; an `int` counter is adequate for one application session. Store each uncommitted command together with the head immediately before and immediately after it. `Push()` allocates a new head after the command has been applied. `DiscardTo(head)` first verifies that the target is the current head or one of the before-head boundaries in the uncommitted sequence, then undoes back to it while restoring the saved before heads. It must return false and change nothing for an unreachable head. A committed `HistoryEntry` records the head before the first pending command and the current head after the last pending command. Undo restores the former and redo restores the latter. `Clear()` removes pending and committed history and assigns a fresh baseline head; it must not reset the monotonic allocator in a way that can collide with a head previously issued by that document.

When committing while `HistoryPosition < History.Count`, remove the entries from `HistoryPosition` to the end before appending the new entry. Removing the entries is sufficient; no command execution occurs because those forward commands are already undone. This is the required “do not preserve overwritten old records” behavior.

For multi-step replay notification batching, add the smallest host-internal mechanism to `DataObject.cs` that enters and exits the existing notification merge state without pushing `BeginMergeNotifyCommand` or `EndMergeNotifyCommand` into the history. It may be a protected disposable scope used only by `DataDocument.MoveToHistory()`. The original entry commands, including their own balanced begin/end merge commands, must still execute normally inside the outer scope. Use the scope only when traversing more than one entry. Add a test that subscribes to settled `Modified` notifications and proves a multi-entry jump reports the final state without a settled notification for every intermediate entry. If implementation research demonstrates that a direct outer scope violates existing merge invariants, record the evidence in `Surprises & Discoveries`, omit data-notification batching, and retain the mandatory single `StatusChanged` emission; correctness takes priority over this optimization.

Add `tests/TuneLab.Tests/DataDocumentHistoryTests.cs`. Cover at least: commits append entries and advance the cursor; undo and redo move the cursor and data; moving directly backward and forward produces the right value; committing after undo removes forward entries; clear produces an empty history at position zero; invalid positions do nothing; movement with uncommitted commands does nothing; `DiscardTo()` still supports interactive preview; a branch at an old depth receives a different head from the abandoned state; and status/modified notification counts match the chosen batching behavior. Use small `DataStruct<int>` or `DataList<int>` objects attached to a `DataDocument` so the tests isolate the document mechanism.

After the edits, the executing agent must ask the user to run the focused test command listed in `Concrete Steps` and wait. The milestone is accepted when the user reports that the new history tests and existing document/merge tests pass.

### Milestone 2: Carry semantic descriptions from editing actions into history

At the end of this milestone, every user-facing project edit will create a useful history label rather than an anonymous numbered row. Existing callers that do not yet provide a name will continue to work with a translated fallback.

In `IDataObject.cs`, add `bool Commit(string description, string? detail = null)` alongside the existing `bool Commit()`. Add the matching virtual overload to `DataObject.cs`, delegating to its parent. Implement and forward it in `IDataObject.Wrapper`, `MultipleDataProperty<T>`, `MultipleDataPropertyObject`, `MultipleDataPropertyArray`, and `ForwardingDataObject`. Search the whole solution for every direct `IDataObject` implementer before considering this complete. `DataDocument.Commit()` should delegate to the named overload using canonical fallback key `Edit Project`; the named overload creates the `HistoryEntry`. Normalize blank descriptions to the same fallback so the sidebar never renders an empty action.

Audit the current project-facing `Commit()` calls with:

    rg -n "\.Commit\(\)" TuneLab TuneLab.GUI -g '*.cs'

Change each user edit to the named overload at the point where its meaning is clearest. Do not name low-level property mutations individually because several mutations may belong to one commit. Reuse a manageable vocabulary rather than creating a unique sentence for every call site. The vocabulary should cover at least tracks, parts, notes, pitch or automation, vibrato, tempo and time signatures, effects and properties, and scripts. Representative canonical keys are `Add Track`, `Delete Track`, `Move Track`, `Rename Track`, `Set Track Color`, `Add Part`, `Delete Part`, `Move Part`, `Resize Part`, `Split`, `Merge`, `Import Audio`, `Import Track`, `Set Voice`, `Set Instrument`, `Add Note`, `Delete Notes`, `Move Notes`, `Resize Notes`, `Change Lyric`, `Transpose Notes`, `Draw Pitch`, `Erase Pitch`, `Edit Automation`, `Add Vibrato`, `Edit Vibrato`, `Add Tempo`, `Edit Tempo`, `Delete Tempo`, `Add Time Signature`, `Edit Time Signature`, `Delete Time Signature`, `Edit Properties`, `Edit Effects`, and `Run Script`.

Use `detail` only when it adds stable context without making replay depend on live objects. Suitable details include the track or part name captured at commit time, a property display label, or a script name. Do not store references to UI controls or call translation functions in the document layer. Do not encode volatile data such as the current selection into a history entry.

Generic bindings in `TuneLab.GUI/GUI/Controllers/IDataValueController.cs`, `ExtensibleObjectController.cs`, and `ArrayController.cs` may use coarse labels such as `Edit Properties`, `Add Property`, `Delete Property`, `Add List Item`, and `Delete List Item`. Where their construction sites already possess a localized field label, extend the binding/controller constructor with an optional raw detail and pass it through; do not redesign the entire property configuration API solely to improve a label.

Change `TuneLab/Scripting/ScriptContext.cs` so a successful script edit commits as `Run Script`. If a stable script name is available from a script-tool invocation, thread it through as the detail; interactive pasted code and agent-generated code may omit detail. Preserve the existing promise that an entire script run is one undoable history entry and that failed scripts roll back without adding an entry.

Repeat the `rg` audit after annotation. Parameterless calls may remain in compatibility forwarders, isolated settings documents, tests specifically exercising fallback behavior, or truly non-project data, but every remaining match must be reviewed and explained in `Artifacts and Notes`. Add test assertions that named and fallback commits expose the expected immutable descriptions.

Ask the user to run the focused tests again and wait for results. This milestone is accepted when named entries survive undo/redo unchanged, branching removes the correct described entries, script edits produce one entry, and no existing edit loses undoability.

### Milestone 3: Add the History sidebar page

At the end of this milestone, a user can inspect and navigate the linear history without invoking Ctrl+Z repeatedly.

Create `TuneLab/UI/MainWindow/Editor/SideBar/History/HistorySideBarContentProvider.cs`. It should own a full-height Avalonia control containing a single scrolling history list. The first visual row is the opened-project baseline and targets position 0. Each `HistoryEntry` produces one row targeting its one-based position. The row at `ProjectDocument.HistoryPosition` is the selected/current row. Rows after the cursor remain visible as redoable forward history but use a weaker foreground or opacity. Rows before and at the cursor use normal foreground. The view must scroll the current row into view after ordinary undo, redo, a direct jump, or a new commit when the History page is visible.

The provider receives the editor's existing `ProjectDocument`, subscribes to `StatusChanged`, and rebuilds or incrementally reconciles the list on change. Correctness and stable selection come first; with session-scale lists, a simple rebuild is acceptable initially. Guard against selection feedback: programmatic selection during refresh must not call `MoveToHistory()` again. A user click calls `MoveToHistory(targetPosition)`. If movement returns false because an edit has uncommitted preview commands, restore the visual selection to the actual cursor. Do not automatically commit or discard another control's in-progress edit.

Render the label with `entry.Description.Tr(TC.Menu)`. If `Detail` is non-empty, append `: ` plus the verbatim detail. Render the baseline using translated `Opened Project`. The page title is translated `History`. The fallback `Edit Project` must also be translated. Do not display raw command type names.

Add `History` to `TuneLab/UI/MainWindow/Editor/SideBar/SideBarTab.cs`. Add an inline 24-by-24 history SVG to `TuneLab.GUI/GUI/Assets.cs`; use a clock or counter-clockwise arrow with short list marks, visually consistent with the existing monochrome sidebar icons. Add the tab in `SideTabBar.cs`. In `Editor.cs`, construct the provider, add the `SideBarTab.History` switch case, and call `SetFullContent` with the provider's root. The existing `mDocument.StatusChanged` subscription that enables Undo/Redo must remain intact.

Do not add a second history model in the UI. `DataDocument.History` and `HistoryPosition` are the sole source of truth, so Ctrl+Z, Ctrl+Y, menu actions, script edits, property edits, and row clicks always remain synchronized.

Ask the user to perform a manual build and launch using the commands in `Concrete Steps`, then wait for feedback. This milestone is accepted when the tab opens, rows appear after commits, Ctrl+Z/Ctrl+Y move the highlight, clicking rows changes project data, and switching projects resets the list to the baseline.

### Milestone 4: Complete localization, regression validation, and long-jump behavior

At the end of this milestone, the feature will be ready for normal use across supported languages and across TuneLab's main edit surfaces.

Update every file under `TuneLab/Resources/Translations/*.toml`. Add missing keys to `[Menu]`, reusing existing translations when a matching menu action already exists. All files must contain translations for `History`, `Opened Project`, `Edit Project`, and every new canonical action key introduced by Milestone 2. Keep TOML syntax valid and do not create duplicate keys in the same table. English source text remains the fallback and therefore requires no separate `en-US.toml` if the repository does not contain one.

Before final validation, close the two generic-property review gaps. In `TuneLab.GUI/GUI/Controllers/IDataValueController.cs`, compare the current Head with the post-begin preview Head before calling `EndMergeNotify()`. If no value command remains, balance the merge and remove the empty boundary commands back to the pre-merge Head without disturbing any older pending edit; when the document was initially pushable, use the normal discard path so observers receive the restored saved and Undo/Redo status. In `TuneLab.GUI/GUI/Controllers/ArrayController.cs`, carry the optional detail supplied to `ArrayControllerBase.Bind()` through `ElementRow` and every scalar or nested `ElementWidget` binding. Add focused coverage for the no-op value-binding behavior.

Review long jumps for responsiveness with a project containing at least 100 lightweight history entries. A direct move from newest to baseline and back should complete synchronously without the history list or title refreshing once per entry. Data commands must still execute in exact order. Do not add background replay: the project data tree is owned by the UI/data thread, and moving command execution to a worker would create races with rendering and synthesis.

Ask the user to run the full solution build and both repository test projects manually, then wait for the results. An SDK surface is not changed, so sample plugins under `tests/plugins/*` do not need rebuilding. Resolve all compiler errors, test failures, and manual acceptance defects before marking the plan complete.

## Concrete Steps

All paths and commands in this section assume the working directory is the repository root:

    D:\Code\TuneLab

The coding agent may use read-only searches and edit files, but, as required by `AGENTS.md`, it must not execute build or test commands. At each validation point it must present the relevant command to the user, ask the user to run it manually, wait for the reported output, and record the result in `Progress` and `Artifacts and Notes`.

Before editing, inspect the current state and locate all document adapters and commits:

    git status --short
    rg -n "class DataDocument|interface IDataObject|class .*: .*IDataObject|bool Commit\(" TuneLab.Hosting.Foundation TuneLab.GUI TuneLab -g '*.cs'
    rg -n "\.Commit\(\)" TuneLab TuneLab.GUI -g '*.cs'

After Milestone 1, ask the user to run:

    dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj --filter "FullyQualifiedName~DataDocumentHistoryTests|FullyQualifiedName~DataObjectMergeNotifyTests|FullyQualifiedName~SortedDataLinkedListUndoTests"

Expect a successful test summary with zero failed tests. Record the actual total because the number will depend on the final test methods; do not hard-code a guessed count in this plan.

After Milestone 2, inspect remaining anonymous commits and record why each is allowed:

    rg -n "\.Commit\(\)" TuneLab TuneLab.GUI -g '*.cs'

Because this milestone changes many UI call sites and generic property bindings, ask the user to run these commands manually from the repository root, one at a time:

    dotnet build TuneLab.sln -c Debug
    dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj --filter "FullyQualifiedName~DataDocumentHistoryTests|FullyQualifiedName~DataObjectMergeNotifyTests|FullyQualifiedName~SortedDataLinkedListUndoTests"
    dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj
    dotnet test legacy/compat/TuneLab.Hosting.Compat.Legacy.Tests/TuneLab.Hosting.Compat.Legacy.Tests.csproj

Expect the focused command in the current tree to report 25 passing tests: the previously validated 21, the two named-description tests, and the two no-op value-binding regressions. All commands must report zero failures. Then launch the already-built application by the user's normal method or with `dotnet run --project TuneLab/TuneLab.csproj -c Debug --no-build` and perform the Milestone 2 smoke scenarios below. If the scripting code receives dedicated automated tests during implementation, include their class name in the focused filter.

After Milestone 3, ask the user to build manually:

    dotnet build TuneLab.sln -c Debug

Expect `Build succeeded.` with zero errors. Warnings that existed before the change may be noted, but new warnings introduced by this feature must be fixed. After a successful user build, ask the user to launch the already-built application by their normal method or, from the repository root, with:

    dotnet run --project TuneLab/TuneLab.csproj -c Debug --no-build

After Milestone 4, ask the user to run all required validation commands manually, one at a time, and wait for each result:

    dotnet build TuneLab.sln -c Debug
    dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj
    dotnet test legacy/compat/TuneLab.Hosting.Compat.Legacy.Tests/TuneLab.Hosting.Compat.Legacy.Tests.csproj

The last test project guards legacy compatibility even though this feature should not edit the frozen legacy source. No sample plugin rebuild, pack, or install cycle is required because this plan does not change `TuneLab.SDK` or `TuneLab.Foundation`.

Useful read-only checks during implementation are:

    git diff --check
    git diff --stat
    git status --short

Do not discard unrelated user changes in a dirty worktree. Review diffs file by file and limit edits to this feature.

## Validation and Acceptance

Automated acceptance requires all new `DataDocumentHistoryTests` to pass, all existing `TuneLab.Tests` to pass, the legacy compatibility tests to pass, and the solution to build with no new warnings. The new tests must prove behavior, not merely inspect private fields.

For the generic binding regression, focus and blur an unchanged text property, then press and release a slider without moving it or changing its quantized value. Neither interaction may add a History row, add or remove the window's modified marker, or consume the next Ctrl+Z; that Ctrl+Z must still undo the preceding real edit.

Before the History sidebar exists, Milestone 2 smoke acceptance verifies that naming changes did not alter undo units. In a disposable project, create a note by dragging its end, create a part by dragging its end, rename and move a track or part, change a property and tempo, edit vibrato or automation, and run an editing script if one is available. After each representative edit, one Ctrl+Z must revert exactly that edit and one Ctrl+Y must restore it. Creating a new note or part must still undo the entire creation in one step, not leave a zero-length object behind. Also try a multi-selection move or delete so the new singular/plural description branching executes without changing the edit result. Report any exception, disabled undo/redo state, unexpected extra undo step, or data that does not round-trip.

Manual acceptance starts with a newly opened or newly created project. Open the History sidebar and verify that it contains only `Opened Project`. Add a note, move it, change its lyric, and draw a pitch or automation edit. Verify that four appropriately named rows appear in the same order and that the newest row is selected.

Click the state after adding the note. The note must return to its original position, lyric, and parameters, later rows must remain visible but visually weaker, and the clicked state must be selected. Click the newest row. The move, lyric, and parameter edit must return exactly, proving forward replay.

Click an earlier row again and then perform a different new edit. The previously visible forward rows must be removed immediately and replaced by the new entry. Ctrl+Y must now do nothing. This proves that abandoned branches are not retained.

Use Ctrl+Z and Ctrl+Y and the Edit menu while History is open. Each action must change project data and move the sidebar selection by exactly one row. The menu enabled states must match whether the cursor has a previous or next entry.

Save a project at depth N, undo once, and make a different edit so the new branch is also at depth N. The window must still show the project as modified. Save again and verify the modified indicator clears. This specifically accepts the collision-free `Head` change.

Run an interactive editing script that changes several objects. It must add exactly one `Run Script` row. Run a script that throws after making a partial edit. The project must return to its pre-script state and no history row may be added.

Start a drag or text edit that has created uncommitted preview commands and attempt to activate History before the edit commits. The history jump must not absorb, commit, or discard the preview. Depending on normal focus behavior the editor may first finish its own interaction; in either case there must never be a history jump while `Pushable()` is false.

Create at least 100 small entries, then jump from the newest state to `Opened Project` and back. The application must remain responsive after each synchronous jump, settle on the correct state, and not start synthesis or rebuild the History page once per intermediate entry. Record an approximate observed duration and hardware context in `Artifacts and Notes`; no strict millisecond threshold is required, but a multi-second freeze for 100 lightweight entries is a defect.

Open or create another project. The prior project's rows must disappear and the new project must begin with only `Opened Project`. Close and reopen a saved project and verify that history is not persisted across sessions.

Switch to at least Chinese and one non-Chinese bundled translation during manual verification. The page title, baseline, fallback, and operation verbs must be translated, while user-provided names in details remain unchanged.

## Idempotence and Recovery

The implementation changes only source code and in-memory behavior; it introduces no file-format migration, database migration, cache conversion, or destructive repository operation. Repeating searches, builds, tests, and manual scenarios is safe. Opening a project always starts a fresh history and must not alter the project file until the user explicitly saves.

`MoveToHistory()` must validate its target and the absence of uncommitted commands before executing anything. If an individual command unexpectedly throws during a multi-step move, update `HistoryPosition` only after each successful command, restore `Head` together with that successful step, emit `StatusChanged` in `finally`, and rethrow. The document will then expose the last valid state it actually reached instead of claiming the original target or an impossible cursor. Record any such failure and its reproducer in `Surprises & Discoveries` before fixing the command that threw.

If notification batching proves unsafe, remove only the direct batching scope and keep ordered replay plus one `StatusChanged`; do not fall back to project snapshots. If the sidebar is broken while the core tests pass, temporarily omit its tab registration while repairing the provider rather than reverting the tested history model. Do not use `git reset --hard` or overwrite unrelated working-tree changes.

Because forward entries contain commands for objects that may currently be detached, removing abandoned entries should simply drop their references. Do not call `Undo()` or `Redo()` while truncating already-undone forward history. Garbage collection will reclaim objects no longer referenced elsewhere.

## Artifacts and Notes

The initial source inventory is:

    DataDocument storage: TuneLab.Hosting.Foundation/Document/DataDocument.cs
    State token:          TuneLab.Hosting.Foundation/Document/Head.cs
    Command interface:    TuneLab.Hosting.Foundation/Document/ICommand.cs
    Composite undo unit:  TuneLab.Hosting.Foundation/Document/CompositeCommand.cs
    Delegation API:       TuneLab.Hosting.Foundation/Document/DataObject.cs
                          TuneLab.Hosting.Foundation/Document/IDataObject.cs
    Project lifecycle:    TuneLab/Data/ProjectDocument.cs
    Editor integration:   TuneLab/UI/MainWindow/Editor/Editor.cs
    Sidebar integration:  TuneLab/UI/MainWindow/Editor/SideBar/SideBar.cs
                          TuneLab/UI/MainWindow/Editor/SideBar/SideTabBar.cs
                          TuneLab/UI/MainWindow/Editor/SideBar/SideBarTab.cs
    Icons:                TuneLab.GUI/GUI/Assets.cs
    Translations:         TuneLab/Resources/Translations/*.toml

The intended history transition is:

    Baseline -> Add Note -> Move Notes -> Change Lyric
                                         ^ cursor after two undo operations

After a new `Draw Pitch` commit from that cursor, the retained line is:

    Baseline -> Add Note -> Draw Pitch

`Move Notes` and `Change Lyric` are dropped because their commands were forward history when the new edit committed.

The first implementation increment added:

    TuneLab.Hosting.Foundation/Document/HistoryEntry.cs
    TuneLab.Hosting.Foundation/Document/DataDocument.cs: list/cursor storage, direct navigation, and branch truncation
    TuneLab.Hosting.Foundation/Document/DataObject.cs: protected command-free merge-notification scope

The second implementation increment replaced depth-derived Heads with per-document monotonic allocation. Each pending command stores its before and after state, each committed entry keeps the first before-state and final after-state, undo/redo restore those saved values, and `Clear()` allocates a fresh baseline without resetting the allocator.

`tests/TuneLab.Tests/DataDocumentHistoryTests.cs` now contains 16 focused tests. The original 12 cover fallback entry creation, cursor and Head movement through undo/redo, direct jumps, forward-history truncation, fresh baselines after clear, invalid and pending-command rejection, full and targeted discard, same-depth branch identity, settled-notification batching, and the cursor/status result when a later command in a multi-step jump throws. Two later tests verify that named descriptions and details survive undo/redo unchanged and that blank descriptions normalize to `Edit Project`. The newest two drive a fake `IDataValueController<int>` through a focus/blur cycle and a slider-style same-value `ValueChanged` event; both assert that the existing immutable history entry, cursor, saved Head, observer-visible saved marker, and next real undo step are preserved. These four later tests have not yet been included together in a user-run focused command.

The named-commit forwarding audit found and updated every direct hosting implementation:

    TuneLab.Hosting.Foundation/Document/DataObject.cs
    TuneLab.Hosting.Foundation/Document/IDataObject.cs: IDataObject.Wrapper
    TuneLab.Hosting.Foundation/Property/MultipleDataProperty.cs
    TuneLab.Hosting.Foundation/Property/MultipleDataPropertyObject.cs
    TuneLab.Hosting.Foundation/Property/MultipleDataPropertyArray.cs
    TuneLab.GUI/GUI/Controllers/ArrayController.cs: ForwardingDataObject

All other current `IDataObject` classes inherit `DataObject` or one of these wrappers. `TuneLab.Hosting.Foundation/Utils/SaveFile.cs` also has a method named `Commit()`, but it is unrelated to `IDataObject` and therefore intentionally has no history-description overload. Frozen legacy SDK sources were not changed.

The final parameterless application-call audit is:

    TuneLab.GUI/GUI/Controllers/ArrayController.cs
        ForwardingDataObject.Commit(): retained compatibility forwarding overload; not a business commit site.
    TuneLab/UI/Settings/SettingsWindow.axaml.cs
        Initial snapshot for one isolated extension-settings DataDocument; never reaches the project document.
    TuneLab/UI/MainWindow/Editor/SideBar/Agent/AgentSideBarContentProvider.cs
        Initial provider selection and loaded provider settings in two isolated DataDocuments; neither reaches project history.

Repository-wide remaining matches are declarations/forwarders that preserve the fallback API, tests that intentionally exercise it, frozen legacy sources, or unrelated `SaveFile` and synthesis `IAudioSegment` APIs. Static review also ran `git diff --check` successfully; its only output was the repository's existing LF-to-CRLF conversion warning. `git diff --name-only -- TuneLab.SDK TuneLab.Foundation` and the cached equivalent were empty, and no `PublicAPI` file is modified. Per `AGENTS.md`, no build or test command was executed by the coding agent.

The user-run Milestone 1 validation result was:

    Command: dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj --filter "FullyQualifiedName~DataDocumentHistoryTests|FullyQualifiedName~DataObjectMergeNotifyTests|FullyQualifiedName~SortedDataLinkedListUndoTests"
    Result:  Passed 21, failed 0, skipped 0, total 21
    Duration: 31 ms (VSTest 17.11.1 x64, .NET 8.0)

The user-run Milestone 2 validation result was:

    Commands: dotnet build TuneLab.sln -c Debug
              focused DataDocumentHistory/DataObjectMergeNotify/SortedDataLinkedListUndo tests
              full tests/TuneLab.Tests/TuneLab.Tests.csproj
              legacy compatibility test project
    Result:   User reported that every command passed; no failure transcript or follow-up fix was needed.
    Smoke:    Representative user editing and undo/redo checks showed no obvious abnormal behavior.

The Milestone 3 source increment added:

    TuneLab/UI/MainWindow/Editor/SideBar/History/HistorySideBarContentProvider.cs
        Full-height scrolling rows, baseline/current/forward rendering, direct navigation,
        current-row scrolling, and no-op filtering for uncommitted preview notifications.
    TuneLab/UI/MainWindow/Editor/SideBar/SideBarTab.cs
    TuneLab/UI/MainWindow/Editor/SideBar/SideTabBar.cs
    TuneLab/UI/MainWindow/Editor/Editor.cs
        History tab registration, provider lifetime, and cached full-page hosting.
    TuneLab.GUI/GUI/Assets.cs
        Monochrome 24-by-24 history icon.

Static review after this increment found no whitespace errors, no changes in `TuneLab.SDK` or `TuneLab.Foundation`, and no modified `PublicAPI` file. Per `AGENTS.md`, the coding agent did not build or launch the application; Milestone 3 remains pending user validation.

The first user-run Milestone 3 build found four compile errors in `HistorySideBarContentProvider.cs`: two missing `ScrollBarVisibility` references, one ambiguous `HorizontalAlignment`, and one `VerticalAlignment.Center` lookup hidden by the inherited property. The source now imports `Avalonia.Controls.Primitives` and fully qualifies the Avalonia alignment values. The user repeated the build and reported success. During the subsequent live smoke test, clicking blank row space navigated but clicking directly on text did not; the decorative content grid now ignores hit testing so the containing row receives the entire click gesture. The user rebuilt and reported no remaining problem in the repeated History sidebar smoke test.

The localization increment covers 64 canonical History keys in every file under `TuneLab/Resources/Translations/*.toml`. The 15 non-English files reused 27 existing `[Menu]` entries and each added the same 37 missing entries; the previously empty `en-US.toml` now contains 64 identity mappings. A read-only key audit reported `required=64/64, duplicates=0` for every file. `git diff --numstat` showed only those intended additions, and `git diff --check` reported no whitespace errors; its output was limited to the repository's existing LF-to-CRLF conversion warnings. Per `AGENTS.md`, no build or test command was executed by the coding agent.

The no-op generic-binding correction changed only `TuneLab.GUI/GUI/Controllers/IDataValueController.cs` and the focused history test file. Static review confirmed that the binding records both merge boundaries, checks for value commands before closing, balances and discards an empty pair, and uses `Discard()` only when `Pushable()` proved there were no older pending commands. `git diff --check` reported no whitespace errors beyond the existing LF-to-CRLF warnings. Per `AGENTS.md`, the coding agent did not execute the new tests.

The array-detail correction completed the already-prepared binding path in `TuneLab.GUI/GUI/Controllers/ArrayController.cs`: `ReconcileRows()` now supplies the controller's stored parent detail to `ElementRow`, and the row supplies it to `ElementWidget.Create()`. Scalar elements therefore use that detail in `BindDataProperty`, while nested array, list, and extensible-object elements pass it to their child controllers. Object elements continue to use their own labeled child fields as the more specific detail. Per `AGENTS.md`, the coding agent did not build or test this correction.

Add concise test transcripts, the final reviewed list of remaining parameterless `Commit()` calls, user-reported build/test summaries, and the long-jump observation here as implementation proceeds. Do not paste full build logs.

## Interfaces and Dependencies

At the end of Milestone 1, `TuneLab.Hosting.Foundation/Document/HistoryEntry.cs` must provide an immutable public view equivalent to:

    public sealed class HistoryEntry
    {
        public Head State { get; }
        public string Description { get; }
        public string? Detail { get; }

        internal Head BeforeState { get; }
        internal ICommand Command { get; }
    }

Exact private field names may differ. The command and before-state members must not be public.

`DataDocument` must provide:

    public IReadOnlyList<HistoryEntry> History { get; }
    public int HistoryPosition { get; }
    public bool MoveToHistory(int position);
    public bool Undoable();
    public bool Redoable();

At the end of Milestone 2, `IDataObject` and `DataObject` must additionally provide:

    bool Commit(string description, string? detail = null);

The existing parameterless signature remains:

    bool Commit();

`DataDocument.Commit()` uses `Edit Project` when no description is supplied. A named commit stores the canonical English key and optional detail without translating either in `TuneLab.Hosting.Foundation`.

At the end of Milestone 3, `HistorySideBarContentProvider` must expose the icon, translated page name, and root `Control` needed by `Editor` to call `SideBar.SetFullContent`. It must read history only through `ProjectDocument.History`, `ProjectDocument.HistoryPosition`, and `ProjectDocument.StatusChanged`, and navigate only through `ProjectDocument.MoveToHistory()`.

Use only the .NET and Avalonia dependencies already referenced by the solution. Do not add a package. Do not change the `.tlp` serialization schema. Do not edit the frozen plugin ABI or `PublicAPI.Shipped.txt` files.

Revision note (2026-07-22, Codex): Created the initial ExecPlan after static analysis of the current command stack, commit sites, project lifecycle, sidebar architecture, and repository build constraints. The plan chooses a linear cursor over the existing commands, includes the discovered `Head` collision fix, and separates core, description, UI, and validation milestones so each can be verified independently.

Revision note (2026-07-23, Codex): Completed the first implementation Progress by replacing the committed/redo stacks with a linear history and cursor, adding direct navigation, branch truncation, and command-free notification batching. Updated the living sections to record the implemented source state and to make explicit that collision-free `Head` allocation and automated validation remain separate next steps.

Revision note (2026-07-23, Codex): Completed the collision-free state-identity Progress by recording before/after Heads around pending commands, restoring entry Heads during replay, allocating fresh baseline and branch tokens, and validating `DiscardTo()` targets before mutation. Updated stale pre-refactor observations and recorded the reserved-zero and checked-allocation decision.

Revision note (2026-07-23, Codex): Added the Milestone 1 focused test suite with 12 scenarios spanning normal history behavior, notification batching, state identity, invalid operations, interactive discard, and replay failure recovery. Added an explicit user-run validation Progress because repository policy forbids the coding agent from executing the test command.

Revision note (2026-07-23, Codex): Recorded the user's successful Milestone 1 focused validation (21 passed, 0 failed, 0 skipped in 31 ms) and updated the retrospective to mark the addressable-history and collision-free-state milestone as validated.

Revision note (2026-07-23, Codex): Completed the first Milestone 2 Progress by adding the named `Commit` overload, preserving parameterless and blank-description fallback behavior, forwarding the overload through all five direct adapter locations, and adding named/fallback history assertions. Recorded the adapter audit and left business call-site annotation as the next Progress.

Revision note (2026-07-23, Codex): Completed and statically reviewed the Milestone 2 business call-site annotation. Recorded the exact 107-to-4 parameterless-call audit, classified every remaining application call, tightened misleading or singular batch action names, confirmed the frozen ABI and PublicAPI files are untouched, and left user-run build/tests and smoke validation as the only Milestone 2 work still pending.

Revision note (2026-07-23, Codex): Recorded the user's successful Milestone 2 validation: solution build, focused and full application tests, legacy compatibility tests, and representative edit/undo smoke checks all passed with no obvious abnormal behavior. Marked the named history-entry milestone complete and left the sidebar, localization, and final end-to-end history acceptance work pending.

Revision note (2026-07-23, Codex): Completed the Milestone 3 source Progress by adding the cached full-height History provider, sidebar tab and icon, document-driven row reconciliation, direct navigation, current/forward styling, reset behavior, and current-row scrolling. Recorded the preview-notification filtering discovery and left the required user-run build and live sidebar smoke test as the next Progress.

Revision note (2026-07-23, Codex): Recorded and fixed the first Milestone 3 user-build failure by importing the Avalonia primitives namespace and fully qualifying alignment enum values that conflicted with TuneLab GUI names or inherited control properties. Left validation open pending the user's repeated build and launch result.

Revision note (2026-07-23, Codex): Recorded the successful repeated Milestone 3 build and the first live UI defect: text inside a History row intercepted the row's click path. Disabled hit testing on the row's decorative child grid so its entire surface has one navigation target, and left live validation pending a user rebuild and retest.

Revision note (2026-07-23, Codex): Recorded the user's successful rebuild and History sidebar retest after the row hit-test fix. Marked Milestone 3 interaction validation complete and left localization as the next Progress.

Revision note (2026-07-23, Codex): Completed the localization Progress by adding the complete 64-key History vocabulary to all 16 bundled translation files, including explicit identity mappings in the existing empty English resource. Recorded the uniform 37-key non-English gap, the English coverage decision, and the successful missing/duplicate-key plus whitespace audits; full user-run regression and manual acceptance remain next.

Revision note (2026-07-23, Codex): Recorded the first two findings from the comprehensive staged-change review as explicit pending Progress items: preventing merge-only no-op history entries in generic value bindings and completing array-property detail propagation. These corrections must be completed before the final build, regression, and manual acceptance steps.

Revision note (2026-07-23, Codex): Completed the first staged-review correction by making generic value bindings test for real value commands before closing their merge, removing empty begin/end pairs back to the pre-edit Head, and restoring the final saved/Undo status notification without discarding older pending work. Added focus/blur and same-value slider regressions, updated the focused-test expectation to 25, and left array-property detail forwarding as the next Progress.

Revision note (2026-07-23, Codex): Completed the second staged-review correction by carrying a labeled array's stored detail across the `ElementRow` construction boundary into the existing scalar and nested widget binding paths. Recorded why object-element child fields retain their own more specific labels and left user-run build, regression tests, and manual acceptance as the remaining Progress.
