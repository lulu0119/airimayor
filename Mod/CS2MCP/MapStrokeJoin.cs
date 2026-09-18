using System;
using System.Collections.Generic;

namespace CS2MCP
{
    /// <summary>
    /// Assemble native edges into OSM ways: one polyline per carriageway
    /// (same style, good continuation through shared native nodes).
    /// Elevation and width are paint, not reasons to break the line.
    /// </summary>
    internal static class MapStrokeJoin
    {
        private const double StraightDot = -0.5;

        internal sealed class Piece
        {
            public int Style;
            public int Layer;
            public double WidthM;
            public double Elev;
            public bool HasElev;
            public long StartNode;
            public long EndNode;
            public bool CapStart = true;
            public bool CapEnd = true;
            public List<double> X = new List<double>();
            public List<double> Y = new List<double>();
            public List<float> Rel = new List<float>();
        }

        public static List<Piece> Join(IReadOnlyList<Piece> pieces)
        {
            var result = new List<Piece>();
            if (pieces.Count == 0)
            {
                return result;
            }

            int[] parent = new int[pieces.Count];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }
            Dictionary<long, List<End>> nodes = CollectEnds(pieces);
            var adj = new List<int>[pieces.Count];
            for (int i = 0; i < adj.Length; i++)
            {
                adj[i] = new List<int>();
            }
            PairContinuations(pieces, nodes, parent, adj);

            var seen = new bool[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                if (seen[i] || pieces[i].X.Count < 2)
                {
                    continue;
                }
                int start = PathStart(adj, i);
                Piece joined = Copy(pieces[start]);
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
                    Concat(joined, pieces[next]);
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

        private static Dictionary<long, List<End>> CollectEnds(IReadOnlyList<Piece> pieces)
        {
            var nodes = new Dictionary<long, List<End>>();
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                if (piece.X.Count < 2)
                {
                    continue;
                }
                AddEnd(nodes, i, piece, true);
                AddEnd(nodes, i, piece, false);
            }
            return nodes;
        }

        private static void AddEnd(Dictionary<long, List<End>> nodes, int index, Piece piece, bool atStart)
        {
            long node = atStart ? piece.StartNode : piece.EndNode;
            if (node == 0)
            {
                return;
            }
            int at = atStart ? 0 : piece.X.Count - 1;
            int next = atStart ? 1 : piece.X.Count - 2;
            double dx = piece.X[next] - piece.X[at];
            double dy = piece.Y[next] - piece.Y[at];
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
            IReadOnlyList<Piece> pieces, Dictionary<long, List<End>> nodes, int[] parent, List<int>[] adj)
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
                        if (ea.Index == eb.Index || !SameClass(pieces[ea.Index], pieces[eb.Index]))
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

        private static bool SameClass(Piece a, Piece b)
        {
            return a.Style == b.Style;
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

        private static Piece Copy(Piece src)
        {
            return new Piece
            {
                Style = src.Style,
                Layer = src.Layer,
                WidthM = src.WidthM,
                Elev = src.Elev,
                HasElev = src.HasElev,
                StartNode = src.StartNode,
                EndNode = src.EndNode,
                CapStart = src.CapStart,
                CapEnd = src.CapEnd,
                X = new List<double>(src.X),
                Y = new List<double>(src.Y),
                Rel = new List<float>(src.Rel),
            };
        }

        private static void Concat(Piece dest, Piece src)
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

        private static void UpdateOuterNodes(Piece dest, Piece src, int attach)
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
        private static int AttachMode(Piece dest, Piece src)
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

        private static void Append(Piece dest, Piece src, bool reverseSrc)
        {
            if (reverseSrc)
            {
                for (int i = src.X.Count - 2; i >= 0; i--)
                {
                    dest.X.Add(src.X[i]);
                    dest.Y.Add(src.Y[i]);
                    if (i < src.Rel.Count)
                    {
                        dest.Rel.Add(src.Rel[i]);
                    }
                }
                return;
            }
            for (int i = 1; i < src.X.Count; i++)
            {
                dest.X.Add(src.X[i]);
                dest.Y.Add(src.Y[i]);
                if (i < src.Rel.Count)
                {
                    dest.Rel.Add(src.Rel[i]);
                }
            }
        }

        private static void Reverse(Piece piece)
        {
            piece.X.Reverse();
            piece.Y.Reverse();
            piece.Rel.Reverse();
            long node = piece.StartNode;
            piece.StartNode = piece.EndNode;
            piece.EndNode = node;
            bool cap = piece.CapStart;
            piece.CapStart = piece.CapEnd;
            piece.CapEnd = cap;
        }

        private static void AssignCaps(List<Piece> pieces)
        {
            foreach (Piece piece in pieces)
            {
                if (piece.X.Count < 2)
                {
                    continue;
                }
                bool loop = piece.StartNode != 0 && piece.StartNode == piece.EndNode;
                piece.CapStart = !loop;
                piece.CapEnd = !loop;
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
