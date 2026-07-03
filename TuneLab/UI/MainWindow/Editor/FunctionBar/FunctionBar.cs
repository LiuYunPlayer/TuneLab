using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.Utils;
using static TuneLab.Foundation.MusicTheory;
using ComboBoxItem = TuneLab.SDK.ComboBoxItem;   // 消歧：避开 Avalonia.Controls.ComboBoxItem

namespace TuneLab.UI;

internal class FunctionBar : LayerPanel
{
    public event Action<double>? Moved;
    public event Action<bool>? CollapsePropertiesAsked;
    public event Action? GotoStartAsked;
    public event Action? GotoEndAsked;

    public INotifiableProperty<PlayScrollTarget> PlayScrollTarget => mDependency.PlayScrollTarget;
    public IActionEvent<QuantizationBase, QuantizationDivision> QuantizationChanged => mQuantizationChanged;

    public interface IDependency
    {
        INotifiableProperty<PianoTool> PianoTool { get; }
        INotifiableProperty<PlayScrollTarget> PlayScrollTarget { get; }
        IPlayhead Playhead { get; }
        IHolder<IProject> ProjectHolder { get; }
        IHolder<IMidiPart> EditingPartHolder { get; }
    }

    public FunctionBar(IDependency dependency)
    {
        mDependency = dependency;

        var mover = new Mover() { Margin = new(0, 1) };
        mover.Moved.Subscribe(p => Moved?.Invoke(p.Y + Bounds.Y));
        Children.Add(mover);

        var dockPanel = new DockPanel() { Margin = new(64, 0, 12, 0) };
        {
            var hoverBack = Colors.White.Opacity(0.05);

            void SetupToolTip(Control toggleButton,string ToolTipText)
            {
                toggleButton.SetupToolTip(ToolTipText, PlacementMode.Top, verticalOffset: -8);
            }

            var audioControlPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new(12, 0) };
            {
                var playButtonIconItem = new IconItem() { Icon = Assets.Play };
                var playButton = new Toggle() { Width = 36, Height = 36 }
                    .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                    .AddContent(new() { Item = playButtonIconItem, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
                
                SetupToolTip(playButton, "Play".Tr(this));
                playButton.Switched.Subscribe(() => { if (playButton.IsChecked) AudioEngine.Play(); else AudioEngine.Pause(); SetupToolTip(playButton, AudioEngine.IsPlaying ? "Pause".Tr(this) : "Play".Tr(this)); });
                AudioEngine.PlayStateChanged += () => { playButtonIconItem.Icon = AudioEngine.IsPlaying ? Assets.Pause : Assets.Play; playButton.Display(AudioEngine.IsPlaying); SetupToolTip(playButton, AudioEngine.IsPlaying ? "Pause".Tr(this) : "Play".Tr(this));};
                audioControlPanel.Children.Add(playButton);

                var autoPageButton = new AutoPageButton(mDependency.PlayScrollTarget) { Width = 36, Height = 36 };

                SetupToolTip(autoPageButton, "Auto Scroll".Tr(this));
                audioControlPanel.Children.Add(autoPageButton);

                var gotoStartButton = new GUI.Components.Button() { Width = 36, Height = 36 }
                    .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                    .AddContent(new() { Item = new IconItem() { Icon = Assets.GotoStart }, ColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
                SetupToolTip(gotoStartButton, "Go to Start".Tr(this));
                gotoStartButton.Clicked += () => { GotoStartAsked?.Invoke(); };
                audioControlPanel.Children.Add(gotoStartButton);

                var gotoEndButton = new GUI.Components.Button() { Width = 36, Height = 36 }
                    .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                    .AddContent(new() { Item = new IconItem() { Icon = Assets.GotoEnd }, ColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
                SetupToolTip(gotoEndButton, "Go to End".Tr(this));
                gotoEndButton.Clicked += () => { GotoEndAsked?.Invoke(); };
                audioControlPanel.Children.Add(gotoEndButton);

                // 时间码：播放头位置的绝对时间。tick→秒依赖 tempo，故除播放头移动外还需跟 tempo 修改与工程切换刷新。
                // 毫秒段单独小号浅色，与时分秒拉开主次。
                var timecodeMainRun = new Run() { FontSize = 16 };
                var timecodeMsRun = new Run() { FontSize = 12, Foreground = Style.WHITE.Opacity(0.4).ToBrush() };
                var timecodeLabel = new TextBlock()
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    FontFamily = Assets.NotoMono,
                    Foreground = Style.TEXT_NORMAL.ToBrush(),
                    Inlines = new InlineCollection() { timecodeMainRun, timecodeMsRun },
                };
                void UpdateTimecode()
                {
                    var project = mDependency.ProjectHolder.Value;
                    double seconds = project == null ? 0 : project.TempoManager.GetTime(mDependency.Playhead.Pos);
                    var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
                    timecodeMainRun.Text = time.ToString(@"hh\:mm\:ss");
                    timecodeMsRun.Text = time.ToString(@"\.fff");
                }
                mDependency.Playhead.PosChanged.Subscribe(UpdateTimecode, s);
                mDependency.ProjectHolder.Modified.Subscribe(UpdateTimecode, s);
                mDependency.ProjectHolder.When(p => p.TempoManager.Modified).Subscribe(UpdateTimecode, s);
                UpdateTimecode();
                var timecodeBorder = new Border()
                {
                    Child = timecodeLabel,
                    Background = Style.DARK.ToBrush(),
                    BorderBrush = Style.LINE.ToBrush(),
                    BorderThickness = new(1),
                    CornerRadius = new(4),
                    Padding = new(12, 7),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                audioControlPanel.Children.Add(timecodeBorder);
            }
            dockPanel.AddDock(audioControlPanel, Dock.Left);

            var quantizationPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            {
                var quantizationLabel = new TextBlock() { Text = "Quantization".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                quantizationPanel.Children.Add(quantizationLabel);
                var quantizationComboBox = new ComboBoxController() { Width = 96 };
                (string option, QuantizationBase quantizationBase, QuantizationDivision quantizationDivision)[] options = 
                [
                    ("1/1", QuantizationBase.Base_1, QuantizationDivision.Division_1),
                    ("1/2", QuantizationBase.Base_1, QuantizationDivision.Division_2),
                    ("1/4", QuantizationBase.Base_1, QuantizationDivision.Division_4),
                    ("1/8", QuantizationBase.Base_1, QuantizationDivision.Division_8),
                    ("1/16", QuantizationBase.Base_1, QuantizationDivision.Division_16),
                    ("1/32", QuantizationBase.Base_1, QuantizationDivision.Division_32),
                    ("1/3", QuantizationBase.Base_3, QuantizationDivision.Division_1),
                    ("1/6", QuantizationBase.Base_3, QuantizationDivision.Division_2),
                    ("1/12", QuantizationBase.Base_3, QuantizationDivision.Division_4),
                    ("1/24", QuantizationBase.Base_3, QuantizationDivision.Division_8),
                    ("1/48", QuantizationBase.Base_3, QuantizationDivision.Division_16),
                    ("1/96", QuantizationBase.Base_3, QuantizationDivision.Division_32),
                    ("1/5", QuantizationBase.Base_5, QuantizationDivision.Division_1),
                    ("1/10", QuantizationBase.Base_5, QuantizationDivision.Division_2),
                    ("1/20", QuantizationBase.Base_5, QuantizationDivision.Division_4),
                    ("1/40", QuantizationBase.Base_5, QuantizationDivision.Division_8),
                    ("1/80", QuantizationBase.Base_5, QuantizationDivision.Division_16),
                    ("1/160", QuantizationBase.Base_5, QuantizationDivision.Division_32),
                ];
                quantizationComboBox.SetConfig(ComboBoxConfig.Create(options.Select(option => (ComboBoxItem)option.option).ToList()).WithDefault(options[3].option));
                quantizationComboBox.ValueCommitted.Subscribe(() => { var index = quantizationComboBox.Index; if ((uint)index >= options.Length) return; mQuantizationChanged.Invoke(options[index].quantizationBase, options[index].quantizationDivision); });
                quantizationPanel.Children.Add(quantizationComboBox);
            }
            dockPanel.AddDock(quantizationPanel, Dock.Right);

            var pianoToolPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            {
                int index = 0;
                Toggle AddButton(PianoTool tool, SvgIcon icon, string tipText)
                {
                    index++;
                    var toggle = new Toggle() { Width = 36, Height = 36 }
                        .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { Color = Style.HIGH_LIGHT }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                        .AddContent(new() { Item = new IconItem() { Icon = icon }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
                    void OnPianoToolChanged()
                    {
                        toggle.Display(mDependency.PianoTool.Value == tool);
                    }
                    SetupToolTip(toggle, $"{tipText} {index}");
                    toggle.AllowSwitch += () => !toggle.IsChecked;
                    toggle.Switched.Subscribe(() => mDependency.PianoTool.Value = tool);
                    mDependency.PianoTool.Modified.Subscribe(OnPianoToolChanged);
                    pianoToolPanel.Children.Add(toggle);
                    OnPianoToolChanged();
                    return toggle;
                }
                AddButton(PianoTool.Note, Assets.Pointer, "Note Tool".Tr(this));
                AddButton(PianoTool.Pitch, Assets.Pitch, "Pitch Pen".Tr(this));
                AddButton(PianoTool.Anchor, Assets.Anchor, "Anchor Tool".Tr(this));
                AddButton(PianoTool.Lock, Assets.Brush, "Pitch Locking Brush".Tr(this));
                var vibratoToggle = AddButton(PianoTool.Vibrato, Assets.Vibrato, "Vibrato Tool".Tr(this));
                // instrument 音源无颤音系统：编辑 instrument part 时直接隐藏颤音工具按钮
                //（快捷键拦截与自动退回音符工具在 Editor 侧做）。
                void UpdateVibratoToolAvailable() => vibratoToggle.IsVisible = mDependency.EditingPartHolder.Value?.SoundSource.Kind != SourceKind.Instrument;
                mDependency.EditingPartHolder.Modified.Subscribe(UpdateVibratoToolAvailable, s);
                mDependency.EditingPartHolder.When(part => part.SoundSource.Modified).Subscribe(UpdateVibratoToolAvailable, s);
                UpdateVibratoToolAvailable();
                // 范围选区(tick 带)不再是工具：任意工具下 Shift+拖即画，零工具切换（见 PianoScrollView 的 SelectionOperation）。
            }
            dockPanel.AddDock(pianoToolPanel);
        }
        Children.Add(dockPanel);

        Height = 60;
        Background = Style.BACK.ToBrush();
    }

    ~FunctionBar()
    {
        s.DisposeAll();
    }

    class Mover : MovableComponent
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Style.INTERFACE.ToBrush(), this.Rect());
        }
    }

    readonly ActionEvent<QuantizationBase, QuantizationDivision> mQuantizationChanged = new();

    readonly DisposableManager s = new();

    readonly IDependency mDependency;
}
