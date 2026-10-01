using System.Collections.Generic;
using System.Globalization;
using airimayor.Host;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;
using Transform = Game.Objects.Transform;

namespace CS2MCP
{
    /// <summary>
    /// Player pick for the chat. A click records a building, a typed-network
    /// edge, or a ground point. While armed, the hovered pick draws its
    /// outline through the overlay buffer (the MoveIt recipe); nothing else
    /// is drawn in the city.
    /// </summary>
    public sealed partial class PointedPlaceToolSystem : ToolBaseSystem
    {
        private const int MaximumOwnerWalk = 8;
        private const uint MaximumEntityIndexExclusive = 134_217_728;
        private const float HoverWidth = 2f;

        private readonly List<PointedPlace> m_Draft = new List<PointedPlace>();
        private ToolRaycastSystem m_Raycast;
        private OverlayRenderSystem m_Overlay;
        private string m_Mode = "";
        private int m_NextId = 1;
        private int m_NextPoint = 1;

        public override string toolID => "airimayor.PointedPlace";

        public string Mode => m_ToolSystem.activeTool == this ? m_Mode : "";

        public string Notice { get; private set; } = "";

        public override PrefabBase GetPrefab()
        {
            return null;
        }

        public override bool TrySetPrefab(PrefabBase prefab)
        {
            return false;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Raycast = World.GetOrCreateSystemManaged<ToolRaycastSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_Raycast.typeMask = TypeMask.StaticObjects | TypeMask.Net | TypeMask.Terrain;
            m_Raycast.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
            m_Raycast.netLayerMask = Layer.Road | Layer.PublicTransportRoad | Layer.Pathway
                | Layer.WaterPipe | Layer.SewagePipe | Layer.PowerlineLow;
            m_Raycast.raycastFlags = RaycastFlags.SubElements | RaycastFlags.SubBuildings | RaycastFlags.BuildingLots;
        }

        public void Begin(string mode)
        {
            if (mode != "place")
            {
                Disarm();
                return;
            }
            if (World.GetOrCreateSystemManaged<BridgeToolSystem>().IsBusy)
            {
                Notice = "busy";
                return;
            }
            if (m_ToolSystem.activeTool == this && m_Mode == mode)
            {
                Disarm();
                return;
            }
            m_Mode = mode;
            Notice = "";
            m_ToolSystem.activeTool = this;
        }

        /// <summary>
        /// Hands the tool back and parks input actions. Arming owns the
        /// enable because mods cannot call the framework UpdateActions.
        /// </summary>
        private void Disarm()
        {
            applyAction.shouldBeEnabled = false;
            cancelAction.shouldBeEnabled = false;
            if (m_ToolSystem != null && m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
            }
        }

        public void Remove(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }
            for (int i = m_Draft.Count - 1; i >= 0; i--)
            {
                if (m_Draft[i].Id == id)
                {
                    m_Draft.RemoveAt(i);
                }
            }
            Notice = "";
        }

        public List<PointedPlace> TakeDraft()
        {
            var taken = new List<PointedPlace>(m_Draft);
            m_Draft.Clear();
            Notice = "";
            return taken;
        }

        public void ClearDraft()
        {
            m_Draft.Clear();
            Notice = "";
            Disarm();
        }

        public string DraftJson()
        {
            return PointedPlaceText.ToJson(m_Draft);
        }

