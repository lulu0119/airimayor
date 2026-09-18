using System;
using System.Collections.Generic;

namespace CS2MCP
{
    /// <summary>
    /// Integer layer assignment for roads (grade separation), so bridges and
    /// tunnels stack instead of flattening into false intersections.
    /// Adapted from HamsterPark/cs2-carto-citymap (MIT,
    /// src/cs2carto/layers.py, docs/road-layering.md): stroke union by good
    /// continuation, spatial-hash crossing detection, longest-path layer
    /// relaxation. Game-meter constants; overfull hash cells are skipped as
    /// a fuse. Features without elevation stay on the ground layer and never
    /// constrain. Pure math, no Unity API.
    /// </summary>
    internal static class MapLayering
    {
        private const int MaxLayer = 5;
        private const double SnapMeters = 0.7;
        private const double CellMeters = 65.0;
        private const double MinCrossingGapMeters = 1.0;
        private const double StraightDot = -0.5;
        private const double EndpointEps = 0.01;

        internal sealed class Feature
        {
            public List<double> X = new List<double>();
            public List<double> Y = new List<double>();
            public double Elev;
            public bool HasElev;
        }

        internal sealed class Merge
        {
            public int Feature;
            public int TopLayer;
            public double EndX;
            public double EndY;
            public double AdjX;
            public double AdjY;
        }

        internal sealed class Result
        {
            public int[] Layers = new int[0];
            public readonly List<Merge> Merges = new List<Merge>();
            public bool Converged = true;
            public int Constraints;
            public int SkippedCells;
            public int Segments;
            public long PairChecks;
        }

        public static Result ComputeLayers(List<Feature> features)
        {
            var result = new Result { Layers = new int[features.Count] };
            if (features.Count == 0)
            {
                return result;
            }

            Dictionary<long, List<EndPoint>> nodeEnds = CollectEnds(features, SnapMeters);
            int[] parent = new int[features.Count];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }
            BuildStrokes(features, nodeEnds, parent);
            HashSet<long> edges = CrossingConstraints(features, parent, CellMeters, result);

            var layer = new Dictionary<int, int>();
            var edgeList = new List<long>(edges);
            bool changed = false;
            for (int pass = 0; pass < MaxLayer + 2; pass++)
            {
                changed = false;
                foreach (long edge in edgeList)
                {
                    int lo = (int)(edge >> 32);
                    int hi = (int)(edge & 0xffffffffL);
                    int loLayer = layer.TryGetValue(lo, out int loValue) ? loValue : 0;
                    int hiLayer = layer.TryGetValue(hi, out int hiValue) ? hiValue : 0;
                    if (hiLayer < loLayer + 1)
                    {
                        layer[hi] = Math.Min(loLayer + 1, MaxLayer);
                        changed = true;
                    }
                }
                if (!changed)
                {
                    break;
                }
            }
            result.Converged = !changed;
            result.Constraints = edges.Count;
            for (int i = 0; i < features.Count; i++)
            {
                result.Layers[i] = layer.TryGetValue(Find(parent, i), out int value) ? value : 0;
            }

            foreach (List<EndPoint> ends in nodeEnds.Values)
            {
                if (ends.Count < 2)
                {
                    continue;
                }
                int top = int.MinValue;
                int bottom = int.MaxValue;
                foreach (EndPoint end in ends)
                {
                    int level = result.Layers[end.Feature];
                    if (level > top) top = level;
                    if (level < bottom) bottom = level;
                }
                if (bottom == top)
                {
                    continue;
                }
                foreach (EndPoint end in ends)
                {
                    if (result.Layers[end.Feature] < top)
                    {
                        result.Merges.Add(new Merge
                        {
                            Feature = end.Feature,
                            TopLayer = top,
                            EndX = end.PointX,
                            EndY = end.PointY,
                            AdjX = end.AdjacentX,
                            AdjY = end.AdjacentY,
                        });
                    }
                }
            }
            return result;
        }

        private sealed class EndPoint
        {
            public int Feature;
            public double TangentX;
            public double TangentY;
            public double PointX;
            public double PointY;
            public double AdjacentX;
            public double AdjacentY;
        }

        private static long NodeKey(double x, double y, double snap)
        {
            long gx = (long)Math.Round(x / snap);
            long gy = (long)Math.Round(y / snap);
            return (gx << 32) ^ (gy & 0xffffffffL);
        }

        private static Dictionary<long, List<EndPoint>> CollectEnds(List<Feature> features, double snap)
        {
            var nodeEnds = new Dictionary<long, List<EndPoint>>();
            for (int i = 0; i < features.Count; i++)
            {
                Feature feature = features[i];
                if (feature.X.Count < 2)
                {
                    continue;
                }
                int last = feature.X.Count - 1;
                AddEnd(nodeEnds, i, feature, 0, 1, snap);
                AddEnd(nodeEnds, i, feature, last, last - 1, snap);
            }
            return nodeEnds;
        }

