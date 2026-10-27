using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using TuneLab.Data;
using TuneLab.GUI.Components;
using TuneLab.UI;

namespace TuneLab.ScreenshotBot;

// 逐张拍摄计划。界面状态一律用编辑器/数据层 API 摆好（不点坐标），标注锚点一律取控件实测边界。
internal static class ShotPlan
{
    const double WindowWidth = 1600;
    const double WindowHeight = 960;
    const int R = 480;
    const int Bar = R * 4;

    public static async Task RunAsync(MainWindow window, Camera camera, string sandboxRoot, string repoRoot)
    {
        var editor = window.Editor;
        window.Width = WindowWidth;
        window.Height = WindowHeight;
        await Camera.Settle(16);

        // ==== 样例工程 ====
        // 音频片段在轨道上会连路径一起显示，写在临时目录里会把开发机用户名印进插图；
        // 优先放公共音乐目录（路径中性），失败再退回沙盒。
        string audio = Sandbox.WriteDemoAudio(sandboxRoot);
        var project = new Project(DemoProject.Build(audio));
        editor.Document.SetProject(project);
        await Camera.Settle(20);

        // 导出页会显示导出目录；默认值取自系统桌面，会把开发机的用户名印在插图上——换成中性路径。
        project.ExportPath = @"C:\Music\TuneLab";
        project.ExportFileName = "晚风";

        var vocalPart = (MidiPart)project.Tracks[0].Parts.First();
        editor.SwitchEditingPart(vocalPart);
        await Camera.Settle(24);

        var tabBar = Camera.Find<SideTabBar>(window) ?? throw new InvalidOperationException("SideTabBar not found");
        var trackWindow = editor.TrackWindow;
        var pianoWindow = editor.PianoWindow;
        var functionBar = Camera.Find<FunctionBar>(window) ?? throw new InvalidOperationException("FunctionBar not found");
        var timeline = Camera.Find<TimelineView>(window);
        var trackHeads = Camera.Find<TrackHeadList>(window);
        var trackScroll = Camera.Find<TrackScrollView>(window);
        var pianoRoll = Camera.Find<PianoRoll>(window);
        var pianoScroll = editor.PianoWindow.PianoScrollView;
        var parameterTabBar = Camera.Find<ParameterTabBar>(window);
        var menu = Camera.Find<Avalonia.Controls.Menu>(window);

        Rect B(Visual? v) => v == null ? default : Camera.BoundsIn(v, window);

        // 取景：让歌声片段整段铺满两个视图，避免默认缩放下只看到两小节。
        FrameTicks(trackWindow.TickAxis, trackScroll ?? (Visual)trackWindow, 0, Bar * 10);
        FrameEditingPart(editor, pianoWindow, pianoScroll, vocalPart);
        await Camera.Settle(16);

        foreach (var note in vocalPart.Notes)
            note.Deselect();   // 选中态留到需要它的那几张图再摆

        // ==== 01 界面总览 ====
        tabBar.Restore([SideBarTab.PartProperties]);   // 侧栏可多面并排，插图一律只开一面
        await Camera.Settle(16);
        var sideBar = Camera.Find<SideBar>(window) ?? throw new InvalidOperationException("SideBar not found");
        camera.Shoot(window, "overview", callouts:
        [
            new(B(menu), "1", At: CalloutAt.TopRight),
            new(B(trackWindow), "2"),
            new(B(functionBar), "3", At: CalloutAt.TopRight),
            new(B(pianoWindow), "4"),
            new(B(sideBar), "5"),
            new(B(tabBar), "6", At: CalloutAt.BottomLeft),
        ]);

        // ==== 02 轨道区 ====
        camera.Shoot(window, "track-window", crop: B(trackWindow).Inflate(2), callouts:
        [
            new(B(trackHeads), "1"),
            new(B(timeline), "2", At: CalloutAt.TopRight),
            new(B(trackScroll), "3", At: CalloutAt.BottomLeft),
        ]);

        // ==== 03 功能栏与五个工具 ====
        var tools = ToolButtons(functionBar);
        var toolCallouts = tools.Select((t, i) => new Callout(B(t), (i + 1).ToString(), Box: true, At: CalloutAt.OutsideCorner)).ToList();
        camera.Shoot(window, "function-bar", crop: B(functionBar), callouts: toolCallouts);

        // ==== 04 钢琴窗 ====
        camera.Shoot(window, "piano-window", crop: B(pianoWindow).Inflate(2), callouts:
        [
            new(B(pianoRoll), "1"),
            new(B(pianoScroll), "2", At: CalloutAt.TopRight),
            new(B(parameterTabBar), "3", At: CalloutAt.TopRight),
        ]);

        // 钢琴区（音符区）的画面矩形：连左侧钢琴键一起取（读者需要它定位音高），不带下面的参数面板。
        Rect PianoArea()
        {
            var area = B(pianoScroll);
            var keys = pianoRoll == null ? area : B(pianoRoll);
            return new Rect(keys.X, area.Y, area.Right - keys.X, PianoAreaHeight(pianoWindow));
        }

        // ==== 05 音符与歌词（放大到前几个字；波形先关掉，免得盖住音符）====
        pianoWindow.SetWaveformVisible(false);
        FrameTicks(editor.PianoTickAxis, pianoScroll, Bar - R / 2, Bar * 3);
        await Camera.Settle(12);
        camera.Shoot(window, "notes-and-lyrics", crop: PianoArea());

        // ==== 06 音素带：画在波形泳道里，故须开着波形；裁剪只取泳道那一条（连它上方两行音符）====
        pianoWindow.SetWaveformVisible(true);
        FrameTicks(editor.PianoTickAxis, pianoScroll, Bar, Bar + R * 4);
        await Camera.Settle(40);
        var pianoArea = PianoArea();
        camera.Shoot(window, "phoneme-band",
            crop: new Rect(pianoArea.X, pianoArea.Bottom - 170, pianoArea.Width, 170));
        pianoWindow.SetWaveformVisible(false);
        await Camera.Settle(8);

        // ==== 07 音高曲线 / 锚点 / 颤音（工具本身只改交互，图上要看的是各自处理的对象）====
        editor.PianoTool.Value = PianoTool.Pitch;
        FrameTicks(editor.PianoTickAxis, pianoScroll, Bar, Bar + R * 5);
        await Camera.Settle(12);
        camera.Shoot(window, "pitch-curve", crop: PianoArea());

        editor.PianoTool.Value = PianoTool.Anchor;
        await Camera.Settle(12);
        camera.Shoot(window, "pitch-anchors", crop: PianoArea());

        // 颤音在最后一个长音上：取景对准它，颤音的各个控制柄才在画面里。
        var lastNote = vocalPart.Notes.Last();
        editor.PianoTool.Value = PianoTool.Vibrato;
        FrameTicks(editor.PianoTickAxis, pianoScroll, lastNote.Pos.Value - R / 2, lastNote.Pos.Value + lastNote.Dur.Value + R / 2);
        await Camera.Settle(14);
        camera.Shoot(window, "vibrato", crop: PianoArea());
        editor.PianoTool.Value = PianoTool.Note;

        // ==== 07 参数面板：逐条参数轨 ====
        FrameEditingPart(editor, pianoWindow, pianoScroll, vocalPart);
        await Camera.Settle(10);
        foreach (var (id, name) in new[] { ("Volume", "parameter-volume"), ("Growl", "parameter-growl") })
        {
            pianoWindow.ActiveAutomation = AutomationKey.Voice(id);
            await Camera.Settle(12);
            var area = B(pianoScroll);
            var tabArea = B(parameterTabBar);
            // 参数区 + 下方页签条一起收进画面（页签条说明「当前在编哪条轨」）。
            double top = area.Y + PianoAreaHeight(pianoWindow);
            camera.Shoot(window, name, crop: new Rect(area.X, top, area.Width, tabArea.Bottom - top));
        }

        // ==== 07 参数面板多开：上面一块看音量、下面一块（焦点，标题栏带圆点）画气声 ====
        pianoWindow.ActiveAutomation = AutomationKey.Voice("Volume");
        pianoWindow.AddParameterPanel();
        pianoWindow.ActiveAutomation = AutomationKey.Voice("Growl");
        await Camera.Settle(6);
        // 面板堆变高了：重新取景，让音符落回堆顶以上（参数区是半透明的，压在音符上会透出来）。
        FrameEditingPart(editor, pianoWindow, pianoScroll, vocalPart);
        await Camera.Settle(12);
        {
            var area = B(pianoScroll);
            var tabArea = B(parameterTabBar);
            double top = area.Y + PianoAreaHeight(pianoWindow);
            camera.Shoot(window, "parameter-panels", crop: new Rect(area.X, top, area.Width, tabArea.Bottom - top));
        }
        pianoWindow.CloseFocusedParameterPanel();
        await Camera.Settle(8);

        // ==== 08 侧栏各页 ====
        vocalPart.Notes.ElementAt(2).Select();
        await Camera.Settle(10);
        foreach (var (tab, name) in new[]
        {
            (SideBarTab.PartProperties, "sidebar-part-properties"),
            (SideBarTab.NoteProperties, "sidebar-note-properties"),
            (SideBarTab.Export, "sidebar-export"),
            (SideBarTab.Script, "sidebar-script"),
            (SideBarTab.Agent, "sidebar-agent"),
            (SideBarTab.Extensions, "sidebar-extensions"),
        })
        {
            tabBar.Restore([tab]);
            await Camera.Settle(14);
            var area = B(Camera.Find<SideBar>(window));
            camera.Shoot(window, name, crop: new Rect(area.X, area.Y, area.Width + B(tabBar).Width, area.Height));
        }

        // ==== 歌词输入窗（选中几个音符后批量输入歌词）====
        var lyricNotes = vocalPart.Notes.Skip(4).Take(3).ToList();
        LyricInput.EnterInput(lyricNotes, window);
        await Camera.Settle(18);
        var lyricWindow = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!)
            .Windows.OfType<LyricInput>().FirstOrDefault();
        if (lyricWindow != null)
        {
            camera.Shoot(lyricWindow, "lyric-input");
            lyricWindow.Close();
            await Camera.Settle(8);
        }
        else
        {
            Console.WriteLine("[skip] lyric input window not found");
        }