        /// <summary>
        /// Moves the gameplay camera to the place. Returns a notice code when
        /// a building or road is gone; the caller tells the player.
        /// </summary>
        public string Frame(PointedPlace place)
        {
            if (place == null)
            {
                return "";
            }
            if (place.Kind == "point")
            {
                LookAt(place.X, place.Z, 140f);
                Notice = "";
                return "";
            }
            if (!TryResolve(place.Index, place.Version, out Entity entity) || !StillListed(entity, place.Kind))
            {
                Notice = MissingCode(place.Kind);
                return Notice;
            }
            if (place.Kind == "building")
            {
                Transform transform = EntityManager.GetComponentData<Transform>(entity);
                LookAt(transform.m_Position.x, transform.m_Position.z, BuildingZoom(entity));
                Notice = "";
                return "";
            }
            Curve curve = EntityManager.GetComponentData<Curve>(entity);
            float3 middle = (curve.m_Bezier.a + curve.m_Bezier.d) * 0.5f;
            LookAt(middle.x, middle.z, math.clamp(curve.m_Length * 0.8f, 80f, 700f));
            Notice = "";
            return "";
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            JobHandle handle = base.OnUpdate(inputDeps);
            if (m_ToolSystem.activeTool != this)
            {
                return handle;
            }
            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            DrawHover();
            if (cancelAction.WasPressedThisFrame())
            {
                Disarm();
                return handle;
            }
            if (!applyAction.WasPressedThisFrame())
            {
                return handle;
            }
            if (!m_Raycast.GetRaycastResult(out RaycastResult result))
            {
                return handle;
            }
            if (TryDescribe(result.m_Hit.m_HitEntity, out PointedPlace place))
            {
                AddDescribed(place);
            }
            else
            {
                AddPoint(result.m_Hit.m_HitPosition);
            }
            return handle;
        }

        private void AddPoint(float3 position)
        {
            if (m_Draft.Count >= PointedPlaceText.Maximum)
            {
                Notice = "full";
                return;
            }
            if (!math.isfinite(position.x) || !math.isfinite(position.z))
            {
                return;
            }
            m_Draft.Add(new PointedPlace
            {
                Id = NextId(),
                Kind = "point",
                Name = "Point " + m_NextPoint.ToString(CultureInfo.InvariantCulture),
                X = position.x,
                Z = position.z,
            });
            m_NextPoint++;
            Notice = "";
        }

        private void AddDescribed(PointedPlace place)
        {
            if (m_Draft.Count >= PointedPlaceText.Maximum)
            {
                Notice = "full";
                return;
            }
            for (int i = 0; i < m_Draft.Count; i++)
            {
                PointedPlace existing = m_Draft[i];
                if (existing.Kind != "point" && existing.Index == place.Index && existing.Version == place.Version)
                {
                    Notice = "";
                    return;
                }
            }
            place.Id = NextId();
            m_Draft.Add(place);
            Notice = "";
        }

