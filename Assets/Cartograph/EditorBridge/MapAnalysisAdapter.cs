using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cartograph.Analysis;
using Cartograph.Core;
using Cartowords.MapPresentation;

namespace Cartograph.EditorUI
{
    public static class MapAnalysisAdapter
    {
        public static IReadOnlyList<string> ConnectedNotes(MapSnapshot snapshot, string currentId)
        {
            var connected = new HashSet<string>(StringComparer.Ordinal);
            if (snapshot == null || currentId == null) return Array.Empty<string>();
            var files = snapshot.Nodes.ToDictionary(node => node.Id, node => node.File);
            foreach (var road in snapshot.Roads)
            {
                if (road.Auto) continue;
                string from = files[road.From], to = files[road.To];
                if (from == currentId && to != currentId) connected.Add(to);
                if (to == currentId && from != currentId) connected.Add(from);
            }
            return connected.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }
        public static async Task<MapSnapshot> Analyze(ProjectEditorWorkspace workspace, MapRequest request, Func<bool> current)
        {
            var tree = workspace.Tree;
            void Check()
            {
                if (!current() || !ReferenceEquals(tree, workspace.Tree) ||
                    !Matches(request.NoteRevisions, workspace.Documents.ToDictionary(d => d.id, d => d.revision)))
                    throw new OperationCanceledException();
            }
            Check();
            foreach (var id in request.NoteRevisions.Keys) { Check(); await workspace.LoadDocument(id); Check(); }
            // No await between copies and version checks: capture on the Unity UI context.
            var inputs = request.NoteRevisions.Select(pair => new AnalysisInput(NotePath.Parse(pair.Key),
                workspace.GetLabeledDocument(pair.Key), pair.Value)).ToArray();
            Check();
            var result = await Task.Run(() => ProjectAnalyzer.Analyze(inputs, EmotionLexicon.Default));
            Check();
            return Convert(request, result);
        }
        public static bool Matches(IReadOnlyDictionary<string, long> expected, IReadOnlyDictionary<string, long> actual) =>
            expected.Count == actual.Count && expected.All(p => actual.TryGetValue(p.Key, out var revision) && p.Value == revision);
        public static MapSnapshot Convert(MapRequest request, AnalysisResult result)
        {
            if (!Matches(request.NoteRevisions, result.Revisions.ToDictionary(p => p.Key.Value, p => p.Value)))
                throw new InvalidOperationException("解析結果の版が入力と一致しません。");
            return new MapSnapshot(request,
                result.Nodes.Select(n => new MapNode(n.Id, n.File.Value, n.Title, n.Line, n.TypedChars, n.Isolated, n.Level)),
                result.Edges.Select(e => new MapRoad(e.From, e.To, Road(e.Kind), e.Auto)),
                result.Fogs.Select(f => new MapFog(f.Node, f.Line)),
                result.Marks.Select(m => new MapMark(m.Node, Mark(m.Type), m.Message)),
                result.Flowers.Select(f => new MapFlower(f.Node, f.Line, Flower(f.Emotion), f.Withered)),
                buildings: result.Buildings.Select(b => new MapBuilding(b.Node, b.ProperNounNote.Value, b.ProperNounNote.Name, b.Strong, b.Medium, b.Weak)));
        }
        private static RoadKind Road(EdgeKind value)
        {
            switch (value) { case EdgeKind.Unspecified: return RoadKind.Unspecified; case EdgeKind.Premise: return RoadKind.Premise;
                case EdgeKind.Contrast: return RoadKind.Contrast; case EdgeKind.Example: return RoadKind.Example;
                default: throw new ArgumentOutOfRangeException(nameof(value)); }
        }
        private static MarkKind Mark(MarkType value)
        {
            switch (value) { case MarkType.NoReason: return MarkKind.MissingReason; case MarkType.BrokenLink: return MarkKind.BrokenLink;
                case MarkType.Isolated: return MarkKind.Isolated; default: throw new ArgumentOutOfRangeException(nameof(value)); }
        }
        private static Cartowords.MapPresentation.Emotion Flower(Cartograph.Analysis.Emotion value)
        {
            switch (value) { case Cartograph.Analysis.Emotion.Joy: return Cartowords.MapPresentation.Emotion.Joy;
                case Cartograph.Analysis.Emotion.Anger: return Cartowords.MapPresentation.Emotion.Anger;
                case Cartograph.Analysis.Emotion.Sorrow: return Cartowords.MapPresentation.Emotion.Sadness;
                case Cartograph.Analysis.Emotion.Pleasure: return Cartowords.MapPresentation.Emotion.Delight;
                default: throw new ArgumentOutOfRangeException(nameof(value)); }
        }
    }
}
