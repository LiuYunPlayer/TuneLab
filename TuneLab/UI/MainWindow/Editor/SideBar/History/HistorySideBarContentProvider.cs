using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

internal sealed class HistorySideBarContentProvider
{
    public IImage Icon => Assets.History.GetImage(Style.LIGHT_WHITE);
    public string Name => "History".Tr(TC.Menu);
    public Control Root => mRoot;

    public HistorySideBarContentProvider(ProjectDocument document)
    {
        mDocument = document;
        mRoot.Content = mRowsPanel;
        mScrollBars = new OverlayScrollBars(mRoot, horizontal: false, vertical: true);
        mDocument.StatusChanged += OnDocumentStatusChanged;
        RebuildRows();
    }

    public void SetActive(bool active)
    {
        mActive = active;
        if (!active)
            return;

        RefreshRowTexts();
        RefreshRowStates();
        ScrollCurrentRowIntoView();
    }

    void OnDocumentStatusChanged()
    {
        if (mRenderedEntries.Count == mDocument.History.Count &&
            mRenderedPosition == mDocument.HistoryPosition)
        {
            // Pending preview commands also raise StatusChanged, but they do not
            // change the visible committed history or its cursor.
            return;
        }

        if (HistoryChanged())
        {
            RebuildRows();
        }
        else if (mRenderedPosition != mDocument.HistoryPosition)
        {
            RefreshRowStates();
        }
        else
        {
            return;
        }

        ScrollCurrentRowIntoView();
    }

    bool HistoryChanged()
    {
        if (mRenderedEntries.Count != mDocument.History.Count)
            return true;

        for (int i = 0; i < mRenderedEntries.Count; i++)
        {
            if (!ReferenceEquals(mRenderedEntries[i], mDocument.History[i]))
                return true;
        }

        return false;
    }

    void RebuildRows()
    {
        mRowsPanel.Children.Clear();
        mRows.Clear();
        mRenderedEntries.Clear();

        AddRow(0, "Opened Project".Tr(TC.Menu));
        for (int i = 0; i < mDocument.History.Count; i++)
        {
            var entry = mDocument.History[i];
            mRenderedEntries.Add(entry);
            AddRow(i + 1, RowText(entry));
        }

        RefreshRowStates();
    }

    void AddRow(int position, string text)
    {
        var row = new HistoryRow(position, text, MoveToHistory);
        mRows.Add(row);
        mRowsPanel.Children.Add(row);
    }

    void RefreshRowTexts()
    {
        if (HistoryChanged())
        {
            RebuildRows();
            return;
        }

        mRows[0].Text = "Opened Project".Tr(TC.Menu);
        for (int i = 0; i < mRenderedEntries.Count; i++)
        {
            mRows[i + 1].Text = RowText(mRenderedEntries[i]);
        }
    }

    void RefreshRowStates()
    {
        int current = mDocument.HistoryPosition;
        for (int i = 0; i < mRows.Count; i++)
        {
            mRows[i].SetState(selected: i == current, forward: i > current);
        }

        mRenderedPosition = current;
    }

    string RowText(HistoryEntry entry)
    {
        string description = entry.Description.Tr(TC.Menu);
        return string.IsNullOrEmpty(entry.Detail)
            ? description
            : description + ": " + entry.Detail;
    }

    void MoveToHistory(int position)
    {
        if (mDocument.MoveToHistory(position))
            return;

        // A click on the current row, or a blocked jump during an uncommitted
        // preview, raises no StatusChanged. Keep the visual state on the real cursor.
        RefreshRowStates();
        ScrollCurrentRowIntoView();
    }

    void ScrollCurrentRowIntoView()
    {
        if (!mActive || mScrollPending)
            return;

        mScrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mScrollPending = false;
            if (!mActive || mRows.Count == 0)
                return;

            int position = Math.Clamp(mDocument.HistoryPosition, 0, mRows.Count - 1);
            var row = mRows[position];
            double rowTop = row.Bounds.Y;
            double rowHeight = row.Bounds.Height > 0 ? row.Bounds.Height : HistoryRow.RowHeight;
            if (rowTop == 0 && position > 0)
                rowTop = position * HistoryRow.RowHeight;

            double viewportHeight = mRoot.Viewport.Height;
            if (viewportHeight <= 0)
                return;

            double offset = mRoot.Offset.Y;
            double target = offset;
            if (rowTop < offset)
                target = rowTop;
            else if (rowTop + rowHeight > offset + viewportHeight)
                target = rowTop + rowHeight - viewportHeight;

            if (target != offset)
                mRoot.Offset = new Vector(mRoot.Offset.X, Math.Max(0, target));
        }, DispatcherPriority.Background);
    }

    sealed class HistoryRow : Border
    {
        public const double RowHeight = 42;

        public string Text
        {
            get => mText.Text ?? string.Empty;
            set
            {
                mText.Text = value;
                ToolTip.SetTip(this, value);
            }
        }

        public HistoryRow(int position, string text, Action<int> activate)
        {
            mPosition = position;
            mActivate = activate;
            Height = RowHeight;
            BorderBrush = Style.LINE.ToBrush();
            BorderThickness = new Thickness(0, 0, 0, 1);
            Cursor = new Cursor(StandardCursorType.Hand);

            // The whole row is one navigation target. Its decorative children must
            // not become separate pointer targets, otherwise pressing the text can
            // split the press/release route and prevent the row click from firing.
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("3,*"),
                IsHitTestVisible = false,
            };
            mSelectionStrip = new Border();
            content.Children.Add(mSelectionStrip);

            mText = new TextBlock
            {
                FontSize = 13,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(13, 0, 16, 0),
            };
            Grid.SetColumn(mText, 1);
            content.Children.Add(mText);
            Child = content;
            Text = text;
            RefreshVisual();
        }

        public void SetState(bool selected, bool forward)
        {
            mSelected = selected;
            mForward = forward;
            RefreshVisual();
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            RefreshVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            RefreshVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            mPressed = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            RefreshVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!mPressed)
                return;

            bool activate = IsPointerOver;
            mPressed = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            RefreshVisual();
            if (activate)
                mActivate(mPosition);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            mPressed = false;
            RefreshVisual();
        }

        void RefreshVisual()
        {
            Background = mSelected
                ? Style.HIGH_LIGHT.Opacity(0.18).ToBrush()
                : IsPointerOver || mPressed
                    ? Colors.White.Opacity(0.05).ToBrush()
                    : Brushes.Transparent;
            mSelectionStrip.Background = mSelected ? Style.HIGH_LIGHT.ToBrush() : Brushes.Transparent;
            mText.Foreground = (mSelected
                ? Style.TEXT_LIGHT
                : mForward
                    ? Style.LIGHT_WHITE.Opacity(0.42)
                    : Style.TEXT_NORMAL).ToBrush();
        }

        readonly int mPosition;
        readonly Action<int> mActivate;
        readonly Border mSelectionStrip;
        readonly TextBlock mText;
        bool mSelected;
        bool mForward;
        bool mPressed;
    }

    readonly ProjectDocument mDocument;
    readonly ScrollViewer mRoot = new()
    {
        Background = Style.INTERFACE.ToBrush(),
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
    };
    readonly StackPanel mRowsPanel = new();
    readonly List<HistoryRow> mRows = new();
    readonly List<HistoryEntry> mRenderedEntries = new();
    readonly OverlayScrollBars mScrollBars;
    int mRenderedPosition = -1;
    bool mActive;
    bool mScrollPending;
}
