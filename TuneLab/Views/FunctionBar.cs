using Avalonia.Controls;
using Avalonia.Media;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Event;
using TuneLab.Base.Science;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.Views;

internal class FunctionBar : LayerPanel
{
    public event Action<double>? Moved;
    public IActionEvent IsAutoPageChanged => mIsAutoPageChanged;
    public bool IsAutoPage { get => mIsAutoPage; set { if (mIsAutoPage == value) return; mIsAutoPage = value; mIsAutoPageChanged.Invoke(); } }

    public interface IDependency
    {
        public IActionEvent PianoToolChanged { get; }
        public PianoTool PianoTool { get; set; }
    }

    public FunctionBar(IDependency dependency)
    {
        mDependency = dependency;

        var mover = new Mover() { Margin = new(0, 1) };
        mover.Moved.Subscribe(p => Moved?.Invoke(p.Y + Bounds.Y));
        Children.Add(mover);

        var dockPanel = new DockPanel() { Margin = new(64, 0, 360, 0) };
        {
            var hoverBack = Colors.White.Opacity(0.05);

            void SetupToolTip(Toggle toggleButton,string ToolTipText)
            {
                ToolTip.SetPlacement(toggleButton, PlacementMode.Top);
                ToolTip.SetVerticalOffset(toggleButton, -6);
                ToolTip.SetShowDelay(toggleButton, 0);
                ToolTip.SetTip(toggleButton, ToolTipText);
            }

            var audioControlPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new(12, 0) };
            {
                var playButton = new Toggle() { Width = 36, Height = 36 }
                    .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                    .AddContent(new() { Item = new IconItem() { Icon = Assets.Play }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
                
                SetupToolTip(playButton,"Play");
                playButton.Switched += () => { if (playButton.IsChecked) AudioEngine.Play(); else AudioEngine.Pause(); SetupToolTip(playButton, AudioEngine.IsPlaying ? "Pause" : "Play"); };
                AudioEngine.PlayStateChanged += () => { playButton.Display(AudioEngine.IsPlaying);SetupToolTip(playButton, AudioEngine.IsPlaying?"Pause":"Play");};
                audioControlPanel.Children.Add(playButton);

                var autoPageButton = new Toggle() { Width = 36, Height = 36 }
                    .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                    .AddContent(new() { Item = new IconItem() { Icon = Assets.AutoPage }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });

                SetupToolTip(autoPageButton, "Auto Scroll");
                autoPageButton.Switched += () => mIsAutoPage = autoPageButton.IsChecked;
                mIsAutoPageChanged.Subscribe(() => { autoPageButton.Display(mIsAutoPage); });
                audioControlPanel.Children.Add(autoPageButton);
            }
            DockPanel.SetDock(audioControlPanel, Dock.Left);
            dockPanel.Children.Add(audioControlPanel);  

            var pianoToolPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            {
                void AddButton(PianoTool tool, SvgIcon icon, string tipText)
                {
                    var toggle = new Toggle() { Width = 36, Height = 36 }
                        .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                        .AddContent(new() { Item = new IconItem() { Icon = icon }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
                    void OnPianoToolChanged()
                    {
                        toggle.Display(mDependency.PianoTool == tool);
                    }
                    SetupToolTip(toggle, tipText);
                    toggle.AllowSwitch += () => !toggle.IsChecked;
                    toggle.Switched += () => mDependency.PianoTool = tool;
                    mDependency.PianoToolChanged.Subscribe(OnPianoToolChanged);
                    pianoToolPanel.Children.Add(toggle);
                    OnPianoToolChanged();
                }
                AddButton(PianoTool.Note, Assets.Pointer, "Note Edit");
                AddButton(PianoTool.Pitch, Assets.Pitch, "Pitch Edit");
                AddButton(PianoTool.Anchor, Assets.Anchor, "Pitch Anchor");
                AddButton(PianoTool.Lock, Assets.Brush, "Pitch Lock Brush");
                AddButton(PianoTool.Vibrato, Assets.Vibrato, "Vibrato Builder");
                AddButton(PianoTool.Select, Assets.Select, "Area Select");
            }
            dockPanel.Children.Add(pianoToolPanel);
        }
        Children.Add(dockPanel);

        Height = 64;
        Background = Style.BACK.ToBrush();
    }

    class Mover : MovableComponent
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Style.INTERFACE.ToBrush(), this.Rect());
        }
    }


    bool mIsAutoPage = false;
    readonly ActionEvent mIsAutoPageChanged = new();

    readonly IDependency mDependency;
}
