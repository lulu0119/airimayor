using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CS2MCP
{
    public sealed partial class RequestHandlers
    {
        private const int kBlueprintDiagnosticCap = 32;

        private sealed class NativeSiteSampler : INetworkSiteSampler
        {
            private TerrainHeightData m_Heights;
            private WaterSurfaceData<SurfaceWater> m_Water;
            private List<OwnedMapBounds> m_Owned;

            public NativeSiteSampler(
                TerrainHeightData heights,
                WaterSurfaceData<SurfaceWater> water,
                List<OwnedMapBounds> owned)
            {
                m_Heights = heights;
                m_Water = water;
                m_Owned = owned;
            }

            public float TerrainHeight(float x, float z)
            {
                return TerrainUtils.SampleHeight(ref m_Heights, new float3(x, 0f, z));
            }

            public float WaterDepth(float x, float z)
            {
                float depth;
                WaterUtils.SampleHeight(ref m_Water, ref m_Heights, new float3(x, 0f, z), out depth);
                return depth;
            }

            public bool Owned(float x, float z)
            {
                return IsInsideOwnedMapBounds(x, z, m_Owned);
            }
        }

        private BridgeResponse PlanNetwork(BridgeRequest request)
        {
            if (!TryGetCity(out Entity city, out BridgeResponse cityError))
            {
                return cityError;
            }
            NetworkSketch sketch;
            string reviseId;
            try
            {
                JObject input = JObject.Parse(request.Body);
                reviseId = ReadOptionalSketchId(input);
                sketch = ParseNetworkSketch(input);
            }
            catch (JsonException e)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, e.Message);
            }
            string fingerprint = FingerprintEnvironment(city, sketch);
            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heights = terrain.GetHeightData();
            WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
            WaterSurfaceData<SurfaceWater> surface = water.GetSurfaceData(out JobHandle dependencies);
            dependencies.Complete();
            var sampler = new NativeSiteSampler(heights, surface, ReadOwnedMapBounds());
            BlueprintPlanResult result = NetworkBlueprintPlanner.Plan(sketch, sampler);
            BlueprintRecord record = string.IsNullOrEmpty(reviseId)
                ? NetworkBlueprintStore.Create(NetworkBlueprintStore.HashSketch(request.Body), fingerprint, sketch, result)
                : NetworkBlueprintStore.Revise(reviseId, NetworkBlueprintStore.HashSketch(request.Body), fingerprint, sketch, result);
            var diagnostics = new List<object>();
            foreach (BlueprintDiagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostics.Count >= kBlueprintDiagnosticCap)
                {
                    break;
                }
                diagnostics.Add(new
                {
                    road = diagnostic.Road,
                    type = diagnostic.Type,
                    at = diagnostic.HasAt ? new { x = diagnostic.AtX, z = diagnostic.AtZ } : null,
                    observed = diagnostic.HasAt ? (float?)diagnostic.Observed : null,
                    allowed = diagnostic.HasAt ? (float?)diagnostic.Allowed : null,
                    message = diagnostic.Message,
                });
            }
            float minX = sketch.AreaMinX;
            float minZ = sketch.AreaMinZ;
            float maxX = sketch.AreaMaxX;
            float maxZ = sketch.AreaMaxZ;
            foreach (ResolvedCourse course in result.Courses)
            {
                minX = math.min(minX, math.min(course.Path.A.x, course.Path.D.x));
                minZ = math.min(minZ, math.min(course.Path.A.z, course.Path.D.z));
                maxX = math.max(maxX, math.max(course.Path.A.x, course.Path.D.x));
                maxZ = math.max(maxZ, math.max(course.Path.A.z, course.Path.D.z));
            }
            return BridgeResponse.Json(new
            {
                blueprint = record.Id,
                version = record.Version,
                status = record.Status,
                courses = result.Courses.Count,
                roads = result.RoadCount,
                totalLengthM = (float)Math.Round(result.TotalLength, 1),
                diagnostics,
                preview = new { xMin = minX, zMin = minZ, xMax = maxX, zMax = maxZ },
                note = result.Status == BlueprintStatus.Ready
                    ? "review this revision on map_image with blueprint and version, then build it with build_network; revise the sketch instead of editing roads one by one"
                    : "resolve every diagnostic by revising the sketch and planning again; nothing was built",
            });
        }

        private BridgeResponse InspectNetworkPlan(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse cityError))
            {
                return cityError;
            }
            if (!request.Query.TryGetValue("blueprint", out string blueprintId) || string.IsNullOrEmpty(blueprintId))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?blueprint=<id from plan_network>");
            }
            int version = request.TryGetInt("version", out int rawVersion) ? rawVersion : 0;
            BlueprintRecord record = NetworkBlueprintStore.Get(blueprintId, version);
            if (record == null)
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound, "unknown blueprint revision; plan the sketch again");
            }
            request.Query.TryGetValue("course", out string courseFilter);
            bool fullGeometry = request.TryGetBool("geometry", out bool geometry) && geometry;
            var courses = new List<object>();
            foreach (ResolvedCourse course in record.Courses)
            {
                if (!string.IsNullOrEmpty(courseFilter) && course.Id != courseFilter && course.RoadId != courseFilter)
                {
                    continue;
                }
                courses.Add(new
                {
                    id = course.Id,
                    road = course.RoadId,
                    prefab = course.Prefab,
                    mode = course.Mode == RoadBuildMode.Ground ? "ground" : "grade-separated",
                    district = course.DistrictId,
                    lengthM = (float)Math.Round(course.Length, 1),
                    start = CoursePoint(course.Path.A),
                    end = CoursePoint(course.Path.D),
                    points = fullGeometry ? CoursePoints(course) : null,
                    startAnchor = AnchorRef(course.Start),
                    endAnchor = AnchorRef(course.End),
                });
                if (courses.Count >= 64)
                {
                    break;
                }
            }
            var diagnostics = new List<object>();
            foreach (BlueprintDiagnostic diagnostic in record.Diagnostics)
            {
                if (diagnostics.Count >= kBlueprintDiagnosticCap)
                {
                    break;
                }
                diagnostics.Add(new
                {
                    road = diagnostic.Road,
                    type = diagnostic.Type,
                    at = diagnostic.HasAt ? new { x = diagnostic.AtX, z = diagnostic.AtZ } : null,
                    message = diagnostic.Message,
                });
            }
            object run = null;
            if (request.Query.TryGetValue("run", out string runId) && !string.IsNullOrEmpty(runId))
            {
                BlueprintRunRecord found = NetworkBlueprintStore.GetRun(runId);
                if (found == null || found.BlueprintId != record.Id)
                {
                    return BridgeResponse.Error(BridgeErrorKind.NotFound, "unknown build run for this blueprint");
                }
                int done = 0;
                int blocked = 0;
                var steps = new List<object>();
                foreach (BlueprintStepRecord step in found.Steps)
                {
                    if (step.State == BlueprintStepState.Done)
                    {
                        done++;
                    }
                    if (step.State == BlueprintStepState.Blocked)
                    {
                        blocked++;
                    }
                    steps.Add(new
                    {
                        course = step.CourseId,
                        state = step.State.ToString().ToLowerInvariant(),
                        applied = step.AppliedIndex >= 0
                            ? new { index = step.AppliedIndex, version = step.AppliedVersion }
                            : null,
                        note = step.Note,
                    });
                }
                run = new
                {
                    id = found.RunId,
                    status = found.Status.ToString().ToLowerInvariant(),
                    done,
                    blocked,
                    total = found.Steps.Count,
                    steps,
                };
            }
            return BridgeResponse.Json(new
            {
                blueprint = record.Id,
                version = record.Version,
                status = record.Status,
                courses = record.Courses.Count,
                roads = record.RoadCount,
                totalLengthM = (float)Math.Round(record.TotalLength, 1),
                listed = courses.Count,
                courseList = courses,
                diagnostics,
                run,
            });
        }

        private static object CoursePoint(float3 point)
        {
            return new { x = point.x, y = point.y, z = point.z };
        }

        private static object CoursePoints(ResolvedCourse course)
        {
            List<float2> center = NetworkBlueprintPlanner.SampleCenter(course.Path);
            var points = new List<object>(center.Count);
            for (int i = 0; i < center.Count; i++)
            {
                float t = center.Count > 1 ? i / (float)(center.Count - 1) : 0f;
                points.Add(new
                {
                    x = center[i].x,
                    y = NetworkBlueprintPlanner.CubicHeight(course.Path, t),
                    z = center[i].y,
                });
            }
            return points.ToArray();
        }

        private static object AnchorRef(BlueprintAnchorRef anchor)
        {
            if (anchor.Kind == BlueprintAnchorKind.Existing)
            {
                return new { kind = "existing", index = anchor.EntityIndex, version = anchor.EntityVersion, split = anchor.Split };
            }
            if (anchor.Kind == BlueprintAnchorKind.Blueprint)
            {
                return new { kind = "blueprint", course = anchor.CourseId, split = anchor.Split };
            }
            return null;
        }

        private string FingerprintEnvironment(Entity city, NetworkSketch sketch)
        {
            return FingerprintArea(city, sketch.AreaMinX, sketch.AreaMinZ, sketch.AreaMaxX, sketch.AreaMaxZ);
        }

        private string FingerprintArea(Entity city, float minX, float minZ, float maxX, float maxZ)
        {
            var text = new StringBuilder();
            text.Append(city.Index).Append(':').Append(city.Version).Append(';');
            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heights = terrain.GetHeightData();
            for (int row = 0; row < 12; row++)
            {
                for (int col = 0; col < 12; col++)
                {
                    float x = minX + (maxX - minX) * (col + 0.5f) / 12f;
                    float z = minZ + (maxZ - minZ) * (row + 0.5f) / 12f;
                    text.Append(((int)(TerrainUtils.SampleHeight(ref heights, new float3(x, 0f, z)) * 2f)).ToString()).Append(',');
                }
            }
            text.Append(';');
            using (Unity.Collections.NativeArray<Entity> tiles = MapTileQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                text.Append(tiles.Length).Append(';');
                foreach (Entity tile in tiles)
                {
                    if (EntityManager.HasComponent<Game.Common.Native>(tile)
                        || EntityManager.HasComponent<Game.Common.Deleted>(tile)
                        || !EntityManager.HasComponent<Game.Areas.Geometry>(tile))
                    {
                        continue;
                    }
                    Game.Areas.Geometry geometry = EntityManager.GetComponentData<Game.Areas.Geometry>(tile);
                    text.Append(((int)geometry.m_Bounds.min.x).ToString()).Append(',')
                        .Append(((int)geometry.m_Bounds.min.z).ToString()).Append(';');
                }
            }
            return NetworkBlueprintStore.HashSketch(text.ToString());
        }

        private BridgeResponse CheckBlueprintFresh(Entity city, BlueprintRecord record)
        {
            if (record.SiteFingerprint != FingerprintArea(city, record.AreaMinX, record.AreaMinZ, record.AreaMaxX, record.AreaMaxZ))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    "the site changed since this revision was reviewed (roads, land or terrain); revise the sketch and review again");
            }
            foreach (BlueprintAnchorIdentity anchor in record.ExistingAnchors)
            {
                var entity = new Entity { Index = anchor.Index, Version = anchor.Version };
                if (!EntityManager.Exists(entity)
                    || EntityManager.HasComponent<Game.Common.Deleted>(entity)
                    || EntityManager.HasComponent<Game.Tools.Temp>(entity))
                {
                    return BridgeResponse.Error(BridgeErrorKind.Conflict,
                        "a reviewed connection changed; read the site, revise the sketch and review again");
                }
            }
            foreach (string prefabName in record.PrefabNames)
            {
                if (!TryFindPrefabByName(NetPrefabQuery, prefabName, out Entity prefabEntity, out PrefabBase _)
                    || !EntityManager.HasComponent<RoadData>(prefabEntity)
                    || IsLocked(prefabEntity))
                {
                    return BridgeResponse.Error(BridgeErrorKind.Conflict,
                        "road prefab '" + prefabName + "' is unavailable or locked; revise the sketch");
                }
            }
            return null;
        }

        private static string ReadOptionalSketchId(JObject input)
        {
            foreach (JProperty property in input.Properties())
            {
                if (property.Name != "blueprint" && property.Name != "area" && property.Name != "anchors"
                    && property.Name != "roads" && property.Name != "crossings" && property.Name != "districts"
                    && property.Name != "blocked")
                {
                    throw new JsonException("unknown sketch field: " + property.Name);
                }
            }
            JToken id = input["blueprint"];
            if (id == null || id.Type == JTokenType.Null)
            {
                return null;
            }
            if (id.Type != JTokenType.String)
            {
                throw new JsonException("blueprint must be a revision id string");
            }
            return id.Value<string>();
        }

        private NetworkSketch ParseNetworkSketch(JObject input)
        {
            var sketch = new NetworkSketch();
            JToken area = input["area"];
            if (area == null)
            {
                throw new JsonException("sketch needs area{xMin,zMin,xMax,zMax}");
            }
            if (!(area is JObject areaObject))
            {
                throw new JsonException("area must be an object");
            }
            sketch.AreaMinX = ReadBound(areaObject, "xMin");
            sketch.AreaMinZ = ReadBound(areaObject, "zMin");
            sketch.AreaMaxX = ReadBound(areaObject, "xMax");
            sketch.AreaMaxZ = ReadBound(areaObject, "zMax");
            if (input["anchors"] is JArray anchors)
            {
                foreach (JToken token in anchors)
                {
                    sketch.Anchors.Add(ParseAnchor(token));
                }
            }
            if (input["roads"] is JArray roads)
            {
                foreach (JToken token in roads)
                {
                    sketch.Roads.Add(ParseSketchRoad(token));
                }
            }
            if (input["crossings"] is JArray crossings)
            {
                foreach (JToken token in crossings)
                {
                    sketch.Crossings.Add(ParseCrossing(token));
                }
            }
            if (input["districts"] is JArray districts)
            {
                foreach (JToken token in districts)
                {
                    sketch.Districts.Add(ParseDistrict(token));
                }
            }
            if (input["blocked"] is JArray blocked)
            {
                foreach (JToken token in blocked)
                {
                    if (!(token is JObject rect))
                    {
                        throw new JsonException("blocked entries must be rect objects");
                    }
                    sketch.Blocked.Add(new SketchBlockedRect
                    {
                        MinX = ReadBound(rect, "xMin"),
                        MinZ = ReadBound(rect, "zMin"),
                        MaxX = ReadBound(rect, "xMax"),
                        MaxZ = ReadBound(rect, "zMax"),
                    });
                }
            }
            return sketch;
        }

        private static float ReadBound(JObject rect, string name)
        {
            JToken value = rect[name];
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float))
            {
                throw new JsonException("rect needs numeric " + name);
            }
            float bound = value.Value<float>();
            if (!math.isfinite(bound) || math.abs(bound) > kMapHalfSize)
            {
                throw new JsonException(name + " must be a finite world coordinate inside the map");
            }
            return bound;
        }

        private SketchAnchor ParseAnchor(JToken token)
        {
            if (!(token is JObject anchor))
            {
                throw new JsonException("anchors must be objects with id");
            }
            JToken id = anchor["id"];
            if (id == null || id.Type != JTokenType.String || string.IsNullOrEmpty(id.Value<string>()))
            {
                throw new JsonException("every anchor needs a string id");
            }
            var parsed = new SketchAnchor { Id = id.Value<string>() };
            int kinds = 0;
            if (anchor["node"] != null)
            {
                kinds++;
                JObject node = RequireObject(anchor["node"], "node must be {index,version}");
                Entity entity = RequireEntity(node, "node");
                if (!EntityManager.Exists(entity)
                    || EntityManager.HasComponent<Game.Common.Deleted>(entity)
                    || EntityManager.HasComponent<Game.Tools.Temp>(entity)
                    || !EntityManager.HasComponent<Game.Net.Node>(entity))
                {
                    throw new JsonException("anchor '" + parsed.Id + "' is not a current road node; read the site again");
                }
                float3 position = EntityManager.GetComponentData<Game.Net.Node>(entity).m_Position;
                parsed.Kind = SketchAnchorKind.ExistingNode;
                parsed.EntityIndex = entity.Index;
                parsed.EntityVersion = entity.Version;
                parsed.X = position.x;
                parsed.Z = position.z;
            }
            if (anchor["edge"] != null)
            {
                kinds++;
                JObject edge = RequireObject(anchor["edge"], "edge must be {index,version}");
                Entity entity = RequireEntity(edge, "edge");
                float split = 0.5f;
                if (anchor["split"] != null)
                {
                    split = RequireFinite(anchor["split"], "split must be a number in 0..1");
                    if (split < 0f || split > 1f)
                    {
                        throw new JsonException("split must be in 0..1");
                    }
                }
                if (!EntityManager.Exists(entity)
                    || EntityManager.HasComponent<Game.Common.Deleted>(entity)
                    || EntityManager.HasComponent<Game.Tools.Temp>(entity)
                    || !EntityManager.HasComponent<Game.Net.Edge>(entity)
                    || !EntityManager.HasComponent<Game.Net.Curve>(entity))
                {
                    throw new JsonException("anchor '" + parsed.Id + "' is not a current road edge; read the site again");
                }
                Bezier4x3 curve = EntityManager.GetComponentData<Game.Net.Curve>(entity).m_Bezier;
                float3 position = Colossal.Mathematics.MathUtils.Position(curve, split);
                float3 tangent = Colossal.Mathematics.MathUtils.Tangent(curve, split);
                parsed.Kind = SketchAnchorKind.ExistingEdge;
                parsed.EntityIndex = entity.Index;
                parsed.EntityVersion = entity.Version;
                parsed.Split = split;
                parsed.X = position.x;
                parsed.Z = position.z;
                if (math.lengthsq(tangent.xz) > 0.0001f)
                {
                    parsed.HasTangent = true;
                    parsed.TangentX = tangent.x;
                    parsed.TangentZ = tangent.z;
                }
            }
            if (anchor["point"] != null)
            {
                kinds++;
                JObject point = RequireObject(anchor["point"], "point must be {x,z}");
                parsed.Kind = SketchAnchorKind.Free;
                parsed.X = RequireCoordinate(point["x"], "point.x");
                parsed.Z = RequireCoordinate(point["z"], "point.z");
            }
            if (anchor["road"] != null)
            {
                kinds++;
                if (anchor["road"].Type != JTokenType.String || string.IsNullOrEmpty(anchor["road"].Value<string>()))
                {
                    throw new JsonException("road anchors need a road id string");
                }
                parsed.Kind = SketchAnchorKind.SketchRoad;
                parsed.RoadId = anchor["road"].Value<string>();
                parsed.Split = anchor["split"] != null ? RequireFinite(anchor["split"], "split must be a number in 0..1") : 0.5f;
                if (parsed.Split < 0f || parsed.Split > 1f)
                {
                    throw new JsonException("split must be in 0..1");
                }
            }
            if (kinds != 1)
            {
                throw new JsonException("anchor '" + parsed.Id + "' needs exactly one of node, edge, point or road");
            }
            return parsed;
        }

        private SketchRoad ParseSketchRoad(JToken token)
        {
            if (!(token is JObject road))
            {
                throw new JsonException("roads must be objects with id, prefab, from and to");
            }
            foreach (JProperty property in road.Properties())
            {
                if (property.Name != "id" && property.Name != "prefab" && property.Name != "from" && property.Name != "to"
                    && property.Name != "via" && property.Name != "through" && property.Name != "corridor"
                    && property.Name != "mode" && property.Name != "e1" && property.Name != "e2"
                    && property.Name != "clearance" && property.Name != "structures"
                    && property.Name != "maxGrade" && property.Name != "minCurveRadius")
                {
                    throw new JsonException("unknown road field: " + property.Name);
                }
            }
            var parsed = new SketchRoad
            {
                Id = RequireId(road["id"]),
                Prefab = RequirePrefab(road["prefab"]),
                From = RequireId(road["from"]),
                To = RequireId(road["to"]),
            };
            if (road["via"] is JArray via)
            {
                foreach (JToken point in via)
                {
                    parsed.Via.Add(ParseSketchPoint(point, "via points must be {x,z}"));
                }
            }
            if (road["through"] is JArray through)
            {
                foreach (JToken point in through)
                {
                    parsed.Through.Add(ParseSketchPoint(point, "through points must be {x,z}"));
                }
            }
            if (road["corridor"] != null)
            {
                parsed.CorridorHalfWidth = RequireFinite(road["corridor"], "corridor must be a positive half width in meters");
            }
            string mode = road["mode"] == null ? "ground" : road["mode"].Value<string>();
            if (mode == "ground")
            {
                parsed.Mode = RoadBuildMode.Ground;
            }
            else if (mode == "grade-separated")
            {
                parsed.Mode = RoadBuildMode.GradeSeparated;
            }
            else
            {
                throw new JsonException("mode must be ground or grade-separated");
            }
            if (road["e1"] != null)
            {
                parsed.E1 = RequireFinite(road["e1"], "e1 must be an endpoint height in -30..60 meters");
            }
            if (road["e2"] != null)
            {
                parsed.E2 = RequireFinite(road["e2"], "e2 must be an endpoint height in -30..60 meters");
            }
            if (road["clearance"] != null)
            {
                parsed.Clearance = RequireFinite(road["clearance"], "clearance must be a positive distance in meters");
                if (parsed.Clearance <= 0f || parsed.Clearance > 30f)
                {
                    throw new JsonException("clearance must be positive and within 30 meters");
                }
            }
            if (road["structures"] is JArray structures)
            {
                foreach (JToken entry in structures)
                {
                    if (entry.Type != JTokenType.String)
                    {
                        throw new JsonException("structures must be ground, bridge, elevated or tunnel");
                    }
                    string structure = entry.Value<string>().ToLowerInvariant();
                    if (structure != "ground" && structure != "bridge" && structure != "elevated" && structure != "tunnel")
                    {
                        throw new JsonException("structures must be ground, bridge, elevated or tunnel");
                    }
                    parsed.Structures.Add(structure);
                }
            }
            FillRoadGeometry(parsed);
            if (road["maxGrade"] != null)
            {
                parsed.MaxGrade = RequireFinite(road["maxGrade"], "maxGrade must be a positive ratio like 0.08");
                if (parsed.MaxGrade <= 0f || parsed.MaxGrade > 0.25f)
                {
                    throw new JsonException("maxGrade must be positive and within 25%");
                }
            }
            if (road["minCurveRadius"] != null)
            {
                parsed.MinCurveRadius = RequireFinite(road["minCurveRadius"], "minCurveRadius must be a positive distance in meters");
                if (parsed.MinCurveRadius <= 0f || parsed.MinCurveRadius > 1000f)
                {
                    throw new JsonException("minCurveRadius must be positive and within 1000 meters");
                }
            }
            return parsed;
        }

        private void FillRoadGeometry(SketchRoad parsed)
        {
            if (!TryFindPrefabByName(NetPrefabQuery, parsed.Prefab, out Entity prefabEntity, out PrefabBase _)
                || !EntityManager.HasComponent<RoadData>(prefabEntity))
            {
                throw new JsonException("unknown road prefab '" + parsed.Prefab + "'; choose an exact name from list_prefabs");
            }
            if (IsLocked(prefabEntity))
            {
                throw new JsonException("road prefab '" + parsed.Prefab + "' is locked");
            }
            if (!EntityManager.HasComponent<NetGeometryData>(prefabEntity)
                || !EntityManager.HasComponent<PlaceableNetData>(prefabEntity))
            {
                throw new JsonException("road prefab '" + parsed.Prefab + "' lacks placement data");
            }
            NetGeometryData geometry = EntityManager.GetComponentData<NetGeometryData>(prefabEntity);
            parsed.HalfWidth = geometry.m_DefaultWidth * 0.5f;
            parsed.MaxGrade = geometry.m_MaxSlopeSteepness > 0f
                ? math.min(0.10f, geometry.m_MaxSlopeSteepness)
                : 0.10f;
            if (parsed.Mode == RoadBuildMode.Ground)
            {
                PlaceableNetData placeable = EntityManager.GetComponentData<PlaceableNetData>(prefabEntity);
                if ((placeable.m_PlacementFlags & PlacementFlags.OnGround) == 0)
                {
                    throw new JsonException("prefab '" + parsed.Prefab + "' does not support ground construction");
                }
            }
        }

        private SketchCrossing ParseCrossing(JToken token)
        {
            if (!(token is JObject crossing))
            {
                throw new JsonException("crossings must be objects with a, b and kind");
            }
            foreach (JProperty property in crossing.Properties())
            {
                if (property.Name != "a" && property.Name != "b" && property.Name != "kind" && property.Name != "upper")
                {
                    throw new JsonException("unknown crossing field: " + property.Name);
                }
            }
            var parsed = new SketchCrossing
            {
                A = RequireId(crossing["a"]),
                B = RequireId(crossing["b"]),
                Kind = crossing["kind"] == null ? "at-grade" : crossing["kind"].Value<string>(),
            };
            if (parsed.Kind != "at-grade" && parsed.Kind != "over" && parsed.Kind != "under")
            {
                throw new JsonException("crossing kind must be at-grade, over or under");
            }
            if (parsed.Kind == "at-grade")
            {
                return parsed;
            }
            if (crossing["upper"] == null || crossing["upper"].Type != JTokenType.String)
            {
                throw new JsonException("separated crossings must name which road passes over in upper");
            }
            parsed.Upper = crossing["upper"].Value<string>();
            if (parsed.Upper != parsed.A && parsed.Upper != parsed.B)
            {
                throw new JsonException("upper must be one of the two crossing roads");
            }
            return parsed;
        }

        private SketchDistrict ParseDistrict(JToken token)
        {
            if (!(token is JObject district))
            {
                throw new JsonException("districts must be objects with id, rect and streetPrefab");
            }
            foreach (JProperty property in district.Properties())
            {
                if (property.Name != "id" && property.Name != "rect" && property.Name != "streetPrefab"
                    && property.Name != "field" && property.Name != "angle" && property.Name != "center"
                    && property.Name != "spacing" && property.Name != "maxStreets" && property.Name != "minExits"
                    && property.Name != "allowDeadEnds" && property.Name != "connectTo")
                {
                    throw new JsonException("unknown district field: " + property.Name);
                }
            }
            var parsed = new SketchDistrict { Id = RequireId(district["id"]) };
            JToken rect = district["rect"];
            if (!(rect is JObject rectObject))
            {
                throw new JsonException("district needs rect{xMin,zMin,xMax,zMax}");
            }
            parsed.MinX = ReadBound(rectObject, "xMin");
            parsed.MinZ = ReadBound(rectObject, "zMin");
            parsed.MaxX = ReadBound(rectObject, "xMax");
            parsed.MaxZ = ReadBound(rectObject, "zMax");
            if (district["streetPrefab"] == null || district["streetPrefab"].Type != JTokenType.String)
            {
                throw new JsonException("district needs an exact streetPrefab name");
            }
            parsed.StreetPrefab = district["streetPrefab"].Value<string>();
            if (!TryFindPrefabByName(NetPrefabQuery, parsed.StreetPrefab, out Entity prefabEntity, out PrefabBase _)
                || !EntityManager.HasComponent<RoadData>(prefabEntity))
            {
                throw new JsonException("unknown street prefab '" + parsed.StreetPrefab + "'");
            }
            if (IsLocked(prefabEntity))
            {
                throw new JsonException("street prefab '" + parsed.StreetPrefab + "' is locked");
            }
            if (EntityManager.HasComponent<NetGeometryData>(prefabEntity))
            {
                NetGeometryData geometry = EntityManager.GetComponentData<NetGeometryData>(prefabEntity);
                parsed.StreetHalfWidth = geometry.m_DefaultWidth * 0.5f;
                parsed.StreetMaxGrade = geometry.m_MaxSlopeSteepness > 0f
                    ? math.min(0.10f, geometry.m_MaxSlopeSteepness)
                    : 0.10f;
            }
            if (district["field"] != null)
            {
                if (district["field"].Type != JTokenType.String)
                {
                    throw new JsonException("field must be grid, radial, circular or mixed");
                }
                parsed.Field = district["field"].Value<string>().ToLowerInvariant();
            }
            if (district["angle"] != null)
            {
                parsed.AngleDeg = RequireFinite(district["angle"], "angle must be degrees");
            }
            if (district["center"] != null)
            {
                JObject center = RequireObject(district["center"], "center must be {x,z}");
                parsed.CenterX = RequireCoordinate(center["x"], "center.x");
                parsed.CenterZ = RequireCoordinate(center["z"], "center.z");
            }
            if (district["spacing"] != null)
            {
                parsed.Spacing = RequireFinite(district["spacing"], "spacing must be 24..400 meters");
            }
            if (district["maxStreets"] != null)
            {
                if (district["maxStreets"].Type != JTokenType.Integer)
                {
                    throw new JsonException("maxStreets must be an integer");
                }
                parsed.MaxStreets = district["maxStreets"].Value<int>();
            }
            if (district["minExits"] != null)
            {
                if (district["minExits"].Type != JTokenType.Integer)
                {
                    throw new JsonException("minExits must be an integer");
                }
                parsed.MinExits = district["minExits"].Value<int>();
            }
            if (district["allowDeadEnds"] != null)
            {
                if (district["allowDeadEnds"].Type != JTokenType.Boolean)
                {
                    throw new JsonException("allowDeadEnds must be true or false");
                }
                parsed.AllowDeadEnds = district["allowDeadEnds"].Value<bool>();
            }
            if (district["connectTo"] is JArray connectTo)
            {
                foreach (JToken entry in connectTo)
                {
                    if (entry.Type != JTokenType.String)
                    {
                        throw new JsonException("connectTo must list sketch road ids");
                    }
                    parsed.ConnectTo.Add(entry.Value<string>());
                }
            }
            return parsed;
        }

        private static SketchPoint ParseSketchPoint(JToken token, string error)
        {
            if (!(token is JObject point))
            {
                throw new JsonException(error);
            }
            return new SketchPoint(RequireCoordinate(point["x"], "x"), RequireCoordinate(point["z"], "z"));
        }

        private static string RequireId(JToken token)
        {
            if (token == null || token.Type != JTokenType.String || string.IsNullOrEmpty(token.Value<string>()))
            {
                throw new JsonException("ids must be nonempty strings");
            }
            return token.Value<string>();
        }

        private static string RequirePrefab(JToken token)
        {
            if (token == null || token.Type != JTokenType.String || string.IsNullOrEmpty(token.Value<string>()))
            {
                throw new JsonException("every road needs an exact prefab name from list_prefabs");
            }
            return token.Value<string>();
        }

        private static JObject RequireObject(JToken token, string error)
        {
            if (!(token is JObject parsed))
            {
                throw new JsonException(error);
            }
            return parsed;
        }

        private static Entity RequireEntity(JObject parent, string kind)
        {
            JToken index = parent["index"];
            JToken version = parent["version"];
            if (index == null || index.Type != JTokenType.Integer || version == null || version.Type != JTokenType.Integer)
            {
                throw new JsonException(kind + " anchors need integer index and version");
            }
            long rawIndex = index.Value<long>();
            long rawVersion = version.Value<long>();
            if (rawIndex < 0 || rawIndex >= kMaximumEntityIndexExclusive || rawVersion < 0 || rawVersion > int.MaxValue
                || (rawIndex == 0 && rawVersion == 0))
            {
                throw new JsonException(kind + " anchor identity is out of range");
            }
            return new Entity { Index = (int)rawIndex, Version = (int)rawVersion };
        }

        private static float RequireFinite(JToken token, string error)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                throw new JsonException(error);
            }
            float value = token.Value<float>();
            if (!math.isfinite(value))
            {
                throw new JsonException(error);
            }
            return value;
        }

        private static float RequireCoordinate(JToken token, string name)
        {
            float value = RequireFinite(token, name + " must be a world coordinate in meters");
            if (math.abs(value) > kMapHalfSize)
            {
                throw new JsonException(name + " must stay inside the map");
            }
            return value;
        }
    }
}
