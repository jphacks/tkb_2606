using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Cartowords.MapPresentation
{
    public enum RoadKind { Unspecified, Premise, Contrast, Example }
    public enum MarkKind { MissingReason, BrokenLink, Isolated }
    public enum Emotion { Joy, Anger, Sadness, Delight }

    // Rendering DTOs, not a Markdown parser or a replacement for Analysis types.
    public sealed class MapNode
    {
        public string Id { get; }
        public string File { get; }
        public string Title { get; }
        public string Town { get; }
        public int Line { get; }
        public int TypedChars { get; }
        public bool Isolated { get; }
        /// <summary>見出しの深さ(1〜3)。見出しのないファイルの拠点は 0。</summary>
        public int Level { get; }
        public MapNode(string id, string file, string title, int line, int typedChars, bool isolated = false, int level = 0)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(file) || line < 1 || typedChars < 0 || level < 0 || level > 3)
                throw new ArgumentException("Invalid rendering node.");
            Id = id; File = file; Title = title ?? ""; Line = line; TypedChars = typedChars; Isolated = isolated; Level = level;
            int slash = file.LastIndexOf('/'); Town = slash < 0 ? "" : file.Substring(0, slash);
        }
    }
    public sealed class MapRoad
    {
        public string From { get; }
        public string To { get; }
        public RoadKind Kind { get; }
        public bool Auto { get; }
        public MapRoad(string from, string to, RoadKind kind = RoadKind.Unspecified, bool auto = false)
        { From = from; To = to; Kind = kind; Auto = auto; }
    }
    public sealed class MapFog
    {
        public string Node { get; }
        public int Line { get; }
        public MapFog(string node, int line) { Node = node; Line = line; }
    }
    public sealed class MapMark
    {
        public string Node { get; }
        public MarkKind Kind { get; }
        public string Message { get; }
        public MapMark(string node, MarkKind kind, string message)
        { Node = node; Kind = kind; Message = message ?? ""; }
    }
    public sealed class MapFlower
    {
        public string Node { get; }
        public int Line { get; }
        public Emotion Emotion { get; }
        public bool Withered { get; }
        public MapFlower(string node, int line, Emotion emotion, bool withered = false)
        { Node = node; Line = line; Emotion = emotion; Withered = withered; }
    }
    public sealed class MapRequest
    {
        public string ProjectKey { get; }
        public long Generation { get; }
        public IReadOnlyDictionary<string, long> NoteRevisions { get; }
        public MapRequest(string projectKey, long generation, IDictionary<string, long> revisions)
        {
            ProjectKey = projectKey ?? throw new ArgumentNullException(nameof(projectKey)); Generation = generation;
            NoteRevisions = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(revisions));
        }
    }
    public sealed class MapSnapshot
    {
        public int SchemaVersion { get; }
        public MapRequest Request { get; }
        public IReadOnlyList<MapNode> Nodes { get; }
        public IReadOnlyList<MapRoad> Roads { get; }
        public IReadOnlyList<MapFog> Fogs { get; }
        public IReadOnlyList<MapMark> Marks { get; }
        public IReadOnlyList<MapFlower> Flowers { get; }
        public IReadOnlyList<MapBuilding> Buildings { get; }
        public MapSnapshot(MapRequest request, IEnumerable<MapNode> nodes, IEnumerable<MapRoad> roads = null,
            IEnumerable<MapFog> fogs = null, IEnumerable<MapMark> marks = null, IEnumerable<MapFlower> flowers = null,
            int schemaVersion = 1, IEnumerable<MapBuilding> buildings = null)
        {
            Buildings = Array.AsReadOnly((buildings ?? Array.Empty<MapBuilding>()).ToArray());
            Request = request ?? throw new ArgumentNullException(nameof(request)); SchemaVersion = schemaVersion;
            Nodes = Array.AsReadOnly((nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray());
            Roads = Array.AsReadOnly((roads ?? Array.Empty<MapRoad>()).ToArray());
            Fogs = Array.AsReadOnly((fogs ?? Array.Empty<MapFog>()).ToArray());
            Marks = Array.AsReadOnly((marks ?? Array.Empty<MapMark>()).ToArray());
            Flowers = Array.AsReadOnly((flowers ?? Array.Empty<MapFlower>()).ToArray());
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in Nodes) if (node == null || !ids.Add(node.Id)) throw new ArgumentException("Duplicate/null node.");
            if (Roads.Any(r => r == null || !ids.Contains(r.From) || !ids.Contains(r.To) || !Enum.IsDefined(typeof(RoadKind), r.Kind)) ||
                Fogs.Any(f => f == null || !ids.Contains(f.Node) || f.Line < 1) ||
                Marks.Any(m => m == null || !ids.Contains(m.Node) || !Enum.IsDefined(typeof(MarkKind), m.Kind)) ||
                Flowers.Any(f => f == null || !ids.Contains(f.Node) || f.Line < 1 || !Enum.IsDefined(typeof(Emotion), f.Emotion)) ||
                Buildings.Any(b => b == null || !ids.Contains(b.Node)))
                throw new ArgumentException("Invalid map item or unknown node reference.");
        }
    }
    public sealed class MapSnapshotGate
    {
        private long generation = -1;
        private string project;
        public void Reset(string projectKey) { project = projectKey; generation = -1; }
        public bool TryAccept(MapSnapshot snapshot, IReadOnlyDictionary<string, long> currentRevisions)
        {
            if (snapshot == null || snapshot.SchemaVersion != 1 || snapshot.Request.ProjectKey != project ||
                snapshot.Request.Generation <= generation || snapshot.Request.NoteRevisions.Count != currentRevisions.Count) return false;
            foreach (var pair in currentRevisions)
                if (!snapshot.Request.NoteRevisions.TryGetValue(pair.Key, out var version) || version != pair.Value) return false;
            generation = snapshot.Request.Generation; return true;
        }
    }
}
