using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CS2MCP
{
    /// <summary>
    /// One edge the native net tool will preview. Entity and split are the
    /// graph anchor; a new end leaves both unset.
    /// </summary>
    internal struct NetworkCourseSegment
    {
        public RoadPath Path;
        public float StartElevation;
        public float EndElevation;
        public Entity StartEntity;
        public float StartSplit;
        public Entity EndEntity;
        public float EndSplit;
        public NetworkJoinKind StartJoin;
        public NetworkJoinKind EndJoin;
    }

    /// <summary>
    /// Turns one validated build_network call into the courses the native net
    /// tool previews. Anchors come from the live graph; the bezier math stays
    /// in <see cref="NetworkCourseMath"/>.
    /// </summary>
    internal static class NetworkCourseBuilder
    {
        private const int kMaximumParallelEdges = 64;
        private const float kMinimumSegmentLength = 8f;
        private const float kMaximumSegmentLength = 1500f;

        public static bool TryBuild(
            World world,
            EntityManager entityManager,
            Entity prefabEntity,
            bool isRoad,
            NetworkBuildArguments arguments,
            float x1,
            float z1,
            float x2,
            float z2,
            float startElevation,
            float endElevation,
            out List<NetworkCourseSegment> segments,
            out float length,
            out float steepestGrade,
            out string error)
        {
            segments = null;
            length = 0f;
            steepestGrade = 0f;
            error = null;
            TerrainHeightData heightData = world
                .GetOrCreateSystemManaged<TerrainSystem>()
                .GetHeightData();
            float snapRadius = SnapRadius(entityManager, prefabEntity);
            ResolvedEnd start = Resolve(
                world,
                entityManager,
                prefabEntity,
                new float2(x1, z1),
                snapRadius,
                ref heightData);
            ResolvedEnd end = Resolve(
                world,
                entityManager,
                prefabEntity,
                new float2(x2, z2),
                snapRadius,
                ref heightData);

            if (arguments.Shape == NetworkCourseShape.Parallel)
            {
                return TryBuildParallel(
                    entityManager,
                    start,
                    end,
                    new float2(x1, z1),
                    new float2(x2, z2),
                    arguments.Offset,
                    arguments.HasRaise ? arguments.Raise : 0f,
                    ref heightData,
                    out segments,
                    out length,
                    out steepestGrade,
                    out error);
            }

            float2 startDirection = NetworkCourseMath.TangentToward(end.Position.xz - start.Position.xz, start.Axes);
            float2 arrival = NetworkCourseMath.TangentToward(end.Position.xz - start.Position.xz, end.Axes);
            float2 endHandle = -arrival;
            float departure = arguments.ResolvedDeparture(startDirection, endHandle);
            float reach = arguments.ResolvedArrival(startDirection, endHandle);
            var built = new List<NetworkCourseSegment>(2);
            if (arguments.Shape == NetworkCourseShape.Complex)
            {
                NetworkCourseMath.Complex(
                    start.Position,
                    startDirection,
                    end.Position,
                    endHandle,
                    departure,
                    reach,
                    arguments.HasMidpoint,
                    arguments.MidX,
                    arguments.MidZ,
                    out RoadPath first,
                    out RoadPath second);
                first = FollowTerrain(first, isRoad, start.Join != NetworkJoinKind.New, false, ref heightData);
                second = FollowTerrain(second, isRoad, false, end.Join != NetworkJoinKind.New, ref heightData);
                float firstLength = NetworkCourseMath.Length(first);
                float secondLength = NetworkCourseMath.Length(second);
                float total = math.max(firstLength + secondLength, 0.001f);
                built.Add(Finish(
                    first,
                    0f,
                    firstLength / total,
                    start,
                    default,
                    arguments,
                    startElevation,
                    endElevation));
                built.Add(Finish(
                    second,
                    firstLength / total,
                    1f,
                    default,
                    end,
                    arguments,
                    startElevation,
                    endElevation));
            }
            else
            {
                RoadPath path = arguments.Shape == NetworkCourseShape.Simple
                    ? NetworkCourseMath.Simple(start.Position, startDirection, end.Position, endHandle, departure, reach)
                    : RoadPath.Straight(start.Position, end.Position);
                path = FollowTerrain(path, isRoad, start.Join != NetworkJoinKind.New, end.Join != NetworkJoinKind.New, ref heightData);
                built.Add(Finish(path, 0f, 1f, start, end, arguments, startElevation, endElevation));
            }

            if (!TryMeasure(built, out length, out steepestGrade, out error))
            {
                return false;
            }

            segments = built;
            return true;
        }

        private static bool TryBuildParallel(
            EntityManager entityManager,
            ResolvedEnd start,
            ResolvedEnd end,
            float2 requestedStart,
            float2 requestedEnd,
            float offset,
            float raise,
            ref TerrainHeightData heightData,
            out List<NetworkCourseSegment> segments,
            out float length,
            out float steepestGrade,
            out string error)
        {
            segments = null;
            length = 0f;
            steepestGrade = 0f;
            if (!ResolveParallelNode(entityManager, start, requestedStart, out Entity startNode, out error)
                || !ResolveParallelNode(entityManager, end, requestedEnd, out Entity endNode, out error))
            {
                return false;
            }
            if (startNode == endNode)
            {
                error = "parallel needs two different points of the road to copy";
                return false;
            }
            if (!TryFindPath(entityManager, startNode, endNode, out List<PathEdge> path, out error))
            {
                return false;
            }

            var nodePositions = new List<float3>(path.Count + 1);
            var incoming = new List<float2>(path.Count + 1);
            var outgoing = new List<float2>(path.Count + 1);
            nodePositions.Add(entityManager.GetComponentData<Node>(path[0].From).m_Position);
            incoming.Add(float2.zero);
            outgoing.Add(math.normalizesafe(path[0].Oriented.B.xz - path[0].Oriented.A.xz));
            for (int i = 0; i < path.Count; i++)
            {
                nodePositions.Add(entityManager.GetComponentData<Node>(path[i].To).m_Position);
                incoming.Add(math.normalizesafe(path[i].Oriented.D.xz - path[i].Oriented.C.xz));
                outgoing.Add(i + 1 < path.Count
                    ? math.normalizesafe(path[i + 1].Oriented.B.xz - path[i + 1].Oriented.A.xz)
                    : float2.zero);
            }

            var built = new List<NetworkCourseSegment>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                float3 from = NetworkCourseMath.OffsetPoint(
                    nodePositions[i],
                    incoming[i],
                    outgoing[i],
                    offset,
                    raise);
                float3 to = NetworkCourseMath.OffsetPoint(
                    nodePositions[i + 1],
                    incoming[i + 1],
                    outgoing[i + 1],
                    offset,
                    raise);
                RoadPath course = NetworkCourseMath.JoinOffset(path[i].Oriented, from, to, offset);
                float startHeight = from.y - TerrainUtils.SampleHeight(ref heightData, from);
                float endHeight = to.y - TerrainUtils.SampleHeight(ref heightData, to);
                built.Add(new NetworkCourseSegment
                {
                    Path = course,
                    StartElevation = startHeight,
                    EndElevation = endHeight,
                    StartJoin = NetworkJoinKind.New,
                    EndJoin = NetworkJoinKind.New,
                });
            }

            if (!TryMeasure(built, out length, out steepestGrade, out error))
            {
                return false;
            }

            segments = built;
            return true;
        }

        private static NetworkCourseSegment Finish(
            RoadPath path,
            float t0,
            float t1,
            ResolvedEnd start,
            ResolvedEnd end,
            NetworkBuildArguments arguments,
            float startElevation,
            float endElevation)
        {
            bool lift = arguments.Profile != NetworkElevationProfile.Linear
                || startElevation != 0f
                || endElevation != 0f;
            RoadPath lifted = lift
                ? NetworkCourseMath.ApplyProfile(
                    path,
                    t0,
                    t1,
                    startElevation,
                    endElevation,
                    arguments.Profile,
                    arguments.EaseStart,
                    arguments.EaseEnd,
                    arguments.ArchHeight,
                    arguments.ArchAt)
                : path;
            return new NetworkCourseSegment
            {
                Path = lifted,
                StartElevation = NetworkCourseMath.ProfileHeight(
                    t0,
                    startElevation,
                    endElevation,
                    arguments.Profile,
                    arguments.EaseStart,
                    arguments.EaseEnd,
                    arguments.ArchHeight,
                    arguments.ArchAt),
                EndElevation = NetworkCourseMath.ProfileHeight(
                    t1,
                    startElevation,
                    endElevation,
                    arguments.Profile,
                    arguments.EaseStart,
                    arguments.EaseEnd,
                    arguments.ArchHeight,
                    arguments.ArchAt),
                StartEntity = start.Entity,
                StartSplit = start.Split,
                EndEntity = end.Entity,
                EndSplit = end.Split,
                StartJoin = start.Join,
                EndJoin = end.Join,
            };
        }

        private static bool TryMeasure(
            List<NetworkCourseSegment> segments,
            out float length,
            out float steepestGrade,
            out string error)
        {
            length = 0f;
            steepestGrade = 0f;
            error = null;
            for (int i = 0; i < segments.Count; i++)
            {
                float segmentLength = NetworkCourseMath.Length(segments[i].Path);
                if (segmentLength < kMinimumSegmentLength)
                {
                    error = $"segment too short ({segmentLength:F1}m); minimum ~8m";
                    return false;
                }
                if (segmentLength > kMaximumSegmentLength)
                {
                    error = $"segment too long ({segmentLength:F0}m); split into segments of <=1500m";
                    return false;
                }
                length += segmentLength;
                steepestGrade = math.max(
                    steepestGrade,
                    NetworkCourseMath.SteepestGrade(segments[i].Path));
            }
            return true;
        }

        private static ResolvedEnd Resolve(
            World world,
            EntityManager entityManager,
            Entity prefabEntity,
            float2 point,
            float snapRadius,
            ref TerrainHeightData heightData)
        {
            var hits = new List<Hit>();
            var candidates = new List<CourseSnapCandidate>();
            CollectHits(world, entityManager, prefabEntity, point, snapRadius, hits, candidates);
            float3 terrain = new float3(point.x, 0f, point.y);
            terrain.y = TerrainUtils.SampleHeight(ref heightData, terrain);
            if (!NetworkCourseMath.TryPickSnap(candidates, snapRadius, out CourseSnapCandidate picked))
            {
                return new ResolvedEnd
                {
                    Position = terrain,
                    Axes = Array.Empty<float2>(),
                    Join = NetworkJoinKind.New,
                };
            }

            Hit hit = hits[picked.Id];
            if (hit.Node)
            {
                return new ResolvedEnd
                {
                    Position = hit.Position,
                    Axes = IntoEdgeDirections(entityManager, hit.Entity),
                    Join = NetworkJoinKind.Node,
                    Entity = hit.Entity,
                };
            }

            if (!CanSplit(entityManager, hit.Entity))
            {
                return new ResolvedEnd
                {
                    Position = terrain,
                    Axes = Array.Empty<float2>(),
                    Join = NetworkJoinKind.New,
                };
            }

            return new ResolvedEnd
            {
                Position = hit.Position,
                Axes = new[] { hit.Into },
                Join = NetworkJoinKind.Split,
                Entity = hit.Entity,
                Split = hit.Split,
            };
        }

        private static void CollectHits(
            World world,
            EntityManager entityManager,
            Entity prefabEntity,
            float2 point,
            float snapRadius,
            List<Hit> hits,
            List<CourseSnapCandidate> candidates)
        {
            Game.Net.SearchSystem search = world.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = search.GetNetSearchTree(true, out JobHandle dependency);
            dependency.Complete();
            using (var found = new NativeList<Entity>(16, Allocator.Temp))
            {
                var iterator = new NearbyNetIterator
                {
                    Bounds = new Bounds3(
                        new float3(point.x - snapRadius, -500f, point.y - snapRadius),
                        new float3(point.x + snapRadius, 500f, point.y + snapRadius)),
                    Found = found,
                };
                tree.Iterate(ref iterator);
                for (int i = 0; i < found.Length; i++)
                {
                    ConsiderEntity(entityManager, prefabEntity, point, found[i], hits, candidates);
                }
            }
        }

        private static void ConsiderEntity(
            EntityManager entityManager,
            Entity prefabEntity,
            float2 point,
            Entity entity,
            List<Hit> hits,
            List<CourseSnapCandidate> candidates)
        {
            if (!entityManager.HasComponent<PrefabRef>(entity))
            {
                return;
            }
            Entity netPrefab = entityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (!Compatible(entityManager, prefabEntity, netPrefab))
            {
                return;
            }

            if (entityManager.HasComponent<Node>(entity))
            {
                float3 position = entityManager.GetComponentData<Node>(entity).m_Position;
                AddHit(hits, candidates, new Hit
                {
                    Entity = entity,
                    Node = true,
                    Position = position,
                }, math.distance(position.xz, point));
                return;
            }

            if (!entityManager.HasComponent<Curve>(entity) || !entityManager.HasComponent<Edge>(entity))
            {
                return;
            }

            Curve curve = entityManager.GetComponentData<Curve>(entity);
            float distance = MathUtils.Distance(curve.m_Bezier.xz, point, out float curveT);
            curveT = math.saturate(curveT);
            Edge edge = entityManager.GetComponentData<Edge>(entity);
            float3 positionOnCurve = MathUtils.Position(curve.m_Bezier, curveT);
            if (curveT <= 0.001f || math.distance(curve.m_Bezier.a.xz, point) <= distance)
            {
                AddNode(entityManager, edge.m_Start, prefabEntity, point, hits, candidates);
            }
            if (curveT >= 0.999f || math.distance(curve.m_Bezier.d.xz, point) <= distance)
            {
                AddNode(entityManager, edge.m_End, prefabEntity, point, hits, candidates);
            }
            if (curveT <= 0.001f || curveT >= 0.999f)
            {
                return;
            }

            float2 tangent = math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, curveT).xz);
            AddHit(hits, candidates, new Hit
            {
                Entity = entity,
                Position = positionOnCurve,
                Split = curveT,
                Into = tangent,
            }, distance);
        }

        private static void AddNode(
            EntityManager entityManager,
            Entity node,
            Entity prefabEntity,
            float2 point,
            List<Hit> hits,
            List<CourseSnapCandidate> candidates)
        {
            if (!entityManager.Exists(node) || !entityManager.HasComponent<Node>(node))
            {
                return;
            }
            if (entityManager.HasComponent<PrefabRef>(node))
            {
                Entity netPrefab = entityManager.GetComponentData<PrefabRef>(node).m_Prefab;
                if (!Compatible(entityManager, prefabEntity, netPrefab))
                {
                    return;
                }
            }
            float3 position = entityManager.GetComponentData<Node>(node).m_Position;
            AddHit(hits, candidates, new Hit
            {
                Entity = node,
                Node = true,
                Position = position,
            }, math.distance(position.xz, point));
        }

        private static void AddHit(
            List<Hit> hits,
            List<CourseSnapCandidate> candidates,
            Hit hit,
            float distance)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i].Entity == hit.Entity && hits[i].Node == hit.Node)
                {
                    return;
                }
            }
            candidates.Add(new CourseSnapCandidate(hits.Count, hit.Node, distance));
            hits.Add(hit);
        }

        private static float2[] IntoEdgeDirections(EntityManager entityManager, Entity node)
        {
            if (!entityManager.HasBuffer<ConnectedEdge>(node))
            {
                return Array.Empty<float2>();
            }
            DynamicBuffer<ConnectedEdge> connected = entityManager.GetBuffer<ConnectedEdge>(node);
            var directions = new List<float2>(connected.Length);
            for (int i = 0; i < connected.Length; i++)
            {
                Entity edgeEntity = connected[i].m_Edge;
                if (!entityManager.HasComponent<Edge>(edgeEntity)
                    || !entityManager.HasComponent<Curve>(edgeEntity))
                {
                    continue;
                }
                Edge edge = entityManager.GetComponentData<Edge>(edgeEntity);
                Curve curve = entityManager.GetComponentData<Curve>(edgeEntity);
                if (edge.m_Start == node)
                {
                    directions.Add(math.normalizesafe(MathUtils.StartTangent(curve.m_Bezier).xz));
                }
                else if (edge.m_End == node)
                {
                    directions.Add(math.normalizesafe(-MathUtils.EndTangent(curve.m_Bezier).xz));
                }
            }
            return directions.ToArray();
        }

        private static bool CanSplit(EntityManager entityManager, Entity edge)
        {
            if (!entityManager.HasComponent<Owner>(edge))
            {
                return true;
            }
            Entity owner = entityManager.GetComponentData<Owner>(edge).m_Owner;
            return entityManager.HasComponent<Edge>(owner);
        }

        private static bool Compatible(EntityManager entityManager, Entity placedPrefab, Entity existingPrefab)
        {
            if (!entityManager.HasComponent<NetData>(placedPrefab)
                || !entityManager.HasComponent<NetData>(existingPrefab))
            {
                return false;
            }
            return NetUtils.CanConnect(
                entityManager.GetComponentData<NetData>(existingPrefab),
                entityManager.GetComponentData<NetData>(placedPrefab));
        }

        private static bool ResolveParallelNode(
            EntityManager entityManager,
            ResolvedEnd end,
            float2 requested,
            out Entity node,
            out string error)
        {
            node = Entity.Null;
            error = null;
            if (end.Join == NetworkJoinKind.Node)
            {
                node = end.Entity;
                return true;
            }
            if (end.Join == NetworkJoinKind.Split
                && entityManager.HasComponent<Edge>(end.Entity))
            {
                Edge edge = entityManager.GetComponentData<Edge>(end.Entity);
                Entity best = Entity.Null;
                float bestDistance = float.PositiveInfinity;
                ConsiderParallelEndpoint(entityManager, edge.m_Start, end.Position.xz, ref best, ref bestDistance);
                ConsiderParallelEndpoint(entityManager, edge.m_End, end.Position.xz, ref best, ref bestDistance);
                if (best != Entity.Null)
                {
                    node = best;
                    return true;
                }
            }
            error = $"no road near ({requested.x:F0}, {requested.y:F0}); place both ends on the road to copy";
            return false;
        }

        private static void ConsiderParallelEndpoint(
            EntityManager entityManager,
            Entity candidate,
            float2 position,
            ref Entity best,
            ref float bestDistance)
        {
            if (!entityManager.Exists(candidate) || !entityManager.HasComponent<Node>(candidate))
            {
                return;
            }
            float distance = math.distance(
                entityManager.GetComponentData<Node>(candidate).m_Position.xz,
                position);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        private static bool TryFindPath(
            EntityManager entityManager,
            Entity start,
            Entity end,
            out List<PathEdge> path,
            out string error)
        {
            path = null;
            error = null;
            var best = new Dictionary<Entity, float> { [start] = 0f };
            var previousNode = new Dictionary<Entity, Entity>();
            var previousEdge = new Dictionary<Entity, Entity>();
            var open = new List<Entity> { start };
            while (open.Count > 0)
            {
                int bestIndex = 0;
                for (int i = 1; i < open.Count; i++)
                {
                    if (best[open[i]] < best[open[bestIndex]])
                    {
                        bestIndex = i;
                    }
                }
                Entity node = open[bestIndex];
                open.RemoveAt(bestIndex);
                if (node == end)
                {
                    break;
                }
                if (!entityManager.HasBuffer<ConnectedEdge>(node))
                {
                    continue;
                }
                DynamicBuffer<ConnectedEdge> connected = entityManager.GetBuffer<ConnectedEdge>(node);
                for (int i = 0; i < connected.Length; i++)
                {
                    Entity edgeEntity = connected[i].m_Edge;
                    if (!entityManager.HasComponent<Road>(edgeEntity)
                        || !entityManager.HasComponent<Edge>(edgeEntity)
                        || !entityManager.HasComponent<Curve>(edgeEntity))
                    {
                        continue;
                    }
                    Edge edge = entityManager.GetComponentData<Edge>(edgeEntity);
                    Entity next = edge.m_Start == node
                        ? edge.m_End
                        : edge.m_End == node
                            ? edge.m_Start
                            : Entity.Null;
                    if (next == Entity.Null)
                    {
                        continue;
                    }
                    float step = math.max(entityManager.GetComponentData<Curve>(edgeEntity).m_Length, 0.01f);
                    float cost = best[node] + step;
                    if (best.TryGetValue(next, out float known) && known <= cost)
                    {
                        continue;
                    }
                    best[next] = cost;
                    previousNode[next] = node;
                    previousEdge[next] = edgeEntity;
                    open.Add(next);
                }
            }

            if (!best.ContainsKey(end))
            {
                error = "no road connects those points; choose two points on the same road";
                return false;
            }

            var reversed = new List<PathEdge>();
            Entity cursor = end;
            while (cursor != start)
            {
                Entity edgeEntity = previousEdge[cursor];
                Entity from = previousNode[cursor];
                Curve curve = entityManager.GetComponentData<Curve>(edgeEntity);
                Edge edge = entityManager.GetComponentData<Edge>(edgeEntity);
                Bezier4x3 bezier = curve.m_Bezier;
                RoadPath oriented = edge.m_Start == from
                    ? new RoadPath(bezier.a, bezier.b, bezier.c, bezier.d)
                    : new RoadPath(bezier.d, bezier.c, bezier.b, bezier.a);
                reversed.Add(new PathEdge(from, cursor, oriented));
                cursor = from;
                if (reversed.Count > kMaximumParallelEdges)
                {
                    error = "that road is too long to copy in one call; choose closer points";
                    return false;
                }
            }
            reversed.Reverse();
            path = reversed;
            return true;
        }

        private static RoadPath FollowTerrain(
            RoadPath path,
            bool road,
            bool fixedStart,
            bool fixedEnd,
            ref TerrainHeightData heightData)
        {
            if (road)
            {
                var raw = new Curve
                {
                    m_Bezier = new Bezier4x3(path.A, path.B, path.C, path.D),
                };
                Bezier4x3 adjusted = NetUtils.AdjustPosition(
                    raw,
                    fixedStart,
                    linearMiddle: false,
                    fixedEnd,
                    ref heightData).m_Bezier;
                return new RoadPath(adjusted.a, adjusted.b, adjusted.c, adjusted.d);
            }

            float3 a = path.A;
            float3 b = path.B;
            float3 c = path.C;
            float3 d = path.D;
            if (!fixedStart)
            {
                a.y = TerrainUtils.SampleHeight(ref heightData, a);
            }
            b.y = TerrainUtils.SampleHeight(ref heightData, b);
            c.y = TerrainUtils.SampleHeight(ref heightData, c);
            if (!fixedEnd)
            {
                d.y = TerrainUtils.SampleHeight(ref heightData, d);
            }
            return new RoadPath(a, b, c, d);
        }

        private static float SnapRadius(EntityManager entityManager, Entity prefabEntity)
        {
            float radius = 8f;
            if (entityManager.HasComponent<PlaceableNetData>(prefabEntity))
            {
                radius = math.max(
                    entityManager.GetComponentData<PlaceableNetData>(prefabEntity).m_SnapDistance,
                    1f);
            }
            if (entityManager.HasComponent<NetGeometryData>(prefabEntity))
            {
                radius += entityManager.GetComponentData<NetGeometryData>(prefabEntity).m_DefaultWidth * 0.5f;
            }
            return radius;
        }

        private struct ResolvedEnd
        {
            public float3 Position;
            public float2[] Axes;
            public NetworkJoinKind Join;
            public Entity Entity;
            public float Split;
        }

        private struct Hit
        {
            public Entity Entity;
            public bool Node;
            public float3 Position;
            public float Split;
            public float2 Into;
        }

        private readonly struct PathEdge
        {
            public PathEdge(Entity from, Entity to, RoadPath oriented)
            {
                From = from;
                To = to;
                Oriented = oriented;
            }

            public Entity From { get; }
            public Entity To { get; }
            public RoadPath Oriented { get; }
        }

        private struct NearbyNetIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>,
            IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds3 Bounds;
            public NativeList<Entity> Found;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return MathUtils.Intersect(bounds.m_Bounds, Bounds);
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (!MathUtils.Intersect(bounds.m_Bounds, Bounds))
                {
                    return;
                }
                for (int i = 0; i < Found.Length; i++)
                {
                    if (Found[i] == entity)
                    {
                        return;
                    }
                }
                Found.Add(entity);
            }
        }
    }
}
