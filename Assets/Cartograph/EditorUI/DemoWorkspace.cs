using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cartograph.EditorUI
{
    // UI demonstration only: deliberately never reports durable save success.
    public sealed class DemoWorkspace : IEditorWorkspace
    {
        private readonly List<DocumentRecord> documents = new List<DocumentRecord>();
        private DocumentRecord current;
        public event Action Changed;
        public IReadOnlyList<DocumentRecord> Documents => documents.Select(d => d.Copy()).ToArray();
        public DocumentRecord Current => current?.Copy();
        public SaveState State => SaveState.Dirty;
        public string Error => "";
        public bool IsPersistent => false;

        public DemoWorkspace()
        {
            Add("はじめてのノート", "# 今日の学び\n\nここから、自分の言葉で書いてみよう。\n\n## 気づいたこと\n\n- わかったこと\n- これから調べること\n");
        }

        private void Add(string title, string markdown)
        {
            var now = DateTime.UtcNow.ToString("O");
            current = new DocumentRecord { id = Guid.NewGuid().ToString("N"), title = title,
                markdown = markdown, createdAt = now, updatedAt = now };
            documents.Add(current);
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
