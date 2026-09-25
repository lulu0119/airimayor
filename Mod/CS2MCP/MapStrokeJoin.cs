using System;
using System.Collections.Generic;

namespace CS2MCP
{
    /// <summary>
    /// Assemble native edges into OSM ways: one polyline per carriageway
    /// (same style and grade, good continuation through shared native nodes).
    /// Width is paint. A bridge edge does not join a ground edge.
    /// </summary>
    internal static class MapStrokeJoin
    {
        private const double StraightDot = -0.5;

        public static List<MapStroke> Join(IReadOnlyList<MapStroke> strokes)
        {
            var result = new List<MapStroke>();
            if (strokes.Count == 0)
            {
                return result;
            }

            int[] parent = new int[strokes.Count];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }
            Dictionary<long, List<End>> nodes = CollectEnds(strokes);
            var adj = new List<int>[strokes.Count];
            for (int i = 0; i < adj.Length; i++)
            {
                adj[i] = new List<int>();
            }
            PairContinuations(strokes, nodes, parent, adj);

            var seen = new bool[strokes.Count];
            for (int i = 0; i < strokes.Count; i++)
            {
                if (seen[i] || strokes[i].X.Count < 2)
                {
                    continue;
                }
                int start = PathStart(adj, i);
                MapStroke joined = Copy(strokes[start]);
                seen[start] = true;
                int prev = -1;
                int cur = start;
                while (true)
                {
                    int next = Next(adj[cur], prev);
                    if (next < 0 || seen[next])
                    {
                        break;
                    }
                    Concat(joined, strokes[next]);
                    seen[next] = true;
                    prev = cur;
                    cur = next;
                }
                result.Add(joined);
            }

            AssignCaps(result);
            return result;
        }

        private sealed class End
        {
            public int Index;
            public bool AtStart;
            public double Tx;
            public double Ty;
        }

        private static Dictionary<long, List<End>> CollectEnds(IReadOnlyList<MapStroke> strokes)
        {
            var nodes = new Dictionary<long, List<End>>();
            for (int i = 0; i < strokes.Count; i++)
            {
                MapStroke stroke = strokes[i];
                if (stroke.X.Count < 2)
                {
                    continue;
                }
                AddEnd(nodes, i, stroke, true);
                AddEnd(nodes, i, stroke, false);
            }
            return nodes;
        }

        private static void AddEnd(Dictionary<long, List<End>> nodes, int index, MapStroke stroke, bool atStart)
        {
            long node = atStart ? stroke.StartNode : stroke.EndNode;
            if (node == 0)
            {
                return;
            }
            int at = atStart ? 0 : stroke.X.Count - 1;
            int next = atStart ? 1 : stroke.X.Count - 2;
            double dx = stroke.X[next] - stroke.X[at];
            double dy = stroke.Y[next] - stroke.Y[at];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (!nodes.TryGetValue(node, out List<End> ends))
            {
                ends = new List<End>();
                nodes[node] = ends;
            }
            ends.Add(new End
            {
                Index = index,
                AtStart = atStart,
                Tx = length > 0 ? dx / length : 0.0,
                Ty = length > 0 ? dy / length : 0.0,
            });
        }