        // ==== 合成波形（重新打开波形显示，单独一张）====
        pianoWindow.SetWaveformVisible(true);
        FrameEditingPart(editor, pianoWindow, pianoScroll, vocalPart);
        await Camera.Settle(16);
        camera.Shoot(window, "waveform", crop: PianoArea());

        // ==== 10 设置窗 ====
        await ShootSettingsWindow(window, camera);

        // ==== 快捷键表（从真实注册表导出）====
        Dump.Keybindings(Path.Combine(repoRoot, "docs", "generated", "keybindings.md"));
    }

    // 设置窗的每一页各拍一张。页签切换走窗体自己的 SelectTab（私有，故用反射）——
    // 这比模拟点击稳：页序与页名都由窗体持有，工具不必复述一份。
    static async Task ShootSettingsWindow(MainWindow owner, Camera camera)
    {
        SettingsWindow.Open(owner);
        await Camera.Settle(20);
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var settings = lifetime.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (settings == null)
        {
            Console.WriteLine("[skip] settings window not found");
            return;
        }

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(SettingsWindow);
        var selectTab = type.GetMethod("SelectTab", flags);
        var pages = type.GetField("mTabPages", flags)?.GetValue(settings) as System.Collections.IList;
        if (selectTab == null || pages == null)
        {
            camera.Shoot(settings, "settings-window");
        }
        else
        {
            for (int i = 0; i < pages.Count; i++)
            {
                selectTab.Invoke(settings, [i]);
                await Camera.Settle(16);
                string name = pages[i]?.GetType().GetProperty("Name")?.GetValue(pages[i]) as string ?? (i + 1).ToString();
                camera.Shoot(settings, "settings-" + Slug(name));
            }
        }

        settings.Close();
        await Camera.Settle(8);
    }

    static string Slug(string name) => name.ToLowerInvariant().Replace(' ', '-');

    // 功能栏中间那一组工具按钮：唯一一个「全是 Toggle 的 StackPanel」。
    static List<Control> ToolButtons(Visual functionBar)
    {
        foreach (var panel in Camera.Descendants<StackPanel>(functionBar))
        {
            var children = panel.Children.OfType<Control>().ToList();
            if (children.Count >= 4 && children.All(c => c is Toggle))
                return children;
        }
        Console.WriteLine("[warn] tool button group not found");
        return new List<Control>();
    }

    // 取景：让 [startTick, endTick] 正好铺满视图宽度。
    static void FrameTicks(TickAxis axis, Visual view, double startTick, double endTick)
    {
        double width = view.Bounds.Width;
        if (width < 1 || endTick <= startTick)
            return;
        axis.Factor = width / (endTick - startTick);
        axis.MoveTickToX(startTick, 0);
    }

    // 钢琴窗取景：横向铺满整段，纵向把音域框进「钢琴区」——即参数面板以上那段可视高度。
    // 用整个 PianoScrollView 的高度算会把音符排到参数面板底下去（同一个滚动视图，参数面板只是压在下半部）。
    static void FrameEditingPart(Editor editor, PianoWindow pianoWindow, Visual pianoScroll, MidiPart part)
    {
        FrameTicks(editor.PianoTickAxis, pianoScroll, part.StartPos, part.EndPos);
        FramePitches(editor, pianoWindow, part);
    }

    static void FramePitches(Editor editor, PianoWindow pianoWindow, MidiPart part)
    {
        var pitches = part.Notes.Select(n => (double)n.Pitch.Value).ToList();
        if (pitches.Count == 0)
            return;
        double low = pitches.Min() - 2;
        double high = pitches.Max() + 2;
        double height = PianoAreaHeight(pianoWindow);
        if (height < 1)
            return;
        var axis = editor.PianoPitchAxis;
        axis.Factor = height / (high - low + 1);
        axis.MovePitchToY(high + 1, 0);
    }

    // 钢琴区（音符区）的可视高度：滚动视图总高减去参数面板占掉的部分。
    static double PianoAreaHeight(PianoWindow pianoWindow) => Math.Max(1, pianoWindow.WaveformBottom);
}
