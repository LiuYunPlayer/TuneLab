using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Extensions.Effect;
using System.Reactive.Linq;
using TuneLab.Data.Synthesis;
using TuneLab.Utils;
using System.Threading;

namespace TuneLab.Data;

internal class MidiPart : Part, IMidiPart
{
    public string RuntimeId { get; } = Data.RuntimeId.Next();
    public IActionEvent<DeclarationInvalidation> DeclarationsInvalidated => mDeclarationsInvalidated;
    public IActionEvent SynthesisStatusChanged => mSynthesisStatusChanged;
    // 分离产物信号（各自仅对应产物变化触发，不被进度/其它产物 tick 带动）：精确响应的消费者订阅这些、而非合并的 SynthesisStatusChanged。
    public IActionEvent SynthesizedPhonemesChanged => mSynthesizedPhonemesChanged;
    public IActionEvent SynthesizedParametersChanged => mSynthesizedParametersChanged;
    public IActionEvent SynthesizedPitchChanged => mSynthesizedPitchChanged;
    // 自动化轨集合因参数 commit 而变（voice 依赖 part 参数、各 effect 依赖自身参数）：UI 收到重建参数栏/默认值面板。
    // 仅在轨集合实际变化时触发（签名比对去抖）；换源/链增删走各自既有 UI 触发，不重复经此事件。
    public IActionEvent AutomationConfigsModified => mAutomationConfigsModified;
    // 参数面板属性 lane 集合（物化缓存，note / phoneme 两 scope）：各 note 声明的并集里钉选了的可钉字段 + 全部时间轴画布，按首见顺序。
    // 重建时机与 voice 轨集合同步（管线重建/换源/part 参数 commit）+ 钉选变更（RefreshPinnedLaneConfigs）
    // + 音素回填补算（phoneme lane 声明面依赖合成产物，见 OnPipelinePhonemesChanged）。
    // + 插件宣告声明失效（InvalidateDeclarations）+ part 数据提交（见 OnDocumentStatusChanged：声明 context 带着
    // part 的全部数据，提交后宿主重新询问声明）。只跟提交、不跟每次改动：批量粘贴 / 拖拽时不反复全量求值。
    public IReadOnlyOrderedMap<PropertyKey, LaneEntry> NoteLaneConfigs => mNoteLaneConfigs;
    public IReadOnlyOrderedMap<PropertyKey, LaneEntry> PhonemeLaneConfigs => mPhonemeLaneConfigs;
    // 单 note 声明（按 note 缓存，见 NoteDeclarationCache）：lane 逐段的形状与侧栏单选都走这里。
    public ObjectConfig GetNotePropertyConfig(INote note) => mNoteDeclarations.NoteConfig(note);
    public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(INote note) => mNoteDeclarations.PhonemeConfigs(note);
    public bool TryGetNoteLaneShape(INote note, string id, out LaneEntry shape) => mNoteDeclarations.TryGetNoteLaneShape(note, id, out shape);
    public bool TryGetPhonemeLaneShape(INote note, int index, string id, out LaneEntry shape) => mNoteDeclarations.TryGetPhonemeLaneShape(note, index, id, out shape);
    public LanePath.Resolved? ResolveNoteLane(INote note, string path) => mNoteDeclarations.ResolveNoteLane(note, path);
    public LanePath.Resolved? ResolvePhonemeLane(INote note, int index, string path) => mNoteDeclarations.ResolvePhonemeLane(note, index, path);
    public override DataString Name { get; }
    public override DataStruct<double> Pos { get; }
    public override DataStruct<double> StartOffset { get; }
    public override DataStruct<double> EndOffset { get; }
    public DataStruct<double> Gain { get; }
    public DataPropertyObject Properties { get; }
    public INoteList Notes => mNotes;
    public ISoundSource SoundSource => mSource;
    IDataProperty<double> IMidiPart.Gain => Gain;
    public IReadOnlyDataObjectMap<string, IAutomation> Automations => mAutomations;
    // 声明分段轨（除 Pitch 外、声源声明的可编辑分段曲线，即 AutomationConfig.IsPiecewise 的轨），按轨 id 存。
    // Pitch 是 part 专属常驻通道、在音符区编辑，不入此 map。
    public IReadOnlyDataObjectMap<string, IPiecewiseAutomation> PiecewiseAutomations => mPiecewiseAutomations;
    public IReadOnlyDataObjectList<IEffect> Effects => mEffects;
    public IPiecewiseAutomation Pitch => mPitchLine;
    public IReadOnlyDataObjectLinkedList<Vibrato> Vibratos => mVibratos;

    // —— 合成消费面（session 模型）：状态/产物全由插件托管，宿主经管线包装拉取展示 ——
    public ISynthesisPipeline? SynthesisPipeline => mPipeline;
    public bool IsSynthesisBatching => mSynthesisBatch.IsBatching;
    internal BatchSignal SynthesisBatch => mSynthesisBatch;
    public IReadOnlyList<SynthesisDisplaySegment> GetSynthesisStatus() => mPipeline?.GetStatus() ?? [];
    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => mPipeline?.SynthesizedPitch ?? [];
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mPipeline?.SynthesizedParameters ?? EmptySynthesizedParameters;
    public IReadOnlyMap<string, SynthesizedParameter> GetEffectSynthesizedParameters(IEffect effect) => mPipeline?.GetEffectSynthesizedParameters(effect) ?? EmptySynthesizedParameters;
    public IReadOnlyList<Synthesis.SynthesizedSegment> SynthesizedSegments => mPipeline?.SynthesizedSegments ?? [];

    public MidiPart(ITrack track, MidiPartInfo info) : base(track)
    {
        mOnTimebaseModified = OnTimebaseModified;
        mOnPronunciationPolicyModified = OnPronunciationPolicyModified;
        mNoteDeclarations = new(this);
        Name = new(this, string.Empty);
        Pos = new(this);
        StartOffset = new(this);
        EndOffset = new(this);
        Gain = new(this);
        Properties = new(this);
        mSource = new(this, new SoundSourceInfo());
        mNotes = new();
        mNotes.Attach(this);
        mVibratos = new();
        mVibratos.Attach(this);
        mAutomations = new(this);
        mPiecewiseAutomations = new(this);
        mEffects = new(this);
        mEffects.ItemAdded.Subscribe(OnEffectAdded);
        mEffects.ItemRemoved.Subscribe(OnEffectRemoved);
        mEffects.MembershipModified.Subscribe(OnEffectChainMembershipModified);
        mPitchLine = new();
        mPitchLine.Attach(this);
        StartOffset.Modified.Subscribe(mDurationChanged);
        EndOffset.Modified.Subscribe(mDurationChanged);
        // 换声源：丢弃旧会话、重建新会话（context 随会话重建）。
        // 注：本订阅在一切 UI 订阅之前注册，UI 收到 Voice.Modified 时声明已刷新。
        mSource.Modified.Subscribe(OnVoiceModified);
        // part 参数 commit：voice 的条件自动化轨集合可能随之变；重算并按需通知 UI。
        Properties.Modified.Subscribe(OnPartPropertiesModified);
        // 延音判定缓存失效（源一）：判定输入域含会话可达的一切 part 数据，part 级任意数据变更保守一律
        // 失效——含 merge 中间态（拖拽期布局同样消费判定，输入变了就得重判）。注意输入域**大于**本数据
        // 树：代理边界是 tempo 派生的秒值，tempo 表在工程级树外——由时基重建接线覆盖（源二，见
        // Activate / OnTimebaseModified）。part 平移（Pos）在本数据树内、经源一覆盖，其会话重建走
        // 时基入口（见下 Pos 订阅）。合成产物是运行时字段、不在任何失效源内，天然不触发——顺带机械
        // 强制"判定禁依赖产物"。
        Modified.AsEverytime().Subscribe(_ => mContinuationDirty = true);
        // 单 note 声明缓存同一个失效面（声明 context 经 Part 读得到整个 part），但**编辑落定才作废**（同 part 级声明，见
        // OnNoteDeclarationInputsModified）。只清表、不重新询问，用到再取。注册在一切 UI 订阅之前，UI 的刷新读到的已是清过的缓存。
        Modified.AsEverytime().Subscribe(_ => OnNoteDeclarationInputsModified());
        // 声明输入变了（见 OnDocumentStatusChanged）：只记一笔，提交时才重新询问声明。
        Modified.AsEverytime().Subscribe(_ => OnDeclarationInputsModified());
        // part 平移 → 时基重建（兑现 VoiceSynthesisContext 既定设计的另一半）：平移跨 bpm 段会改
        // 各 note 秒时长、非纯偏移；note 边界代理刻意不订阅 part.Pos，其变化由 session 重建覆盖。
        // 拖动的每步是 DiscardTo+重放（皆 settled），重建靠 OnTimebaseModified 的调度轮折叠去抖。
        Pos.Modified.Subscribe(mOnTimebaseModified);
        // 其余数据变更（note/pitch/automation/tempo/平移）不再由 part 驱动重分片——
        // 失效判定归插件，变更流经 VoiceSynthesisContext 转发。
        SetInfo(info);
    }

