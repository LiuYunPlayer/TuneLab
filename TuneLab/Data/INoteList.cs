using TuneLab.Foundation.Document;

namespace TuneLab.Data;

internal interface INoteList : IReadOnlyDataObjectLinkedList<INote>, ISelectableCollection<INote>
{

}
