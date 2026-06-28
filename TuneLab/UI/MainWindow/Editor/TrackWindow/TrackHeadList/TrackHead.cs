using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.SDK;
using Avalonia.Controls.Primitives;
using Slider = TuneLab.GUI.Components.Slider;
using TuneLab.I18N;

using TuneLab.GUI.Controllers;

namespace TuneLab.UI;

// 轨道头拖拽调位的宿主（由承载轨道头的布局层实现）。轨道头不直接依赖布局层私有类型，
// 经此接口驱动实时预览与落定提交。
internal interface ITrackHeadDragHost
{
    void BeginTrackHeadDrag(TrackHead head, double grabOffsetY);
    void UpdateTrackHeadDrag(double pointerYInHost);
    void EndTrackHeadDrag();
    void CancelTrackHeadDrag();
}

internal class TrackHead : DockPanel
{
    public TrackHead()
    {
        mName.Bind(mTrackHolder.Select(track => track.Name), s);
        mGainSlider.SetRange(-24, 6);
        mGainSlider.Select((double value) => value <= mGainSlider.MinValue ? double.NegativeInfinity : value).Bind(mTrackHolder.Select(track => track.Gain), s);
        mPanSlider.SetRange(-1, 1);
        mPanSlider.Bind(mTrackHolder.Select(track => track.Pan), s);
        mMuteToggle
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 3 }, CheckedColorSet = new() { Color = new(255, 0, 186, 173) }, UncheckedColorSet = new() { Color = Style.BACK } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.M }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE } });
        mMuteToggle.Bind(mTrackHolder.Select(track => track.IsMute), s);
        mSoloToggle
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 3 }, CheckedColorSet = new() { Color = new(255, 135, 84, 255) }, UncheckedColorSet = new() { Color = Style.BACK } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.S }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE } });
        mSoloToggle.Bind(mTrackHolder.Select(track => track.IsSolo), s);
        mIndexLabel.EndInput.Subscribe(() => { if (Track == null) return; if (!int.TryParse(mIndexLabel.Text, out int newIndex)) mIndexLabel.Text = mTrackIndex.ToString(); newIndex = newIndex.Limit(1, Track.Project.Tracks.Count()); newIndex--; MoveToIndex(newIndex); });
        this.AddDock(mSelectionStrip, Dock.Left);
        var leftArea = new DockPanel() { Margin = new(6, 2, 0, 3) };
        {
            leftArea.AddDock(mAmplitudeViewer);
        }
        this.AddDock(leftArea, Dock.Left);
        var rightArea = new DockPanel() { Margin = new(0, 0, 0, 0) };
        {
            mIndexPanel.Children.Add(mIndexLabel);
            rightArea.AddDock(mIndexPanel);
        }
        this.AddDock(rightArea, Dock.Right);
        var topArea = new DockPanel() { Margin = new(12, 12, 12, 0) };
        {
            topArea.AddDock(mSoloToggle, Dock.Right);
            topArea.AddDock(mMuteToggle, Dock.Right);
            topArea.AddDock(mName);
        }
        this.AddDock(topArea, Dock.Top);
        var bottomArea = new DockPanel() { Margin = new(12, 12, 12, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        {
            bottomArea.AddDock(mPanSlider, Dock.Right);
            bottomArea.AddDock(mGainSlider);
        }
        this.AddDock(bottomArea);

        mTrackHolder.When(track => track.SelectionChanged).Subscribe(UpdateSelectionVisual, s);
        mTrackHolder.When(track => track.Color.Modified).Subscribe(() => { if (Track == null) return; mIndexLabel.Background = Track.GetColor().ToBrush(); mIndexPanel.Background = Track.GetColor().ToBrush(); }, s);
        mIndexPanel.RegisterOnTrackColorUpdated(() => { if (Track == null) return; mIndexLabel.Background = Track.GetColor().ToBrush(); mIndexPanel.Background = Track.GetColor().ToBrush(); });
        mTrackHolder.WillModify.Subscribe(() =>
        {
            if (Track == null)
                return;

            AudioEngine.ProgressChanged -= AudioEngine_ProgressChanged;
            AudioEngine.PlayStateChanged -= AudioEngine_PlayStateChanged;
        }, s);
        mTrackHolder.Modified.Subscribe(() =>
        {
            if (Track == null)
                return;

            AudioEngine.ProgressChanged += AudioEngine_ProgressChanged;
            AudioEngine.PlayStateChanged += AudioEngine_PlayStateChanged;
        }, s);

        MinWidth = 200;

        var menu = new ContextMenu();
        {
            var menuItem = new MenuItem().SetTrName("Export Audio").SetAction(async () =>
            {
                if (Track == null)
                    return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null)
                    return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save File".Tr(TC.Dialog),
                    DefaultExtension = ".wav",
                    SuggestedFileName = Track.Name.Value,
                    ShowOverwritePrompt = true,
                    FileTypeChoices = [new("WAVE File".Tr(TC.Dialog)) { Patterns = ["*.wav"] }]
                });
                var result = file?.TryGetLocalPath();
                if (result == null)
                    return;

                try
                {
                    AudioEngine.ExportTrack(result, Track, false);
                }
                catch (Exception ex)
                {
                    await this.ShowMessage("Error".Tr(TC.Dialog), "Export failed: \n".Tr(TC.Dialog) + ex.Message);
                }
            });
            menu.Items.Add(menuItem);
        }
        {
            var menuItem = new MenuItem().SetTrName("Move Up").SetAction(() =>
            {
                var track = Track;
                if (track == null)
                    return;

                var project = track.Project;
                int index = project.Tracks.IndexOf(track);
                if (index == 0)
                    return;

                project.RemoveTrackAt(index);
                project.InsertTrack(index - 1, track);
                project.Commit();
            });
            menu.Items.Add(menuItem);
            menu.Opening += (s, e) =>
            {
                menuItem.IsEnabled = false;
                if (Track == null)
                    return;

                var project = Track.Project;
                int index = project.Tracks.IndexOf(Track);
                if (index == 0)
                    return;

                menuItem.IsEnabled = true;
            };
        }
        {
            var menuItem = new MenuItem().SetTrName("Move Down").SetAction(() =>
            {
                var track = Track;
                if (track == null)
                    return;

                var project = track.Project;
                int index = project.Tracks.IndexOf(track);
                if (index == project.Tracks.Count - 1)
                    return;

                project.RemoveTrackAt(index);
                project.InsertTrack(index + 1, track);
                project.Commit();
            });
            menu.Items.Add(menuItem);
            menu.Opening += (s, e) =>
            {
                menuItem.IsEnabled = false;
                if (Track == null)
                    return;

                var project = Track.Project;
                int index = project.Tracks.IndexOf(Track);
                if (index == project.Tracks.Count - 1)
                    return;

                menuItem.IsEnabled = true;
            };
        }
        {
            var menuItem = new MenuItem().SetTrName("Set Color");
            {
                foreach (var color in Style.TRACK_COLORS.Select(Color.Parse))
                {
                    MenuItem colorItem = new MenuItem
                    {
                        Header = new Border
                        {
                            Background = new SolidColorBrush(color),
                            Width = 20,
                            Height = 20
                        },
                        Tag = color
                    };
                    colorItem.Width = 20;
                    colorItem.Height = 20;
                    colorItem.Margin = new Avalonia.Thickness(5);
                    colorItem.SetAction(() =>
                    {
                        if (Track == null)
                            return;

                        Track.Color.Set(((Color)colorItem.Tag).ToString());
                        Track.Color.Commit();
                    });
                    menuItem.Items.Add(colorItem);
                }
            }
            menu.Items.Add(menuItem);
        }
        {
            var menuItem = new MenuItem().SetTrName("As Refer").SetAction(() =>
            {
                var track = Track;
                if (track == null)
                    return;

                track.AsRefer.Set(!track.AsRefer.GetInfo());
                track.AsRefer.Commit();
            });
            menu.Items.Add(menuItem);
            menu.Opening += (s, e) =>
            {
                if (Track == null)
                    return;

                menuItem.SetName(!Track.AsRefer.GetInfo() ? "Visible as Refer".Tr(TC.Menu) : "Hidden as Refer".Tr(TC.Menu));
            };
        }
        {
            var menuItem = new MenuItem().SetTrName("Delete").SetAction(() =>
            {
                if (Track == null)
                    return;

                var project = Track.Project;
                project.RemoveTrack(Track);
                project.Commit();
            });
            menu.Items.Add(menuItem);
        }

        ContextMenu = menu;
        Background = Brushes.Transparent;

        this.RegisterOnTrackColorUpdated();
        
    }

    ~TrackHead()
    {
        s.DisposeAll();
    }

    // 选中态：整行底色提亮 + 左缘强调条（强调条常驻 3px，仅切换颜色，避免选中时布局抖动）。
    void UpdateSelectionVisual()
    {
        bool selected = Track != null && Track.IsSelected;
        Background = selected ? Style.HIGH_LIGHT.Opacity(0.18).ToBrush() : Brushes.Transparent;
        mSelectionStrip.Background = selected ? Style.HIGH_LIGHT.ToBrush() : Brushes.Transparent;
        // 名称标签默认是不透明 INTERFACE 底，选中时改透明以露出整行高亮底色，避免出现一块未变色的色斑。
        mName.Background = selected ? Brushes.Transparent : Style.INTERFACE.ToBrush();
    }

    // 按下源是否落在“占用指针的交互控件”（滑条/开关）上。这些控件靠指针捕获自行拖动，
    // 轨道头不得介入；而名称/序号标签（双击才进编辑）、电平表、空白区都可作为选中/拖拽候选。
    static bool IsOnInteractiveControl(object? source, Control stopAt)
    {
        var v = source as Visual;
        while (v != null && !ReferenceEquals(v, stopAt))
        {
            if (v is AbstractSlider || v is Toggle)
                return true;

            v = v.GetVisualParent();
        }
        return false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || Track == null)
            return;

        mDragCandidate = !IsOnInteractiveControl(e.Source, this);
        if (!mDragCandidate)
            return;

        var point = e.GetCurrentPoint(this);
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (point.Properties.IsLeftButtonPressed)
        {
            SelectOnPress(ctrl);
            mPressCtrl = ctrl;

            // 仅记录起点，不在此处抢捕获：越过阈值后再惰性捕获，否则会毁掉名称/序号标签的双击进编辑，
            // 也会夺走滑条/开关的指针捕获。pointerYInHost 取相对布局层，拖拽期间稳定。
            if (this.GetVisualParent() is Visual host)
            {
                mPointerDown = true;
                mDownYInHost = e.GetPosition(host).Y;
                mGrabOffsetY = mDownYInHost - Bounds.Y;
            }
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            // 右键命中未选中的轨道头：先独占选中它，使后续右键菜单作用于本轨（镜像 part）。
            if (!Track.IsSelected)
                SelectOnPress(false);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!mPointerDown)
            return;

        // 按键已松（捕获在子控件上时本头收不到 up，标志可能滞留）→ 复位，避免悬空触发拖拽。
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            mPointerDown = false;
            return;
        }

        if (this.GetVisualParent() is not Visual host || host is not ITrackHeadDragHost dragHost)
            return;

        double yInHost = e.GetPosition(host).Y;
        if (!mDragging)
        {
            if (Math.Abs(yInHost - mDownYInHost) < DragThreshold)
                return;

            mDragging = true;
            e.Pointer.Capture(this);   // 此刻才接管：确保是真正的拖动而非点击/双击
            dragHost.BeginTrackHeadDrag(this, mGrabOffsetY);
        }
        dragHost.UpdateTrackHeadDrag(yInHost);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        bool wasPlainClick = mPointerDown && !mDragging && mDragCandidate && !mPressCtrl;
        if (mDragging && this.GetVisualParent() is ITrackHeadDragHost dragHost)
        {
            dragHost.EndTrackHeadDrag();
            e.Pointer.Capture(null);   // 只释放我们自己惰性捕获的指针，别误清其他控件的捕获
        }
        else if (wasPlainClick && Track != null)
        {
            // 平地单击（无 ctrl、未拖拽）收敛为只选中本轨——按下时为支持多选拖拽保留了原选区，此处落定收敛。
            Track.Project.Tracks.DeselectAllItems();
            Track.Select();
        }

        mPointerDown = false;
        mDragging = false;
        mDragCandidate = false;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (mDragging && this.GetVisualParent() is ITrackHeadDragHost dragHost)
            dragHost.CancelTrackHeadDrag();   // 捕获丢失：取消并回弹，不提交重排

        mPointerDown = false;
        mDragging = false;
    }

    // 选中语义镜像 part：Ctrl 增减选；普通点击若未选中则独占选中，已选中则保留（便于多选拖拽）。
    void SelectOnPress(bool ctrl)
    {
        if (Track == null)
            return;

        if (ctrl)
        {
            Track.Inselect();
        }
        else if (!Track.IsSelected)
        {
            Track.Project.Tracks.DeselectAllItems();
            Track.Select();
        }
    }

    private void MoveToIndex(int newIndex)
    {
        if (Track == null) return;
        var track = Track;
        var project = track.Project;
        int index = project.Tracks.IndexOf(track);
        if (index == newIndex) mIndexLabel.Text = mTrackIndex.ToString();
        else
        {
            project.RemoveTrackAt(index);
            project.InsertTrack(newIndex, track);
            project.Commit();
        }
    }

    public void SetTrack(ITrack? track, int index=0)
    {
        mTrackIndex = index;

        mIndexLabel.Text = mTrackIndex.ToString();
        mTrackHolder.Set(track);
        if (Track != null)
        {
            mName.Text = Track.Name.Value;
            mGainSlider.Display(Track.Gain.Value);
            mPanSlider.Display(Track.Pan.Value);
            mMuteToggle.Display(Track.IsMute.Value);
            mSoloToggle.Display(Track.IsSolo.Value);
            mIndexLabel.Background = Track.GetColor().ToBrush();
            mIndexPanel.Background = Track.GetColor().ToBrush();
        }
        UpdateSelectionVisual();
    }

    private void AudioEngine_PlayStateChanged()
    {
        try
        {
            if (!AudioEngine.IsPlaying)
            {
                mAmplitudeViewer.Release();
            }
        }
        catch {; }
    }

    private void AudioEngine_ProgressChanged()
    {
        try
        {
            if (Track != null && AudioEngine.IsPlaying)
            {
                AudioEngine.InvokeRealtimeAmplitude(Track, out var amp);
                if (amp == null) mAmplitudeViewer.Release(); else mAmplitudeViewer.SetValue(amp);
            }
        }
        catch {; }
    }

    Holder<ITrack> mTrackHolder = new();
    public ITrack? Track => mTrackHolder.Value;
    int mTrackIndex = -1;

    const double DragThreshold = 4;
    bool mDragCandidate = false;
    bool mPressCtrl = false;
    bool mPointerDown = false;
    bool mDragging = false;
    double mDownYInHost = 0;
    double mGrabOffsetY = 0;

    readonly Border mSelectionStrip = new() { Width = 3, Margin = new(0, 0, 0, 1), Background = Brushes.Transparent, IsHitTestVisible = false };
    readonly LayerPanel mIndexPanel = new() { Background = Style.ITEM.ToBrush(), Width = 24, Margin = new(0, 0, 0, 1) };
    readonly EditableLabel mIndexLabel = new() { MinWidth = 16, Foreground = Brushes.Black, CornerRadius = new(0), Padding = new(0), FontSize = 12, VerticalAlignment =Avalonia.Layout.VerticalAlignment.Center,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
    readonly EditableLabel mName = new() { FontSize = 12, CornerRadius = new(0), Padding = new(0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), InputBackground = Style.BACK.ToBrush(), Height = 16 };
    readonly GainSlider mGainSlider = new() { Height = 12 };
    readonly PanSlider mPanSlider = new() { Width = 40, Height = 12, Margin = new(8, 0, 0, 0) };
    readonly Toggle mMuteToggle = new() { Width = 16, Height = 16, Margin = new(8, 0, 0, 0) };
    readonly Toggle mSoloToggle = new() { Width = 16, Height = 16, Margin = new(8, 0, 0, 0) };
    readonly DisposableManager s = new();
    readonly StereoAmplitudeViewer mAmplitudeViewer = new();
}