    public void InsertNote(INote note)
    {
        mNotes.Insert(note);
    }

    public bool RemoveNote(INote note)
    {
        // 归属由集合判定（链表反向指针）；非成员删除是编程错误，DEBUG 期就地暴露，Release 仍宽容 no-op。
        System.Diagnostics.Debug.Assert(mNotes.Contains(note), "RemoveNote: note 不属于本 part。");
        return mNotes.Remove(note);
    }

    // —— 延音判定缓存 —— 会话 IsContinuation 的按 part 求值结果（插件完整判定，宿主不叠加任何自己的
    // 判据、照单消费）。缓存是承重件：消费在渲染速率（DisplayPhonemes → ForwardFillEnd → 重绘），跨插件
    // 边界的判定调用不能跟着渲染跑；失效在编辑速率（构造里的 part 级 Modified 单订阅点）。
    public bool IsPluginContinuation(INote note)
    {
        if (mContinuationDirty)
        {
            mContinuation.Clear();
            foreach (var n in mNotes)
                mContinuation[n] = JudgeContinuation(n);
            mContinuationDirty = false;
        }
        return mContinuation.TryGetValue(note, out var v) && v;
    }

    // 会话判定护栏（与声明求值同策略：插件抛异常记日志回退 false，不拖垮布局）。
    // voice part **恒有会话**（无声源/未知源回退空引擎会话，其判定 = 编辑器 "-" 约定，见
    // EmptyVoiceSynthesisEngine），故无会话分支实际只剩 instrument part（无音素/延音概念，
    // 恒 false 如实）与管线重建的同步瞬时窗口（现实不可达）。
    bool JudgeContinuation(INote note)
    {
        if (mVoicePipeline == null)
            return false;
        try
        {
            return mVoicePipeline.JudgeContinuation(note);
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed("Session continuation judgment threw", ex);
            return false;
        }
    }

    public IEffect CreateEffect(EffectInfo info)
    {
        return new Effect(this, info);
    }

    public void InsertEffect(int index, IEffect effect)
    {
        mEffects.Insert(index, effect);
    }

    public bool RemoveEffect(IEffect effect)
    {
        return mEffects.Remove(effect);
    }

    void OnEffectAdded(IEffect effect)
    {
        void handler() => OnEffectModified(effect);
        mEffectModifiedHandlers[effect] = handler;
        effect.Modified.Subscribe(handler);
    }

    void OnEffectRemoved(IEffect effect)
    {
        if (mEffectModifiedHandlers.Remove(effect, out var handler))
            effect.Modified.Unsubscribe(handler);
    }

    // 某个 effect 的参数/启用/自动化变化：失效与重处理由效果图各 processor 订阅自管（processor 经
    // IEffectSynthesisContext 自算 dirty），宿主此处只处理条件自动化轨集合的 UI 增量刷新。
    void OnEffectModified(IEffect effect)
    {
        int index = mEffects.IndexOf(effect);
        if (index < 0)
            return;
        // 该 effect 的参数变可能改其条件自动化轨集合（仅活跃 part 才有 UI 在看，且引擎已 Init）。
        // AutomationConfigs 为 live 求值，此刻读即取当前参数值（参数值在 Notify 前已写入），无缓存时序问题。
        if (mPipeline != null && RefreshAutomationConfigsSignatureChanged())
            mAutomationConfigsModified.Invoke();
    }

    // part 参数 commit：voice 条件自动化轨集合可能随之变；重算 voice 轨集合，仅当聚合签名变时通知 UI。
    void OnPartPropertiesModified()
    {
        if (mPipeline == null)
            return;
        mSource.RebuildAutomationConfigs(BuildPartPropertyContext());
        mNoteDeclarations.Invalidate();   // 不依赖与 Modified 订阅的先后：重算 lane 之前先作废
        RebuildLaneConfigs();   // note/phoneme 属性 config 依赖 part 值，lane 集合/量程可能随之变
        if (RefreshAutomationConfigsSignatureChanged())
            mAutomationConfigsModified.Invoke();
    }

    // 声明 context 带着 part 的全部数据（音高 / 歌词、note 集合、自动化曲线、part 属性……），SDK 也写明插件可据此条件化
    // 声明——这些数据一提交，宿主就重新询问 part 级声明（note 声明随之级联），与插件 InvalidatePart 走同一条合拍路径，
    // 插件不必自己订阅再 Invalidate。只在撤销根落定时（提交 / 放弃 / 撤销 / 重做）触发、且本 part 的数据确实改过：
    // 钢琴窗拖动的每一步都是 settled 的改动（DiscardTo + 重放），跟每次改动就会每帧重新声明。
    void OnDeclarationInputsModified()
    {
        mDeclarationInputsDirty = true;
        if (mDocument == null && mHostInvalidator != null && (Track?.Project as Project)?.Document is { } document)
        {
            mDocument = document;
            document.StatusChanged += OnDocumentStatusChanged;
        }
    }

    // 编辑进行中（拖动等：文档里有未提交的改动）不作废单 note 声明缓存，落定时由 InvalidateDeclarations 统一作废、重算 lane、
    // 通知 UI（与 part 级声明同一个时机）。否则画一笔曲线每一帧都把视口里全部 note / 音素的声明重问一遍，画布实例也逐帧重建。
    // 代价：拖动途中声明滞后到松手（插件按曲线值条件化的声明、拖动改了音高的 note 的 lane 形状，都在松手后才更新）；
    // 拖动中新出现的 note 不受影响（缓存里没有它，当场去问）。没有落定通知可等时（未激活 / 不在文档里，如测试）照旧立即作废。
    void OnNoteDeclarationInputsModified()
    {
        if (mHostInvalidator != null && (Track?.Project as Project)?.Document is { HasUncommittedChanges: true })
            return;
        mNoteDeclarations.Invalidate();
    }

    void OnDocumentStatusChanged()
    {
        if (!mDeclarationInputsDirty || mDocument is not { HasUncommittedChanges: false })
            return;   // 本 part 没改过，或还在编辑中（StatusChanged 在每压进一条命令时也会发）
        mDeclarationInputsDirty = false;
        mHostInvalidator?.InvalidatePart();
    }

