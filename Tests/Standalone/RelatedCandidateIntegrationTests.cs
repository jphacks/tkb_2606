using System;
using System.Linq;
using System.Threading.Tasks;
using Cartograph.Analysis;
using Cartograph.Core.Tests;
using Cartograph.EditorUI;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.IntegrationTests
{
    public sealed class RelatedCandidateIntegrationTests
    {
        private static async Task<MapSnapshot> Analyze(ProjectEditorWorkspace ws, int generation = 1)
        {
            var request = new MapRequest("p", generation, ws.Documents.ToDictionary(d => d.id, d => d.revision));
            return await MapAnalysisAdapter.Analyze(ws, request, () => true, ws.RejectedRelatedPairs().ToArray());
        }
        private static async Task<(Env, ProjectEditorWorkspace)> Open()
        {
            var env = new Env();
            env.PutNote("光合成", "# 光合成\n葉緑体の働きを調べた。");
            env.PutNote("葉緑体", "# 葉緑体\n植物の細胞にある。");
            env.PutNote("日記", "# 日記\n今日は散歩した。");
            var ws = new ProjectEditorWorkspace(env.Options, env.Fs); await ws.OpenProject(env.Paths.Root);
            return (env, ws);
        }

        [Test]
        public async Task MentionedNoteBecomesCandidateAnchoredOnFirstHeadings()
        {
            var (_, ws) = await Open();
            var map = await Analyze(ws);
            var candidate = map.Candidates.Single();
            Assert.That(new[] { candidate.FromNote, candidate.ToNote }, Is.EquivalentTo(new[] { "光合成", "葉緑体" }));
            Assert.That(map.Nodes.Single(n => n.Id == candidate.FromNode).File, Is.EqualTo(candidate.FromNote));
            Assert.That(candidate.Reason, Is.Not.Empty);
            Assert.That(MapAnalysisAdapter.SuggestedNotes(map, "光合成"), Is.EqualTo(new[] { "葉緑体" }));
            Assert.That(MapAnalysisAdapter.SuggestedNotes(map, "日記"), Is.Empty);
        }

        [Test]
        public async Task RejectedPairIsNotProposedAgainAndSurvivesReopening()
        {
            var (env, ws) = await Open();
            Assert.That(ws.RejectRelated("葉緑体", "光合成"), Is.True);
            Assert.That((await Analyze(ws)).Candidates, Is.Empty);
            var again = new ProjectEditorWorkspace(env.Options, env.Fs); await again.OpenProject(env.Paths.Root);
            Assert.That((await Analyze(again, 2)).Candidates, Is.Empty);
        }

        [Test]
        public async Task ApprovingAppendsALinkAndTheCandidateBecomesARoad()
        {
            var (env, ws) = await Open();
            await ws.AppendRelatedLink("光合成", "葉緑体");
            Assert.That(env.Body("光合成"), Does.EndWith("[[葉緑体]]\n"));
            var map = await Analyze(ws);
            Assert.That(map.Candidates, Is.Empty);
            Assert.That(MapAnalysisAdapter.ConnectedNotes(map, "光合成"), Is.EqualTo(new[] { "葉緑体" }));
            await ws.AppendRelatedLink("光合成", "葉緑体"); // 二重には足さない
            Assert.That(env.Body("光合成").Split(new[] { "[[葉緑体]]" }, StringSplitOptions.None).Length, Is.EqualTo(2));
        }

        [Test]
        public async Task AtMostThreeCandidatesPerNote()
        {
            var env = new Env();
            env.PutNote("中心", "# 中心\n白猫 黒猫 赤猫 青猫 金猫 のこと。");
            foreach (var name in new[] { "白猫", "黒猫", "赤猫", "青猫", "金猫" }) env.PutNote(name, "# " + name + "\n一匹いる。");
            var ws = new ProjectEditorWorkspace(env.Options, env.Fs); await ws.OpenProject(env.Paths.Root);
            var map = await Analyze(ws);
            Assert.That(map.Candidates.Count(c => c.FromNote == "中心" || c.ToNote == "中心"), Is.EqualTo(MapAnalysisAdapter.MaxCandidatesPerNote));
        }

        [Test]
        public void RejectFileSkipsBrokenLinesAndRoundTripsInOrder()
        {
            var pairs = RelatedRejectFile.Parse("b\ta\n\n壊れた行\n同じ\t同じ\nc\td\r\nb\ta\n");
            Assert.That(pairs.Count, Is.EqualTo(2));
            Assert.That(RelatedRejectFile.Serialize(pairs), Is.EqualTo("a\tb\nc\td\n"));
            Assert.That(RelatedRejectFile.Parse(null).Count, Is.EqualTo(0));
        }
    }
}
