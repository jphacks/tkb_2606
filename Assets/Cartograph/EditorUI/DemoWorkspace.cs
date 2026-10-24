using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cartograph.EditorUI.Navigation;

namespace Cartograph.EditorUI
{
    // UI demonstration only: deliberately never reports durable save success.
    public sealed class DemoWorkspace : IEditorWorkspace, IProjectNavigationSource
    {
        private readonly List<DocumentRecord> documents = new List<DocumentRecord>();
        private DocumentRecord current;
        private readonly Dictionary<string, string> paths = new Dictionary<string, string>();
        public string ProjectName => "デモプロジェクト";
        public IReadOnlyList<NoteTreeEntry> Notes => documents.Select(d =>
            new NoteTreeEntry(d.id, paths[d.id], d.title)).ToArray();
        public event Action Changed;
        public IReadOnlyList<DocumentRecord> Documents => documents.Select(d => d.Copy()).ToArray();
        public DocumentRecord Current => current?.Copy();
        public SaveState State => SaveState.Dirty;
        public string Error => "";
        public bool IsPersistent => false;

        public DemoWorkspace()
        {
            Add("はじめてのノート", "# 今日の学び\n\nここから、自分の言葉で書いてみよう。\n\n## 気づいたこと\n\n- わかったこと\n- これから調べること\n");
            var first = current;
            paths[first.id] = "大学用/授業/科目A/はじめてのノート.md";
            Add("次の授業", "# 次の授業\n\n");
            paths[current.id] = "大学用/授業/科目B/次の授業.md";
            Add("アイデア", "# アイデア\n\n");
            paths[current.id] = "個人用/アイデア.md";
            current = first;
        }

        private void Add(string title, string markdown)
        {
            var now = DateTime.UtcNow.ToString("O");
            current = new DocumentRecord { id = Guid.NewGuid().ToString("N"), title = title,
                markdown = markdown, createdAt = now, updatedAt = now };
            documents.Add(current);
            paths[current.id] = "無題/" + current.id + ".md";
        }

        public Task CreateDocument()
        {
            Add("無題のノート", "");
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task OpenDocument(string id)
        {
            current = documents.First(d => d.id == id);
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public void UpdateTitle(string title)
        {
            current.title = title;
            Touch();
        }

        public void ApplyEditorChange(string documentId, string markdown, ChangeOrigin origin)
        {
            if (current.id != documentId) throw new InvalidOperationException("文書が切り替わっています。");
            current.markdown = markdown;
            Touch();
        }

        private void Touch()
        {
            current.revision++;
            current.updatedAt = DateTime.UtcNow.ToString("O");
            Changed?.Invoke();
        }

        public Task FlushSave() => Task.CompletedTask;
        public string ExportMarkdown() => current?.markdown ?? "";
    }
}
