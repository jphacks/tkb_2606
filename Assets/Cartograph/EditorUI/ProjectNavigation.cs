using System.Collections.Generic;
using Cartograph.EditorUI.Navigation;

namespace Cartograph.EditorUI
{
    // Optional UI adapter. Does not change IEditorWorkspace or prescribe a core API.
    public interface IProjectNavigationSource
    {
        string ProjectName { get; }
        IReadOnlyList<NoteTreeEntry> Notes { get; }
    }
}
