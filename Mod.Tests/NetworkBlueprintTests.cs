using System.Collections.Generic;
using Unity.Mathematics;
using Xunit;

namespace CS2MCP
{
    internal sealed class FlatSiteSampler : INetworkSiteSampler
    {
        public float WaterX = float.NaN;

        public float TerrainHeight(float x, float z)
        {
            return 0f;
        }

        public float WaterDepth(float x, float z)
        {
            return !float.IsNaN(WaterX) && x > WaterX ? 2f : 0f;
        }

        public bool Owned(float x, float z)
        {
            return true;
        }
    }

    public sealed class NetworkBlueprintTests
    {
        [Fact]
        public void Straight_ground_road_is_ready_with_matching_endpoints()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("A", 0f, 0f));
            sketch.Anchors.Add(Free("B", 200f, 0f));
            sketch.Roads.Add(Road("r1", "SmallRoad", "A", "B"));

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Ready, result.Status);
            Assert.Single(result.Courses);
            ResolvedCourse course = result.Courses[0];
            Assert.Equal(0f, course.Path.A.x, 1);
            Assert.Equal(200f, course.Path.D.x, 1);
        }

        [Fact]
        public void Guide_bend_produces_a_curve_not_a_chord()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("A", 0f, 0f));
            sketch.Anchors.Add(Free("B", 200f, 0f));
            SketchRoad road = Road("r1", "SmallRoad", "A", "B");
            road.Via.Add(new SketchPoint(100f, 80f));
            sketch.Roads.Add(road);

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Ready, result.Status);
            float top = 0f;
            foreach (ResolvedCourse course in result.Courses)
            {
                foreach (float2 point in NetworkBlueprintPlanner.SampleCenter(course.Path))
                {
                    top = math.max(top, point.y);
                }
            }
            Assert.True(top > 20f);
        }

        [Fact]
        public void Through_point_is_honored()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("A", 0f, 0f));
            sketch.Anchors.Add(Free("B", 200f, 0f));
            SketchRoad road = Road("r1", "SmallRoad", "A", "B");
            road.Through.Add(new SketchPoint(100f, 60f));
            sketch.Roads.Add(road);

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Ready, result.Status);
            float best = float.MaxValue;
            foreach (ResolvedCourse course in result.Courses)
            {
                foreach (float2 point in NetworkBlueprintPlanner.SampleCenter(course.Path))
                {
                    best = math.min(best, math.distance(point, new float2(100f, 60f)));
                }
            }
            Assert.True(best <= 8f);
        }

        [Fact]
        public void Walled_corridor_is_unresolved_not_silent()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("A", 0f, 0f));
            sketch.Anchors.Add(Free("B", 200f, 0f));
            SketchRoad road = Road("r1", "SmallRoad", "A", "B");
            road.CorridorHalfWidth = 12f;
            sketch.Roads.Add(road);
            sketch.Blocked.Add(Rect(-10f, -60f, 210f, 60f));

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Unresolved, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Road == "r1");
        }

        [Fact]
        public void Undeclared_crossing_is_invalid()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("A", -100f, 0f));
            sketch.Anchors.Add(Free("B", 100f, 0f));
            sketch.Anchors.Add(Free("C", 0f, -100f));
            sketch.Anchors.Add(Free("D", 0f, 100f));
            sketch.Roads.Add(Road("h", "SmallRoad", "A", "B"));
            sketch.Roads.Add(Road("v", "SmallRoad", "C", "D"));

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Invalid, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Type == "crossing");
        }

        [Fact]
        public void Declared_over_crossing_with_heights_is_ready()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("A", -100f, 0f));
            sketch.Anchors.Add(Free("B", 100f, 0f));
            sketch.Anchors.Add(Free("C", 0f, -100f));
            sketch.Anchors.Add(Free("D", 0f, 100f));
            SketchRoad low = Road("low", "SmallRoad", "A", "B");
            SketchRoad high = Road("high", "SmallRoad", "C", "D");
            high.Mode = RoadBuildMode.GradeSeparated;
            high.E1 = 10f;
            high.E2 = 10f;
            high.Structures.Add("bridge");
            sketch.Roads.Add(low);
            sketch.Roads.Add(high);
            sketch.Crossings.Add(new SketchCrossing { A = "low", B = "high", Kind = "over", Upper = "high" });

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Ready, result.Status);
        }

        [Fact]
        public void Water_crossing_without_bridge_allowance_is_invalid()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("A", -100f, 0f));
            sketch.Anchors.Add(Free("B", 100f, 0f));
            SketchRoad road = Road("r1", "SmallRoad", "A", "B");
            road.Mode = RoadBuildMode.GradeSeparated;
            road.E1 = 8f;
            road.E2 = 8f;
            sketch.Roads.Add(road);

            var sampler = new FlatSiteSampler { WaterX = 0f };
            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, sampler);

            Assert.Equal(BlueprintStatus.Invalid, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Type == "structure");
        }

        [Fact]
        public void Ramp_on_mainline_orders_after_it()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("A", 0f, 0f));
            sketch.Anchors.Add(Free("B", 400f, 0f));
            sketch.Anchors.Add(Free("C", 200f, 200f));
            sketch.Anchors.Add(new SketchAnchor { Id = "M", Kind = SketchAnchorKind.SketchRoad, RoadId = "main", Split = 0.5f });
            sketch.Roads.Add(Road("main", "Highway", "A", "B"));
            sketch.Roads.Add(Road("ramp", "Ramp", "M", "C"));

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Ready, result.Status);
            Assert.True(result.BuildOrder.IndexOf("main#0") < result.BuildOrder.IndexOf("ramp#0"));
        }

        [Fact]
        public void Circular_geometry_dependency_is_invalid()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(new SketchAnchor { Id = "X", Kind = SketchAnchorKind.SketchRoad, RoadId = "r2", Split = 0.5f });
            sketch.Anchors.Add(new SketchAnchor { Id = "Y", Kind = SketchAnchorKind.SketchRoad, RoadId = "r1", Split = 0.5f });
            sketch.Anchors.Add(Free("P", 500f, 500f));
            sketch.Anchors.Add(Free("Q", -500f, -500f));
            sketch.Roads.Add(Road("r1", "SmallRoad", "X", "P"));
            sketch.Roads.Add(Road("r2", "SmallRoad", "Y", "Q"));

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Invalid, result.Status);
        }

        [Fact]
        public void Same_inputs_plan_identically()
        {
            BlueprintPlanResult first = PlanCurved();
            BlueprintPlanResult second = PlanCurved();

            Assert.Equal(BlueprintStatus.Ready, first.Status);
            Assert.Equal(BlueprintStatus.Ready, second.Status);
            Assert.Equal(first.Courses.Count, second.Courses.Count);
            for (int i = 0; i < first.Courses.Count; i++)
            {
                Assert.Equal(first.Courses[i].Path.A, second.Courses[i].Path.A);
                Assert.Equal(first.Courses[i].Path.D, second.Courses[i].Path.D);
            }
        }

        [Fact]
        public void Grid_district_grows_connected_streets()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("W1", -50f, -150f));
            sketch.Anchors.Add(Free("W2", -50f, 150f));
            sketch.Anchors.Add(Free("E1", 350f, -150f));
            sketch.Anchors.Add(Free("E2", 350f, 150f));
            sketch.Roads.Add(Road("west", "Collector", "W1", "W2"));
            sketch.Roads.Add(Road("east", "Collector", "E1", "E2"));
            var district = new SketchDistrict
            {
                Id = "d1",
                MinX = -100f,
                MinZ = -100f,
                MaxX = 400f,
                MaxZ = 100f,
                Field = "grid",
                AngleDeg = 0f,
                Spacing = 60f,
                StreetPrefab = "LocalStreet",
                MinExits = 2,
            };
            sketch.Districts.Add(district);

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Ready, result.Status);
            Assert.Contains(result.Courses, c => c.DistrictId == "d1");
        }

        [Fact]
        public void Field_shape_changes_the_layout()
        {
            int grid = PlanDistrictStreets("grid");
            int radial = PlanDistrictStreets("radial");
            int circular = PlanDistrictStreets("circular");

            Assert.True(grid > 0 && radial > 0 && circular > 0);
            Assert.NotEqual(grid, radial);
        }

        [Fact]
        public void Oversized_sketch_is_rejected_with_a_limit()
        {
            NetworkSketch sketch = Sketch();
            for (int i = 0; i < 65; i++)
            {
                sketch.Anchors.Add(Free("A" + i, i * 10f, 0f));
                sketch.Anchors.Add(Free("B" + i, i * 10f, 100f));
                sketch.Roads.Add(Road("r" + i, "SmallRoad", "A" + i, "B" + i));
            }

            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());

            Assert.Equal(BlueprintStatus.Invalid, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Type == "limit");
        }

        [Fact]
        public void Store_versions_and_resumes_runs()
        {
            NetworkBlueprintStore.ClearForTests();
            BlueprintPlanResult plan = PlanCurved();
            BlueprintRecord created = NetworkBlueprintStore.Create("hash1", "site1", plan);

            Assert.Equal(1, created.Version);
            BlueprintRecord revised = NetworkBlueprintStore.Revise(created.Id, "hash2", "site1", plan);
            Assert.Equal(2, revised.Version);
            Assert.Equal(revised, NetworkBlueprintStore.Get(created.Id, 0));

            BlueprintRunRecord run = NetworkBlueprintStore.GetOrCreateRun(revised);
            Assert.Same(run, NetworkBlueprintStore.GetOrCreateRun(revised));
            string first = NetworkBlueprintStore.NextReadyStep(revised, run);
            Assert.NotNull(first);
            NetworkBlueprintStore.MarkStepDone(run, first, 7, 1);
            foreach (BlueprintStepRecord step in run.Steps)
            {
                if (step.State == BlueprintStepState.Pending)
                {
                    NetworkBlueprintStore.MarkStepDone(run, step.CourseId, 7, 1);
                }
            }
            Assert.Equal(BlueprintRunStatus.Completed, run.Status);
            Assert.Null(NetworkBlueprintStore.NextReadyStep(revised, run));
            Assert.Same(run, NetworkBlueprintStore.GetOrCreateRun(revised));
            Assert.True(NetworkBlueprintStore.IsCurrent(revised, "site1"));
            Assert.False(NetworkBlueprintStore.IsCurrent(revised, "site2"));
        }

        private static BlueprintPlanResult PlanCurved()
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("A", 0f, 0f));
            sketch.Anchors.Add(Free("B", 200f, 0f));
            SketchRoad road = Road("r1", "SmallRoad", "A", "B");
            road.Via.Add(new SketchPoint(100f, 80f));
            sketch.Roads.Add(road);
            return NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());
        }

        private static int PlanDistrictStreets(string field)
        {
            NetworkSketch sketch = Sketch();
            sketch.Anchors.Add(Free("W1", -50f, -150f));
            sketch.Anchors.Add(Free("W2", -50f, 150f));
            sketch.Anchors.Add(Free("E1", 350f, -150f));
            sketch.Anchors.Add(Free("E2", 350f, 150f));
            sketch.Roads.Add(Road("west", "Collector", "W1", "W2"));
            sketch.Roads.Add(Road("east", "Collector", "E1", "E2"));
            var district = new SketchDistrict
            {
                Id = "d1",
                MinX = -100f,
                MinZ = -100f,
                MaxX = 400f,
                MaxZ = 100f,
                Field = field,
                AngleDeg = 0f,
                CenterX = 150f,
                CenterZ = 0f,
                Spacing = 60f,
                StreetPrefab = "LocalStreet",
                MinExits = 2,
                AllowDeadEnds = true,
            };
            sketch.Districts.Add(district);
            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, new FlatSiteSampler());
            Assert.Equal(BlueprintStatus.Ready, result.Status);
            return result.Courses.FindAll(c => c.DistrictId == "d1").Count;
        }

        private static NetworkSketch Sketch()
        {
            return new NetworkSketch
            {
                AreaMinX = -600f,
                AreaMinZ = -600f,
                AreaMaxX = 600f,
                AreaMaxZ = 600f,
            };
        }

        private static SketchAnchor Free(string id, float x, float z)
        {
            return new SketchAnchor { Id = id, Kind = SketchAnchorKind.Free, X = x, Z = z };
        }

        private static SketchRoad Road(string id, string prefab, string from, string to)
        {
            return new SketchRoad { Id = id, Prefab = prefab, From = from, To = to };
        }

        private static SketchBlockedRect Rect(float x1, float z1, float x2, float z2)
        {
            return new SketchBlockedRect { MinX = x1, MinZ = z1, MaxX = x2, MaxZ = z2 };
        }
    }
}
