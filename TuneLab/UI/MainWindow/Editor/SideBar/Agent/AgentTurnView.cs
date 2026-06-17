using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TuneLab.Agent;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Utils;
using ScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace TuneLab.UI;

// 一条助手回复（一轮用户输入对应一条气泡）的「分步」内容视图：按 AgentRunner 发来的事件序，把
// 助手自然语言文本段与工具调用步骤块交替铺进一个竖直面板——让"模型说了什么 / 正在调哪个工具 / 工具结果"全程可见。
//
// 关键点：
//  · 文本段边流边渲：累积原文，用 ~100ms 节流定时器合并多次增量、整段重渲 Markdown（不逐 token 重建，避免卡顿/闪烁）。
//    被工具调用打断或本轮结束时「封口」做一次最终渲染。流到一半的未闭合结构（如代码围栏）会短暂半成品，闭合即恢复。
//  · 多轮叙述各自成段、按序保留——这天然修掉了"循环结束用最后一轮文本整体替换气泡、导致前面叙述消失"的旧 bug。
//  · 工具块按 Id 关联「开始/完成」两事件；可点击展开看完整参数与结果。
internal sealed class AgentTurnView
{
    public AgentTurnView()
    {
        // 两层：内容区在上、底部"生成中"三点动画常驻——让用户知道这条消息还没结束（生成结束时移除）。
        mRoot.Children.Add(mContent);
        mRoot.Children.Add(mThinking);
    }

    public Control Root => mRoot;

    // 是否还没产生任何内容（用于回退显示 "(no text reply)"）。
    public bool IsEmpty => mContent.Children.Count == 0;

    // 末尾追加任意控件（脚注、停止/出错提示行）——加在内容区，位于底部"生成中"指示之上。
    public void Append(Control control) => mContent.Children.Add(control);

    // 生成结束：移除底部"生成中"动画（移出视觉树即自动停表）。成功/停止/出错路径都应调用。
    public void EndThinking() => mRoot.Children.Remove(mThinking);

    public void Apply(AgentEvent e)
    {
        switch (e)
        {
            case AgentTextDelta d:
                AppendTextDelta(d.Delta);
                break;
            case AgentToolStarted s:
                SealText(); // 文本段被工具调用打断 → 封口转 Markdown
                AddToolStep(s.Id, s.Name, s.ArgumentsJson);
                break;
            case AgentToolFinished f:
                FinishToolStep(f.Id, f.Result, f.IsError);
                break;
        }
    }

    // 封口当前流式文本段：停掉节流定时器、做一次最终 Markdown 渲染定稿（轮结束/停止/出错前调用）。
    public void SealText()
    {
        StopTimer();
        if (mLiveControl == null)
            return;
        if (mDirty)
            RenderLive();
        mLiveControl = null;
        mDirty = false;
        mRawText.Clear();
    }

    // 停止/出错时把仍在「运行中」的工具步骤标记为已中止（否则会永远停在运行态的圆点上）。
    public void MarkPendingAborted()
    {
        foreach (var step in mSteps.Values)
            if (!step.Finished)
                step.SetAborted("Stopped".Tr(this));
    }

    void AppendTextDelta(string delta)
    {
        mRawText.Append(delta);
        if (mLiveControl == null)
        {
            // 段首：立刻渲一次并起节流定时器；之后的增量只置脏标志，由定时器合并重渲。
            mLiveControl = ChatMarkdownRenderer.Render(mRawText.ToString());
            mContent.Children.Add(mLiveControl);
            mDirty = false;
            StartTimer();
        }
        else
        {
            mDirty = true;
        }
    }

    // 把当前文本段按累积原文重渲 Markdown，原地替换。
    void RenderLive()
    {
        if (mLiveControl == null)
            return;
        int idx = mContent.Children.IndexOf(mLiveControl);
        if (idx < 0)
            return;
        var rendered = ChatMarkdownRenderer.Render(mRawText.ToString());
        mContent.Children[idx] = rendered;
        mLiveControl = rendered;
        mDirty = false;
    }

