using System;
using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.Data;

// 参数同步模式（Settings.ParameterSyncMode）下移动音符时的参数搬运：把每个被移动音符覆盖区间里的
// 音高线与自动化曲线抠出来，跟着音符平移到落点。
//
// 三步分开、由调用方夹在音符位置改动的两侧，是这件事的固有时序：
//  ① Capture 必须在音符还在原处时做——曲线的【来处】只有那一刻问得出来；
//  ② ClearSources 清来处，与音符位置改动同在一个 MoveNotes 的 mutate 里（同属一条可撤销记录）；
//  ③ Apply 写落点，在【全部】来处都清完之后——多选拖动时一个音符的落点可能正是另一个的来处。
// 曾按音符移动【后】的位置清（即用落点当来处），结果只把刚要写进去的地方清了一遍，来处的曲线原地
// 留着不动，观感就是"参数同步模式没生效"。
//
// 【交界点归右邻】相邻音符共享 note.EndPos() == next.StartPos() 这一个 tick 上的锚点。谁搬它都会把它
// 搬走，于是先后各移动一次两个相邻音符时，交界点被平移两次——原本连续的音高断出一根 1 tick 宽的尖刺。
// 故被移动音符的曲线域止于交界之前（让出 gap 宽），而写出的线仍延伸到交界点、那一点取【现值】：
// 缝里的旧锚点因此被覆盖、新曲线与右邻连成一条（不延伸就会断成两组、缝成了 NaN 自由段），而交界点
// 的值没被改写，右邻下一次搬运读到的还是自己的值。
internal sealed class ParameterSyncMove
{
    // extension = Settings.ParameterBoundaryExtension（tick）：新旧曲线在域边界的吸并宽度，同时也是
    // 让给右邻的宽度上限。
    public static ParameterSyncMove Capture(IMidiPart part, IReadOnlyCollection<INote> notes,
        double posOffset, double pitchOffset, double extension)
    {
        var move = new ParameterSyncMove(part);
        var moving = new HashSet<INote>(notes);
        foreach (var note in notes)
        {
            double start = note.StartPos();
            double noteEnd = note.EndPos();

            // 让出与收尾只在【来处与落点重合】时才有意义，也就是纯移调（posOffset == 0）：那时 Clear 与
            // AddLine 落在同一段上，不让出会把交界那个共有的锚点连搬两次（先后移动两个相邻音符 → 尖刺），
            // 而收尾点取的是交界点的原值，正好把让出的缝接回右邻。
            // 水平移动恰恰相反：落点在别处，落点那一侧的 tick 上摆着的是**别人的**曲线（或什么都没有），
            // 把它的现值当收尾点就是在音符尾部竖起一根 gap 宽的翘尾；而来处的交界点由音高线 Clear 在域
            // 右界补的封边点保住原值，本来就不需要让出。故水平移动一律不让出、不收尾。
            // 紧邻的右邻还须不在本批里才算争用：同批一起走的邻居位移相同、写出的值一致，无从冲突。
            var next = note.Next;
            bool contested = posOffset == 0
                && next != null && !moving.Contains(next) && next.StartPos() <= noteEnd;
            // 至多让出域宽的一半，短音符也留得下形状。
            double gap = contested ? Math.Min(extension, (noteEnd - start) / 2) : 0;
            var entry = new Entry(start, noteEnd - gap, noteEnd, contested ? gap : extension);
            // gap > 0 蕴含 posOffset == 0，故收尾点就落在来处的交界 tick 上。
            double tail = noteEnd;

            var pitchInfo = part.Pitch.RangeInfo(entry.Start, entry.End);
            foreach (var line in pitchInfo)
            {
                for (int i = 0; i < line.Count; i++)
                {
                    line[i] = new(line[i].X + entry.Start + posOffset, line[i].Y + pitchOffset);
                }
            }
            if (gap > 0 && pitchInfo.Count > 0)
            {
                // 音高线可以没有（段间空隙是 NaN）：那就不必收尾，曲线止于让出之前。
                double value = part.Pitch.GetValue(tail);
                if (!double.IsNaN(value))
                    pitchInfo[pitchInfo.Count - 1].Add(new Point(tail, value));
            }
            entry.Pitch.AddRange(pitchInfo);

            foreach (var kvp in part.Automations)
            {
                var autoInfo = kvp.Value.RangeInfo(entry.Start, entry.End);
                for (int i = 0; i < autoInfo.Count; i++)
                {
                    // 自动化只随时间平移；音高偏移与它无关。
                    autoInfo[i] = new(autoInfo[i].X + entry.Start + posOffset, autoInfo[i].Y);
                }
                if (gap > 0 && autoInfo.Count > 0)
                {
                    // RangeInfo 是相对值口径（不含 DefaultValue），收尾点也按相对值给。
                    autoInfo.Add(new Point(tail, kvp.Value.GetValue(tail) - kvp.Value.DefaultValue.Value));
                }
                entry.Automations[kvp.Key] = autoInfo;
            }

            move.mEntries.Add(entry);
        }
        return move;
    }

    // 清来处：音符已经移走，那里不该再留着曲线。
    public void ClearSources()
    {
        foreach (var entry in mEntries)
        {
            // 音高线的 Clear 是删锚点、并在右界补一个取原值的封边点，所以清到音符末端也不会改写交界点。
            mPart.Pitch.Clear(entry.Start, entry.NoteEnd);
            foreach (var kvp in mPart.Automations)
            {
                // 自动化的 Clear 是把区间抹平到 DefaultValue——那会盖掉右界上的点，故只清到让出之前。
                kvp.Value.Clear(entry.Start, entry.End, entry.Extension);
            }
        }
    }

    // 写落点。
    public void Apply()
    {
        foreach (var entry in mEntries)
        {
            foreach (var line in entry.Pitch)
            {
                mPart.Pitch.AddLine(line, entry.Extension);
            }

            foreach (var kvp in entry.Automations)
            {
                if (!mPart.Automations.TryGetValue(kvp.Key, out var automation))
                    continue;

                // 自动化的 RangeInfo 是相对值口径（不含 DefaultValue），AddLine 收绝对值。
                var defaultValue = automation.DefaultValue.Value;
                automation.AddLine(kvp.Value.Convert(p => new Point(p.X, p.Y + defaultValue)), entry.Extension);
            }
        }
    }

    ParameterSyncMove(IMidiPart part)
    {
        mPart = part;
    }

    // 一个被移动音符的一份账：曲线域（Start~End，右界已让出交界点）、音符自身的末端（音高线清到这里）、
    // 该域写入时的吸并宽度，以及抠出来并平移好的曲线。
    sealed class Entry(double start, double end, double noteEnd, double extension)
    {
        public double Start => start;
        public double End => end;
        public double NoteEnd => noteEnd;
        public double Extension => extension;
        public List<List<Point>> Pitch { get; } = new();
        public Dictionary<string, List<Point>> Automations { get; } = new();
    }

    readonly IMidiPart mPart;
    readonly List<Entry> mEntries = new();
}