    // 插件宣告声明失效（会话 context 的 InvalidatePart / InvalidateNotes，经 DeclarationInvalidator 合拍、已在数据线程）。
    // 先重算本 part 持有的声明物化缓存，再发 DeclarationsInvalidated 给常驻显示声明的 UI（属性面板各自按范围刷新）。
    // · part 级：与 part 参数 commit 同一套（轨集合 + lane），note 声明随之级联（UI 侧按 Part 标志处理）；
    // · note 级：只重算 lane——lane 按「全 part note」口径求值，任何 note 的声明变了都可能改它，与选区无关；
    //   轨集合是 part 作用域声明、不向上级联，故不动。
    // 不碰延音缓存、不标任何合成脏（契约：只作废插件自己的声明回答）。
    internal void InvalidateDeclarations(DeclarationInvalidation invalidation)
    {
        if (mPipeline == null)
            return;   // 已停用：context 随管线销毁，正常不会到这里；防御迟到的 flush
        mNoteDeclarations.Invalidate();
        if (invalidation.Part)
            mSource.RebuildAutomationConfigs(BuildPartPropertyContext());
        RebuildLaneConfigs();
        if (RefreshAutomationConfigsSignatureChanged())
            mAutomationConfigsModified.Invoke();
        mDeclarationsInvalidated.Invoke(invalidation);
    }

    // 钉选变更后由动作方（侧栏菜单）调用：重算两 scope 的 lane 集合，实变则通知参数面板 UI（tabbar/渲染器同批消费者）。
    public void RefreshPinnedLaneConfigs()
    {
        RebuildLaneConfigs();
        if (RefreshAutomationConfigsSignatureChanged())
            mAutomationConfigsModified.Invoke();
    }

    // lane 集合 = 各 note 自己声明的条目的并集中：钉选了的可钉字段（有界数值、侧栏画布，轨色来自钉选）+ 全部时间轴画布
    //（插件声明即常驻，不经钉选）。某个 note 声明了它，这条 lane 就在（其余 note 在这条 lane 上没有段，见 TryGetNoteLaneShape）。
    // 不能拿全部 note 合并成一份声明来判：插件的声明随所选 note 的值变，合并后的形状可能哪个 note 都不是。
    // 条目里的量程 / 默认值取首见声明（note 序、同 note 内 slot 升序），只作标签栏与没有段可画时的口径；逐段的形状各按各的 note。
    // 没有 note 时按"空选区"问一次，标签栏不因 part 暂时清空而丢掉钉选。
    // 常驻的时间轴画布没有钉选可短路，所以每次都要把全部 note 问一遍（按 note 缓存，part 数据一变才重问）。
    // phoneme scope：instrument 无音素（GetPhonemePropertyConfigs 恒空）→ 走真空分支出占位 tab（钉过 phoneme
    // 的 instrument 场景本就不存在）。音素级时间轴画布在合成音素产出前没有声明可问，不占位、不猜。
    // 时间轴画布在声明暂时问不到时保留（见 KeepSeenTimelineLanes），音源一换才清。
    void RebuildLaneConfigs()
    {
        var previousNoteLanes = Copy(mNoteLaneConfigs);
        var previousPhonemeLanes = Copy(mPhonemeLaneConfigs);
        var source = (mSource.Kind, mSource.Type, mSource.ID);
        if (source != mSeenTimelineSource)
        {
            mSeenTimelineSource = source;
            mSeenNoteTimelineLanes.Clear();
            mSeenPhonemeTimelineLanes.Clear();
        }
        mNoteLaneConfigs.Clear();
        mPhonemeLaneConfigs.Clear();
        var pinnedNote = ParameterPinning.GetPinned(mSource, ParameterPinKind.NoteProperty);
        var pinnedPhoneme = ParameterPinning.GetPinned(mSource, ParameterPinKind.PhonemeProperty);

        foreach (var config in NoteDeclarationsOfAllNotes())
        {
            foreach (var leaf in LanePath.Leaves(config))
                TryAddLaneEntry(mNoteLaneConfigs, pinnedNote, leaf);
        }

        bool anyPhonemeDeclaration = false;
        foreach (var configs in PhonemeDeclarationsOfAllNotes())
        {
            // 有音素声明面：正常求并集，哪个 note 都没声明的钉选自然缺席（引擎条件隐藏语义，同条件自动化轨）。
            // slot 升序遍历（=引导→核→核后的时间序）：map 无序，「首见声明」须有确定口径。
            foreach (int slot in configs.Keys.Order())
            {
                anyPhonemeDeclaration = true;
                foreach (var leaf in LanePath.Leaves(configs[slot]))
                    TryAddLaneEntry(mPhonemeLaneConfigs, pinnedPhoneme, leaf);
            }
        }
        if (!anyPhonemeDeclaration)
        {
            // 信息真空（尚无任何显示音素，典型 = 激活后合成未产出）：这不是引擎声明隐藏，钉选的 lane 须在场
            // （tab 可见）。有旧物化条目沿用（保量程/名字），否则给未解析占位；合成回填后升级（见 OnPipelinePhonemesChanged）。
            foreach (var (id, color) in pinnedPhoneme)
            {
                if (previousPhonemeLanes.TryGetValue(id, out var previous))
                    mPhonemeLaneConfigs.Add(id, previous with { Color = color });
                else
                    mPhonemeLaneConfigs.Add(id, new LaneEntry(NormalizedScale.Linear(0, 1), 0, null, null, null, color, Resolved: false));
            }
        }

        AssignTimelineCanvasColors();
        // note 声明随时问得到（同步），没有"暂时不知道"：问到没有就是没有。音素声明要等引擎对音素表态，有 note 还没表态
        //（标脏 / 合成中）就有一部分声明暂时问不到。
        KeepSeenTimelineLanes(mNoteLaneConfigs, previousNoteLanes, mSeenNoteTimelineLanes, keepAbsent: false);
        KeepSeenTimelineLanes(mPhonemeLaneConfigs, previousPhonemeLanes, mSeenPhonemeTimelineLanes,
            keepAbsent: mNotes.Any(note => !INote.HasPhonemeAnswer(note)));
        AssignLaneLabels();
    }

    // 标签名 = 最短不重名（见 LanePath.ShortestLabels）：两个 scope 的 lane 同在一条标签栏上，一起判重名。
    // 写进键的 DisplayText（标签栏、标题栏、动作面都读它）；顶层字段就是它的显示名本身。
    void AssignLaneLabels()
    {
        var all = new List<(OrderedMap<PropertyKey, LaneEntry> Lanes, PropertyKey Key, LaneEntry Entry)>();
        foreach (var lanes in new[] { mNoteLaneConfigs, mPhonemeLaneConfigs })
        {
            foreach (var kvp in lanes)
                all.Add((lanes, kvp.Key, kvp.Value));
        }
        var labels = LanePath.ShortestLabels(all.Select(item => item.Entry.Label ?? [new LanePath.LabelPart(item.Key.DisplayText ?? item.Key.Id, null)]).ToList());
        mNoteLaneConfigs.Clear();
        mPhonemeLaneConfigs.Clear();
        for (int i = 0; i < all.Count; i++)
            all[i].Lanes.Add(new PropertyKey(all[i].Key.Id, labels[i]), all[i].Entry);
    }

    static OrderedMap<PropertyKey, LaneEntry> Copy(OrderedMap<PropertyKey, LaneEntry> lanes)
    {
        var copy = new OrderedMap<PropertyKey, LaneEntry>();
        foreach (var kvp in lanes)
            copy.Add(kvp.Key, kvp.Value);
        return copy;
    }