        private static void AddEnd(
            Dictionary<long, List<EndPoint>> nodeEnds, int index, Feature feature, int at, int next,
            double snap)
        {
            double dx = feature.X[next] - feature.X[at];
            double dy = feature.Y[next] - feature.Y[at];
            double length = Math.Sqrt(dx * dx + dy * dy);
            long key = NodeKey(feature.X[at], feature.Y[at], snap);
            if (!nodeEnds.TryGetValue(key, out List<EndPoint> ends))
            {
                ends = new List<EndPoint>();
                nodeEnds[key] = ends;
            }
            ends.Add(new EndPoint
            {
                Feature = index,
                TangentX = length > 0 ? dx / length : 0.0,
                TangentY = length > 0 ? dy / length : 0.0,
                PointX = feature.X[at],
                PointY = feature.Y[at],
                AdjacentX = feature.X[next],
                AdjacentY = feature.Y[next],
            });
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private static void BuildStrokes(
            List<Feature> features, Dictionary<long, List<EndPoint>> nodeEnds, int[] parent)
        {
            foreach (List<EndPoint> ends in nodeEnds.Values)
            {
                if (ends.Count < 2)
                {
                    continue;
                }
                var candidates = new List<Candidate>();
                for (int a = 0; a < ends.Count; a++)
                {
                    for (int b = a + 1; b < ends.Count; b++)
                    {
                        if (ends[a].Feature == ends[b].Feature)
                        {
                            continue;
                        }
                        double dot = ends[a].TangentX * ends[b].TangentX
                            + ends[a].TangentY * ends[b].TangentY;
                        candidates.Add(new Candidate { Dot = dot, A = ends[a].Feature, B = ends[b].Feature });
                    }
                }
                candidates.Sort((left, right) => left.Dot.CompareTo(right.Dot));
                var used = new HashSet<int>();
                foreach (Candidate candidate in candidates)
                {
                    if (candidate.Dot > StraightDot)
                    {
                        break;
                    }
                    if (used.Contains(candidate.A) || used.Contains(candidate.B))
                    {
                        continue;
                    }
                    int rootA = Find(parent, candidate.A);
                    int rootB = Find(parent, candidate.B);
                    if (rootA != rootB)
                    {
                        parent[rootB] = rootA;
                    }
                    used.Add(candidate.A);
                    used.Add(candidate.B);
                }
            }
        }

        private sealed class Candidate
        {
            public double Dot;
            public int A;
            public int B;
        }

        /// <summary>
        /// Fuse against quadratic blowup: cells holding more segments are
        /// skipped and counted.
        /// </summary>
        private const int MaxSegmentsPerCell = 2000;

        private static HashSet<long> CrossingConstraints(
            List<Feature> features, int[] parent, double cell, Result result)
        {
            var segments = new List<Segment>();
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < features.Count; i++)
            {
                Feature feature = features[i];
                for (int k = 0; k + 1 < feature.X.Count; k++)
                {
                    double x0 = feature.X[k];
                    double y0 = feature.Y[k];
                    double x1 = feature.X[k + 1];
                    double y1 = feature.Y[k + 1];
                    int index = segments.Count;
                    segments.Add(new Segment { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Feature = i });
                    int steps = (int)(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) / cell) + 1;
                    for (int t = 0; t <= steps; t++)
                    {
                        double cx = (x0 + (x1 - x0) * t / steps) / cell;
                        double cy = (y0 + (y1 - y0) * t / steps) / cell;
                        long key = (((long)Math.Floor(cx)) << 32) ^ (((long)Math.Floor(cy)) & 0xffffffffL);
                        if (!grid.TryGetValue(key, out List<int> ids))
                        {
                            ids = new List<int>();
                            grid[key] = ids;
                        }
                        ids.Add(index);
                    }
                }
            }

            // No cross-cell dedup set: re-testing a pair shared by adjacent
            // cells only repeats a deterministic check, while the set itself
            // was the memory hog (millions of entries on dense exports).
            var edges = new HashSet<long>();
            result.Segments = segments.Count;
            foreach (List<int> ids in grid.Values)
            {
                if (ids.Count > MaxSegmentsPerCell)
                {
                    result.SkippedCells++;
                    continue;
                }
                for (int a = 0; a < ids.Count; a++)
                {
                for (int b = a + 1; b < ids.Count; b++)
                {
                    result.PairChecks++;
                    int ja = ids[a];
                    int jb = ids[b];
                        Segment sa = segments[ja];
                        Segment sb = segments[jb];
                        if (sa.Feature == sb.Feature || Find(parent, sa.Feature) == Find(parent, sb.Feature))
                        {
                            continue;
                        }
                        if (!SegmentsCross(sa, sb))
                        {
                            continue;
                        }
                        Feature fa = features[sa.Feature];
                        Feature fb = features[sb.Feature];
                        if (!fa.HasElev || !fb.HasElev || Math.Abs(fa.Elev - fb.Elev) < MinCrossingGapMeters)
                        {
                            continue;
                        }
                        int lo = fa.Elev < fb.Elev ? sa.Feature : sb.Feature;
                        int hi = fa.Elev < fb.Elev ? sb.Feature : sa.Feature;
                        long edge = (((long)Find(parent, lo)) << 32) | ((long)(uint)Find(parent, hi));
                        edges.Add(edge);
                    }
                }
            }
            return edges;
        }

        private sealed class Segment
        {
            public double X0;
            public double Y0;
            public double X1;
            public double Y1;
            public int Feature;
        }

        private static bool SegmentsCross(Segment a, Segment b)
        {
            double rx = a.X1 - a.X0;
            double ry = a.Y1 - a.Y0;
            double sx = b.X1 - b.X0;
            double sy = b.Y1 - b.Y0;
            double d = rx * sy - ry * sx;
            if (d == 0)
            {
                return false;
            }
            double t = ((b.X0 - a.X0) * sy - (b.Y0 - a.Y0) * sx) / d;
            double u = ((b.X0 - a.X0) * ry - (b.Y0 - a.Y0) * rx) / d;
            return t > EndpointEps && t < 1 - EndpointEps && u > EndpointEps && u < 1 - EndpointEps;
        }
    }
}