        /// <summary>
        /// Hover outline while armed (MoveIt recipe). Overlay lines are
        /// fire-and-forget; ground draws nothing.
        /// </summary>
        private void DrawHover()
        {
            if (!m_Raycast.GetRaycastResult(out RaycastResult result))
            {
                return;
            }
            if (!TryDescribe(result.m_Hit.m_HitEntity, out PointedPlace place)
                || !TryResolve(place.Index, place.Version, out Entity entity))
            {
                return;
            }
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out _);
            if (place.Kind == "building")
            {
                DrawBuildingOutline(buffer, entity);
            }
            else
            {
                DrawEdgeOutline(buffer, entity);
            }
            m_Overlay.AddBufferWriter(default);
        }

        private void DrawBuildingOutline(OverlayRenderSystem.Buffer buffer, Entity entity)
        {
            if (!EntityManager.HasComponent<Transform>(entity)
                || !EntityManager.HasComponent<PrefabRef>(entity))
            {
                return;
            }
            Transform transform = EntityManager.GetComponentData<Transform>(entity);
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (!EntityManager.Exists(prefab) || !EntityManager.HasComponent<BuildingData>(prefab))
            {
                return;
            }
            int2 lot = EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize;
            float halfX = lot.x * 4f;
            float halfZ = lot.y * 4f;
            float3 baseCenter = new float3(transform.m_Position.x, transform.m_Position.y, transform.m_Position.z);
            float3 a = Corner(baseCenter, transform.m_Rotation, -halfX, -halfZ);
            float3 b = Corner(baseCenter, transform.m_Rotation, halfX, -halfZ);
            float3 c = Corner(baseCenter, transform.m_Rotation, halfX, halfZ);
            float3 d = Corner(baseCenter, transform.m_Rotation, -halfX, halfZ);
            DrawSegment(buffer, a, b);
            DrawSegment(buffer, b, c);
            DrawSegment(buffer, c, d);
            DrawSegment(buffer, d, a);
        }

        private void DrawEdgeOutline(OverlayRenderSystem.Buffer buffer, Entity entity)
        {
            if (!EntityManager.HasComponent<Curve>(entity))
            {
                return;
            }
            Bezier4x3 bezier = EntityManager.GetComponentData<Curve>(entity).m_Bezier;
            buffer.DrawCurve(
                new Color(0.3f, 0.75f, 0.95f),
                bezier,
                HoverWidth);
        }

        private static float3 Corner(float3 center, quaternion rotation, float x, float z)
        {
            float3 offset = math.mul(rotation, new float3(x, 0f, z));
            offset.y = 0f;
            return center + offset;
        }

        private static void DrawSegment(OverlayRenderSystem.Buffer buffer, float3 from, float3 to)
        {
            // Run each end half a width past the corner so the bands
            // close the joint instead of leaving a gap there.
            float3 direction = to - from;
            float length = math.length(direction);
            if (length > HoverWidth)
            {
                float3 step = direction / length * (HoverWidth / 2f);
                from -= step;
                to += step;
            }
            buffer.DrawLine(
                new Color(0.3f, 0.75f, 0.95f),
                new Line3.Segment(from, to),
                HoverWidth,
                false);
        }

        private string NextId()
        {
            string id = "p" + m_NextId.ToString(CultureInfo.InvariantCulture);
            m_NextId++;
            return id;
        }

        private bool TryDescribe(Entity hit, out PointedPlace place)
        {
            place = null;
            Entity current = hit;
            for (int step = 0; step < MaximumOwnerWalk && current != Entity.Null && EntityManager.Exists(current); step++)
            {
                if (EntityManager.HasComponent<Temp>(current) || EntityManager.HasComponent<Deleted>(current))
                {
                    return false;
                }
                if (IsListedBuilding(current))
                {
                    place = DescribeBuilding(current);
                    return true;
                }
                if (IsListedEdge(current, out PointedPlace edge))
                {
                    place = edge;
                    return true;
                }
                if (!EntityManager.HasComponent<Owner>(current))
                {
                    return false;
                }
                current = EntityManager.GetComponentData<Owner>(current).m_Owner;
            }
            return false;
        }

        private bool IsListedBuilding(Entity entity)
        {
            return EntityManager.HasComponent<Game.Buildings.Building>(entity)
                && EntityManager.HasComponent<Transform>(entity)
                && EntityManager.HasComponent<PrefabRef>(entity);
        }

        private bool IsListedEdge(Entity entity, out PointedPlace place)
        {
            place = null;
            if (EntityManager.HasComponent<Owner>(entity)
                || !EntityManager.HasComponent<Edge>(entity)
                || !EntityManager.HasComponent<Curve>(entity)
                || !EntityManager.HasComponent<PrefabRef>(entity))
            {
                return false;
            }
            string kind = KindOf(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
            if (kind == null)
            {
                return false;
            }
            Curve curve = EntityManager.GetComponentData<Curve>(entity);
            place = new PointedPlace
            {
                Kind = kind,
                Name = PrefabName(entity, kind == "road" ? "Road" : "Line"),
                X = curve.m_Bezier.a.x,
                Z = curve.m_Bezier.a.z,
                EndX = curve.m_Bezier.d.x,
                EndZ = curve.m_Bezier.d.z,
                HasEnd = true,
                Index = entity.Index,
                Version = entity.Version,
            };
            return true;
        }

        private PointedPlace DescribeBuilding(Entity entity)
        {
            Transform transform = EntityManager.GetComponentData<Transform>(entity);
            return new PointedPlace
            {
                Kind = "building",
                Name = PrefabName(entity, "Building"),
                X = transform.m_Position.x,
                Z = transform.m_Position.z,
                Index = entity.Index,
                Version = entity.Version,
            };
        }

        private string PrefabName(Entity entity, string fallback)
        {
            if (!EntityManager.HasComponent<PrefabRef>(entity))
            {
                return fallback;
            }
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (!EntityManager.Exists(prefab))
            {
                return fallback;
            }
            PrefabBase asset = World.GetOrCreateSystemManaged<PrefabSystem>().GetPrefab<PrefabBase>(prefab);
            if (asset == null || string.IsNullOrWhiteSpace(asset.name))
            {
                return fallback;
            }
            return asset.name;
        }

        private string KindOf(Entity prefabEntity)
        {
            if (!EntityManager.Exists(prefabEntity))
            {
                return null;
            }
            bool isRoad = EntityManager.HasComponent<RoadData>(prefabEntity);
            bool water = false;
            bool sewage = false;
            bool lowVoltage = false;
            if (EntityManager.HasComponent<NetData>(prefabEntity))
            {
                Layer layers = EntityManager.GetComponentData<NetData>(prefabEntity).m_RequiredLayers;
                water = (layers & Layer.WaterPipe) != 0;
                sewage = (layers & Layer.SewagePipe) != 0;
                lowVoltage = (layers & Layer.PowerlineLow) != 0;
            }
            if (EntityManager.HasComponent<WaterPipeConnectionData>(prefabEntity))
            {
                WaterPipeConnectionData pipe = EntityManager.GetComponentData<WaterPipeConnectionData>(prefabEntity);
                water |= pipe.m_FreshCapacity > 0;
                sewage |= pipe.m_SewageCapacity > 0;
            }
            if (EntityManager.HasComponent<ElectricityConnectionData>(prefabEntity))
            {
                lowVoltage |= EntityManager.GetComponentData<ElectricityConnectionData>(prefabEntity).m_Voltage
                    == Game.Prefabs.ElectricityConnection.Voltage.Low;
            }
            TypedNetworkKinds kinds = TypedNetworkMath.Classify(isRoad, water, sewage, lowVoltage);
            if ((kinds & TypedNetworkKinds.Road) != 0)
            {
                return "road";
            }
            if ((kinds & TypedNetworkKinds.Water) != 0)
            {
                return "water";
            }
            if ((kinds & TypedNetworkKinds.Sewage) != 0)
            {
                return "sewage";
            }
            if ((kinds & TypedNetworkKinds.LowVoltage) != 0)
            {
                return "cable";
            }
            return null;
        }

        private bool TryResolve(int index, int version, out Entity entity)
        {
            entity = Entity.Null;
            if ((uint)index >= MaximumEntityIndexExclusive)
            {
                return false;
            }
            var candidate = new Entity { Index = index, Version = version };
            if (!EntityManager.Exists(candidate))
            {
                return false;
            }
            entity = candidate;
            return true;
        }

        private bool StillListed(Entity entity, string kind)
        {
            if (EntityManager.HasComponent<Temp>(entity) || EntityManager.HasComponent<Deleted>(entity))
            {
                return false;
            }
            if (kind == "building")
            {
                return IsListedBuilding(entity);
            }
            return IsListedEdge(entity, out _);
        }

        private static string MissingCode(string kind)
        {
            if (kind == "building")
            {
                return "missing-building";
            }
            if (kind == "road")
            {
                return "missing-road";
            }
            return "missing-line";
        }

        private float BuildingZoom(Entity entity)
        {
            const float fallback = 160f;
            if (!EntityManager.HasComponent<PrefabRef>(entity))
            {
                return fallback;
            }
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (!EntityManager.HasComponent<BuildingData>(prefab))
            {
                return fallback;
            }
            int2 lot = EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize;
            float span = math.max(lot.x, lot.y) * 8f;
            return math.clamp(span * 2.5f, 50f, 500f);
        }

        private void LookAt(float x, float z, float zoom)
        {
            CameraUpdateSystem cameraSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            CameraController controller = cameraSystem.gamePlayController;
            if (controller == null)
            {
                return;
            }
            TerrainHeightData heightData = World.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData();
            float y = TerrainUtils.SampleHeight(ref heightData, new float3(x, 0f, z));
            controller.pivot = new Vector3(x, y, z);
            controller.zoom = math.clamp(zoom, 10f, 10000f);
        }
    }
}