        private static void PairContinuations(
            IReadOnlyList<MapStroke> strokes, Dictionary<long, List<End>> nodes, int[] parent, List<int>[] adj)
        {
            var used = new HashSet<int>();
            foreach (List<End> ends in nodes.Values)
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
                        End ea = ends[a];
                        End eb = ends[b];
                        if (ea.Index == eb.Index || !SameClass(strokes[ea.Index], strokes[eb.Index]))
                        {
                            continue;
                        }
                        double dot = ea.Tx * eb.Tx + ea.Ty * eb.Ty;
                        candidates.Add(new Candidate { Dot = dot, A = ea.Index, B = eb.Index });
                    }
                }
                candidates.Sort((left, right) => left.Dot.CompareTo(right.Dot));
                used.Clear();
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
                    if (rootA == rootB)
                    {
                        continue;
                    }
                    parent[rootB] = rootA;
                    used.Add(candidate.A);
                    used.Add(candidate.B);
                    adj[candidate.A].Add(candidate.B);
                    adj[candidate.B].Add(candidate.A);
                }
            }
        }

        private static bool SameClass(MapStroke a, MapStroke b)
        {
            return a.Style == b.Style && a.Grade == b.Grade;
        }

        private static int PathStart(List<int>[] adj, int seed)
        {
            int cur = seed;
            int prev = -1;
            var seen = new HashSet<int>();
            while (seen.Add(cur) && adj[cur].Count == 2)
            {
                int next = Next(adj[cur], prev);
                if (next < 0)
                {
                    break;
                }
                prev = cur;
                cur = next;
            }
            return adj[cur].Count <= 1 ? cur : seed;
        }

        private static int Next(List<int> neighbors, int prev)
        {
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i] != prev)
                {
                    return neighbors[i];
                }
            }
            return -1;
        }

        private static MapStroke Copy(MapStroke src)
        {
            var dest = new MapStroke
            {
                Style = src.Style,
                Grade = src.Grade,
                Layer = src.Layer,
                WidthM = src.WidthM,
                Elev = src.Elev,
                HasElev = src.HasElev,
                StartNode = src.StartNode,
                EndNode = src.EndNode,
                CapStart = src.CapStart,
                CapEnd = src.CapEnd,
            };
            dest.X.AddRange(src.X);
            dest.Y.AddRange(src.Y);
            return dest;
        }

        private static void Concat(MapStroke dest, MapStroke src)
        {
            int attach = AttachMode(dest, src);
            if (attach == 0 || attach == 1)
            {
                Append(dest, src, reverseSrc: attach == 1);
            }
            else
            {
                Reverse(dest);
                Append(dest, src, reverseSrc: attach == 3);
            }
            UpdateOuterNodes(dest, src, attach);
            dest.Layer = Math.Max(dest.Layer, src.Layer);
            dest.HasElev = dest.HasElev || src.HasElev;
            int count = dest.X.Count;
            int srcCount = src.X.Count;
            int prevCount = count - srcCount + 1;
            if (prevCount > 0)
            {
                dest.Elev = (dest.Elev * prevCount + src.Elev * srcCount) / Math.Max(1, count);
                dest.WidthM = (dest.WidthM * prevCount + src.WidthM * srcCount) / Math.Max(1, count);
            }
        }

        private static void UpdateOuterNodes(MapStroke dest, MapStroke src, int attach)
        {
            long destStart = dest.StartNode;
            long destEnd = dest.EndNode;
            long srcStart = src.StartNode;
            long srcEnd = src.EndNode;
            bool destReversed = attach == 2 || attach == 3;
            if (destReversed)
            {
                long swap = destStart;
                destStart = destEnd;
                destEnd = swap;
            }
            bool srcReversed = attach == 1 || attach == 3;
            if (srcReversed)
            {
                long swap = srcStart;
                srcStart = srcEnd;
                srcEnd = swap;
            }
            if (attach == 0 || attach == 1)
            {
                dest.StartNode = destStart;
                dest.EndNode = srcEnd;
                return;
            }
            dest.StartNode = srcStart;
            dest.EndNode = destEnd;
        }

        /// <summary>
        /// 0 dest-end to src-start, 1 dest-end to src-end,
        /// 2 dest-start to src-end, 3 dest-start to src-start.
        /// </summary>
        private static int AttachMode(MapStroke dest, MapStroke src)
        {
            int dLast = dest.X.Count - 1;
            int sLast = src.X.Count - 1;
            double[] d =
            {
                Dist(dest.X[dLast], dest.Y[dLast], src.X[0], src.Y[0]),
                Dist(dest.X[dLast], dest.Y[dLast], src.X[sLast], src.Y[sLast]),
                Dist(dest.X[0], dest.Y[0], src.X[sLast], src.Y[sLast]),
                Dist(dest.X[0], dest.Y[0], src.X[0], src.Y[0]),
            };
            int best = 0;
            for (int i = 1; i < 4; i++)
            {
                if (d[i] < d[best])
                {
                    best = i;
                }
            }
            return best;
        }

        private static double Dist(double x0, double y0, double x1, double y1)
        {
            double dx = x0 - x1;
            double dy = y0 - y1;
            return dx * dx + dy * dy;
        }

        private static void Append(MapStroke dest, MapStroke src, bool reverseSrc)
        {
            if (reverseSrc)
            {
                for (int i = src.X.Count - 2; i >= 0; i--)
                {
                    dest.X.Add(src.X[i]);
                    dest.Y.Add(src.Y[i]);
                }
                return;
            }
            for (int i = 1; i < src.X.Count; i++)
            {
                dest.X.Add(src.X[i]);
                dest.Y.Add(src.Y[i]);
            }
        }

        private static void Reverse(MapStroke stroke)
        {
            stroke.X.Reverse();
            stroke.Y.Reverse();
            long node = stroke.StartNode;
            stroke.StartNode = stroke.EndNode;
            stroke.EndNode = node;
            bool cap = stroke.CapStart;
            stroke.CapStart = stroke.CapEnd;
            stroke.CapEnd = cap;
        }

        private static void AssignCaps(List<MapStroke> strokes)
        {
            foreach (MapStroke stroke in strokes)
            {
                if (stroke.X.Count < 2)
                {
                    continue;
                }
                bool loop = stroke.StartNode != 0 && stroke.StartNode == stroke.EndNode;
                stroke.CapStart = !loop;
                stroke.CapEnd = !loop;
            }
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

        private sealed class Candidate
        {
            public double Dot;
            public int A;
            public int B;
        }
    }
}
