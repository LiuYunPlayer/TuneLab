using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;
using CheckBox = TuneLab.GUI.Components.CheckBox;

namespace TuneLab.Views;

internal partial class LyricInput : Window
{
    public LyricInput()
    {
        InitializeComponent();
        Focusable = true;
        CanResize = false;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        TitleLabel.Content = "Input Lyrics".Tr(TC.Dialog);

        this.Background = Style.BACK.ToBrush();
        TitleLabel.Foreground = Style.TEXT_LIGHT.ToBrush();

        var closeButton = new Button() { Width = 48, Height = 40 }
                .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
                .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
        closeButton.Clicked += () => Close();

        WindowControl.Children.Add(closeButton);

        Content.Background = Style.INTERFACE.ToBrush();

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
        SkipTenutoLabelPanel.Children.Add(new Label() { Content = "Skip Tenuto".Tr(TC.Dialog), FontSize = 12, Foreground = Style.TEXT_LIGHT.ToBrush(), Margin = new(14, 1) });
        ActionsPanel.Children.Add(SkipTenutoLabelPanel);
        Grid.SetColumn(SkipTenutoLabelPanel, 0);
        var OkButtonPanel = new StackPanel();
        OkButtonPanel.Orientation = Orientation.Horizontal;
        OkButtonPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        var OkButton = new Button() { Width = 64, Height = 28 };
        OkButton.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER } });
        OkButton.AddContent(new() { Item = new TextItem() { Text = "OK".Tr(TC.Dialog) }, ColorSet = new() { Color = Colors.White } });
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

        var lyricResults = LyricUtils.Split(mLyricInputBox.Text);
        var notes = mSkipTenutoCheckBox.IsChecked ? mNotes.Where(note => note.Lyric.Value != "-") : mNotes;
        using var enumerator = (mSkipTenutoCheckBox.IsChecked ? lyricResults.Where(lyricResult => lyricResult.Lyric != "-") : lyricResults).GetEnumerator();
        foreach (var note in notes)
        {
            if (!enumerator.MoveNext())
                break;

            var current = enumerator.Current;
            note.Lyric.Set(current.Lyric);
            if (current.Lyric == "-")
                continue;

            note.Pronunciation.Set(current.Pronunciation);
        }

        mNotes.First().Commit();
        Close();
    }

    IReadOnlyCollection<INote>? mNotes = null;

    readonly TextInput mLyricInputBox;
    readonly CheckBox mSkipTenutoCheckBox;
}