    // 时间轴画布只在声明暂时问不到时保留（keepAbsent）：如全部 note 标脏、合成回来前音素声明暂时为空，标签栏跟着声明
    // 一会儿有一会儿没有观感很差；这时面板里只是空的。问得到却没人声明（删了列表元素、换了引擎选项）就真的去掉——
    // 列表长度是用户在增删，"出现过就留"会越积越多。
    // 留下的放回上次显示时的位置（排在它前面、此刻仍在的那个条目之后），颜色沿用上次的。
    static void KeepSeenTimelineLanes(OrderedMap<PropertyKey, LaneEntry> lanes, OrderedMap<PropertyKey, LaneEntry> previous, OrderedMap<PropertyKey, LaneEntry> seen, bool keepAbsent)
    {
        foreach (var kvp in lanes)
        {
            if (kvp.Value.Kind == LaneKind.TimelineCanvas)
                seen[kvp.Key] = kvp.Value;
        }
        if (!keepAbsent)
            return;

        var result = new List<(PropertyKey Key, LaneEntry Entry)>();
        foreach (var kvp in lanes)
            result.Add((kvp.Key, kvp.Value));
        var previousKeys = new List<PropertyKey>();
        foreach (var kvp in previous)
            previousKeys.Add(kvp.Key);

        bool changed = false;
        foreach (var kvp in seen)
        {
            if (lanes.ContainsKey(kvp.Key))
                continue;
            int at = result.Count;
            int index = previousKeys.IndexOf(kvp.Key);
            if (index >= 0)
            {
                at = 0;
                for (int i = index - 1; i >= 0; i--)
                {
                    int position = result.FindIndex(item => item.Key.Equals(previousKeys[i]));
                    if (position >= 0)
                    {
                        at = position + 1;
                        break;
                    }
                }
            }
            result.Insert(at, (kvp.Key, kvp.Value));
            changed = true;
        }
        if (!changed)
            return;

        lanes.Clear();
        foreach (var (key, entry) in result)
            lanes.Add(key, entry);
    }

    // 时间轴画布没指定颜色的，按出现顺序从调色板分（避开本 part 的自动化轨、钉选 lane 与已指定颜色的画布）。
    void AssignTimelineCanvasColors()
    {
        var occupied = new List<string>();
        foreach (var kvp in mSource.AutomationConfigs)
            occupied.Add(kvp.Value.Color);
        foreach (var effect in mEffects)
        {
            foreach (var kvp in effect.AutomationConfigs)
                occupied.Add(kvp.Value.Color);
        }
        var unassigned = new List<(OrderedMap<PropertyKey, LaneEntry> Lanes, PropertyKey Key)>();
        foreach (var lanes in new[] { mNoteLaneConfigs, mPhonemeLaneConfigs })
        {
            foreach (var kvp in lanes)
            {
                if (kvp.Value.Kind == LaneKind.TimelineCanvas && TrackColors.IsUnassigned(kvp.Value.Color))
                    unassigned.Add((lanes, kvp.Key));
                else
                    occupied.Add(kvp.Value.Color);
            }
        }
        foreach (var (lanes, key) in unassigned)
        {
            // 出现过的沿用上次分到的颜色：标签不因别的 lane 增减而换色。
            var seen = lanes == mNoteLaneConfigs ? mSeenNoteTimelineLanes : mSeenPhonemeTimelineLanes;
            var color = seen.TryGetValue(key, out var before) ? before.Color : TrackColors.Pick(occupied);
            occupied.Add(color);
            lanes[key] = lanes[key] with { Color = color };
        }
    }

    // 参数面板【可钉选】的属性（动作面的值域自省用，见 TuneLab.Input.ActionParameter）：
    // 各 note 声明里可钉的 note / phoneme 属性的并集，按首见顺序
    // ——口径与 RebuildLaneConfigs 同一份，故值域里的每一项钉上去都必然物化成一条 lane。可钉 = 有界数值或侧栏画布
    //（时间轴画布本就常驻，不在此列）。
    public IReadOnlyList<(ParameterPinKind Kind, PropertyKey Key)> PinnableProperties()
    {
        var result = new List<(ParameterPinKind, PropertyKey)>();
        var seenNote = new HashSet<string>();
        foreach (var config in NoteDeclarationsOfAllNotes())
        {
            foreach (var leaf in LanePath.Leaves(config))
            {
                if (LaneEntry.IsPinnable(leaf.Config) && seenNote.Add(leaf.Path))
                    result.Add((ParameterPinKind.NoteProperty, new PropertyKey(leaf.Path, LanePath.FullLabel(leaf.Label))));
            }
        }

        var seenPhoneme = new HashSet<string>();
        foreach (var phonemeConfigs in PhonemeDeclarationsOfAllNotes())
        {
            foreach (int slot in phonemeConfigs.Keys.Order())
            {
                foreach (var leaf in LanePath.Leaves(phonemeConfigs[slot]))
                {
                    if (LaneEntry.IsPinnable(leaf.Config) && seenPhoneme.Add(leaf.Path))
                        result.Add((ParameterPinKind.PhonemeProperty, new PropertyKey(leaf.Path, LanePath.FullLabel(leaf.Label))));
                }
            }
        }
        return result;
    }

    // 逐 note 的单 note 声明（走缓存）；没有 note 时给一份"空选区"声明，让不依赖 note 的字段照样在场。
    IEnumerable<ObjectConfig> NoteDeclarationsOfAllNotes()
    {
        if (mNotes.Count == 0)
        {
            yield return mSource.GetNotePropertyConfig(new NotePropertyContext(new PartContext(this), []));
            yield break;
        }
        foreach (var note in mNotes)
            yield return mNoteDeclarations.NoteConfig(note);
    }

    // 音素声明依赖 note 里真实的音素，没有 note 就没有可问的。
    IEnumerable<IReadOnlyMap<int, ObjectConfig>> PhonemeDeclarationsOfAllNotes()
    {
        foreach (var note in mNotes)
            yield return mNoteDeclarations.PhonemeConfigs(note);
    }

    // 时间轴画布常驻（轨色取声明，未指定的稍后由 AssignTimelineCanvasColors 分）；其余可钉字段要钉了才进（轨色取钉选）。
    // 字段在声明树里的任何位置都行（按路径，见 LanePath）：lane 的键 = 路径，标签名稍后统一算（AssignLaneLabels）。
    static void TryAddLaneEntry(OrderedMap<PropertyKey, LaneEntry> lanes, Dictionary<string, string> pinned, LanePath.Leaf leaf)
    {
        if (lanes.ContainsKey(leaf.Path))
            return;
        var key = new PropertyKey(leaf.Path, LanePath.FullLabel(leaf.Label));
        if (leaf.Config is TimelineCanvasConfig timeline)
        {
            lanes.Add(key, LaneEntry.Canvas(LaneKind.TimelineCanvas, timeline.Color) with { Label = leaf.Label });
            return;
        }
        if (!pinned.TryGetValue(leaf.Path, out var color))
            return;
        if (!LaneEntry.TryGetPinnable(leaf.Config, out var shape))
            return;
        lanes.Add(key, shape with { Color = color, Label = leaf.Label });
    }

    // 链结构变化（增删/重排）：各段弃处理器、从头重建效果链（voice 输出已缓存，不重跑 voice）。
    void OnEffectChainMembershipModified()
    {
        mPipeline?.OnEffectChainStructureChanged();
        // 链变更经 Effects.MembershipModified 已驱动 UI 重建；此处只对齐签名基线，避免后续参数 commit 误判。
        mAutomationConfigsSignature = ComputeAutomationConfigsSignature();
    }

    PartPropertyContext BuildPartPropertyContext() => PartPropertyContext.Single(this);

    // 聚合签名 = voice 轨集合 + 各 effect 轨集合（按 key + 字段平铺）；用于参数 commit 时的增量去抖。
    string ComputeAutomationConfigsSignature()
    {
        var sb = new StringBuilder();
        AppendAutomationConfigs(sb, "v", mSource.AutomationConfigs);
        // 回显轨集合（context 驱动、可随参数显隐）纳入签名：其增删要驱动标题栏工具条重建。
        AppendAutomationConfigs(sb, "r", mSource.SynthesizedParameterConfigs);
        for (int i = 0; i < mEffects.Count; i++)
            AppendAutomationConfigs(sb, "e" + i, mEffects[i].AutomationConfigs);
        // 属性 lane 集合纳入签名：钉选增删/量程变要驱动 tabbar/渲染器重建。
        AppendLaneConfigs(sb, "n", mNoteLaneConfigs);
        AppendLaneConfigs(sb, "p", mPhonemeLaneConfigs);
        return sb.ToString();
    }

