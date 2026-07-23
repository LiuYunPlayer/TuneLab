using System;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using Xunit;

namespace TuneLab.Tests;

public class DataDocumentHistoryTests
{
    [Fact]
    public void Commit_AppendsHistoryAndKeepsFinalPendingHead()
    {
        var (document, value) = CreateDocument();
        var baseline = document.Head;
        int statusChanged = 0;
        document.StatusChanged += () => statusChanged++;

        value.Set(10);
        var committedState = document.Head;

        Assert.NotEqual(baseline, committedState);
        Assert.True(document.Commit());
        Assert.Single(document.History);
        Assert.Equal(1, document.HistoryPosition);
        Assert.Equal(committedState, document.Head);
        Assert.Equal(committedState, document.History[0].State);
        Assert.Equal("Edit Project", document.History[0].Description);
        Assert.Null(document.History[0].Detail);
        Assert.True(document.Undoable());
        Assert.False(document.Redoable());
        Assert.Equal(2, statusChanged);
    }

    [Fact]
    public void NamedCommit_StoresDescriptionAndDetailAcrossUndoRedo()
    {
        var (document, value) = CreateDocument();
        value.Set(1);

        Assert.True(value.Commit("Move Notes", "Verse 1"));
        var entry = Assert.Single(document.History);
        Assert.Equal("Move Notes", entry.Description);
        Assert.Equal("Verse 1", entry.Detail);

        Assert.True(document.Undo());
        Assert.True(document.Redo());

        Assert.Same(entry, Assert.Single(document.History));
        Assert.Equal("Move Notes", entry.Description);
        Assert.Equal("Verse 1", entry.Detail);
    }

    [Fact]
    public void BlankDescription_UsesFallbackAndPreservesDetail()
    {
        var (document, value) = CreateDocument();
        value.Set(1);

        Assert.True(document.Commit(" \t\r\n", "Gain"));

        var entry = Assert.Single(document.History);
        Assert.Equal("Edit Project", entry.Description);
        Assert.Equal("Gain", entry.Detail);
    }

    [Fact]
    public void DataValueBinding_FocusBlurWithoutChangePreservesHistoryAndSavedState()
    {
        AssertUnchangedDataValueEdit(raiseValueChanged: false);
    }

    [Fact]
    public void DataValueBinding_UnchangedSliderEventPreservesHistoryAndNextUndo()
    {
        AssertUnchangedDataValueEdit(raiseValueChanged: true);
    }

    [Fact]
    public void UndoRedo_MoveCursorDataAndHead()
    {
        var (document, value) = CreateDocument();
        var baseline = document.Head;
        CommitValue(document, value, 1);
        var firstState = document.Head;
        CommitValue(document, value, 2);
        var secondState = document.Head;

        Assert.True(document.Undo());
        Assert.Equal(1, value.Value);
        Assert.Equal(1, document.HistoryPosition);
        Assert.Equal(firstState, document.Head);
        Assert.True(document.Undoable());
        Assert.True(document.Redoable());

        Assert.True(document.Undo());
        Assert.Equal(0, value.Value);
        Assert.Equal(0, document.HistoryPosition);
        Assert.Equal(baseline, document.Head);
        Assert.False(document.Undoable());
        Assert.True(document.Redoable());

        Assert.True(document.Redo());
        Assert.Equal(1, value.Value);
        Assert.Equal(firstState, document.Head);

        Assert.True(document.Redo());
        Assert.Equal(2, value.Value);
        Assert.Equal(2, document.HistoryPosition);
        Assert.Equal(secondState, document.Head);
        Assert.True(document.Undoable());
        Assert.False(document.Redoable());
    }

    [Fact]
    public void MoveToHistory_JumpsBackwardAndForward()
    {
        var (document, value) = CreateDocument();
        CommitValue(document, value, 1);
        CommitValue(document, value, 2);
        CommitValue(document, value, 3);

        Assert.True(document.MoveToHistory(1));
        Assert.Equal(1, value.Value);
        Assert.Equal(1, document.HistoryPosition);
        Assert.Equal(document.History[0].State, document.Head);

        Assert.True(document.MoveToHistory(3));
        Assert.Equal(3, value.Value);
        Assert.Equal(3, document.HistoryPosition);
        Assert.Equal(document.History[2].State, document.Head);
    }