    void StartTimer()
    {
        mRenderTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        mRenderTimer.Tick -= OnRenderTick; // 防重复订阅
        mRenderTimer.Tick += OnRenderTick;
        mRenderTimer.Start();
    }

    void StopTimer()
    {
        if (mRenderTimer == null)
            return;
        mRenderTimer.Stop();
        mRenderTimer.Tick -= OnRenderTick;
    }

    void OnRenderTick(object? sender, EventArgs e)
    {
        if (mDirty)
            RenderLive();
    }

    void AddToolStep(string id, string name, string argumentsJson)
    {
        var step = new ToolStep(this, name, argumentsJson);
        if (!string.IsNullOrEmpty(id))
            mSteps[id] = step;
        mContent.Children.Add(step.Root);
    }

    void FinishToolStep(string id, string result, bool isError)
    {
        if (!string.IsNullOrEmpty(id) && mSteps.TryGetValue(id, out var step))
            step.Finish(result, isError);
    }

    // 单个工具步骤块：可点击展开/收起。头部=状态点 + 工具名 + 参数单行预览 + 展开箭头；详情=完整参数 + 结果文本。
    sealed class ToolStep
    {
        public Control Root => mBorder;
        public bool Finished { get; private set; }

        public ToolStep(AgentTurnView owner, string name, string argumentsJson)
        {
            mStatus = new TextBlock
            {
                Text = "●",
                FontSize = 11,
                Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(), // 运行中=灰点
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new(0, 0, 6, 0),
            };
            var nameBlock = new TextBlock
            {
                Text = name,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Colors.White.ToBrush(),
                VerticalAlignment = VerticalAlignment.Center,
            };
            mChevron = new TextBlock
            {
                Text = "▸",
                FontSize = 10,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new(6, 0, 0, 0),
            };
            // 参数单行预览（收起时也能瞥见在干什么）：填充中间剩余宽，过长省略号。
            var preview = new TextBlock
            {
                Text = OneLine(argumentsJson),
                FontSize = 11,
                Foreground = Style.LIGHT_WHITE.Opacity(0.45).ToBrush(),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new(8, 0, 0, 0),
            };

            // 透明背景（非 null）让整行参与命中测试——点击行内任意空白处都能展开/收起，不必恰好点到文字。
            var header = new DockPanel { LastChildFill = true, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            DockPanel.SetDock(mStatus, Dock.Left);
            DockPanel.SetDock(nameBlock, Dock.Left);
            DockPanel.SetDock(mChevron, Dock.Right);
            header.Children.Add(mStatus);
            header.Children.Add(nameBlock);
            header.Children.Add(mChevron);
            header.Children.Add(preview); // 填充
            header.PointerPressed += (_, _) => Toggle();

            // 详情区（默认收起）：完整参数 + 结果文本。
            mDetails = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
            mDetails.Children.Add(Mono("Arguments".Tr(owner), PrettyJson(argumentsJson)));
            // 限高可滚动：长参数/结果不再把气泡撑得老长——超出在块内滚动，收起只需点回头部、无需翻回顶部。
            mDetailsScroll = new ScrollViewer
            {
                MaxHeight = 240,
                Margin = new(0, 6, 0, 0),
                IsVisible = false,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = mDetails,
            };

            var panel = new StackPanel { Orientation = Orientation.Vertical, Children = { header, mDetailsScroll } };
            mBorder = new Border
            {
                Background = Style.INTERFACE.ToBrush(), // 取消助手气泡后，工具块用 INTERFACE 浮在更暗的面板上、清晰成块
                BorderBrush = Style.LIGHT_WHITE.Opacity(0.12).ToBrush(),
                BorderThickness = new(1),
                CornerRadius = new(6),
                Padding = new(8, 6),
                Margin = new(0, 4),
                Child = panel,
            };
        }

        public void Finish(string result, bool isError)
        {
            Finished = true;
            mStatus.Text = isError ? "✗" : "✓";
            mStatus.Foreground = (isError ? Colors.IndianRed : Colors.MediumSeaGreen).ToBrush();
            mDetails.Children.Add(Mono(isError ? "Error".Tr(this) : "Result".Tr(this), result));
            // 出错时自动展开，结果直接可见（成功保持收起，避免长结果刷屏）。
            if (isError)
                SetExpanded(true);
        }

        public void SetAborted(string label)
        {
            Finished = true;
            mStatus.Text = "○";
            mStatus.Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush();
            mDetails.Children.Add(Mono(label, "—"));
        }

        void Toggle() => SetExpanded(!mExpanded);

        void SetExpanded(bool expanded)
        {
            mExpanded = expanded;
            mDetailsScroll.IsVisible = expanded;
            mChevron.Text = expanded ? "▾" : "▸";
        }

        // 一个带小标签的等宽结果块（参数 / 结果 / 错误），文本可选中复制。
        static Control Mono(string label, string body)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush(),
            });
            panel.Children.Add(new SelectableTextBlock
            {
                Text = body,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas, Menlo, Courier New"),
                Foreground = Style.LIGHT_WHITE.Opacity(0.8).ToBrush(),
                TextWrapping = TextWrapping.Wrap,
            });
            return panel;
        }

        readonly Border mBorder;
        readonly TextBlock mStatus;
        readonly TextBlock mChevron;
        readonly StackPanel mDetails;
        readonly ScrollViewer mDetailsScroll;
        bool mExpanded;
    }

    // 把参数 JSON 压成单行预览（去多余空白、限长）。
    static string OneLine(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return string.Empty;
        var sb = new StringBuilder(json.Length);
        bool inString = false, prevSpace = false;
        foreach (var ch in json)
        {
            if (ch == '"')
                inString = !inString;
            if (!inString && (ch == '\n' || ch == '\r' || ch == '\t' || ch == ' '))
            {
                if (prevSpace) continue;
                sb.Append(' ');
                prevSpace = true;
                continue;
            }
            sb.Append(ch);
            prevSpace = false;
        }
        var s = sb.ToString().Trim();
        return s.Length <= 80 ? s : s[..80].TrimEnd() + "…";
    }

    // 缩进美化参数 JSON；非法 JSON 原样返回。用 UnsafeRelaxed 编码器避免中文被转义成 \uXXXX。
    static string PrettyJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, PrettyOptions);
        }
        catch
        {
            return json;
        }
    }

    static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 等待/生成中三点动画：循环 • / • • / • • •。计时器随控件挂载启动、脱离视觉树即停，自清理。
    // 同时用于：响应初期占位气泡（首事件前）与本视图底部常驻指示（生成期间）。
    public static Control ThinkingDots()
    {
        string[] frames = { "•", "• •", "• • •" };
        var tb = new TextBlock { Text = frames[0], FontSize = 14, Foreground = Colors.White.ToBrush(), Margin = new(2, 4, 0, 2) };
        DispatcherTimer? timer = null;
        int i = 0;
        tb.AttachedToVisualTree += (_, _) =>
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (_, _) => { i = (i + 1) % frames.Length; tb.Text = frames[i]; };
            timer.Start();
        };
        tb.DetachedFromVisualTree += (_, _) => { timer?.Stop(); timer = null; };
        return tb;
    }

    readonly StackPanel mRoot = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    readonly StackPanel mContent = new() { Orientation = Orientation.Vertical, Spacing = 2 }; // 文本段 + 工具块 + 脚注
    readonly Control mThinking = ThinkingDots(); // 底部常驻"生成中"指示（EndThinking 时移除）
    readonly Dictionary<string, ToolStep> mSteps = [];
    readonly StringBuilder mRawText = new(); // 当前文本段原文（节流重渲 Markdown 的源）
    Control? mLiveControl;        // 当前文本段已渲染控件（未封口，节流时原地替换）
    bool mDirty;                  // 自上次渲染后有新增量待重渲
    DispatcherTimer? mRenderTimer; // 节流定时器（~100ms 合并多次增量）
}
