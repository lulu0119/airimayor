using Unity.Entities;
using Unity.Mathematics;

namespace CS2MCP
{
    internal readonly struct RoadConnection
    {
        public RoadConnection(Entity entity, float split = 0f)
        {
            Entity = entity;
            Split = split;
        }

        public Entity Entity { get; }
        public float Split { get; }
    }

    /// <summary>A resolved world-space course. The native adapter must not fit or lift it again.</summary>
    internal sealed class CompiledRoadCourse
    {
        private CompiledRoadCourse(RoadPath path, RoadBuildMode mode, float2 elevations,
            RoadConnection start, RoadConnection end)
        {
            Path = path;
            Mode = mode;
            Elevations = elevations;
            Start = start;
            End = end;
        }

        public RoadPath Path { get; }
        public RoadBuildMode Mode { get; }
        public float2 Elevations { get; }
        public RoadConnection Start { get; }
        public RoadConnection End { get; }

        public static bool TryCreate(RoadPath path, RoadBuildMode mode, float2 elevations,
            RoadConnection start, RoadConnection end, out CompiledRoadCourse course, out string error)
        {
            course = null;
            error = null;
            if (!math.all(math.isfinite(path.A)) || !math.all(math.isfinite(path.B))
                || !math.all(math.isfinite(path.C)) || !math.all(math.isfinite(path.D))
                || !math.all(math.isfinite(elevations)))
            {
                error = "road coordinates and elevations must be finite";
                return false;
            }
            if (mode != RoadBuildMode.Ground && mode != RoadBuildMode.GradeSeparated)
            {
                error = "unknown road mode";
                return false;
            }
            if (math.lengthsq(path.B.xz - path.A.xz) < 0.000001f
                || math.lengthsq(path.D.xz - path.C.xz) < 0.000001f)
            {
                error = "road endpoints need a horizontal tangent";
                return false;
            }
            float controlLength = math.distance(path.A, path.B)
                + math.distance(path.B, path.C) + math.distance(path.C, path.D);
            if (controlLength < 8f || controlLength > 4500f)
            {
                error = "course control polygon must be between 8 and 4500 meters; split longer courses";
                return false;
            }
            if (math.any(elevations < -30f) || math.any(elevations > 60f)
                || (mode == RoadBuildMode.Ground && math.any(elevations != 0f))
                || (mode == RoadBuildMode.GradeSeparated && math.all(elevations == 0f)))
            {
                error = "ground endpoints require zero elevation; grade-separated courses require a nonzero endpoint in -30..60 meters";
                return false;
            }
            if (!ValidConnection(start) || !ValidConnection(end))
            {
                error = "connection split must be finite and in 0..1; an unbound endpoint has split zero";
                return false;
            }
            course = new CompiledRoadCourse(path, mode, elevations, start, end);
            return true;
        }

        private static bool ValidConnection(RoadConnection connection)
        {
            return math.isfinite(connection.Split) && connection.Split >= 0f && connection.Split <= 1f
                && (connection.Entity != Entity.Null || connection.Split == 0f);
        }
    }
}