    static void AppendLaneConfigs(StringBuilder sb, string source, IReadOnlyOrderedMap<PropertyKey, LaneEntry> lanes)
    {
        sb.Append(source).Append('{');
        foreach (var kvp in lanes)
        {
            var e = kvp.Value;
            sb.Append(kvp.Key.Id).Append('|').Append(kvp.Key.DisplayText).Append('|')
              .Append(e.DefaultValue).Append('|').Append(e.MinValue).Append('|')
              .Append(e.MaxValue).Append('|').Append(e.Color).Append('|')
              .Append(e.Kind).Append('|').Append(e.Resolved).Append(';');
        }
        sb.Append('}');
    }

    static void AppendAutomationConfigs(StringBuilder sb, string source, IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs)
    {
        sb.Append(source).Append('{');
        foreach (var kvp in configs)
        {
            var c = kvp.Value;
            sb.Append(kvp.Key.Id).Append('|').Append(kvp.Key.DisplayText).Append('|')
              .Append(c.DefaultValue).Append('|').Append(c.MinValue).Append('|')
              .Append(c.MaxValue).Append('|').Append(c.Color).Append(';');
        }
        sb.Append('}');
    }

    bool RefreshAutomationConfigsSignatureChanged()
    {
        var next = ComputeAutomationConfigsSignature();
        if (next == mAutomationConfigsSignature)
            return false;
        mAutomationConfigsSignature = next;
        return true;
    }

    public void InsertVibrato(Vibrato vibrato)
    {
        // 链表按 VibratoList.IsInOrder（Pos↑、同 Pos 时 Dur↓）自排，无需手工定位下标。
        mVibratos.Insert(vibrato);
    }

    public bool RemoveVibrato(Vibrato vibrato)
    {
        System.Diagnostics.Debug.Assert(mVibratos.Contains(vibrato), "RemoveVibrato: vibrato 不属于本 part。");
        return mVibratos.Remove(vibrato);
    }

    // 改 note 排序键（pos/dur）统一走 move：摘除→跑 mutate→按新键重插，维序与 undo 由集合保证。
    public void MoveNote(INote note, Action mutate) => mNotes.Move(note, mutate);
    public void MoveNotes(IReadOnlyCollection<INote> notes, Action mutate) => mNotes.Move(notes, mutate);
    public void MoveVibrato(Vibrato vibrato, Action mutate) => mVibratos.Move(vibrato, mutate);
    public void MoveVibratos(IReadOnlyCollection<Vibrato> vibratos, Action mutate) => mVibratos.Move(vibratos, mutate);

    // 固定合成音高到 Pitch 的实现已收敛到 SynthesisLock（与参数回显固定同一族：同一套秒→tick 换算、
    // 边界插值裁剪、vibrato 扣减、简化口径），此处不再另有一份。

    public double[] GetPitchVibratoDeviation(IReadOnlyList<double> ticks) => GetVibratoDeviation(ticks);

    public double[] GetVibratoDeviation(IReadOnlyList<double> ticks, string automationID = "")
    {
        // live 侧投影到共享纯函数（与冻结快照同一套算法）：振幅按目标轨现场解析。
        var vibratos = new List<VibratoMath.VibratoData>();
        foreach (var vibrato in mVibratos)
        {
            double amplitude = string.IsNullOrEmpty(automationID) ? vibrato.Amplitude : vibrato.AffectedAutomations.GetValueOrDefault(automationID, 0);
            if (amplitude == 0)
                continue;

            vibratos.Add(new VibratoMath.VibratoData(
                vibrato.Pos, vibrato.Dur, vibrato.Frequency, amplitude,
                vibrato.Phase, vibrato.Attack, vibrato.Release));
        }

        Func<double[], double[]>? envelopeSampler = Automations.TryGetValue(ConstantDefine.VibratoEnvelopeID, out var vibratoEnvelope)
            ? vibratoEnvelope.GetValues
            : null;
        return VibratoMath.GetDeviation(vibratos, ticks, envelopeSampler, Pos.Value, TempoManager.GetTime);
    }

    public double[] GetFinalPitch(IReadOnlyList<double> ticks)
    {
        var pitch = mPitchLine.GetValues(ticks);
        var vibratos = GetVibratoDeviation(ticks);
        for (int i = 0; i < ticks.Count; i++)
        {
            if (double.IsNaN(pitch[i]))
                continue;

            pitch[i] += vibratos[i];
        }

        return pitch;
    }

