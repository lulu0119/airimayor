using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace CS2MCP
{
    /// <summary>
    /// Native map source: emits rasterizer input directly in game meters with
    /// no Carto install, no GeoJSON roundtrip, and no projection branch.
    /// Geometry shapes are grounded in taipei-native/Carto (MIT, Copyright (c)
    /// 2025 Chang-Yu Ho): Systems/NetworkSystem.cs (Centerline),
    /// Systems/BuildingSystem.cs (Boundary, including its circular-prefab
    /// branch), Systems/RouteSystem.cs (Centerline). No Carto code is copied
    /// verbatim in v1; only native ECS queries. Routes are stop-to-stop loops
    /// (the exact on-road path port is deferred); rails match by prefab name.
    /// The Carto reflection path in RequestHandlers.MapImage.cs stays as the
    /// fallback adapter until the native source passes live acceptance.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        /// <summary>
        /// Every Carto System kind, so deferred layers stay visible instead of
        /// silently dropped. Network, Building, and Route are collected; the
        /// rest stay deferred with per-kind reasons.
        /// </summary>
        internal enum MapSourceKind
        {
            Network,
            Building,
            Route,
            Area,
            Zoning,
            PointOfInterest,
            Raster,
        }

        internal static bool IsNativeMapSourceSupported(MapSourceKind kind)
        {
            return kind == MapSourceKind.Network
                || kind == MapSourceKind.Building
                || kind == MapSourceKind.Route;
        }

        internal static string NativeMapSourceDeferredReason(MapSourceKind kind)
        {
            switch (kind)
            {
                case MapSourceKind.Network:
                case MapSourceKind.Building:
                case MapSourceKind.Route:
                    return null;
                case MapSourceKind.Area:
                    return "district/lot boundary port missing";
                case MapSourceKind.Zoning:
                    return "every zoning cell every call does not fit a synchronous tool call";
                case MapSourceKind.PointOfInterest:
                    return "text rendering and icons do not belong in this rasterizer";
                case MapSourceKind.Raster:
                    return "hillshade stays an offline job; water already samples natively";
                default:
                    return "unknown map source kind";
            }
        }

        /// <summary>
        /// Fills strokes/fills for the requested kinds. Fail-closed: returns
        /// false with the first unsupported kind's reason, collecting nothing.
        /// Bounds are game XZ; null collects citywide. Never throws.
        /// </summary>
        private bool TryCollectNativeMapGeometry(
            IList<MapSourceKind> kinds,
            float? xMin,
            float? zMin,
            float? xMax,
            float? zMax,
            List<MapStroke> strokes,
            List<MapPolygon> fills,
            out string unsupported)
        {
            unsupported = null;
            if (kinds == null || strokes == null || fills == null)
            {
                unsupported = "nil map source request";
                return false;
            }
            foreach (MapSourceKind kind in kinds)
            {
                if (!IsNativeMapSourceSupported(kind))
                {
                    unsupported = NativeMapSourceDeferredReason(kind) ?? kind.ToString();
                    return false;
                }
            }
            try
            {
                foreach (MapSourceKind kind in kinds)
                {
                    switch (kind)
                    {
                        case MapSourceKind.Network:
                            CollectNativeNetworkStrokes(xMin, zMin, xMax, zMax, strokes);
                            break;
                        case MapSourceKind.Building:
                            CollectNativeBuildingFootprints(xMin, zMin, xMax, zMax, fills);
                            break;
                        case MapSourceKind.Route:
                            CollectNativeRouteLoops(xMin, zMin, xMax, zMax, strokes);
                            break;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                strokes.Clear();
                fills.Clear();
                unsupported = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// Road centerlines plus name-matched rails (the typed-network graph
        /// does not classify rails either, so no exact track-component port).
        /// Elevation is the per-feature mean curve height, the same definition
        /// the layering solver already consumes from Carto.
        /// </summary>
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
                    if (!EntityManager.HasComponent<RoadData>(prefabRef.m_Prefab)
                        && !IsNativeRailPrefab(prefabName))
                    {
                        continue;
                    }
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                    if (xMin.HasValue && !CurveOverlapsBounds(curve, xMin.Value, zMin.Value, xMax.Value, zMax.Value))
                    {
                        continue;
                    }
                    var stroke = new MapStroke { Style = ClassifyNativeRoadStyle(prefabRef.m_Prefab, prefabName) };
                    int samples = math.clamp((int)math.ceil(curve.m_Length / 16f), 1, 32);
                    double heightSum = 0.0;
                    for (int i = 0; i <= samples; i++)
                    {
                        float3 point = BezierPoint(curve.m_Bezier, i / (float)samples);
                        stroke.X.Add(point.x);
                        stroke.Y.Add(point.z);
                        heightSum += point.y;
                    }
                    if (stroke.X.Count >= 2)
                    {
                        stroke.Elev = heightSum / stroke.X.Count;
                        stroke.HasElev = true;
                        strokes.Add(stroke);
                    }
                }
            }
        }

        /// <summary>
        /// Transit overlay as stop-to-stop loops. Waypoint positions are the
        /// established line proxy (TransitLineIntersectsRadius); the exact
        /// on-road path port is deferred. Incomplete routes are skipped, like
        /// the transit-line write path requires. Loops carry no elevation, so
        /// they never constrain layering, matching the Carto route features.
        /// </summary>
        private void CollectNativeRouteLoops(
            float? xMin,
            float? zMin,
            float? xMax,
            float? zMax,
            List<MapStroke> strokes)
        {
            using (NativeArray<Entity> entities = TransitLineQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if ((EntityManager.GetComponentData<Route>(entity).m_Flags & RouteFlags.Complete) == 0)
                    {
                        continue;
                    }
                    DynamicBuffer<RouteWaypoint> waypoints =
                        EntityManager.GetBuffer<RouteWaypoint>(entity, isReadOnly: true);
                    var stroke = new MapStroke { Style = MapStrokeStyle.Transit };
                    foreach (RouteWaypoint waypoint in waypoints)
                    {
                        if (!EntityManager.Exists(waypoint.m_Waypoint)
                            || !EntityManager.HasComponent<Position>(waypoint.m_Waypoint))
                        {
                            continue;
                        }
                        float3 position = EntityManager.GetComponentData<Position>(waypoint.m_Waypoint).m_Position;
                        stroke.X.Add(position.x);
                        stroke.Y.Add(position.z);
                    }
                    if (stroke.X.Count < 2)
                    {
                        continue;
                    }
                    stroke.X.Add(stroke.X[0]);
                    stroke.Y.Add(stroke.Y[0]);
                    if (xMin.HasValue && !StrokeOverlapsBounds(stroke, xMin.Value, zMin.Value, xMax.Value, zMax.Value))
                    {
                        continue;
                    }
                    strokes.Add(stroke);
                }
            }
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
                    var polygon = new MapPolygon();
                    AddFootprintCorner(polygon, center, right, forward, -halfX, -halfZ);
                    AddFootprintCorner(polygon, center, right, forward, halfX, -halfZ);
                    AddFootprintCorner(polygon, center, right, forward, halfX, halfZ);
                    AddFootprintCorner(polygon, center, right, forward, -halfX, halfZ);
                    fills.Add(polygon);
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

        private static bool IsNativeRailPrefab(string prefabName)
        {
            return prefabName.IndexOf("rail", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("train", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("tram", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("subway", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("metro", StringComparison.OrdinalIgnoreCase) >= 0
                || prefabName.IndexOf("track", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool StrokeOverlapsBounds(MapStroke stroke, float xMin, float zMin, float xMax, float zMax)
        {
            for (int i = 0; i + 1 < stroke.X.Count; i++)
            {
                double minX = Math.Min(stroke.X[i], stroke.X[i + 1]);
                double maxX = Math.Max(stroke.X[i], stroke.X[i + 1]);
                double minY = Math.Min(stroke.Y[i], stroke.Y[i + 1]);
                double maxY = Math.Max(stroke.Y[i], stroke.Y[i + 1]);
                if (maxX >= xMin && minX <= xMax && maxY >= zMin && minY <= zMax)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CurveOverlapsBounds(Game.Net.Curve curve, float xMin, float zMin, float xMax, float zMax)
        {
            float minX = math.min(math.min(curve.m_Bezier.a.x, curve.m_Bezier.b.x), math.min(curve.m_Bezier.c.x, curve.m_Bezier.d.x));
            float maxX = math.max(math.max(curve.m_Bezier.a.x, curve.m_Bezier.b.x), math.max(curve.m_Bezier.c.x, curve.m_Bezier.d.x));
            float minZ = math.min(math.min(curve.m_Bezier.a.z, curve.m_Bezier.b.z), math.min(curve.m_Bezier.c.z, curve.m_Bezier.d.z));
            float maxZ = math.max(math.max(curve.m_Bezier.a.z, curve.m_Bezier.b.z), math.max(curve.m_Bezier.c.z, curve.m_Bezier.d.z));
            return maxX >= xMin && minX <= xMax && maxZ >= zMin && minZ <= zMax;
        }

        /// <summary>
        /// Hierarchy without Carto's Category: prefab name first, road width
        /// as fallback. Rails and tram/train-bearing roads draw as transit.
        /// </summary>
        private MapStrokeStyle ClassifyNativeRoadStyle(Entity prefab, string name)
        {
            if (name.IndexOf("Highway", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Highway;
            }
            if (name.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Large;
            }
            if (name.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Medium;
            }
            if (name.IndexOf("Tram", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Subway", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Transit;
            }
            float width = EntityManager.HasComponent<NetGeometryData>(prefab)
                ? EntityManager.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth
                : 0f;
            if (width >= 24f)
            {
                return MapStrokeStyle.Highway;
            }
            if (width >= 16f)
            {
                return MapStrokeStyle.Large;
            }
            if (width >= 10f)
            {
                return MapStrokeStyle.Medium;
            }
            return MapStrokeStyle.Minor;
        }
    }
}