    [Fact]
    public void CommitAfterUndo_TruncatesForwardHistory()
    {
        var (document, value) = CreateDocument();
        CommitValue(document, value, 1);
        CommitValue(document, value, 2);
        CommitValue(document, value, 3);
        var abandonedSecondState = document.History[1].State;
        var abandonedThirdState = document.History[2].State;

        Assert.True(document.MoveToHistory(1));
        value.Set(10);
        var branchState = document.Head;
        Assert.True(document.Commit());

        Assert.Equal(10, value.Value);
        Assert.Equal(2, document.History.Count);
        Assert.Equal(2, document.HistoryPosition);
        Assert.Equal(branchState, document.History[1].State);
        Assert.DoesNotContain(document.History, entry => entry.State == abandonedSecondState);
        Assert.DoesNotContain(document.History, entry => entry.State == abandonedThirdState);
        Assert.False(document.Redoable());
        Assert.False(document.Redo());
    }

    [Fact]
    public void Clear_ResetsHistoryAndAllocatesFreshBaseline()
    {
        var (document, value) = CreateDocument();
        var originalBaseline = document.Head;
        value.Set(1);
        var pendingState = document.Head;
        Assert.True(document.Commit());
        var committedState = document.Head;

        document.Clear();

        Assert.Empty(document.History);
        Assert.Equal(0, document.HistoryPosition);
        Assert.Equal(1, value.Value);
        Assert.NotEqual(default(Head), document.Head);
        Assert.NotEqual(originalBaseline, document.Head);
        Assert.NotEqual(pendingState, document.Head);
        Assert.NotEqual(committedState, document.Head);
        Assert.False(document.Undoable());
        Assert.False(document.Redoable());
    }

    [Fact]
    public void MoveToHistory_InvalidPositionsDoNothing()
    {
        var (document, value) = CreateDocument();
        CommitValue(document, value, 1);
        var head = document.Head;
        int statusChanged = 0;
        document.StatusChanged += () => statusChanged++;

        Assert.False(document.MoveToHistory(-1));
        Assert.False(document.MoveToHistory(document.History.Count + 1));
        Assert.False(document.MoveToHistory(document.HistoryPosition));

        Assert.Equal(1, value.Value);
        Assert.Equal(1, document.HistoryPosition);
        Assert.Equal(head, document.Head);
        Assert.Equal(0, statusChanged);
    }

    [Fact]
    public void MoveToHistory_WithUncommittedCommandsDoesNothing()
    {
        var (document, value) = CreateDocument();
        CommitValue(document, value, 1);
        value.Set(2);
        var pendingHead = document.Head;
        int statusChanged = 0;
        document.StatusChanged += () => statusChanged++;

        Assert.False(document.MoveToHistory(0));
        Assert.False(document.Undo());
        Assert.False(document.Redo());

        Assert.Equal(2, value.Value);
        Assert.Equal(1, document.HistoryPosition);
        Assert.Equal(pendingHead, document.Head);
        Assert.False(document.Undoable());
        Assert.False(document.Redoable());
        Assert.Equal(0, statusChanged);
    }

    [Fact]
    public void Discard_RestoresDataAndPendingBoundaryHead()
    {
        var (document, value) = CreateDocument();
        var baseline = document.Head;

        value.Set(1);
        value.Set(2);

        Assert.True(document.Discard());
        Assert.Equal(0, value.Value);
        Assert.Equal(baseline, document.Head);
        Assert.Empty(document.History);
        Assert.True(document.Pushable());
        Assert.False(document.Discard());
    }

    [Fact]
    public void DiscardTo_RestoresReachablePreviewAndRejectsStaleHead()
    {
        var (document, value) = CreateDocument();
        var baseline = document.Head;

        value.BeginMergeNotify();
        var previewStart = document.Head;
        value.Set(1);
        var stalePreviewState = document.Head;

        Assert.True(document.DiscardTo(previewStart));
        Assert.Equal(0, value.Value);
        Assert.Equal(previewStart, document.Head);
        Assert.False(document.DiscardTo(previewStart));

        value.Set(2);
        var currentPreviewState = document.Head;
        Assert.False(document.DiscardTo(stalePreviewState));
        Assert.Equal(2, value.Value);
        Assert.Equal(currentPreviewState, document.Head);

        Assert.True(document.DiscardTo(previewStart));
        value.Set(3);
        value.EndMergeNotify();
        Assert.True(document.Commit());

        Assert.Equal(3, value.Value);
        Assert.True(document.Undo());
        Assert.Equal(0, value.Value);
        Assert.Equal(baseline, document.Head);
    }