    // 取值按【当前声明的形态】分派，而非"哪张 map 里有数据"：同一 key 换过形态（连续 ⇄ 分段）时两张 map
    // 可能同时留有数据（孤儿保留策略），此时只有声明说得清该读哪一张。未声明的 key 仍按老路走
    // （有数据即读、无数据则 GetEffectiveAutomationConfig 抛错），不为分段轨放宽。
    public double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID)
    {
        if (SoundSource.AutomationConfigs.TryGetValue(automationID, out var declared) && declared.IsPiecewise)
        {
            // 分段轨：数据在另一张 map，未绘制处 NaN（= 无值，与 Pitch 同口径）。
            return mPiecewiseAutomations.TryGetValue(automationID, out var piecewise)
                ? piecewise.GetValues(ticks)
                : NaNs(ticks.Count);
        }

        double[] values;
        if (mAutomations.TryGetValue(automationID, out var automation))
        {
            values = automation.GetValues(ticks);
        }
        else
        {
            var defaultValue = GetEffectiveAutomationConfig(automationID).DefaultValue;
            values = new double[ticks.Count];
            values.Fill(defaultValue);
        }

        return values;
    }

    // 终值 = 曲线值 + 该轨 vibrato 偏移。连续轨处处有值故处处叠加；**分段轨只在有值段内叠加**——
    // NaN 段是"用户没给值、引擎自己定"，宿主没有基线可叠，如实不产生值（与 pitch 的自由区不同：pitch 的
    // 自由区有引擎必然产生的载体、且宿主知道它大致落在音符音高上，故 pitch 走 PitchDeviation 单独传偏差；
    // 参数轨的 NaN 区宿主连量级都不知道，不设平行偏差通道。要让颤音作用于模型输出区，先用固定笔刷把它
    // 变成已画区——见 SynthesisLock）。与快照侧同口径（那边由 FrozenFinalAutomationEvaluator 的 skipNaN 承担）。
    public double[] GetFinalAutomationValues(IReadOnlyList<double> ticks, string automationID)
    {
        bool isPiecewise = SoundSource.AutomationConfigs.TryGetValue(automationID, out var declared) && declared.IsPiecewise;
        var values = GetAutomationValues(ticks, automationID);

        var vibratos = GetVibratoDeviation(ticks, automationID);
        for (int i = 0; i < ticks.Count; i++)
        {
            if (isPiecewise && double.IsNaN(values[i]))
                continue;

            values[i] += vibratos[i];
        }

        return values;
    }

    static double[] NaNs(int count)
    {
        var values = new double[count];
        values.Fill(double.NaN);
        return values;
    }

    // 音符基线 pitch：每个 tick 取覆盖它的音符半音值，无音符为 NaN。ticks part 相对、升序。
    // 重叠时"后 note 头盖前 note 尾"：按起点升序入栈，栈顶=最近开始且仍覆盖的 note；
    // 栈顶结束则弹出回退到下层 note（其仍覆盖则恢复其音高）。
    public double[] GetBasePitch(IReadOnlyList<double> ticks)
    {
        double[] pitch = new double[ticks.Count];
        pitch.Fill(double.NaN);
        if (ticks.Count == 0)
            return pitch;

        var stack = new List<INote>();
        using var it = mNotes.GetEnumerator();
        INote? next = it.MoveNext() ? it.Current : null;
        for (int i = 0; i < ticks.Count; i++)
        {
            double tick = ticks[i];
            while (next != null && next.StartPos() <= tick)
            {
                stack.Add(next);
                next = it.MoveNext() ? it.Current : null;
            }
            while (stack.Count > 0 && stack[^1].EndPos() <= tick)
                stack.RemoveAt(stack.Count - 1);

            if (stack.Count > 0)
                pitch[i] = stack[^1].Pitch.Value;
        }
        return pitch;
    }

    // 有效基线（单点）：用户绘制曲线优先，未绘制处落到覆盖音符的半音；都没有则 NaN。
    // 颤音控制柄定位用它，使没画 pitch 时也能锚在音符上、即时可编辑。重叠同样"后 note 盖前 note"（取最末覆盖者）。
    public double GetEffectivePitchValue(double tick)
    {
        double drawn = mPitchLine.GetValue(tick);
        if (!double.IsNaN(drawn))
            return drawn;

        double result = double.NaN;
        foreach (var note in mNotes)
        {
            if (note.StartPos() > tick)
                break;

            if (note.EndPos() > tick)
                result = note.Pitch.Value;
        }
        return result;
    }

    // 颤音覆盖区内、未绘制 pitch 处的兜底虚线波（= 音符基线 + 颤音偏差）：只在"画了颤音的位置"显示预期音高，
    // 颤音区外不画。已绘制 pitch 的区段交给实线 GetFinalPitch，避免二次叠加。
    public double[] GetVibratoFallbackPitch(IReadOnlyList<double> ticks)
    {
        double[] result = new double[ticks.Count];
        result.Fill(double.NaN);
        if (mVibratos.Count == 0)
            return result;

        double[] drawn = mPitchLine.GetValues(ticks);
        double[] basePitch = GetBasePitch(ticks);
        double[] deviation = GetVibratoDeviation(ticks);
        using var it = mVibratos.GetEnumerator();
        Vibrato? vibrato = it.MoveNext() ? it.Current : null;
        for (int i = 0; i < ticks.Count; i++)
        {
            double tick = ticks[i];
            while (vibrato != null && vibrato.EndPos() < tick)
                vibrato = it.MoveNext() ? it.Current : null;
            if (vibrato == null)
                break;

            if (vibrato.StartPos() > tick || !double.IsNaN(drawn[i]) || double.IsNaN(basePitch[i]))
                continue;

            result[i] = basePitch[i] + deviation[i];
        }
        return result;
    }

    // 悬浮添加预览波：在有效基线（绘制优先、否则音符）上叠加单个待建颤音的偏移，无基线处 NaN。
    public double[] GetVibratoAddPreviewPitch(IReadOnlyList<double> ticks, VibratoInfo info)
    {
        double[] drawn = mPitchLine.GetValues(ticks);
        double[] basePitch = GetBasePitch(ticks);
        var data = new[] { new VibratoMath.VibratoData(
            info.Pos, info.Dur, info.Frequency, info.Amplitude, info.Phase, info.Attack, info.Release) };
        Func<double[], double[]>? envelopeSampler = Automations.TryGetValue(ConstantDefine.VibratoEnvelopeID, out var envelope)
            ? envelope.GetValues
            : null;
        double[] deviation = VibratoMath.GetDeviation(data, ticks, envelopeSampler, Pos.Value, TempoManager.GetTime);
        double[] result = new double[ticks.Count];
        for (int i = 0; i < ticks.Count; i++)
        {
            double baseline = double.IsNaN(drawn[i]) ? basePitch[i] : drawn[i];
            result[i] = double.IsNaN(baseline) ? double.NaN : baseline + deviation[i];
        }
        return result;
    }

    // 批量变更括号（undo/redo 重放时同样成对触发）：让插件把重活（如重分片）延迟到 BatchEnd。
    // 调用点信息随括号带进 BatchSignal：括号跨鼠标手势，漏配平会让该 part 永久停摆且不抛异常，
    // 留下开启者才能在日志里直接点名（见 BatchSignal 的诊断面）。
    public void BeginMergeDirty([CallerFilePath] string? file = null, [CallerLineNumber] int line = 0, [CallerMemberName] string? member = null)
    {
        PushAndDo(new DataCommand(() => mSynthesisBatch.Begin(file, line, member), mSynthesisBatch.End));
    }

    // 撤销这一步 = 重新开括号，故逆动作同样带调用点（撤销重放时括号也要能追溯到出处）。
    public void EndMergeDirty([CallerFilePath] string? file = null, [CallerLineNumber] int line = 0, [CallerMemberName] string? member = null)
    {
        PushAndDo(new DataCommand(mSynthesisBatch.End, () => mSynthesisBatch.Begin(file, line, member)));
    }

    public override void Activate()
    {
        RebuildSynthesisPipeline();
        // 时基变更（tempo 表）→ 整体重建会话：兑现 VoiceSynthesisContext 的既定设计（note 边界代理
        // 刻意不订阅 TempoManager，其变化由 session 重建覆盖——此前该接线缺失，tempo 一改插件收不到
        // 任何通知）。重建经 OnTimebaseModified 折叠到调度轮（settled 通道挡不住头重放式编辑手势），
        // 并顺带失效延音判定缓存（秒轴是判定的契约合法输入域）。tempo 属工程级对象、寿命长于 part，
        // 订阅挂激活生命周期。
        TempoManager.Modified.Subscribe(mOnTimebaseModified);
        // 声明 context 里的 part 数据提交 → 重新询问声明（见 OnDocumentStatusChanged）。撤销根的订阅在第一次改动时才挂
        //（激活时工程可能还没挂到撤销根下：先建工程、再交给文档），随激活生命周期解除。
        mHostInvalidator = new DeclarationInvalidator(this);
        // 编辑器 G2P 开关同理：它是喂引擎那个 lyric 的一部分（空发音时的兜底，见 INote.FinalPronunciation），
        // 改它等于全部 note 的歌词同时变了——与换声源同量级，故整体重建而非逐 note 标脏。设置对象是
        // 进程级、寿命远长于 part，订阅同挂激活生命周期。
        Settings.AutoGeneratePronunciation.Modified.Subscribe(mOnPronunciationPolicyModified);
    }

    public override void Deactivate()
    {
        Settings.AutoGeneratePronunciation.Modified.Unsubscribe(mOnPronunciationPolicyModified);
        TempoManager.Modified.Unsubscribe(mOnTimebaseModified);
        if (mDocument != null)
            mDocument.StatusChanged -= OnDocumentStatusChanged;
        mDocument = null;
        mHostInvalidator?.Dispose();
        mHostInvalidator = null;
        DisposeSynthesisPipeline();
    }

    // 时基变更统一入口（tempo 表变更 / part 平移）：整体重建会话（含 context）——平移跨 bpm 段会改
    // 各 note 秒时长、非纯偏移，第一版无脑重建最简单正确。未激活（无管线）时只失效判定缓存（回退
    // 路径同样消费秒轴）。
    //
    // 重建**折叠到调度轮末尾**执行：settled 通道挡不住头重放式拖拽——part 拖动每个量化步进是
    // DiscardTo(还原 Pos) → Pos.Set → 可能的摘除重插 的三连触发、且皆为 settled（该路径不走 merge
    // 作用域），逐触发重建即 3× 会话抖动（插件方 review 发现）。判定缓存同步标脏（拖拽期布局立即
    // 消费、必须实时），重建则挂起到 SynchronizationContext 回调，一轮内多次触发合并为一次；期间
    // 任何一次重建（如摘除重插的 Activate）都会清除挂起（见 RebuildSynthesisPipeline 入口），不双跑。
    void OnTimebaseModified()
    {
        mContinuationDirty = true;
        mNoteDeclarations.Invalidate();   // note 视图里的秒值随 tempo 变（tempo 表在 part 的数据树外，不经 Modified）
        if (mPipeline == null || mTimebaseRebuildPending)
            return;

        var syncContext = SynchronizationContext.Current;
        if (syncContext == null)
        {
            RebuildSynthesisPipeline();   // 无调度上下文（测试等环境）：退化为就地重建
            return;
        }

        mTimebaseRebuildPending = true;
        syncContext.Post(_ =>
        {
            if (!mTimebaseRebuildPending)
                return;                   // 期间已被其它路径重建（拖动跨排序的摘除重插）或已停用
            mTimebaseRebuildPending = false;
            if (mPipeline != null)
                RebuildSynthesisPipeline();
        }, null);
    }

    void OnVoiceModified()
    {
        if (mPipeline != null)
            RebuildSynthesisPipeline();
    }

    // 编辑器 G2P 开关变更 → 整体重建会话（喂引擎的 lyric 随之变，见 Activate 处的订阅说明）。
    // 走与换声源同一条重建路径：钉死音素是用户数据、不受影响，被清的只有合成产物（SynthesizedSyllable）。
    void OnPronunciationPolicyModified()
    {
        if (mPipeline != null)
            RebuildSynthesisPipeline();
    }

    void RebuildSynthesisPipeline()
    {
        // 生命周期留痕：重建会把会话连同其分块一起换新，新会话在被 peek 或收到 Committed 之前没有任何块
        // （状态带因此为空）。「条整个消失」的现场要能对上是哪次重建造成的，故记一行。
        Log.Debug($"Rebuilding synthesis pipeline for part at {Pos.Value} (source {mSource.Type}/{mSource.ID}).");
        mTimebaseRebuildPending = false;   // 任何一次重建都满足挂起的时基重建诉求（见 OnTimebaseModified）
        DisposeSynthesisPipeline();
        // 【顺序不变量，勿调换】先填声明、后建会话：mSource.AutomationConfigs 是物化缓存（详见 Voice.AutomationConfigs），
        // 必须在建会话之前填好——否则会话构造期经 context.Automations 订阅自己声明的轨会读到空缓存、订阅落空、绘制参数不重渲。
        mSource.RefreshDeclarations(BuildPartPropertyContext());
        mNoteDeclarations.Invalidate();   // 换了会话（可能换了引擎）：旧回答一概作废
        RebuildLaneConfigs();
        if (mSource.Kind == SourceKind.Voice)
        {
            var voicePipeline = new VoiceSynthesisPipeline(this, mSource.Type, mSource.ID);
            voicePipeline.StatusChanged += OnPipelineStatusChanged;
            mSource.SetSession(voicePipeline.Session);   // 注入会话供 DefaultLyric 等运行时取值（建会话之后）
            mPipeline = voicePipeline;
            mVoicePipeline = voicePipeline;              // 延音判定的会话查询入口（换会话即换判定语义）
            mContinuationDirty = true;
        }
        else
        {
            // instrument 无会话级 DefaultLyric 注入（恒 "a"）；产物仅音频 + 参数回显，无音素回填。
            var instrumentPipeline = new InstrumentSynthesisPipeline(this, mSource.Type, mSource.ID);
            instrumentPipeline.StatusChanged += OnPipelineStatusChanged;
            mPipeline = instrumentPipeline;
        }
        mPipeline.PhonemesChanged += OnPipelinePhonemesChanged;
        mPipeline.ParametersChanged += OnPipelineParametersChanged;
        mPipeline.PitchChanged += OnPipelinePitchChanged;
        // 重置签名基线（激活 / 换源时 UI 经各自既有触发整体重建，此处不发 AutomationConfigsModified，
        // 只对齐基线供后续参数 commit 的增量比对）。
        mAutomationConfigsSignature = ComputeAutomationConfigsSignature();
        mSynthesisStatusChanged.Invoke();
    }

    void DisposeSynthesisPipeline()
    {
        if (mPipeline == null)
            return;

        mPipeline.StatusChanged -= OnPipelineStatusChanged;
        mPipeline.PhonemesChanged -= OnPipelinePhonemesChanged;
        mPipeline.ParametersChanged -= OnPipelineParametersChanged;
        mPipeline.PitchChanged -= OnPipelinePitchChanged;
        mPipeline.Dispose();
        mPipeline = null;
        mVoicePipeline = null;
        mContinuationDirty = true;
        mSource.SetSession(null);
        mSource.RefreshDeclarations(BuildPartPropertyContext());
        mNoteDeclarations.Invalidate();
        RebuildLaneConfigs();
        foreach (var note in mNotes)
        {
            note.SynthesizedSyllable = null;
        }
        mNoteDeclarations.InvalidatePhonemes();   // 音素声明问的是刚清掉的合成音素
    }

    void OnPipelineStatusChanged()
    {
        mSynthesisStatusChanged.Invoke();
    }

    void OnPipelinePhonemesChanged()
    {
        // 音素声明依赖显示音素，而合成产物不是数据、不经 part 的 Modified：回填一到就作废缓存里的音素那一半。
        mNoteDeclarations.InvalidatePhonemes();
        // phoneme lane 的声明面依赖合成产物：激活时合成音素尚未产出的话，钉选的条目是未解析占位（tab 在场、面板空），
        // 音素级时间轴画布则还不在（没有声明可问，不猜）。回填一到就重算：占位升级为已解析、时间轴画布按声明出现；
        // 集合没变时签名比对挡住、不惊动 UI（声明按 note 缓存，只重问音素那一半）。
        RebuildLaneConfigs();
        if (RefreshAutomationConfigsSignatureChanged())
            mAutomationConfigsModified.Invoke();
        mSynthesizedPhonemesChanged.Invoke();
    }

    void OnPipelineParametersChanged()
    {
        mSynthesizedParametersChanged.Invoke();
    }

    void OnPipelinePitchChanged()
    {
        mSynthesizedPitchChanged.Invoke();
    }

    public INote CreateNote(NoteInfo info)
    {
        return new Note(this, info);
    }

    public Vibrato CreateVibrato(VibratoInfo info)
    {
        var vibrato = new Vibrato(this);
        vibrato.SetInfo(info);
        return vibrato;
    }

    public IAutomation? AddAutomation(string automationID)
    {
        if (mAutomations.TryGetValue(automationID, out var value))
            return value;

        if (!IsEffectiveAutomation(automationID))
            return null;

        var config = GetEffectiveAutomationConfig(automationID);
        var automation = CreateAutomation(automationID, new() { DefaultValue = config.DefaultValue });
        mAutomations.Add(automationID, automation);
        return automation;
    }

    public bool IsEffectiveAutomation(string id)
    {
        return SoundSource.AutomationConfigs.TryGetValue(id, out var config) && !config.IsPiecewise;
    }

    public AutomationConfig GetEffectiveAutomationConfig(string id)
    {
        if (SoundSource.AutomationConfigs.TryGetValue(id, out var config) && !config.IsPiecewise)
            return config;

        throw new ArgumentException(string.Format("Automation {0} is not effective!", id));
    }

    // 分段轨按需创建（无默认基线，建空轨）；轨须在当前声明里才创建。孤儿轨（声明已消失但数据仍在）不重建、保留隐藏。
    public IPiecewiseAutomation? AddPiecewiseAutomation(string id)
    {
        if (mPiecewiseAutomations.TryGetValue(id, out var value))
            return value;

        if (!IsEffectivePiecewiseAutomation(id))
            return null;

        var automation = new PiecewiseAutomation();
        mPiecewiseAutomations.Add(id, automation);
        return automation;
    }

    public bool IsEffectivePiecewiseAutomation(string id)
    {
        return SoundSource.AutomationConfigs.TryGetValue(id, out var config) && config.IsPiecewise;
    }

    public AutomationConfig GetEffectivePiecewiseAutomationConfig(string id)
    {
        if (SoundSource.AutomationConfigs.TryGetValue(id, out var config) && config.IsPiecewise)
            return config;

        throw new ArgumentException(string.Format("Piecewise automation {0} is not effective!", id));
    }

    public override MidiPartInfo GetInfo()
    {
        return new()
        {
            Name = Name,
            Pos = Pos,
            StartOffset = StartOffset,
            EndOffset = EndOffset,
            Gain = Gain,
            Notes = mNotes.GetInfo().ToInfo(),
            Effects = mEffects.GetInfo().ToInfo(),
            Automations = mAutomations.GetInfo().ToInfo(),
            PiecewiseAutomations = mPiecewiseAutomations.PiecewiseAutomationsToInfo(),
            Pitch = new PitchInfo { Segments = mPitchLine.GetInfo() },
            Vibratos = mVibratos.GetInfo().ToInfo(),
            SoundSource = mSource.GetInfo(),
            Properties = Properties.GetInfo(),
        };
    }

    public void SetInfo(MidiPartInfo info)
    {
        using var _ = MergeNotify();
        Name.SetInfo(info.Name);
        Pos.SetInfo(info.Pos);
        StartOffset.SetInfo(info.StartOffset);
        EndOffset.SetInfo(info.EndOffset);
        Gain.SetInfo(info.Gain);
        mNotes.SetInfo(info.Notes.Convert(CreateNote).ToArray());
        mEffects.SetInfo(info.Effects.Convert(CreateEffect).ToArray());
        mVibratos.SetInfo(info.Vibratos.Convert(CreateVibrato).ToArray());
        mAutomations.SetInfo(info.Automations.Convert(CreateAutomation).ToMap());
        mPiecewiseAutomations.SetInfo(info.PiecewiseAutomations.ToPiecewiseAutomations());
        mPitchLine.SetInfo(info.Pitch.Segments);
        mSource.SetInfo(info.SoundSource);
        Properties.SetInfo(info.Properties);
    }

    // 合成失效不再由 part 驱动：automation 的 RangeModified/DefaultValue 变更经
    // VoiceSynthesisContext 转发给插件，由插件按自己的失效依赖图标脏。
    Automation CreateAutomation(string automationID, AutomationInfo info)
    {
        return new Automation(this, info);
    }

    public override IAudioData GetAudioData(int offset, int count)
    {
        float[] data = new float[count];
        int sampleRate = ((IAudioSource)this).SampleRate;
        double startTime = ((IAudioSource)this).StartTime;
        int partOffset = (int)(sampleRate * startTime);
        // 各段末级音频（已适配工程率）按绝对时间对齐混入 part 缓冲（段不重叠；若重叠则 += 叠加）。
        foreach (var segment in SynthesizedSegments)
        {
            var audio = segment.Audio;
            if (audio.Samples is not { } samples)
                continue;

            int audioOffset = (int)(audio.StartTime * audio.SampleRate);
            int startIndex = partOffset + offset - audioOffset;
            int from = Math.Max(startIndex, 0);
            int to = Math.Min(startIndex + count, samples.Length);
            for (int i = from; i < to; i++)
                data[i - startIndex] += samples[i];
        }
        double pos = Pos;
        var ticks = new double[count];
        for (int i = 0; i < count; i++)
        {
            ticks[i] = TempoManager.GetTick((double)(offset + i) / sampleRate + startTime) - pos;
        }
        var volumes = GetFinalAutomationValues(ticks, ConstantDefine.VolumeID);
        for (int i = 0; i < volumes.Length; i++)
        {
            data[i] = (float)(data[i] * Volume2Level(volumes[i]));
        }
        if (Gain != 0)
        {
            float volume = (float)MusicTheory.dB2Level(Gain);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= volume;
            }
        }
        return new MonoAudioData(data);
    }

    public override void OnSampleRateChanged()
    {
        mPipeline?.OnSampleRateChanged();
    }

    static MidiPart()
    {
        var a = Math.Log(Math.Pow(10, 1 / 20.0));
        x0 = 1 / a - 12;
        k = Math.Pow(10, x0 / 20) * a;
    }
    const double VOLUME_MAX = 12;
    public static double Volume2Level(double volume)
    {
        if (volume > x0)
            return MusicTheory.dB2Level(volume);

        if (volume > -VOLUME_MAX)
            return (volume + VOLUME_MAX) * k;

        return 0;
    }
    static readonly double x0;
    static readonly double k;

    protected override int SampleRate => AudioEngine.SampleRate.Value;

    readonly ActionEvent mSynthesisStatusChanged = new();
    readonly ActionEvent mSynthesizedPhonemesChanged = new();
    readonly ActionEvent mSynthesizedParametersChanged = new();
    readonly ActionEvent mSynthesizedPitchChanged = new();
    readonly ActionEvent mAutomationConfigsModified = new();
    readonly ActionEvent<DeclarationInvalidation> mDeclarationsInvalidated = new();
    string mAutomationConfigsSignature = string.Empty;
    readonly OrderedMap<PropertyKey, LaneEntry> mNoteLaneConfigs = new();
    readonly OrderedMap<PropertyKey, LaneEntry> mPhonemeLaneConfigs = new();
    readonly NoteDeclarationCache mNoteDeclarations;
    // 出现过的时间轴画布（按 scope，含分到的颜色）与它们所属的音源：音源一换就清（见 KeepSeenTimelineLanes）。
    readonly OrderedMap<PropertyKey, LaneEntry> mSeenNoteTimelineLanes = new();
    readonly OrderedMap<PropertyKey, LaneEntry> mSeenPhonemeTimelineLanes = new();
    (SourceKind Kind, string Type, string ID) mSeenTimelineSource;
    readonly BatchSignal mSynthesisBatch = new();
    ISynthesisPipeline? mPipeline;
    VoiceSynthesisPipeline? mVoicePipeline;
    bool mContinuationDirty = true;
    bool mDeclarationInputsDirty;              // 自上次提交以来本 part 的数据改过（声明输入变了）
    DeclarationInvalidator? mHostInvalidator;   // 宿主侧的声明失效合拍（数据提交触发），随激活生命周期
    DataDocument? mDocument;                    // 所在撤销根，订阅其提交（StatusChanged）
    readonly Dictionary<INote, bool> mContinuation = new();
    // 时基变更钩的稳定句柄（tempo 订阅/退订须同一实例，见 Activate/Deactivate；Pos 订阅共用）。
    readonly Action mOnTimebaseModified;
    readonly Action mOnPronunciationPolicyModified;
    // 时基重建挂起位（调度轮内合并多次触发为一次重建，见 OnTimebaseModified）。
    bool mTimebaseRebuildPending;

    readonly NoteList mNotes;
    readonly VibratoList mVibratos;
    readonly DataObjectMap<string, IAutomation> mAutomations;
    readonly DataObjectMap<string, IPiecewiseAutomation> mPiecewiseAutomations;
    readonly DataObjectList<IEffect> mEffects;
    readonly Dictionary<IEffect, Action> mEffectModifiedHandlers = new();
    readonly PiecewiseAutomation mPitchLine;
    readonly SoundSource mSource;

    static readonly Map<string, SynthesizedParameter> EmptySynthesizedParameters = new();

    class NoteList : SortedDataObjectLinkedList<INote>, INoteList
    {
        public IMergableEvent SelectionChanged => mSelectionChanged;

        public NoteList() : base(IsInOrder)
        {
            MembershipModified.Subscribe(mSelectionChanged);
            this.WhenAny(note => note.SelectionChanged).Subscribe(mSelectionChanged);
        }

        static bool IsInOrder(INote prev, INote next)
        {
            if (prev.StartPos() < next.StartPos())
                return true;

            if (prev.StartPos() > next.StartPos())
                return false;

            if (prev.EndPos() > next.EndPos())
                return true;

            if (prev.EndPos() < next.EndPos())
                return false;

            return true;
        }

        readonly MergableEvent mSelectionChanged = new();
    }

    // 颤音有序链表：与 NoteList 同构的排序键（StartPos↑、同起点时 EndPos↓，即 Pos↑/Dur↓），
    // 与改链表前 InsertVibrato 的手工定位顺序一致。
    class VibratoList : SortedDataObjectLinkedList<Vibrato>
    {
        public VibratoList() : base(IsInOrder) { }

        static bool IsInOrder(Vibrato prev, Vibrato next)
        {
            if (prev.StartPos() < next.StartPos())
                return true;

            if (prev.StartPos() > next.StartPos())
                return false;

            if (prev.EndPos() > next.EndPos())
                return true;

            if (prev.EndPos() < next.EndPos())
                return false;

            return true;
        }
    }
}
