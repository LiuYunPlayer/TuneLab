using System.Collections.Generic;

namespace TuneLab.Data;

// 一次合拍后的声明失效（插件经会话 context 的 InvalidatePart / InvalidateNotes 宣告，见 DeclarationInvalidator）。
// Part = 整 part 作废，向下级联到全部 note（note 声明以 part 为上下文）；否则 NoteIds = 作废的 note（INote.RuntimeId）。
internal sealed record DeclarationInvalidation(bool Part, IReadOnlySet<string> NoteIds)
{
    public bool Affects(INote note) => Part || NoteIds.Contains(note.RuntimeId);
}