    [Fact]
    public void BranchAtSameDepthGetsDifferentHead()
    {
        var (document, value) = CreateDocument();
        CommitValue(document, value, 1);
        CommitValue(document, value, 2);
        var abandonedStateAtDepthTwo = document.Head;

        Assert.True(document.Undo());
        value.Set(20);
        var branchStateAtDepthTwo = document.Head;
        Assert.True(document.Commit());

        Assert.Equal(2, document.HistoryPosition);
        Assert.Equal(20, value.Value);
        Assert.NotEqual(abandonedStateAtDepthTwo, branchStateAtDepthTwo);
        Assert.Equal(branchStateAtDepthTwo, document.Head);
        Assert.Equal(branchStateAtDepthTwo, document.History[1].State);
    }

    [Fact]
    public void MoveToHistory_MultiStepBatchesStatusAndSettledNotifications()
    {
        var (document, value) = CreateDocument();
        CommitValue(document, value, 1);
        CommitValue(document, value, 2);
        CommitValue(document, value, 3);
        int statusChanged = 0;
        int settledModified = 0;
        document.StatusChanged += () => statusChanged++;
        value.Modified.Subscribe(() => settledModified++);

        Assert.True(document.MoveToHistory(0));
        Assert.Equal(0, value.Value);
        Assert.Equal(1, statusChanged);
        Assert.Equal(1, settledModified);

        statusChanged = 0;
        settledModified = 0;

        Assert.True(document.MoveToHistory(3));
        Assert.Equal(3, value.Value);
        Assert.Equal(1, statusChanged);
        Assert.Equal(1, settledModified);
    }

    [Fact]
    public void MoveToHistory_CommandFailureLeavesLastReachedStateAndNotifiesOnce()
    {
        var document = new TestDocument();
        int value = 0;
        document.Apply(new DelegateCommand(
            () => throw new InvalidOperationException("undo failed"),
            () => value = 1));
        Assert.True(document.Commit());
        var firstState = document.Head;

        document.Apply(new DelegateCommand(
            () => value = 1,
            () => value = 2));
        Assert.True(document.Commit());
        int statusChanged = 0;
        document.StatusChanged += () => statusChanged++;

        var exception = Assert.Throws<InvalidOperationException>(() => document.MoveToHistory(0));

        Assert.Equal("undo failed", exception.Message);
        Assert.Equal(1, value);
        Assert.Equal(1, document.HistoryPosition);
        Assert.Equal(firstState, document.Head);
        Assert.Equal(1, statusChanged);
    }

    static (DataDocument Document, DataStruct<int> Value) CreateDocument()
    {
        var document = new DataDocument();
        var value = new DataStruct<int>(document);
        return (document, value);
    }

    static void CommitValue(DataDocument document, DataStruct<int> value, int nextValue)
    {
        value.Set(nextValue);
        Assert.True(document.Commit());
    }

    static void AssertUnchangedDataValueEdit(bool raiseValueChanged)
    {
        var (document, value) = CreateDocument();
        value.Set(1);
        Assert.True(document.Commit("Edit Properties", "Value"));
        var savedHead = document.Head;
        var existingEntry = Assert.Single(document.History);
        bool savedStateMarker = true;
        document.StatusChanged += () => savedStateMarker = document.Head == savedHead;

        var controller = new TestDataValueController<int>();
        using var bindings = new DisposableManager();
        controller.BindDataProperty(value, bindings);

        controller.BeginEdit();
        if (raiseValueChanged)
            controller.ChangeValue(value.Value);
        controller.CommitEdit();

        Assert.Same(existingEntry, Assert.Single(document.History));
        Assert.Equal(1, document.HistoryPosition);
        Assert.Equal(savedHead, document.Head);
        Assert.True(savedStateMarker);
        Assert.True(document.Undoable());

        Assert.True(document.Undo());
        Assert.Equal(0, value.Value);
        Assert.Equal(0, document.HistoryPosition);
    }

    sealed class TestDocument : DataDocument
    {
        public void Apply(ICommand command)
        {
            command.Redo();
            Push(command);
        }
    }

    sealed class DelegateCommand(Action undo, Action redo) : ICommand
    {
        public void Undo() => undo();
        public void Redo() => redo();
    }

    sealed class TestDataValueController<T> : IDataValueController<T> where T : notnull
    {
        public IActionEvent ValueWillChange => mValueWillChange;
        public IActionEvent ValueChanged => mValueChanged;
        public IActionEvent ValueCommitted => mValueCommitted;
        public T Value { get; private set; } = default!;

        public void Display(T value) => Value = value;

        public void BeginEdit() => mValueWillChange.Invoke();

        public void ChangeValue(T value)
        {
            Value = value;
            mValueChanged.Invoke();
        }

        public void CommitEdit() => mValueCommitted.Invoke();

        readonly ActionEvent mValueWillChange = new();
        readonly ActionEvent mValueChanged = new();
        readonly ActionEvent mValueCommitted = new();
    }
}
