using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using System;
using TuneLab.Utils;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using CheckBox = TuneLab.GUI.Components.CheckBox;
using Avalonia.Layout;
using Button = TuneLab.GUI.Components.Button;
using Avalonia.Media;
using TuneLab.Data;
using System.Collections.Generic;
using System.Linq;

namespace TuneLab.Views;

internal partial class LyricInput : Window
{
    LyricInput()
    {
        InitializeComponent();
        Focusable = true;
        CanResize = false;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        this.DataContext = this;
        this.Background = Style.INTERFACE.ToBrush();
        TitleBar.Background = Style.BACK.ToBrush();

        mLyricInputBox = new TextInput();
        mLyricInputBox.AcceptsReturn = true;
        mLyricInputBox.Width = 432;
        mLyricInputBox.Height = 168;
        mLyricInputBox.Background = Style.BACK.ToBrush();
        mLyricInputBox.Padding = new(8, 8);
        mLyricInputBox.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top;
        mLyricInputBox.Foreground = Style.WHITE.ToBrush();
        mLyricInputBox.TextWrapping = TextWrapping.Wrap;
        TextareaBox.Children.Add(mLyricInputBox);

        var SkipTenutoLabelPanel = new StackPanel();
        mSkipTenutoCheckBox = new CheckBox();
        SkipTenutoLabelPanel.Orientation = Orientation.Horizontal;
        SkipTenutoLabelPanel.Height = 24;
        SkipTenutoLabelPanel.Children.Add(mSkipTenutoCheckBox);
        SkipTenutoLabelPanel.Children.Add(new Label() { Content = "Skip Tenuto", FontSize = 12, Foreground = Style.TEXT_LIGHT.ToBrush(), Margin = new(14, 1) });
        ActionsPanel.Children.Add(SkipTenutoLabelPanel);
        Grid.SetColumn(SkipTenutoLabelPanel, 0);
        var OkButtonPanel = new StackPanel();
        OkButtonPanel.Orientation = Orientation.Horizontal;
        OkButtonPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        var OkButton = new Button() { Width = 64, Height = 28 };
        OkButton.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER } });
        OkButton.AddContent(new() { Item = new TextItem() { Text = "OK" }, ColorSet = new() { Color = Colors.White } });
        OkButtonPanel.Children.Add(OkButton);
        ActionsPanel.Children.Add(OkButtonPanel);
        Grid.SetColumn(OkButton, 1);

        OkButton.Clicked += OnLyricInputConfirm;
    }

    public static void EnterInput(IReadOnlyCollection<INote> notes)
    {
        var lyricInput = new LyricInput();
        lyricInput.mNotes = notes;
        lyricInput.mLyricInputBox.Text = string.Join(' ', notes.Select(note => note.Lyric.Value));
        lyricInput.Show();
    }

    void OnLyricInputConfirm()
    {
        if (mNotes == null)
            return;

        if (mNotes.Count == 0)
            return;

        var lyrics = LyricUtils.SplitLyrics(mLyricInputBox.Text);
        var notes = mSkipTenutoCheckBox.IsChecked ? mNotes.Where(note => note.Lyric.Value != "-") : mNotes;
        using var enumerator = (mSkipTenutoCheckBox.IsChecked ? lyrics.Where(lyric => lyric != "-") : lyrics).GetEnumerator();
        foreach (var note in notes)
        {
            if (!enumerator.MoveNext())
                break;

            note.Lyric.Set(enumerator.Current);
        }

        mNotes.First().Commit();
        Close();
    }

    IReadOnlyCollection<INote>? mNotes = null;

    readonly TextInput mLyricInputBox;
    readonly CheckBox mSkipTenutoCheckBox;
}
