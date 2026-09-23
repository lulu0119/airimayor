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
        private BridgeResponse DebugNetworkCourse(BridgeRequest request)
        {
            if (!CitiesSkylines2Agent.Setting.StaticEnableDevelopmentTools)
                return BridgeResponse.Error(BridgeErrorKind.Unavailable, "development tools are disabled");
            if (!TryGetCity(out _, out BridgeResponse error)) return error;

            RoadPath path;
            RoadBuildMode mode;
            RoadConnection start;
            RoadConnection end;
            string prefabName;
            try
            {
                JObject input = JObject.Parse(request.Body);
                foreach (JProperty property in input.Properties())
                    if (property.Name != "prefab" && property.Name != "mode" && property.Name != "points"
                        && property.Name != "start" && property.Name != "end")
                        throw new JsonException("unknown course field: " + property.Name);
                prefabName = input.Value<string>("prefab");
                string roadMode = input.Value<string>("mode");
                mode = roadMode == "ground" ? RoadBuildMode.Ground
                    : roadMode == "grade-separated" ? RoadBuildMode.GradeSeparated
                    : throw new JsonException("mode must be ground or grade-separated");
                if (!(input["points"] is JArray points) || points.Count != 4)
                    throw new JsonException("points must contain four cubic control points in world x/y/z meters");
                path = new RoadPath(ReadCoursePoint(points[0]), ReadCoursePoint(points[1]),
                    ReadCoursePoint(points[2]), ReadCoursePoint(points[3]));
                start = ReadCourseConnection(input["start"]);
                end = ReadCourseConnection(input["end"]);
            }
            catch (JsonException e)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, e.Message);
            }
            if (string.IsNullOrWhiteSpace(prefabName)
                || !TryFindPrefabByName(NetPrefabQuery, prefabName, out Entity prefabEntity, out PrefabBase prefab)
                || !EntityManager.HasComponent<RoadData>(prefabEntity))
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "choose an exact road prefab from list_prefabs");
            if (IsLocked(prefabEntity))
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "road prefab is locked");

            TerrainHeightData heights = World.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData();
            float2 elevations = new float2(path.A.y - TerrainUtils.SampleHeight(ref heights, path.A),
                path.D.y - TerrainUtils.SampleHeight(ref heights, path.D));
            if (mode == RoadBuildMode.Ground)
            {
                if (math.any(math.abs(elevations) > 0.05f))
                    return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "ground endpoints must match terrain height within 0.05 meters");
                elevations = default;
            }
            if (!CompiledRoadCourse.TryCreate(path, mode, elevations, start, end,
                out CompiledRoadCourse course, out string courseError))
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, courseError);

            if (!EntityManager.HasComponent<NetGeometryData>(prefabEntity)
                || !EntityManager.HasComponent<PlaceableNetData>(prefabEntity))
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "road geometry or placement data is unavailable");
            NetGeometryData geometry = EntityManager.GetComponentData<NetGeometryData>(prefabEntity);
            if (mode == RoadBuildMode.Ground)
            {
                PlaceableNetData placeable = EntityManager.GetComponentData<PlaceableNetData>(prefabEntity);
                if ((placeable.m_PlacementFlags & PlacementFlags.OnGround) == 0)
                    return BridgeResponse.Error(BridgeErrorKind.Conflict, "this road does not support ground construction");
                WaterSurfaceData<SurfaceWater> water = World.GetOrCreateSystemManaged<WaterSystem>()
                    .GetSurfaceData(out JobHandle waterDependencies);
                waterDependencies.Complete();
                RoadGroundPreflightResult preflight = RoadGroundPreflight.Evaluate(path,
                    geometry.m_DefaultWidth * 0.5f, geometry.m_MaxSlopeSteepness,
                    geometry.m_DefaultHeightRange.min, new RoadSurfaceSampler(heights, water));
                if (!preflight.Allowed)
                    return BridgeResponse.Error(BridgeErrorKind.Conflict,
                        $"ground course rejected: {preflight.Block} near ({preflight.Position.x:F1}, {preflight.Position.z:F1})");
            }
            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueCompiledRoad(prefabEntity, prefab, course, request))
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "another construction is in progress");
            return null;
        }

        private static float3 ReadCoursePoint(JToken value)
        {
            if (!(value is JArray point) || point.Count != 3)
                throw new JsonException("each control point must be [x,y,z]");
            var coordinates = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if (point[i].Type != JTokenType.Integer && point[i].Type != JTokenType.Float)
                    throw new JsonException("control point coordinates must be numbers");
                coordinates[i] = point[i].Value<float>();
                if (!math.isfinite(coordinates[i])) throw new JsonException("control point coordinates must be finite");
            }
            if (math.abs(coordinates[0]) > kMapHalfSize || math.abs(coordinates[2]) > kMapHalfSize)
                throw new JsonException("control points must be inside the map");
            return new float3(coordinates[0], coordinates[1], coordinates[2]);
        }

        private static RoadConnection ReadCourseConnection(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return default;
            if (!(value is JObject connection)) throw new JsonException("connection must be an object");
            foreach (JProperty property in connection.Properties())
                if (property.Name != "index" && property.Name != "version" && property.Name != "split")
                    throw new JsonException("unknown connection field: " + property.Name);
            if (connection["index"]?.Type != JTokenType.Integer || connection["version"]?.Type != JTokenType.Integer)
                throw new JsonException("connection requires integer index and version");
            long index = connection.Value<long>("index");
            long version = connection.Value<long>("version");
            if (index < 0 || index >= kMaximumEntityIndexExclusive || version < 0 || version > int.MaxValue
                || (index == 0 && version == 0))
                throw new JsonException("connection identity is out of range");
            JToken split = connection["split"];
            if (split != null && split.Type != JTokenType.Integer && split.Type != JTokenType.Float)
                throw new JsonException("connection split must be a number");
            return new RoadConnection(new Entity { Index = (int)index, Version = (int)version },
                split == null ? 0f : split.Value<float>());
        }

    }
}
