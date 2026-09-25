using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Transform = Game.Objects.Transform;
using AreaType = Game.Zones.AreaType;

namespace CS2MCP
{
    /// <summary>
    /// Native map source: emits rasterizer input directly in game meters.
    /// Geometry shapes are grounded in taipei-native/Carto (MIT, Copyright (c)
    /// 2025 Chang-Yu Ho): Systems/NetworkSystem.cs (Centerline),
    /// Systems/BuildingSystem.cs (Boundary, including its circular-prefab
    /// branch). No Carto code is copied verbatim; only native ECS queries.
    /// Rails use TrackData. Transit route overlays are omitted.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private EntityQuery m_MapSurfaceQuery;
        private bool m_MapSurfaceQueryCreated;

        private EntityQuery MapSurfaceQuery
        {
            get
            {
                if (!m_MapSurfaceQueryCreated)
                {
                    m_MapSurfaceQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<Game.Areas.Surface>(),
                            ComponentType.ReadOnly<Game.Areas.Node>(),
                            ComponentType.ReadOnly<PrefabRef>(),
                        },
                        None = new[]
                        {
                            ComponentType.ReadOnly<Temp>(),
                            ComponentType.ReadOnly<Deleted>(),
                        },
                    });
                    m_MapSurfaceQueryCreated = true;
                }
                return m_MapSurfaceQuery;
            }
        }

        /// <summary>
        /// Bounds are game XZ; null collects citywide. Never throws.
        /// </summary>
        private bool TryCollectNativeMapGeometry(
            float? xMin,
            float? zMin,
            float? xMax,
            float? zMax,
            List<MapStroke> strokes,
            List<MapPolygon> fills,
            out string error)
        {
            error = null;
            if (strokes == null || fills == null)
            {
                error = "nil map source request";
                return false;
            }
            try
            {
                CollectNativeNetworkStrokes(xMin, zMin, xMax, zMax, strokes);
                CollectNativeBuildingFootprints(xMin, zMin, xMax, zMax, fills);
                CollectNativeParkSurfaces(xMin, zMin, xMax, zMax, fills);
                return true;
            }
            catch (Exception e)
            {
                strokes.Clear();
                fills.Clear();
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        private void CollectNativeNetworkStrokes(
            float? xMin,
            float? zMin,
            float? xMax,
            float? zMax,
            List<MapStroke> strokes)
        {
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            using (NativeArray<Entity> entities = PlacedRoadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    PrefabBase prefabBase = prefabSystem.GetPrefab<PrefabBase>(prefabRef.m_Prefab);
                    string prefabName = prefabBase != null ? prefabBase.name ?? string.Empty : string.Empty;
                    bool isRoad = EntityManager.HasComponent<RoadData>(prefabRef.m_Prefab);
                    bool isTrack = IsNativeTrackEntity(entity, prefabRef.m_Prefab, prefabName);
                    if (!isRoad && !isTrack)
                    {
                        continue;
                    }
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                    if (xMin.HasValue && !CurveOverlapsBounds(curve, xMin.Value, zMin.Value, xMax.Value, zMax.Value))
                    {
                        continue;
                    }
                    Game.Net.Edge edge = EntityManager.GetComponentData<Game.Net.Edge>(entity);
                    float widthM = NativeRoadWidthM(prefabRef.m_Prefab);
                    MapStrokeStyle style = isRoad
                        ? ClassifyNativeRoadStyle(
                            ReadUiGroupName(prefabRef.m_Prefab, prefabSystem),
                            prefabName)
                        : ClassifyNativeTrackStyle(entity, prefabRef.m_Prefab, prefabName);
                    if (widthM <= 0f)
                    {
                        widthM = DefaultTrackWidthM(style);
                    }
                    EmitNetworkStroke(entity, edge, curve, style, widthM, strokes);
                }
            }
        }

        /// <summary>
        /// One polyline per native edge. The edge composition is the grade:
        /// elevated is a bridge, tunnel is a tunnel, and a raised embankment
        /// stays ground. Start/end native nodes carry the topology.
        /// </summary>
        private void EmitNetworkStroke(
            Entity entity, Game.Net.Edge edge, Game.Net.Curve curve, MapStrokeStyle style, float widthM, List<MapStroke> strokes)
        {
            int samples = math.clamp((int)math.ceil(curve.m_Length / 16f), 1, 32);
            var stroke = new MapStroke
            {
                Style = style,
                Grade = EdgeGrade(entity),
                WidthM = widthM,
                HasElev = true,
                StartNode = NetNodeKey(edge.m_Start),
                EndNode = NetNodeKey(edge.m_End),
            };
            double heightSum = 0.0;
            AppendSample(stroke, curve, 0f, ref heightSum);
            for (int i = 0; i < samples; i++)
            {
                AppendSample(stroke, curve, (i + 1) / (float)samples, ref heightSum);
            }
            if (stroke.X.Count < 2)
            {
                return;
            }
            stroke.Elev = heightSum / stroke.X.Count;
            strokes.Add(stroke);
        }

        private MapGrade EdgeGrade(Entity entity)
        {
            if (!EntityManager.HasComponent<Composition>(entity))
            {
                return MapGrade.Ground;
            }
            Entity composition = EntityManager.GetComponentData<Composition>(entity).m_Edge;
            if (!EntityManager.HasComponent<NetCompositionData>(composition))
            {
                return MapGrade.Ground;
            }
            CompositionFlags.General general =
                EntityManager.GetComponentData<NetCompositionData>(composition).m_Flags.m_General;
            if ((general & CompositionFlags.General.Tunnel) != 0)
            {
                return MapGrade.Tunnel;
            }
            if ((general & CompositionFlags.General.Elevated) != 0)
            {
                return MapGrade.Bridge;
            }
            return MapGrade.Ground;
        }

        private static void AppendSample(MapStroke stroke, Game.Net.Curve curve, float t, ref double heightSum)
        {
            float3 point = BezierPoint(curve.m_Bezier, t);
            stroke.X.Add(point.x);
            stroke.Y.Add(point.z);
            heightSum += point.y;
        }

        private void CollectNativeBuildingFootprints(
            float? xMin,
            float? zMin,
            float? xMax,
            float? zMax,
            List<MapPolygon> fills)
        {
            using (NativeArray<Entity> entities = PlacedBuildingQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    if (!EntityManager.HasComponent<BuildingData>(prefabRef.m_Prefab))
                    {
                        continue;
                    }
                    Transform transform = EntityManager.GetComponentData<Transform>(entity);
                    int2 lot = EntityManager.GetComponentData<BuildingData>(prefabRef.m_Prefab).m_LotSize;
                    float halfX = lot.x * 4f;
                    float halfZ = lot.y * 4f;
                    if (halfX <= 0f || halfZ <= 0f)
                    {
                        continue;
                    }
                    float2 center = transform.m_Position.xz;
                    if (xMin.HasValue
                        && (center.x + halfX < xMin.Value || center.x - halfX > xMax.Value
                            || center.y + halfZ < zMin.Value || center.y - halfZ > zMax.Value))
                    {
                        continue;
                    }
                    float2 right = math.mul(transform.m_Rotation, new float3(1f, 0f, 0f)).xz;
                    float2 forward = math.mul(transform.m_Rotation, new float3(0f, 0f, 1f)).xz;
                    var polygon = new MapPolygon { Kind = ClassifyNativeBuildingFill(prefabRef.m_Prefab) };
                    AddFootprintCorner(polygon, center, right, forward, -halfX, -halfZ);
                    AddFootprintCorner(polygon, center, right, forward, halfX, -halfZ);
                    AddFootprintCorner(polygon, center, right, forward, halfX, halfZ);
                    AddFootprintCorner(polygon, center, right, forward, -halfX, halfZ);
                    fills.Add(polygon);
                }
            }
        }

        private void CollectNativeParkSurfaces(
            float? xMin,
            float? zMin,
            float? xMax,
            float? zMax,
            List<MapPolygon> fills)
        {
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            using (NativeArray<Entity> entities = MapSurfaceQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    PrefabBase prefabBase = prefabSystem.GetPrefab<PrefabBase>(prefabRef.m_Prefab);
                    string prefabName = prefabBase != null ? prefabBase.name ?? string.Empty : string.Empty;
                    if (!IsGreenSurfacePrefab(prefabRef.m_Prefab, prefabName))
                    {
                        continue;
                    }
                    DynamicBuffer<Game.Areas.Node> nodes =
                        EntityManager.GetBuffer<Game.Areas.Node>(entity, isReadOnly: true);
                    if (nodes.Length < 3)
                    {
                        continue;
                    }
                    var polygon = new MapPolygon { Kind = MapFillKind.Park };
                    bool anyInside = !xMin.HasValue;
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        float3 position = nodes[i].m_Position;
                        polygon.X.Add(position.x);
                        polygon.Y.Add(position.z);
                        if (!anyInside
                            && position.x >= xMin.Value && position.x <= xMax.Value
                            && position.z >= zMin.Value && position.z <= zMax.Value)
                        {
                            anyInside = true;
                        }
                    }
                    if (anyInside)
                    {
                        fills.Add(polygon);
                    }
                }
            }
        }

        private static void AddFootprintCorner(
            MapPolygon polygon, float2 center, float2 right, float2 forward, float dx, float dz)
        {
            float2 point = center + right * dx + forward * dz;
            polygon.X.Add(point.x);
            polygon.Y.Add(point.y);
        }

        private bool IsNativeTrackEntity(Entity entity, Entity prefab, string prefabName)
        {
            return EntityManager.HasComponent<TrackData>(prefab)
                || EntityManager.HasComponent<TrainTrack>(entity)
                || EntityManager.HasComponent<TramTrack>(entity)
                || EntityManager.HasComponent<SubwayTrack>(entity)
                || IsNativeRailPrefab(prefabName);
        }

        private static bool IsNativeRailPrefab(string prefabName)
        {
            return prefabName.IndexOf("rail", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("train", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("tram", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("subway", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("metro", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("track", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsGreenSurfacePrefab(Entity prefab, string name)
        {
            if (EntityManager.HasComponent<AreaColorData>(prefab))
            {
                Color32 fill = EntityManager.GetComponentData<AreaColorData>(prefab).m_FillColor;
                if (fill.g > fill.r + 8 && fill.g > fill.b + 8 && fill.g > 40)
                {
                    return true;
                }
            }
            return name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("park", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("lawn", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("meadow", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("forest", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("garden", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private MapFillKind ClassifyNativeBuildingFill(Entity prefab)
        {
            if (EntityManager.HasComponent<ParkData>(prefab))
            {
                return MapFillKind.Park;
            }
            if (EntityManager.HasComponent<SpawnableBuildingData>(prefab))
            {
                Entity zonePrefab = EntityManager.GetComponentData<SpawnableBuildingData>(prefab).m_ZonePrefab;
                if (EntityManager.Exists(zonePrefab) && EntityManager.HasComponent<ZoneData>(zonePrefab))
                {
                    ZoneData zone = EntityManager.GetComponentData<ZoneData>(zonePrefab);
                    if (zone.IsOffice())
                    {
                        return MapFillKind.Office;
                    }
                    switch (zone.m_AreaType)
                    {
                        case AreaType.Residential:
                            return MapFillKind.Residential;
                        case AreaType.Commercial:
                            return MapFillKind.Commercial;
                        case AreaType.Industrial:
                            return MapFillKind.Industrial;
                    }
                }
            }
            if (EntityManager.HasComponent<PowerPlantData>(prefab)
                || EntityManager.HasComponent<PowerLineData>(prefab)
                || EntityManager.HasComponent<WaterPumpingStationData>(prefab)
                || EntityManager.HasComponent<WaterTowerData>(prefab)
                || EntityManager.HasComponent<SewageOutletData>(prefab)
                || EntityManager.HasComponent<GarbageFacilityData>(prefab)
                || EntityManager.HasComponent<HospitalData>(prefab)
                || EntityManager.HasComponent<FireStationData>(prefab)
                || EntityManager.HasComponent<PoliceStationData>(prefab)
                || EntityManager.HasComponent<SchoolData>(prefab)
                || EntityManager.HasComponent<TransportDepotData>(prefab)
                || EntityManager.HasComponent<TransportStationData>(prefab)
                || EntityManager.HasComponent<PostFacilityData>(prefab)
                || EntityManager.HasComponent<TelecomFacilityData>(prefab))
            {
                return MapFillKind.Service;
            }
            return MapFillKind.Building;
        }

        private static bool CurveOverlapsBounds(Game.Net.Curve curve, float xMin, float zMin, float xMax, float zMax)
        {
            float minX = math.min(math.min(curve.m_Bezier.a.x, curve.m_Bezier.b.x), math.min(curve.m_Bezier.c.x, curve.m_Bezier.d.x));
            float maxX = math.max(math.max(curve.m_Bezier.a.x, curve.m_Bezier.b.x), math.max(curve.m_Bezier.c.x, curve.m_Bezier.d.x));
            float minZ = math.min(math.min(curve.m_Bezier.a.z, curve.m_Bezier.b.z), math.min(curve.m_Bezier.c.z, curve.m_Bezier.d.z));
            float maxZ = math.max(math.max(curve.m_Bezier.a.z, curve.m_Bezier.b.z), math.max(curve.m_Bezier.c.z, curve.m_Bezier.d.z));
            return maxX >= xMin && minX <= xMax && maxZ >= zMin && minZ <= zMax;
        }

        private static long NetNodeKey(Entity node)
        {
            if (node == Entity.Null)
            {
                return 0;
            }
            return ((long)node.Index << 32) | (uint)node.Version;
        }

        private float NativeRoadWidthM(Entity prefab)
        {
            return EntityManager.HasComponent<NetGeometryData>(prefab)
                ? EntityManager.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth
                : 0f;
        }

        private static float DefaultTrackWidthM(MapStrokeStyle style)
        {
            switch (style)
            {
                case MapStrokeStyle.Metro:
                    return 4f;
                case MapStrokeStyle.Tram:
                    return 3f;
                default:
                    return 5f;
            }
        }

        /// <summary>
        /// Prefab name first, road width as fallback. Dedicated tracks are
        /// classified separately; tram-bearing roads stay roads.
        /// </summary>
        private static MapStrokeStyle ClassifyNativeRoadStyle(string groupName, string name)
        {
            switch (RoadFactsMath.Classify(groupName, name))
            {
                case RoadFactsMath.RoadClassHighway:
                    return MapStrokeStyle.Highway;
                case RoadFactsMath.RoadClassLarge:
                    return MapStrokeStyle.Large;
                case RoadFactsMath.RoadClassMedium:
                    return MapStrokeStyle.Medium;
                default:
                    return MapStrokeStyle.Minor;
            }
        }

        private MapStrokeStyle ClassifyNativeTrackStyle(Entity entity, Entity prefab, string name)
        {
            if (EntityManager.HasComponent<SubwayTrack>(entity)
                || name.IndexOf("Subway", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Metro", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Metro;
            }
            if (EntityManager.HasComponent<TramTrack>(entity)
                || name.IndexOf("Tram", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Tram;
            }
            if (EntityManager.HasComponent<TrackData>(prefab))
            {
                TrackTypes types = EntityManager.GetComponentData<TrackData>(prefab).m_TrackType;
                if ((types & TrackTypes.Subway) != 0)
                {
                    return MapStrokeStyle.Metro;
                }
                if ((types & TrackTypes.Tram) != 0)
                {
                    return MapStrokeStyle.Tram;
                }
            }
            return MapStrokeStyle.Rail;
        }
    }
}
