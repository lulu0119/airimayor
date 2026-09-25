using System.Collections.Generic;
using Xunit;

namespace CS2MCP
{
    public sealed class MapStrokeJoinTests
    {
        [Fact]
        public void Collinear_same_class_edges_become_one_way()
        {
            List<MapStroke> joined = MapStrokeJoin.Join(new[]
            {
                Line(0, 0, 10, 0, startNode: 100, endNode: 200),
                Line(10, 0, 20, 0, startNode: 200, endNode: 300),
            });

            Assert.Single(joined);
            Assert.Equal(new[] { 0.0, 10.0, 20.0 }, joined[0].X);
        }

        [Fact]
        public void Ground_and_bridge_edges_stay_apart()
        {
            var ground = Line(0, 0, 10, 0, startNode: 100, endNode: 200);
            ground.Grade = MapGrade.Ground;
            var bridge = Line(10, 0, 20, 0, startNode: 200, endNode: 300);
            bridge.Grade = MapGrade.Bridge;
            List<MapStroke> joined = MapStrokeJoin.Join(new[] { ground, bridge });

            Assert.Equal(2, joined.Count);
        }

        [Fact]
        public void Reversed_second_edge_still_concatenates()
        {
            List<MapStroke> joined = MapStrokeJoin.Join(new[]
            {
                Line(0, 0, 10, 0, startNode: 100, endNode: 200),
                Line(20, 0, 10, 0, startNode: 300, endNode: 200),
            });

            Assert.Single(joined);
            Assert.Equal(3, joined[0].X.Count);
            Assert.Equal(0.0, joined[0].X[0]);
            Assert.Equal(20.0, joined[0].X[joined[0].X.Count - 1]);
        }

        [Fact]
        public void Three_edge_chain_keeps_every_vertex()
        {
            List<MapStroke> joined = MapStrokeJoin.Join(new[]
            {
                Line(0, 0, 10, 0, startNode: 100, endNode: 200),
                Line(10, 0, 20, 0, startNode: 200, endNode: 300),
                Line(20, 0, 30, 0, startNode: 300, endNode: 400),
            });

            Assert.Single(joined);
            Assert.Equal(new[] { 0.0, 10.0, 20.0, 30.0 }, joined[0].X);
        }

        [Fact]
        public void Through_pair_at_a_T_joins_the_branch_stays()
        {
            List<MapStroke> joined = MapStrokeJoin.Join(new[]
            {
                Line(0, 0, 10, 0, startNode: 100, endNode: 200),
                Line(10, 0, 20, 0, startNode: 200, endNode: 300),
                Line(10, 0, 10, 10, startNode: 200, endNode: 400),
            });

            Assert.Equal(2, joined.Count);
            Assert.Contains(joined, piece => piece.X.Count == 3);
            Assert.Contains(joined, piece => piece.X.Count == 2);
        }

        [Fact]
        public void Width_change_with_offset_ends_stays_one_way()
        {
            var narrow = Line(0, 0, 10, 0, startNode: 100, endNode: 200);
            narrow.WidthM = 12;
            var wide = Line(12, 0, 20, 0, startNode: 200, endNode: 300);
            wide.WidthM = 16;
            List<MapStroke> joined = MapStrokeJoin.Join(new[] { narrow, wide });

            Assert.Single(joined);
        }

        [Fact]
        public void Opposite_carriageways_with_different_nodes_stay_apart()
        {
            var north = Line(530.1, -369.0, 531.6, -370.4, startNode: 100, endNode: 200);
            var south = Line(513.7, -354.4, 515.2, -355.7, startNode: 300, endNode: 400);
            List<MapStroke> joined = MapStrokeJoin.Join(new[] { north, south });

            Assert.Equal(2, joined.Count);
        }

        [Fact]
        public void Different_style_does_not_merge()
        {
            var a = Line(0, 0, 10, 0, startNode: 100, endNode: 200);
            a.Style = MapStrokeStyle.Highway;
            var b = Line(10, 0, 20, 0, startNode: 200, endNode: 300);
            b.Style = MapStrokeStyle.Metro;
            List<MapStroke> joined = MapStrokeJoin.Join(new[] { a, b });
            Assert.Equal(2, joined.Count);
        }

        private static MapStroke Line(
            double x0, double y0, double x1, double y1,
            long startNode = 0, long endNode = 0)
        {
            var line = new MapStroke
            {
                Style = MapStrokeStyle.Highway,
                WidthM = 12,
                HasElev = true,
                StartNode = startNode,
                EndNode = endNode,
            };
            line.X.Add(x0);
            line.X.Add(x1);
            line.Y.Add(y0);
            line.Y.Add(y1);
            return line;
        }
    }
}
