using System;
using System.Collections.Generic;
using System.Linq;
using Cartowords.MapPresentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cartograph.MapUI
{
    public sealed class MapCanvas : VisualElement
    {
        private readonly struct Quad
        {
            public readonly Rect Rect;
            public readonly Color32 Color;
            public Quad(Rect rect, Color32 color) { Rect = rect; Color = color; }
        }
        // 目印の中心の、拠点からの世界座標でのずれ(旗の右上)。
        private static readonly Vector2 MarkOffset = new Vector2(14, -18);
        private readonly MapViewport viewport = new MapViewport();
        private Rect viewRect;
        private readonly MapTouchGesture touchGesture = new MapTouchGesture();
        private readonly VisualElement labels = new VisualElement { pickingMode = PickingMode.Ignore };
        private readonly List<Label> labelPool = new List<Label>();
        private MapSnapshot snapshot;
        private Dictionary<string, MapPoint> positions = new Dictionary<string, MapPoint>();
        private readonly HashSet<long> land = new HashSet<long>();
        private int frame;
        // 道のマス: 道が伸びる向きの組み合わせ(上=1 右=2 下=4 左=8)と、種別の重ね絵(向きは 0上 1右 2下 3左)。
        private readonly Dictionary<long, int> pathMask = new Dictionary<long, int>();
        private readonly Dictionary<long, KeyValuePair<RoadKind, int>> pathKind = new Dictionary<long, KeyValuePair<RoadKind, int>>();
        // 壁から土地に変わったマスと、崩れ始めたコマ番号。
        private readonly Dictionary<long, int> collapsing = new Dictionary<long, int>();
        private readonly Dictionary<string, MapNode> nodes = new Dictionary<string, MapNode>();
        // 前の解析で置いた建物と、いま建ち始めの建物(建ち始めのコマ番号)。
        private HashSet<string> placedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> appearing = new Dictionary<string, int>(StringComparer.Ordinal);
        private static string BuildingKey(MapBuilding building) => building.File + "|" + building.Node;
        private string selected, lastClick;
        // 選んだ関連ノートの候補(MapCandidate.Key)。拠点の選択とは別に点線を太く示す。
        private string selectedCandidate;
        /// <summary>候補の点線を選んだ。第2引数は、クリックした位置(地図の左上が原点)。</summary>
        public event Action<MapCandidate, Vector2> CandidateSelected;
        public void ClearCandidateSelection() { if (selectedCandidate != null) { selectedCandidate = null; Repaint(); } }
        private const float CandidateReach = 8f;
        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = ab.sqrMagnitude < 0.0001f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }
        private MapCandidate CandidateAt(Vector2 at)
        {
            if (snapshot == null) return null;
            MapCandidate best = null; float bestDistance = CandidateReach;
            foreach (var candidate in snapshot.Candidates)
            {
                if (!positions.TryGetValue(candidate.FromNode, out var f) || !positions.TryGetValue(candidate.ToNode, out var t)) continue;
                var a = viewport.Screen(f); var b = viewport.Screen(t);
                float distance = DistanceToSegment(at, new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            return best;
        }
        private float lastClickAt;
        private Vector2 pointerStart, previousPointer;
        private bool dragging, moved, pendingFit;
        public event Action<MapNode> NodeSelected;
        public event Action<MapNode> NodeDoubleClicked;
        public event Action<string> MarkSelected;
        public event Action<MapBuilding, MapNode> BuildingSelected;
        public event Action<MapBuilding, MapNode> BuildingDoubleClicked;
        // 拠点の位置が、最後に通知した(または読み込んだ)ものから変わった時。保存のために使う。
        public event Action<IReadOnlyDictionary<string, MapPoint>> LayoutChanged;
        private Dictionary<string, MapPoint> reportedLayout = new Dictionary<string, MapPoint>();
        // 置いた建物。SetSnapshot で、道のマスが決まったあとに決める。
        private readonly List<BuildingPlacement> placements = new List<BuildingPlacement>();
        // 置いた自然物(解析のたびに一度だけ決める)。描く時は画面内のものだけ拾う。
        private readonly List<NatureItem> natureItems = new List<NatureItem>();
        private readonly Label hoverLabel = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
        // エディタのカーソルがある拠点。クリックで選んだ拠点(selected)とは別に、脈打つ枠で示す。
        private string editorNode;
        // マップを操作している間と直後は、自動スクロールを止める。
        private readonly MapInputGate inputGate = new MapInputGate(3f);
        private MapPanTween panTween;
        private float panStartedAt;
        private IVisualElementScheduledItem panJob;
        private const float PanSeconds = 0.25f;
        // 自動スクロールの間は、新しい土地が崩れ始めるのを待つコマ数(140ms 単位)。
        private const int PanWaitFrames = 2;

        public MapCanvas()
        {
            hoverLabel.style.position = Position.Absolute; hoverLabel.style.fontSize = 11; hoverLabel.style.display = DisplayStyle.None;
            hoverLabel.style.backgroundColor = (Color)new Color32(12, 22, 36, 230);
            hoverLabel.style.color = (Color)new Color32(236, 226, 190, 255);
            hoverLabel.style.paddingLeft = hoverLabel.style.paddingRight = 4;
            Add(hoverLabel);
            name = "knowledge-map"; style.flexGrow = 1; style.minHeight = 200;
            style.overflow = Overflow.Hidden; style.position = Position.Relative;
            labels.style.position = Position.Absolute; labels.style.left = labels.style.top = 0;
            labels.style.right = labels.style.bottom = 0; Add(labels);
            generateVisualContent += Draw;
            // 花と旗のアニメ。拡大率が小さい(描く数が多い)時は止めて、再描画を増やさない。
            schedule.Execute(() =>
            {
                frame++;
                int length = MapArt.Instance.Collapse.Length;
                if (collapsing.Count > 0)
                    foreach (long key in collapsing.Where(p => frame - p.Value >= length).Select(p => p.Key).ToList()) collapsing.Remove(key);
                if (appearing.Count > 0)
                    foreach (string key in appearing.Where(p => frame - p.Value >= BuildingAppearance.Frames).Select(p => p.Key).ToList()) appearing.Remove(key);
                if ((viewport.Scale >= 0.3f && snapshot != null && snapshot.Nodes.Count > 0) || collapsing.Count > 0 || appearing.Count > 0) MarkDirtyRepaint();
            }).Every(140);
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (pendingFit && contentRect.width > 1) { pendingFit = false; Fit(); }
                // まだ動かしていない地図は、パネルの大きさが変わるたびに全体が収まるよう合わせ直す。
                else if (!userMoved && contentRect.width > 1 && snapshot != null && snapshot.Nodes.Count > 0) Fit();
                else UpdateLabels();
            });
            RegisterCallback<PointerDownEvent>(evt =>
            {
                UserTouched();
                if (evt.pointerType == UnityEngine.UIElements.PointerType.touch)
                {
                    dragging = false;
                    if (touchGesture.Count > 0) lastClick = null;
                    touchGesture.Down(evt.pointerId, new MapPoint(evt.localPosition.x, evt.localPosition.y));
                    this.CapturePointer(evt.pointerId); evt.StopPropagation(); return;
                }
                if (evt.button != 0) return;
                pointerStart = previousPointer = evt.localPosition; dragging = true; moved = false;
                this.CapturePointer(evt.pointerId); evt.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (dragging || touchGesture.Count > 0) UserTouched();
                if (touchGesture.Move(evt.pointerId, new MapPoint(evt.localPosition.x, evt.localPosition.y), viewport))
                { Repaint(); evt.StopPropagation(); return; }
                if (!dragging) { UpdateHover(evt.localPosition); return; }
                Vector2 point = evt.localPosition;
                if ((point - pointerStart).sqrMagnitude > 25) moved = true;
                if (moved) { HideHover(); viewport.Pan(point.x - previousPointer.x, point.y - previousPointer.y); Repaint(); }
                previousPointer = point;
            });
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (touchGesture.Contains(evt.pointerId))
                {
                    bool tap = touchGesture.Up(evt.pointerId);
                    this.ReleasePointer(evt.pointerId);
                    if (tap) SelectAt(evt.localPosition);
                    evt.StopPropagation(); return;
                }
                if (!dragging || evt.button != 0) return;
                dragging = false; this.ReleasePointer(evt.pointerId);
                if (!moved) SelectAt(evt.localPosition);
                evt.StopPropagation();
            });
            RegisterCallback<PointerLeaveEvent>(_ => HideHover());
            RegisterCallback<PointerCaptureOutEvent>(evt => { touchGesture.Up(evt.pointerId, true); dragging = false; });
            RegisterCallback<PointerCancelEvent>(evt =>
            { touchGesture.Up(evt.pointerId, true); this.ReleasePointer(evt.pointerId); dragging = false; });
            RegisterCallback<DetachFromPanelEvent>(_ => { touchGesture.Clear(); dragging = false; });
            RegisterCallback<WheelEvent>(evt =>
            {
                UserTouched();
                viewport.Zoom(Mathf.Exp(-evt.delta.y * 0.08f), new MapPoint(evt.localMousePosition.x, evt.localMousePosition.y));
                Repaint(); evt.StopPropagation();
            });
        }

        public void SetSnapshot(MapSnapshot value)
        {
            bool firstData = snapshot == null || snapshot.Nodes.Count == 0;
            snapshot = value; lastClick = null;
            // 見出しの書き換えで ID が変わった拠点は、前の拠点として扱う(位置・選択・カーソルの拠点を引き継ぎ、追加の演出は出さない)。
            var renamed = value == null ? new Dictionary<string, string>() : MapRename.Match(nodes.Values, value.Nodes);
            var oldIds = new HashSet<string>(nodes.Keys, StringComparer.Ordinal);
            foreach (var pair in renamed)
            {
                if (positions.TryGetValue(pair.Value, out var kept)) positions[pair.Key] = kept;
                if (selected == pair.Value) selected = pair.Key;
                if (editorNode == pair.Value) editorNode = pair.Key;
            }
            if (value == null || !value.Nodes.Any(n => n.Id == selected)) selected = null;
            if (value == null || !value.Nodes.Any(n => n.Id == editorNode)) editorNode = null;
            if (value == null || !value.Candidates.Any(c => c.Key == selectedCandidate)) selectedCandidate = null;
            var before = new HashSet<long>(land);
            nodes.Clear(); land.Clear(); pathMask.Clear(); pathKind.Clear(); placements.Clear(); natureItems.Clear(); HideHover();
            long setStart = MapProfile.Start(), setPhase = setStart;
            positions = value == null ? new Dictionary<string, MapPoint>() : MapLayout.Solve(value, positions);
            MapProfile.Stop("set.solve", setPhase); setPhase = MapProfile.Start();
            var newNodeLand = new HashSet<long>(); MapNode firstAdded = null;
            // 前の地図と共通の拠点が 1 つもないとき(表示サンプルとの切り替えなど)は、増分ではなく別の地図として扱う。
            bool incremental = before.Count > 0 && value != null && value.Nodes.Any(n => oldIds.Contains(n.Id) || renamed.ContainsKey(n.Id));
            if (value != null)
            {
                foreach (var node in value.Nodes)
                {
                    nodes.Add(node.Id, node);
                    bool added = incremental && !oldIds.Contains(node.Id) && !renamed.ContainsKey(node.Id);
                    if (added && firstAdded == null) firstAdded = node;
                    var point = positions[node.Id]; int radius = MapLayout.LandRadius(node.TypedChars);
                    int cx = (int)(point.X / MapLayout.Tile), cy = (int)(point.Y / MapLayout.Tile);
                    for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                        if (x * x + y * y <= radius * radius + radius)
                        { PutLand(cx + x, cy + y); if (added) newNodeLand.Add(Key(cx + x, cy + y)); }
                }
                MapProfile.Stop("set.land", setPhase); setPhase = MapProfile.Start();
                foreach (var edge in value.Roads) Carve(edge, positions[edge.From], positions[edge.To]);
                MapProfile.Stop("set.carve", setPhase); setPhase = MapProfile.Start();
                PlaceObjects(value);
                MapProfile.Stop("set.place", setPhase); setPhase = MapProfile.Start();
                PrepareDrawCaches(value);
                MapProfile.Stop("set.caches", setPhase);
                // 壁が崩れるのは、新しく現れた見出しの土地だけ。文字が増えて土地の半径が広がっただけでは崩さない。
                // 新しい拠点が画面の外なら、そこへスクロールし終わってから崩れ始める。
                int wait = firstAdded != null && contentRect.width > 1 && TryAutoScroll(firstAdded.Id) ? PanWaitFrames : 0;
                foreach (long key in newNodeLand)
                    if (!before.Contains(key) && !collapsing.ContainsKey(key)) collapsing[key] = frame + wait;
                // 新しく建つ建物は、下から伸びる。
                var keys = placements.Select(p => BuildingKey(p.Building)).ToList();
                foreach (var key in BuildingAppearance.NewKeys(placedKeys, keys, incremental)) appearing[key] = frame + wait;
                placedKeys = new HashSet<string>(keys, StringComparer.Ordinal);
                foreach (var key in appearing.Keys.Where(k => !placedKeys.Contains(k)).ToList()) appearing.Remove(key);
            }
            else { placedKeys.Clear(); appearing.Clear(); }
            foreach (long key in collapsing.Keys.Where(k => !land.Contains(k)).ToList()) collapsing.Remove(key);
            if (value == null) { flowerDraws.Clear(); flagDraws.Clear(); townLabels.Clear(); terrainCells.Clear(); }
            overviewDirty = true;
            pendingFit = firstData;
            if (pendingFit && contentRect.width > 1) { pendingFit = false; Fit(); }
            // 最初の地図は、画面の分割や拡大率が落ち着いたあとの大きさに合わせて、もう一度全体を収める。
            if (firstData && value != null && value.Nodes.Count > 0)
                schedule.Execute(() => { if (!userMoved && contentRect.width > 1) Fit(); }).StartingIn(400);
            MapProfile.Stop("set.total", setStart);
            Repaint();
            if (value != null && !MapLayoutFile.Same(positions, reportedLayout))
            { reportedLayout = new Dictionary<string, MapPoint>(positions); LayoutChanged?.Invoke(reportedLayout); }
        }
        // 保存してあった拠点の位置を、最初の解析結果が来る前に渡す。次の SetSnapshot で、その位置に固定される。
        public void SetSavedLayout(IReadOnlyDictionary<string, MapPoint> saved)
        {
            if (snapshot != null && snapshot.Nodes.Count > 0) return;
            positions = saved == null ? new Dictionary<string, MapPoint>() : new Dictionary<string, MapPoint>(saved);
            reportedLayout = new Dictionary<string, MapPoint>(positions);
        }
        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;
        private void PutLand(int x, int y) => land.Add(Key(x, y));
        // 2つの拠点のマスを上下左右だけでたどり、土地を開いて、道のマスの向きを記録する。
        private void Carve(MapRoad edge, MapPoint a, MapPoint b)
        {
            int x = (int)(a.X / MapLayout.Tile), y = (int)(a.Y / MapLayout.Tile);
            int ex = (int)(b.X / MapLayout.Tile), ey = (int)(b.Y / MapLayout.Tile);
            int dx = Math.Abs(ex - x), dy = Math.Abs(ey - y), sx = x < ex ? 1 : -1, sy = y < ey ? 1 : -1, error = dx - dy;
            var cells = new List<Vector2Int> { new Vector2Int(x, y) };
            for (int guard = 0; (x != ex || y != ey) && guard <= dx + dy + 2; guard++)
            {
                int twice = 2 * error;
                if (twice > -dy && twice < dx) { error -= dy; x += sx; cells.Add(new Vector2Int(x, y)); error += dx; y += sy; }
                else if (twice > -dy) { error -= dy; x += sx; }
                else { error += dx; y += sy; }
                cells.Add(new Vector2Int(x, y));
            }
            // 道のマスは、両端の拠点の土地(領域)の外側だけ。道は領域の端から出る。
            int radiusA = MapLayout.LandRadius(nodes[edge.From].TypedChars), radiusB = MapLayout.LandRadius(nodes[edge.To].TypedChars);
            var startCell = cells[0]; var endCell = cells[cells.Count - 1];
            bool InRegion(Vector2Int c, Vector2Int center, int radius)
            { int ox = c.x - center.x, oy = c.y - center.y; return ox * ox + oy * oy <= radius * radius + radius; }
            bool IsPath(int i) => !InRegion(cells[i], startCell, radiusA) && !InRegion(cells[i], endCell, radiusB);
            for (int i = 0; i < cells.Count; i++)
            {
                PutLand(cells[i].x, cells[i].y);
                if (i + 1 >= cells.Count) break;
                var step = cells[i + 1] - cells[i];
                int dir = step.y < 0 ? 0 : step.x > 0 ? 1 : step.y > 0 ? 2 : 3;
                long from = Key(cells[i].x, cells[i].y), to = Key(cells[i + 1].x, cells[i + 1].y);
                // 領域の中のマスは土地のまま。領域に接する道のマスには、領域の側へ向かう口が開く。
                if (IsPath(i)) pathMask[from] = (pathMask.TryGetValue(from, out int m1) ? m1 : 0) | (1 << dir);
                if (IsPath(i + 1)) pathMask[to] = (pathMask.TryGetValue(to, out int m2) ? m2 : 0) | (1 << ((dir + 2) % 4));
                if (IsPath(i) && !edge.Auto && edge.Kind != RoadKind.Unspecified) pathKind[from] = new KeyValuePair<RoadKind, int>(edge.Kind, dir);
            }
        }
        // 解析のたびに一度だけ求めておく、毎フレームの描画で使う値(文字列のハッシュや並べ替えを、毎フレームしないため)。
        private struct FlowerDraw { public MapFlower Flower; public int Hash; public float X, Bottom; }
        private struct FlagDraw { public float X, Bottom; public Sprite[] Frames; public MapPoint At; }
        private struct TownLabel { public string Name; public MapPoint Anchor; }
        private readonly List<FlowerDraw> flowerDraws = new List<FlowerDraw>();
        private readonly List<FlagDraw> flagDraws = new List<FlagDraw>();
        private readonly List<TownLabel> townLabels = new List<TownLabel>();
        private void PrepareDrawCaches(MapSnapshot value)
        {
            var art = MapArt.Instance;
            flowerDraws.Clear(); flagDraws.Clear(); townLabels.Clear();
            BuildTerrainCells(art);
            foreach (var flower in value.Flowers)
            {
                if (nodes[flower.Node].TypedChars == 0) continue;
                int hash = MapLayout.StableHash(flower.Node + ":" + flower.Line); var p = positions[flower.Node];
                // 拠点のマスと、そのまわり1マスのどこかに咲かせる。
                flowerDraws.Add(new FlowerDraw { Flower = flower, Hash = hash, X = p.X + (hash % 3 - 1) * MapLayout.Tile,
                    Bottom = p.Y + (hash / 3 % 3 - 1) * MapLayout.Tile + MapLayout.Tile / 2 });
            }
            foreach (var node in value.Nodes.OrderBy(n => positions[n.Id].Y))
            {
                var at = positions[node.Id];
                flagDraws.Add(new FlagDraw { X = at.X, Bottom = at.Y + MapLayout.Tile / 2, Frames = art.FlagFor(MapLayout.StableHash(node.Town)), At = at });
            }
            foreach (var town in value.Nodes.GroupBy(n => n.Town).Take(16))
            {
                var points = town.Select(n => positions[n.Id]).ToArray();
                townLabels.Add(new TownLabel { Name = town.Key.Length == 0 ? "ノート" : town.Key,
                    Anchor = new MapPoint(points.Average(point => point.X), points.Min(point => point.Y) - 65) });
            }
        }
        // 描ける絵のある建物だけを、旗・花・道を避けて置く。建物のマスは土地にして、足りない土地を広げる。
        // 占有マップに、旗・花・目印(拠点の中心 3x3)、道、建物、自然物の順に予約していく。先に予約したものが優先される。
        private void PlaceObjects(MapSnapshot value)
        {
            var art = MapArt.Instance;
            var cells = new Dictionary<string, long>();
            foreach (var node in value.Nodes)
            { var p = positions[node.Id]; cells[node.Id] = MapCell.Key((int)(p.X / MapLayout.Tile), (int)(p.Y / MapLayout.Tile)); }
            var occupancy = MapOccupancy.ForNodes(cells.Values, pathMask.Keys);

            var settings = BuildingConfig.Settings;
            var drawable = value.Buildings.Where(b => HasArt(BuildingSizing.Measure(b, settings), b, art));
            foreach (var placement in BuildingPlacer.PlaceOn(drawable, cells, occupancy, settings))
            {
                placements.Add(placement);
                for (int dx = 0; dx < placement.Size.Width; dx++)
                for (int dy = 0; dy < placement.Size.Depth; dy++)
                    PutLand(placement.X + dx, placement.Y + dy);
            }

            var natureNodes = value.Nodes.Select(n => new NatureNode(n.Id, cells[n.Id], n.TypedChars, n.Level, n.Town));
            foreach (var item in NaturePlacer.Place(natureNodes, value.Roads, occupancy, NatureConfig.Settings))
                if (art.Nature[(int)item.Kind] != null) natureItems.Add(item);
        }
        private static bool HasArt(BuildingSize size, MapBuilding building, MapArt art)
            => size.Shape == BuildingShape.Tower ? art.TowerFor(size.Width, MapLayout.StableHash(building.File)) != null : art.Fixed(size.Shape) != null;
        // 建物が地図上で占める範囲(世界座標)。ビルは地面の1段より上へ伸びる。
        private static Rect BuildingWorldRect(BuildingPlacement p)
        {
            float tile = MapLayout.Tile, width = p.Size.Width * tile;
            float height = (p.Size.Shape == BuildingShape.Tower ? p.Size.Floors + 2 : p.Size.Depth) * tile;
            float bottom = (p.Y + p.Size.Depth - 1) * tile + tile / 2;
            return new Rect((p.X + (p.Size.Width - 1) / 2f) * tile - width / 2, bottom - height, width, height);
        }
        // 点にある建物のうち、いちばん手前(下)のものの番号。なければ -1。
        private int BuildingAt(Vector2 at)
        {
            int best = -1; float bestBottom = float.MinValue;
            for (int i = 0; i < placements.Count; i++)
            {
                var w = BuildingWorldRect(placements[i]);
                var a = viewport.Screen(new MapPoint(w.xMin, w.yMin)); var b = viewport.Screen(new MapPoint(w.xMax, w.yMax));
                if (at.x < a.X || at.x > b.X || at.y < a.Y || at.y > b.Y || b.Y < bestBottom) continue;
                best = i; bestBottom = b.Y;
            }
            return best;
        }
        private void UpdateHover(Vector2 at)
        {
            int index = BuildingAt(at);
            if (index < 0)
            {
                var candidate = CandidateAt(at);
                if (candidate == null) { HideHover(); return; }
                hoverLabel.text = "関連の候補: " + candidate.Reason;
                hoverLabel.style.left = at.x + 14; hoverLabel.style.top = at.y + 14; hoverLabel.style.display = DisplayStyle.Flex;
                return;
            }
            var building = placements[index].Building;
            hoverLabel.text = building.Name + "  ×" + building.Count;
            hoverLabel.style.left = at.x + 14; hoverLabel.style.top = at.y + 14; hoverLabel.style.display = DisplayStyle.Flex;
        }
        private void HideHover() { if (hoverLabel.style.display != DisplayStyle.None) hoverLabel.style.display = DisplayStyle.None; }
        public float Scale => viewport.Scale;
        public void Fit() { viewport.Fit(positions.Values, contentRect.width, contentRect.height); Repaint(); }
        public void Zoom(float factor) { UserTouched(); viewport.Zoom(factor, new MapPoint(contentRect.width / 2, contentRect.height / 2)); Repaint(); }
        // ユーザーがマップを操作した。動いている自動スクロールをやめ、しばらく自動スクロールを止める。
        private bool userMoved;
        private void UserTouched()
        {
            userMoved = true;
            inputGate.Touch(Time.realtimeSinceStartup);
            panJob?.Pause(); panTween = null;
        }
        // エディタのカーソルがある拠点を示す。別の拠点に移ったときだけ、画面の外ならそこへスクロールする。
        public void SetEditorFocus(string nodeId)
        {
            if (nodeId != null && !nodes.ContainsKey(nodeId)) nodeId = null;
            if (nodeId == editorNode) return;
            editorNode = nodeId;
            if (nodeId != null) TryAutoScroll(nodeId);
            Repaint();
        }
        // 拠点が画面の内側の余白より外にあれば、中央へ滑らかにパンする。操作中・操作の直後は何もしない。
        private bool TryAutoScroll(string nodeId)
        {
            if (!positions.TryGetValue(nodeId, out var world) || contentRect.width <= 1) return false;
            bool active = dragging || touchGesture.Count > 0;
            if (!inputGate.IsQuiet(Time.realtimeSinceStartup, active)) return false;
            var at = viewport.Screen(world);
            if (!MapAutoScroll.TryDelta(at, contentRect.width, contentRect.height, out float dx, out float dy)) return false;
            panTween = new MapPanTween(dx, dy, PanSeconds); panStartedAt = Time.realtimeSinceStartup;
            panJob?.Pause();
            panJob = schedule.Execute(StepPan); panJob.Every(16);
            return true;
        }
        private void StepPan()
        {
            if (panTween == null) { panJob?.Pause(); return; }
            bool done = panTween.Advance(Time.realtimeSinceStartup - panStartedAt, out float dx, out float dy);
            viewport.Pan(dx, dy); Repaint();
            if (done) { panJob?.Pause(); panTween = null; }
        }
        public void FocusSelection()
        {
            if (selected == null || !positions.TryGetValue(selected, out var p)) { Fit(); return; }
            var at = viewport.Screen(p); viewport.Pan(contentRect.width / 2 - at.X, contentRect.height / 2 - at.Y); Repaint();
        }
        private void Repaint() { MarkDirtyRepaint(); UpdateLabels(); }
        private void SelectAt(Vector2 at)
        {
            if (snapshot == null) return;
            selectedCandidate = null;
            foreach (var mark in snapshot.Marks)
            {
                var p = viewport.Screen(positions[mark.Node]); float s = viewport.Scale, reach = Mathf.Max(10, 12 * s);
                if ((new Vector2(p.X + MarkOffset.x * s, p.Y + MarkOffset.y * s) - at).sqrMagnitude < reach * reach)
                { selected = mark.Node; lastClick = null; MarkSelected?.Invoke(mark.Message); Repaint(); return; }
            }
            int hit = BuildingAt(at);
            if (hit >= 0)
            {
                var building = placements[hit].Building; string key = "\u0001" + building.File + "|" + building.Node;
                selected = null; BuildingSelected?.Invoke(building, nodes[building.Node]);
                if (lastClick == key && Time.realtimeSinceStartup - lastClickAt < 0.35f)
                { lastClick = null; BuildingDoubleClicked?.Invoke(building, nodes[building.Node]); }
                else { lastClick = key; lastClickAt = Time.realtimeSinceStartup; }
                Repaint(); return;
            }
            string closest = null; float distance = 24 * 24;
            foreach (var pair in positions)
            {
                var p = viewport.Screen(pair.Value); float squared = (new Vector2(p.X, p.Y) - at).sqrMagnitude;
                if (squared < distance) { distance = squared; closest = pair.Key; }
            }
            if (closest == null)
            {
                selected = null; lastClick = null;
                var candidate = CandidateAt(at);
                if (candidate != null) { selectedCandidate = candidate.Key; CandidateSelected?.Invoke(candidate, at); }
                Repaint(); return;
            }
            selected = closest; NodeSelected?.Invoke(nodes[closest]);
            if (lastClick == closest && Time.realtimeSinceStartup - lastClickAt < 0.35f)
            { lastClick = null; NodeDoubleClicked?.Invoke(nodes[closest]); }
            else { lastClick = closest; lastClickAt = Time.realtimeSinceStartup; }
            Repaint();
        }
        private void UpdateLabels()
        {
            long profileStart = MapProfile.Start();
            try { UpdateLabelsCore(); } finally { MapProfile.Stop("labels", profileStart); }
        }
        private void UpdateLabelsCore()
        {
            int count = 0;
            if (snapshot == null)
            { foreach (var label in labelPool) label.style.display = DisplayStyle.None; return; }
            foreach (var town in townLabels)
            {
                var p = viewport.Screen(town.Anchor);
                if (p.X < -80 || p.Y < -30 || p.X > contentRect.width || p.Y > contentRect.height) continue;
                SetLabel(count++, town.Name, p.X, p.Y, true);
            }
            // 選んだ拠点とカーソルの拠点のラベルを先に出し、数の上限で切られないようにする。
            void NodeLabel(MapNode node)
            {
                var p = viewport.Screen(positions[node.Id]);
                if (p.X < -40 || p.Y < -30 || p.X > contentRect.width + 40 || p.Y > contentRect.height + 30) return;
                SetLabel(count++, node.Title.Length > 22 ? node.Title.Substring(0, 21) + "…" : node.Title, p.X + 9, p.Y + 9, false);
            }
            if (selected != null && nodes.TryGetValue(selected, out var selectedNode)) NodeLabel(selectedNode);
            if (editorNode != null && editorNode != selected && nodes.TryGetValue(editorNode, out var editorNodeValue)) NodeLabel(editorNodeValue);
            if (viewport.Scale >= 0.3f)
                foreach (var node in snapshot.Nodes)
                {
                    if (node.Id == selected || node.Id == editorNode) continue;
                    if (count >= 106) break;
                    NodeLabel(node);
                }
            for (int i = count; i < labelPool.Count; i++) labelPool[i].style.display = DisplayStyle.None;
        }
        private void SetLabel(int index, string text, float x, float y, bool town)
        {
            while (labelPool.Count <= index)
            {
                var created = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                created.style.position = Position.Absolute; created.style.fontSize = 11;
                created.style.backgroundColor = (Color)new Color32(12, 22, 36, 210);
                created.style.paddingLeft = created.style.paddingRight = 3;
                labels.Add(created); labelPool.Add(created);
            }
            var label = labelPool[index]; label.text = text; label.style.display = DisplayStyle.Flex;
            label.style.left = x; label.style.top = y;
            label.style.color = (Color)(town ? new Color32(183, 195, 152, 255) : new Color32(223, 231, 232, 255));
        }
        private void AddQuad(List<Quad> quads, float x, float y, float width, float height, Color32 color)
        {
            var rect = new Rect(x, y, width, height);
            if (width > 0.2f && height > 0.2f && rect.Overlaps(viewRect)) quads.Add(new Quad(rect, color));
        }
        private void WorldQuad(List<Quad> quads, float x, float y, float size, Color32 color)
        {
            var p = viewport.Screen(new MapPoint(x, y));
            AddQuad(quads, p.X - size * viewport.Scale / 2, p.Y - size * viewport.Scale / 2,
                size * viewport.Scale, size * viewport.Scale, color);
        }
        private void Draw(MeshGenerationContext context)
        {
            var quads = new List<Quad>();
            var art = MapArt.Instance; var sprites = new List<SpriteDraw>();
            var overlays = new List<SpriteDraw>();
            viewRect = contentRect; // contentRect は取るたびに計算されるので、描画の間は 1 回だけ取る
            long drawStart = MapProfile.Start(), phase = drawStart;
            overviewVisible = false;
            if (art.HasTiles) AddTerrain(sprites, overlays, quads, art);
            else
            {
                // 素材が読めない時の代用(色つきの四角)。
                AddQuad(quads, 0, 0, viewRect.width, viewRect.height, new Color32(13, 24, 38, 255));
                foreach (long key in land)
                    WorldQuad(quads, (int)(key >> 32) * MapLayout.Tile, (int)(uint)key * MapLayout.Tile, MapLayout.Tile - 0.6f, new Color32(60, 96, 91, 255));
            }
            MapProfile.Stop("draw.terrain.build", phase); phase = MapProfile.Start();
            Flush(context, quads); quads.Clear();
            if (overviewVisible) DrawOverview(context);
            FlushSprites(context, sprites); sprites.Clear();
            FlushSprites(context, overlays); overlays.Clear();
            MapProfile.Stop("draw.terrain.flush", phase); phase = MapProfile.Start();
            if (snapshot == null) return;
            var painter = context.painter2D;
            // 道の絵が読めない時だけ、線で代用する。
            if (!(art.HasTiles && art.HasPaths))
            foreach (var edge in snapshot.Roads)
            {
                var a = viewport.Screen(positions[edge.From]); var b = viewport.Screen(positions[edge.To]);
                Vector2 from = new Vector2(a.X, a.Y), to = new Vector2(b.X, b.Y);
                if (!new Rect(Math.Min(a.X, b.X) - 10, Math.Min(a.Y, b.Y) - 10, Math.Abs(a.X - b.X) + 20,
                    Math.Abs(a.Y - b.Y) + 20).Overlaps(viewRect)) continue;
                var direction = (to - from).normalized; var normal = new Vector2(-direction.y, direction.x);
                painter.lineWidth = edge.Auto ? 1.5f : edge.Kind == RoadKind.Example ? 2 : 3;
                painter.strokeColor = edge.Auto ? new Color32(131, 172, 166, 220) : new Color32(224, 197, 124, 255);
                painter.BeginPath();
                if (edge.Kind == RoadKind.Example)
                    for (int i = 0; i < 8; i++) { painter.MoveTo(Vector2.Lerp(from, to, i / 8f)); painter.LineTo(Vector2.Lerp(from, to, (i + 0.55f) / 8f)); }
                else if (edge.Kind == RoadKind.Contrast)
                    for (int i = -1; i <= 1; i += 2) { painter.MoveTo(from + normal * i * 2); painter.LineTo(to + normal * i * 2); }
                else { painter.MoveTo(from); painter.LineTo(to); }
                if (edge.Kind == RoadKind.Premise)
                {
                    var tip = to - direction * 9;
                    painter.MoveTo(tip - direction * 8 + normal * 4); painter.LineTo(tip); painter.LineTo(tip - direction * 8 - normal * 4);
                }
                painter.Stroke();
            }
            // 自動検出した関連ノートの候補は、確定した道と区別できる点線で描く。
            foreach (var candidate in snapshot.Candidates)
            {
                if (!positions.TryGetValue(candidate.FromNode, out var cf) || !positions.TryGetValue(candidate.ToNode, out var ct)) continue;
                var a = viewport.Screen(cf); var b = viewport.Screen(ct);
                Vector2 from = new Vector2(a.X, a.Y), to = new Vector2(b.X, b.Y);
                if (!new Rect(Math.Min(a.X, b.X) - 10, Math.Min(a.Y, b.Y) - 10, Math.Abs(a.X - b.X) + 20,
                    Math.Abs(a.Y - b.Y) + 20).Overlaps(viewRect)) continue;
                bool chosen = candidate.Key == selectedCandidate;
                painter.lineWidth = chosen ? 4 : 2.5f;
                painter.strokeColor = chosen ? new Color32(120, 235, 245, 255) : new Color32(120, 205, 225, 200);
                float length = (to - from).magnitude; int dashes = Mathf.Clamp((int)(length / 14f), 2, 200);
                painter.BeginPath();
                for (int i = 0; i < dashes; i++)
                { painter.MoveTo(Vector2.Lerp(from, to, i / (float)dashes)); painter.LineTo(Vector2.Lerp(from, to, (i + 0.55f) / dashes)); }
                painter.Stroke();
            }
            foreach (var fog in snapshot.Fogs)
            {
                var p = positions[fog.Node];
                int radius = MapLayout.LandRadius(nodes[fog.Node].TypedChars);
                var fogFrames = art.FogAnim.Length > 0 ? art.FogAnim : art.FogTile;
                for (int x = -Math.Min(1, radius); x <= Math.Min(1, radius); x++)
                {
                    if (fogFrames.Length == 0)
                    { WorldQuad(quads, p.X + x * MapLayout.Tile, p.Y, MapLayout.Tile, new Color32(181, 193, 208, 125)); continue; }
                    int cx = (int)(p.X / MapLayout.Tile) + x, cy = (int)(p.Y / MapLayout.Tile);
                    AddTile(sprites, cx, cy, fogFrames[(frame + MapLayout.StableHash(fog.Node + ":" + fog.Line)) % fogFrames.Length]);
                }
            }
            FlushSprites(context, sprites); sprites.Clear();
            MapProfile.Stop("draw.roads+fog", phase); phase = MapProfile.Start();
            foreach (var node in snapshot.Nodes)
                if (node.Id == selected)
                { var p = viewport.Screen(positions[node.Id]); AddQuad(quads, p.X - 14, p.Y - 14, 28, 28, new Color32(86, 210, 218, 255)); }
            if (editorNode != null && positions.TryGetValue(editorNode, out var focus))
            {
                // 拠点の旗と花の置き場(3x3 マス)を囲む金色の枠。拡大率が低くても見えるよう、最小の大きさを確保し、少しだけ脈打たせる。
                var p = viewport.Screen(focus); float s = viewport.Scale;
                float pulse = viewport.Scale >= 0.3f ? Math.Abs(frame % 8 - 4) * 0.012f : 0;
                float half = Mathf.Max(10, MapLayout.Tile * 1.5f * s * (1 + pulse)), thick = Mathf.Max(2, 3 * s);
                var gold = new Color32(255, 208, 106, 235);
                AddQuad(quads, p.X - half, p.Y - half, half * 2, half * 2, new Color32(255, 208, 106, 40));
                AddQuad(quads, p.X - half, p.Y - half, half * 2, thick, gold); AddQuad(quads, p.X - half, p.Y + half - thick, half * 2, thick, gold);
                AddQuad(quads, p.X - half, p.Y - half, thick, half * 2, gold); AddQuad(quads, p.X + half - thick, p.Y - half, thick, half * 2, gold);
            }
            Flush(context, quads); quads.Clear();
            // 花・旗・建物は、下の辺が奥(上)にあるものから順に重ねる。
            var objects = new List<DepthSprite>();
            long objectsPhase = MapProfile.Start();
            AddNature(objects, art); MapProfile.Stop("draw.objects.nature", objectsPhase); objectsPhase = MapProfile.Start();
            AddFlowers(objects, art); MapProfile.Stop("draw.objects.flowers", objectsPhase); objectsPhase = MapProfile.Start();
            AddFlags(objects, quads, art); MapProfile.Stop("draw.objects.flags", objectsPhase); objectsPhase = MapProfile.Start();
            AddBuildings(objects, art); MapProfile.Stop("draw.objects.buildings", objectsPhase); objectsPhase = MapProfile.Start();
            FlushOrdered(context, objects);
            MapProfile.Stop("draw.objects.flush", objectsPhase);
            foreach (var mark in snapshot.Marks)
            {
                var at = positions[mark.Node]; var markSprite = art.Marks[(int)mark.Kind];
                if (markSprite != null) { AddAnchored(sprites, at.X + MarkOffset.x, at.Y + MarkOffset.y + MapLayout.Tile / 2, markSprite); continue; }
                var p = viewport.Screen(at);
                var gold = new Color32(255, 208, 106, 255);
                AddQuad(quads, p.X + 11, p.Y - 23, 4, 9, gold); AddQuad(quads, p.X + 11, p.Y - 11, 4, 3, gold);
            }
            Flush(context, quads); FlushSprites(context, sprites);
            MapProfile.Stop("draw.objects+marks", phase); MapProfile.Stop("draw.total", drawStart);
        }
        private readonly struct SpriteDraw
        {
            public readonly Rect Rect;
            public readonly Sprite Sprite;
            public SpriteDraw(Rect rect, Sprite sprite) { Rect = rect; Sprite = sprite; }
        }
        // 1マス(世界座標で Tile)ごとの絵。隣のマスと辺をそろえて、拡大率が中途半端でも隙間を出さない。
        private void AddTile(List<SpriteDraw> sprites, int cx, int cy, Sprite sprite)
        {
            if (sprite == null) return;
            var a = viewport.Screen(new MapPoint(cx * MapLayout.Tile - MapLayout.Tile / 2, cy * MapLayout.Tile - MapLayout.Tile / 2));
            var b = viewport.Screen(new MapPoint(cx * MapLayout.Tile + MapLayout.Tile / 2, cy * MapLayout.Tile + MapLayout.Tile / 2));
            var rect = Rect.MinMaxRect(Mathf.Round(a.X), Mathf.Round(a.Y), Mathf.Round(b.X), Mathf.Round(b.Y));
            if (rect.Overlaps(viewRect)) sprites.Add(new SpriteDraw(rect, sprite));
        }
        // 絵の下の辺の中央を、世界座標の (x, bottom) に合わせて置く。
        // grow は大きさの割合(建物が建つ演出で、下の辺を固定したまま伸ばす)。
        private bool AddAnchored(List<SpriteDraw> sprites, float x, float bottom, Sprite sprite, float grow = 1f)
        {
            if (sprite == null) return false;
            var r = sprite.rect; float s = viewport.Scale * grow;
            var p = viewport.Screen(new MapPoint(x, bottom));
            var rect = Rect.MinMaxRect(Mathf.Round(p.X - r.width * s / 2), Mathf.Round(p.Y - r.height * s),
                Mathf.Round(p.X + r.width * s / 2), Mathf.Round(p.Y));
            if (!rect.Overlaps(viewRect)) return false;
            sprites.Add(new SpriteDraw(rect, sprite)); return true;
        }
        private void AddTerrain(List<SpriteDraw> sprites, List<SpriteDraw> overlays, List<Quad> quads, MapArt art)
        {
            float tile = MapLayout.Tile;
            var topLeft = viewport.World(new MapPoint(0, 0)); var bottomRight = viewport.World(new MapPoint(viewRect.width, viewRect.height));
            int x0 = Mathf.FloorToInt(topLeft.X / tile + 0.5f) - 1, x1 = Mathf.FloorToInt(bottomRight.X / tile + 0.5f) + 1;
            int y0 = Mathf.FloorToInt(topLeft.Y / tile + 0.5f) - 1, y1 = Mathf.FloorToInt(bottomRight.Y / tile + 0.5f) + 1;
            long visible = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
            // 引いて見た時(見える範囲が広い時)は、マスを1枚ずつ描かず、土地を 1 マス 1 ピクセルにした絵を 1 枚だけ描く。
            if (visible > OverviewCells)
            {
                AddQuad(quads, 0, 0, viewRect.width, viewRect.height, new Color32(30, 36, 51, 255));
                if (EnsureOverview()) { overviewVisible = true; return; }
                if (visible > 20000) return; // 絵が作れない(広すぎる)時は、暗い色だけにする。
            }
            long wallStart = MapProfile.Start();
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (!land.Contains(Key(x, y))) AddTile(sprites, x, y, art.Wall[MapLayout.CellHash(x, y) % art.Wall.Length]);
            MapProfile.Stop("terrain.walls", wallStart);
            // 見える範囲のマスが、土地全体より少なければ範囲を、そうでなければ土地を走査する。
            long landStart = MapProfile.Start(); int landCells = 0;
            if (visible < land.Count)
            {
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                { long cell = Key(x, y); if (land.Contains(cell)) { AddLandCell(sprites, overlays, art, cell, x, y); landCells++; } }
            }
            else
                foreach (long key in land)
                {
                    int x = (int)(key >> 32), y = (int)(uint)key;
                    if (x < x0 || x > x1 || y < y0 || y > y1) continue;
                    AddLandCell(sprites, overlays, art, key, x, y); landCells++;
                }
            MapProfile.Stop("terrain.land", landStart); MapProfile.Count("terrain.landCells", landCells); MapProfile.Count("terrain.visibleCells", visible);
        }
        // 引いた時の地形の絵。土地のマスを 1 ピクセルにして、解析のたびに作り直す(描く時に初めて作る)。
        private const int OverviewCells = 6000, OverviewMaxSide = 4096;
        private Texture2D overview;
        private bool overviewDirty = true, overviewVisible;
        private int overviewX0, overviewY0, overviewW, overviewH;
        private bool EnsureOverview()
        {
            if (!overviewDirty) return overview != null;
            overviewDirty = false;
            if (overview != null) { UnityEngine.Object.Destroy(overview); overview = null; }
            if (land.Count == 0) return false;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (long key in land)
            {
                int x = (int)(key >> 32), y = (int)(uint)key;
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            int w = maxX - minX + 1, h = maxY - minY + 1;
            if (w > OverviewMaxSide || h > OverviewMaxSide) return false;
            var pixels = new Color32[w * h];
            var ground = new Color32(88, 118, 82, 255); var path = new Color32(150, 112, 72, 255);
            foreach (long key in land)
            {
                int x = (int)(key >> 32), y = (int)(uint)key;
                pixels[(maxY - y) * w + (x - minX)] = pathMask.ContainsKey(key) ? path : ground;
            }
            overview = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            overview.SetPixels32(pixels); overview.Apply(false, true);
            overviewX0 = minX; overviewY0 = minY; overviewW = w; overviewH = h;
            return true;
        }
        private void DrawOverview(MeshGenerationContext context)
        {
            float tile = MapLayout.Tile;
            var a = viewport.Screen(new MapPoint((overviewX0 - 0.5f) * tile, (overviewY0 - 0.5f) * tile));
            var b = viewport.Screen(new MapPoint((overviewX0 + overviewW - 0.5f) * tile, (overviewY0 + overviewH - 0.5f) * tile));
            var mesh = context.Allocate(4, 6, overview);
            Color32 white = new Color32(255, 255, 255, 255);
            mesh.SetNextVertex(new Vertex { position = new Vector3(a.X, a.Y, Vertex.nearZ), uv = new Vector2(0, 1), tint = white });
            mesh.SetNextVertex(new Vertex { position = new Vector3(b.X, a.Y, Vertex.nearZ), uv = new Vector2(1, 1), tint = white });
            mesh.SetNextVertex(new Vertex { position = new Vector3(b.X, b.Y, Vertex.nearZ), uv = new Vector2(1, 0), tint = white });
            mesh.SetNextVertex(new Vertex { position = new Vector3(a.X, b.Y, Vertex.nearZ), uv = new Vector2(0, 0), tint = white });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2); mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
        }
        // 土地のマスの絵(崩れる演出を除く)。壁との境目や道の向きは、解析のたびに一度だけ決めて持つ。
        // 縁・道の絵は壁の部分が透けているので、下に壁を敷く(Backed)。
        private struct TerrainCell { public Sprite Tile, Overlay; public bool Path, Backed; public int Open; }
        private readonly Dictionary<long, TerrainCell> terrainCells = new Dictionary<long, TerrainCell>();
        private void BuildTerrainCells(MapArt art)
        {
            terrainCells.Clear();
            if (!art.HasTiles) return;
            foreach (long key in land)
            {
                int x = (int)(key >> 32), y = (int)(uint)key;
                if (art.HasPaths && pathMask.TryGetValue(key, out int open))
                { terrainCells[key] = new TerrainCell { Tile = art.PathPlain[open], Overlay = PathOverlay(key, open, art), Path = true, Backed = true, Open = open }; continue; }
                // 壁に接する辺の組み合わせ: 上=1 右=2 下=4 左=8。縁なしの時だけ、斜めが壁の角を調べる。
                int mask = (land.Contains(Key(x, y - 1)) ? 0 : 1) | (land.Contains(Key(x + 1, y)) ? 0 : 2)
                    | (land.Contains(Key(x, y + 1)) ? 0 : 4) | (land.Contains(Key(x - 1, y)) ? 0 : 8);
                Sprite sprite; bool backed = true;
                if (mask != 0) sprite = art.Border[mask];
                else
                {
                    int inner = !land.Contains(Key(x - 1, y - 1)) ? 0 : !land.Contains(Key(x + 1, y - 1)) ? 1
                        : !land.Contains(Key(x + 1, y + 1)) ? 2 : !land.Contains(Key(x - 1, y + 1)) ? 3 : -1;
                    if (inner >= 0 && inner < art.BorderInner.Length) sprite = art.BorderInner[inner];
                    else { sprite = art.Ground[MapLayout.CellHash(x, y) % art.Ground.Length]; backed = false; }
                }
                terrainCells[key] = new TerrainCell { Tile = sprite, Backed = backed };
            }
        }
        // 1 つの土地のマスの絵を加える。
        private void AddLandCell(List<SpriteDraw> sprites, List<SpriteDraw> overlays, MapArt art, long key, int x, int y)
        {
            if (!terrainCells.TryGetValue(key, out var cell)) return;
            var wall = art.Wall[MapLayout.CellHash(x, y) % art.Wall.Length];
            if (!cell.Path && collapsing.Count > 0 && collapsing.TryGetValue(key, out int started))
            {
                // 現れ始めるまでは、まだ壁のまま。現れる間は、土地の上に壁の横棒を重ね、1本ずつ消していく。
                if (frame < started) { AddTile(sprites, x, y, wall); return; }
                if (frame - started < art.Collapse.Length)
                {
                    if (cell.Backed) AddTile(sprites, x, y, wall);
                    AddTile(sprites, x, y, cell.Tile);
                    AddTile(overlays, x, y, art.Collapse[frame - started]);
                    return;
                }
            }
            if (cell.Backed) AddTile(sprites, x, y, wall);
            if (cell.Path && art.HasThinPaths)
            {
                // 道は半分の太さ: 中央の正方形と、開いている向きの腕だけを描く。
                const int a = MapArt.PathInset, c = MapArt.PathCore, edge = 32 - MapArt.PathInset;
                AddTilePiece(sprites, x, y, art.PathCenter, a, a, c, c);
                if ((cell.Open & 1) != 0) AddTilePiece(sprites, x, y, art.PathArm[0], a, 0, c, a);
                if ((cell.Open & 2) != 0) AddTilePiece(sprites, x, y, art.PathArm[1], edge, a, a, c);
                if ((cell.Open & 4) != 0) AddTilePiece(sprites, x, y, art.PathArm[2], a, edge, c, a);
                if ((cell.Open & 8) != 0) AddTilePiece(sprites, x, y, art.PathArm[3], 0, a, a, c);
                // 種別の重ね絵も、半分の大きさでマスの中央に置く。
                if (cell.Overlay != null) AddTilePiece(overlays, x, y, cell.Overlay, 8, 8, 16, 16);
                return;
            }
            AddTile(sprites, x, y, cell.Tile);
            if (cell.Overlay != null) AddTile(overlays, x, y, cell.Overlay);
        }
        // マスの一部分(左上からの位置と大きさを、マス = 32 として指定)に絵を置く。隣と辺をそろえるのは AddTile と同じ。
        private void AddTilePiece(List<SpriteDraw> sprites, int cx, int cy, Sprite sprite, float x, float y, float w, float h)
        {
            if (sprite == null) return;
            float t = MapLayout.Tile, k = t / 32f, left = cx * t - t / 2 + x * k, top = cy * t - t / 2 + y * k;
            var a = viewport.Screen(new MapPoint(left, top));
            var b = viewport.Screen(new MapPoint(left + w * k, top + h * k));
            var rect = Rect.MinMaxRect(Mathf.Round(a.X), Mathf.Round(a.Y), Mathf.Round(b.X), Mathf.Round(b.Y));
            if (rect.Overlaps(viewRect)) sprites.Add(new SpriteDraw(rect, sprite));
        }
        // 種別の重ね絵は、まっすぐ(縦5・横10)のマスだけに載せる。
        private Sprite PathOverlay(long key, int open, MapArt art)
        {
            if ((open != 5 && open != 10) || !pathKind.TryGetValue(key, out var kind)) return null;
            bool vertical = open == 5;
            if (vertical != (kind.Value == 0 || kind.Value == 2)) return null;
            Sprite[] frames = kind.Key == RoadKind.Premise ? art.OverlayPremise
                : kind.Key == RoadKind.Contrast ? art.OverlayContrast : art.OverlayExample;
            int index = kind.Key == RoadKind.Premise ? kind.Value : vertical ? 1 : 0;
            return index < frames.Length ? frames[index] : null;
        }
        // 奥行きの順に並べるための絵。Bottom は絵の下の辺の世界座標。
        // 同じ高さでは、Layer(自然物 < 花 < 旗 < 建物の順に手前)、次に同じテクスチャ同士でまとめる(メッシュの数を抑えるため)。
        private const int LayerNature = 0, LayerFlower = 1, LayerFlag = 2, LayerBuilding = 3;
        private readonly struct DepthSprite
        {
            public readonly float Bottom;
            public readonly int Layer, Texture, Order;
            public readonly SpriteDraw Draw;
            public DepthSprite(float bottom, int layer, int order, SpriteDraw draw)
            { Bottom = bottom; Layer = layer; Order = order; Draw = draw; Texture = draw.Sprite.texture.GetInstanceID(); }
        }
        private void AddFlowers(List<DepthSprite> objects, MapArt art)
        {
            if (snapshot == null) return;
            var one = new List<SpriteDraw>(1);
            foreach (var item in flowerDraws)
            {
                var flower = item.Flower; int emotion = (int)flower.Emotion;
                var frames = art.Flowers[emotion];
                var sprite = flower.Withered ? art.Withered[emotion] : frames.Length == 0 ? null : frames[(frame + item.Hash) % frames.Length];
                one.Clear();
                if (AddAnchored(one, item.X, item.Bottom, sprite)) objects.Add(new DepthSprite(item.Bottom, LayerFlower, objects.Count, one[0]));
            }
        }
        // 画面内の自然物だけを加える。拡大率が小さい時は描かない。
        private void AddNature(List<DepthSprite> objects, MapArt art)
        {
            if (natureItems.Count == 0 || viewport.Scale < NatureConfig.Settings.HideBelowScale) return;
            float tile = MapLayout.Tile;
            var topLeft = viewport.World(new MapPoint(0, 0)); var bottomRight = viewport.World(new MapPoint(viewRect.width, viewRect.height));
            float left = topLeft.X - tile * 2, right = bottomRight.X + tile * 2, top = topLeft.Y - tile * 2, bottom = bottomRight.Y + tile * 4;
            var one = new List<SpriteDraw>(1);
            foreach (var item in natureItems)
            {
                float x = item.X * tile, baseline = item.Y * tile + tile / 2;
                if (x < left || x > right || baseline < top || baseline > bottom) continue;
                one.Clear();
                if (AddAnchored(one, x, baseline, art.Nature[(int)item.Kind])) objects.Add(new DepthSprite(baseline, LayerNature, objects.Count, one[0]));
            }
        }
        private void AddFlags(List<DepthSprite> objects, List<Quad> quads, MapArt art)
        {
            if (snapshot == null) return;
            var one = new List<SpriteDraw>(1);
            foreach (var flag in flagDraws)
            {
                var frames = flag.Frames;
                if (frames.Length == 0)
                { var p = viewport.Screen(flag.At); AddQuad(quads, p.X - 5, p.Y - 5, 10, 10, new Color32(210, 230, 188, 255)); continue; }
                one.Clear();
                if (AddAnchored(one, flag.X, flag.Bottom, frames[frame % frames.Length])) objects.Add(new DepthSprite(flag.Bottom, LayerFlag, objects.Count, one[0]));
            }
        }
        // 固定サイズの建物は1枚絵、ビルは1階・中階(a と b を交互)・屋上を下から積む。
        private void AddBuildings(List<DepthSprite> objects, MapArt art)
        {
            var one = new List<SpriteDraw>(1);
            foreach (var p in placements)
            {
                float tile = MapLayout.Tile, x = (p.X + (p.Size.Width - 1) / 2f) * tile, bottom = (p.Y + p.Size.Depth - 1) * tile + tile / 2;
                float top = bottom, grow = 1f;
                // 建ち始めの建物は、下の辺を固定したまま伸びる。スクロールを待つ間は、まだ見せない。
                if (appearing.Count > 0 && appearing.TryGetValue(BuildingKey(p.Building), out int startedAt))
                { grow = BuildingAppearance.Grow(frame - startedAt); if (grow <= 0) continue; }
                void Part(Sprite sprite)
                {
                    if (sprite == null) return;
                    one.Clear();
                    if (AddAnchored(one, x, top, sprite, grow)) objects.Add(new DepthSprite(bottom, LayerBuilding, objects.Count, one[0]));
                    top -= sprite.rect.height * grow;
                }
                if (p.Size.Shape != BuildingShape.Tower) { Part(art.Fixed(p.Size.Shape)); continue; }
                var tower = art.TowerFor(p.Size.Width, MapLayout.StableHash(p.Building.File));
                if (tower == null) continue;
                Part(tower.Ground);
                for (int floor = 0; floor < p.Size.Floors; floor++) Part(floor % 2 == 0 ? tower.MidA : tower.MidB);
                Part(tower.Roof);
            }
        }
        private static void FlushOrdered(MeshGenerationContext context, List<DepthSprite> objects)
        {
            objects.Sort((a, b) => a.Bottom != b.Bottom ? a.Bottom.CompareTo(b.Bottom) : a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer)
                : a.Texture != b.Texture ? a.Texture.CompareTo(b.Texture) : a.Order.CompareTo(b.Order));
            var run = new List<SpriteDraw>();
            for (int i = 0; i < objects.Count; i++)
            {
                run.Add(objects[i].Draw);
                // 同じテクスチャが続く間だけ1つのメッシュにまとめる(順序を保つため、違う絵が挟まれば区切る)。
                if (i + 1 < objects.Count && objects[i + 1].Draw.Sprite.texture == objects[i].Draw.Sprite.texture) continue;
                FlushSprites(context, run); run.Clear();
            }
        }
        // 同じ絵(テクスチャ)のものをまとめて1つのメッシュにする。
        private static void FlushSprites(MeshGenerationContext context, List<SpriteDraw> draws)
        {
            const int batch = 12000;
            foreach (var group in draws.GroupBy(d => d.Sprite.texture))
            {
                var list = group.ToList(); var texture = group.Key; float tw = texture.width, th = texture.height;
                for (int offset = 0; offset < list.Count; offset += batch)
                {
                    int count = Math.Min(batch, list.Count - offset); var mesh = context.Allocate(count * 4, count * 6, texture);
                    for (int i = 0; i < count; i++)
                    {
                        var d = list[offset + i]; var t = d.Sprite.textureRect;
                        float u0 = t.xMin / tw, u1 = t.xMax / tw, v0 = t.yMin / th, v1 = t.yMax / th;
                        Color32 white = new Color32(255, 255, 255, 255);
                        mesh.SetNextVertex(new Vertex { position = new Vector3(d.Rect.xMin, d.Rect.yMin, Vertex.nearZ), uv = new Vector2(u0, v1), tint = white });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(d.Rect.xMax, d.Rect.yMin, Vertex.nearZ), uv = new Vector2(u1, v1), tint = white });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(d.Rect.xMax, d.Rect.yMax, Vertex.nearZ), uv = new Vector2(u1, v0), tint = white });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(d.Rect.xMin, d.Rect.yMax, Vertex.nearZ), uv = new Vector2(u0, v0), tint = white });
                        ushort start = (ushort)(i * 4);
                        mesh.SetNextIndex(start); mesh.SetNextIndex((ushort)(start + 1)); mesh.SetNextIndex((ushort)(start + 2));
                        mesh.SetNextIndex((ushort)(start + 2)); mesh.SetNextIndex((ushort)(start + 3)); mesh.SetNextIndex(start);
                    }
                }
            }
        }
        private static void Flush(MeshGenerationContext context, List<Quad> quads)
        {
            // Each mesh stays below the UInt16 vertex-index limit.
            const int batch = 12000;
            for (int offset = 0; offset < quads.Count; offset += batch)
            {
                int count = Math.Min(batch, quads.Count - offset); var mesh = context.Allocate(count * 4, count * 6);
                for (int i = 0; i < count; i++)
                {
                    var q = quads[offset + i];
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.Rect.xMin, q.Rect.yMin, Vertex.nearZ), tint = q.Color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.Rect.xMax, q.Rect.yMin, Vertex.nearZ), tint = q.Color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.Rect.xMax, q.Rect.yMax, Vertex.nearZ), tint = q.Color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.Rect.xMin, q.Rect.yMax, Vertex.nearZ), tint = q.Color });
                    ushort start = (ushort)(i * 4);
                    mesh.SetNextIndex(start); mesh.SetNextIndex((ushort)(start + 1)); mesh.SetNextIndex((ushort)(start + 2));
                    mesh.SetNextIndex((ushort)(start + 2)); mesh.SetNextIndex((ushort)(start + 3)); mesh.SetNextIndex(start);
                }
            }
        }
    }
}
