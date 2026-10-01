using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace CS2MCP
{
    /// <summary>
    /// The validated model-facing options for one build_network call. This module
    /// owns the distinctions between omitted, malformed and incompatible
    /// values so the ECS handler only has to construct the requested course.
    /// </summary>
    internal readonly struct NetworkBuildArguments
    {
        private NetworkBuildArguments(
            RoadBuildMode? roadMode,
            NetworkCourseShape shape,
            bool hasDeparture,
            float departure,
            bool hasArrival,
            float arrival,
            bool hasMidpoint,
            float midX,
            float midZ,
            bool hasOffset,
            float offset,
            bool hasRaise,
            float raise,
            NetworkElevationProfile profile,
            float easeStart,
            float easeEnd,
            float archHeight,
            float archAt,
            bool hasElevation,
            float startElevation,
            float endElevation)
        {
            RoadMode = roadMode;
            Shape = shape;
            HasDeparture = hasDeparture;
            Departure = departure;
            HasArrival = hasArrival;
            Arrival = arrival;
            HasMidpoint = hasMidpoint;
            MidX = midX;
            MidZ = midZ;
            HasOffset = hasOffset;
            Offset = offset;
            HasRaise = hasRaise;
            Raise = raise;
            Profile = profile;
            EaseStart = easeStart;
            EaseEnd = easeEnd;
            ArchHeight = archHeight;
            ArchAt = archAt;
            HasElevation = hasElevation;
            StartElevation = startElevation;
            EndElevation = endElevation;
        }

        public RoadBuildMode? RoadMode { get; }
        public NetworkCourseShape Shape { get; }
        public bool HasDeparture { get; }
        public float Departure { get; }
        public bool HasArrival { get; }
        public float Arrival { get; }
        public bool HasMidpoint { get; }
        public float MidX { get; }
        public float MidZ { get; }
        public bool HasOffset { get; }
        public float Offset { get; }
        public bool HasRaise { get; }
        public float Raise { get; }
        public NetworkElevationProfile Profile { get; }
        public float EaseStart { get; }
        public float EaseEnd { get; }
        public float ArchHeight { get; }
        public float ArchAt { get; }
        public bool HasElevation { get; }
        public float StartElevation { get; }
        public float EndElevation { get; }

        public float ResolvedDeparture(float2 startDirection, float2 endHandleDirection)
        {
            return HasDeparture
                ? Departure
                : NetworkCourseMath.DefaultReach(startDirection, endHandleDirection);
        }

        public float ResolvedArrival(float2 startDirection, float2 endHandleDirection)
        {
            return HasArrival
                ? Arrival
                : NetworkCourseMath.DefaultReach(startDirection, endHandleDirection);
        }

        public static bool TryParse(
            IReadOnlyDictionary<string, string> query,
            bool isRoad,
            out NetworkBuildArguments arguments,
            out string error)
        {
            arguments = default;
            error = null;
            if (query.ContainsKey("cx") || query.ContainsKey("cz"))
            {
                error = "cx and cz are removed; use shape=simple or shape=complex";
                return false;
            }

            if (!TryParseShape(query, isRoad, out NetworkCourseShape shape, out error)
                || !TryParseMode(query, isRoad, shape, out RoadBuildMode? roadMode, out error)
                || !TryParseReaches(query, shape, out bool hasDeparture, out float departure, out bool hasArrival, out float arrival, out error)
                || !TryParseMidpoint(query, shape, out bool hasMidpoint, out float midX, out float midZ, out error)
                || !TryParseOffset(query, shape, out bool hasOffset, out float offset, out bool hasRaise, out float raise, out error)
                || !TryParseElevations(query, isRoad, roadMode, out bool hasElevation, out float startElevation, out float endElevation, out error)
                || !TryParseProfile(
                    query,
                    roadMode,
                    shape,
                    out NetworkElevationProfile profile,
                    out float easeStart,
                    out float easeEnd,
                    out float archHeight,
                    out float archAt,
                    out error))
            {
                return false;
            }

            arguments = new NetworkBuildArguments(
                roadMode,
                shape,
                hasDeparture,
                departure,
                hasArrival,
                arrival,
                hasMidpoint,
                midX,
                midZ,
                hasOffset,
                offset,
                hasRaise,
                raise,
                profile,
                easeStart,
                easeEnd,
                archHeight,
                archAt,
                hasElevation,
                startElevation,
                endElevation);
            return true;
        }

        private static bool TryParseShape(
            IReadOnlyDictionary<string, string> query,
            bool isRoad,
            out NetworkCourseShape shape,
            out string error)
        {
            shape = NetworkCourseShape.Straight;
            error = null;
            if (!query.TryGetValue("shape", out string raw))
            {
                return true;
            }

            string normalized = Normalize(raw);
            if (string.Equals(normalized, "straight", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(normalized, "simple", StringComparison.OrdinalIgnoreCase))
            {
                shape = NetworkCourseShape.Simple;
                return true;
            }
            if (string.Equals(normalized, "complex", StringComparison.OrdinalIgnoreCase))
            {
                shape = NetworkCourseShape.Complex;
                return true;
            }
            if (string.Equals(normalized, "parallel", StringComparison.OrdinalIgnoreCase))
            {
                if (!isRoad)
                {
                    error = "parallel copies a road; omit shape for pipes, cables and other utility networks";
                    return false;
                }
                shape = NetworkCourseShape.Parallel;
                return true;
            }

            error = "shape must be 'straight', 'simple', 'complex', or 'parallel'";
            return false;
        }

        private static bool TryParseMode(
            IReadOnlyDictionary<string, string> query,
            bool isRoad,
            NetworkCourseShape shape,
            out RoadBuildMode? mode,
            out string error)
        {
            bool provided = query.TryGetValue("mode", out string rawMode);
            mode = null;
            error = null;
            if (shape == NetworkCourseShape.Parallel)
            {
                if (provided)
                {
                    error = "parallel does not take mode; it copies the road between the two nodes";
                    return false;
                }
                return true;
            }

            if (!isRoad)
            {
                if (provided)
                {
                    error = "mode is only valid for road prefabs; omit it for pipes, cables, power lines and other utility networks";
                    return false;
                }
                return true;
            }

            if (!provided)
            {
                mode = RoadBuildMode.Ground;
                return true;
            }

            string normalized = Normalize(rawMode);
            if (string.Equals(normalized, "ground", StringComparison.OrdinalIgnoreCase))
            {
                mode = RoadBuildMode.Ground;
                return true;
            }
            if (string.Equals(normalized, "grade-separated", StringComparison.OrdinalIgnoreCase))
            {
                mode = RoadBuildMode.GradeSeparated;
                return true;
            }

            error = "mode must be 'ground' or 'grade-separated' for road prefabs";
            return false;
        }

        private static bool TryParseReaches(
            IReadOnlyDictionary<string, string> query,
            NetworkCourseShape shape,
            out bool hasDeparture,
            out float departure,
            out bool hasArrival,
            out float arrival,
            out string error)
        {
            hasDeparture = query.TryGetValue("departure", out string rawDeparture);
            hasArrival = query.TryGetValue("arrival", out string rawArrival);
            departure = 0f;
            arrival = 0f;
            error = null;
            bool curve = shape == NetworkCourseShape.Simple || shape == NetworkCourseShape.Complex;
            if (!curve && (hasDeparture || hasArrival))
            {
                error = "departure and arrival are only valid for shape=simple or shape=complex";
                return false;
            }
            if (hasDeparture && !TryParseUnit(rawDeparture, out departure))
            {
                error = "departure must be from 0 to 1";
                return false;
            }
            if (hasArrival && !TryParseUnit(rawArrival, out arrival))
            {
                error = "arrival must be from 0 to 1";
                return false;
            }
            return true;
        }

        private static bool TryParseMidpoint(
            IReadOnlyDictionary<string, string> query,
            NetworkCourseShape shape,
            out bool hasMidpoint,
            out float midX,
            out float midZ,
            out string error)
        {
            bool hasX = query.TryGetValue("mx", out string rawX);
            bool hasZ = query.TryGetValue("mz", out string rawZ);
            hasMidpoint = false;
            midX = 0f;
            midZ = 0f;
            error = null;
            if (shape != NetworkCourseShape.Complex && (hasX || hasZ))
            {
                error = "mx and mz are only valid for shape=complex";
                return false;
            }
            if (hasX != hasZ)
            {
                error = "provide both mx and mz to move the joint, or omit both";
                return false;
            }
            if (!hasX)
            {
                return true;
            }
            if (!TryParseFinite(rawX, out midX) || !TryParseFinite(rawZ, out midZ))
            {
                error = "mx and mz must both be finite world coordinates";
                return false;
            }

            hasMidpoint = true;
            return true;
        }

        private static bool TryParseOffset(
            IReadOnlyDictionary<string, string> query,
            NetworkCourseShape shape,
            out bool hasOffset,
            out float offset,
            out bool hasRaise,
            out float raise,
            out string error)
        {
            hasOffset = query.TryGetValue("offset", out string rawOffset);
            hasRaise = query.TryGetValue("raise", out string rawRaise);
            offset = 0f;
            raise = 0f;
            error = null;
            if (shape != NetworkCourseShape.Parallel && (hasOffset || hasRaise))
            {
                error = "offset and raise are only valid for shape=parallel";
                return false;
            }
            if (shape == NetworkCourseShape.Parallel && !hasOffset)
            {
                error = "parallel requires offset in meters (-80..80); positive is to the right walking from the start to the end";
                return false;
            }
            if (hasOffset && !TryParseRange(rawOffset, -80f, 80f, out offset))
            {
                error = "offset must be from -80 to 80 meters";
                return false;
            }
            if (hasRaise && !TryParseRange(rawRaise, -80f, 80f, out raise))
            {
                error = "raise must be from -80 to 80 meters";
                return false;
            }
            return true;
        }

        private static bool TryParseElevations(
            IReadOnlyDictionary<string, string> query,
            bool isRoad,
            RoadBuildMode? roadMode,
            out bool hasElevation,
            out float startElevation,
            out float endElevation,
            out string error)
        {
            bool hasStart = query.TryGetValue("e1", out string rawStart);
            bool hasEnd = query.TryGetValue("e2", out string rawEnd);
            hasElevation = hasStart || hasEnd;
            startElevation = 0f;
            endElevation = 0f;
            error = null;

            if (roadMode == null && isRoad && hasElevation)
            {
                error = "parallel does not take e1 or e2";
                return false;
            }
            if (hasStart && !TryParseFinite(rawStart, out startElevation))
            {
                error = "e1 must be a finite elevation in meters";
                return false;
            }
            if (hasEnd && !TryParseFinite(rawEnd, out endElevation))
            {
                error = "e2 must be a finite elevation in meters";
                return false;
            }
            if (hasStart && (startElevation < -30f || startElevation > 60f))
            {
                error = $"e1={startElevation:F0} out of range; e1/e2 are elevation in meters relative to terrain (-30..60), not entity indexes.";
                return false;
            }
            if (hasEnd && (endElevation < -30f || endElevation > 60f))
            {
                error = $"e2={endElevation:F0} out of range; e1/e2 are elevation in meters relative to terrain (-30..60), not entity indexes.";
                return false;
            }

            if (!isRoad)
            {
                return true;
            }
            if (roadMode == RoadBuildMode.Ground && hasElevation)
            {
                error = "mode=ground does not accept e1/e2; omit elevation for an ordinary road, or explicitly use mode=grade-separated with both e1/e2";
                return false;
            }
            if (roadMode != RoadBuildMode.GradeSeparated)
            {
                return true;
            }
            if (!hasStart || !hasEnd)
            {
                error = "mode=grade-separated requires both e1 and e2 elevation values";
                return false;
            }
            if (startElevation == 0f && endElevation == 0f)
            {
                error = "mode=grade-separated requires a nonzero elevation at one or both endpoints; positive is elevated/bridge and negative is underground";
                return false;
            }

            return true;
        }

        private static bool TryParseProfile(
            IReadOnlyDictionary<string, string> query,
            RoadBuildMode? roadMode,
            NetworkCourseShape shape,
            out NetworkElevationProfile profile,
            out float easeStart,
            out float easeEnd,
            out float archHeight,
            out float archAt,
            out string error)
        {
            bool hasProfile = query.TryGetValue("profile", out string rawProfile);
            bool hasEaseStart = query.TryGetValue("easeStart", out string rawEaseStart);
            bool hasEaseEnd = query.TryGetValue("easeEnd", out string rawEaseEnd);
            bool hasArchHeight = query.TryGetValue("archHeight", out string rawArchHeight);
            bool hasArchAt = query.TryGetValue("archAt", out string rawArchAt);
            profile = NetworkElevationProfile.Linear;
            easeStart = 0.1f;
            easeEnd = 0.1f;
            archHeight = 0f;
            archAt = 0.5f;
            error = null;

            bool gradeSeparated = roadMode == RoadBuildMode.GradeSeparated;
            if (!gradeSeparated && (hasProfile || hasEaseStart || hasEaseEnd || hasArchHeight || hasArchAt))
            {
                error = shape == NetworkCourseShape.Parallel
                    ? "parallel does not take profile"
                    : "profile, easeStart, easeEnd, archHeight and archAt require mode=grade-separated";
                return false;
            }
            if (!hasProfile)
            {
                if (hasEaseStart || hasEaseEnd || hasArchHeight || hasArchAt)
                {
                    error = "easeStart and easeEnd require profile=ease; archHeight and archAt require profile=arch";
                    return false;
                }
                return true;
            }

            string normalized = Normalize(rawProfile);
            if (string.Equals(normalized, "linear", StringComparison.OrdinalIgnoreCase))
            {
                if (hasEaseStart || hasEaseEnd || hasArchHeight || hasArchAt)
                {
                    error = "profile=linear does not take easeStart, easeEnd, archHeight or archAt";
                    return false;
                }
                return true;
            }
            if (string.Equals(normalized, "ease", StringComparison.OrdinalIgnoreCase))
            {
                profile = NetworkElevationProfile.Ease;
                if (hasArchHeight || hasArchAt)
                {
                    error = "profile=ease does not take archHeight or archAt";
                    return false;
                }
                if (hasEaseStart && !TryParseRange(rawEaseStart, 0f, 0.5f, out easeStart))
                {
                    error = "easeStart must be from 0 to 0.5";
                    return false;
                }
                if (hasEaseEnd && !TryParseRange(rawEaseEnd, 0f, 0.5f, out easeEnd))
                {
                    error = "easeEnd must be from 0 to 0.5";
                    return false;
                }
                if (easeStart + easeEnd >= 1f)
                {
                    error = "easeStart and easeEnd must leave a middle section of the ramp";
                    return false;
                }
                return true;
            }
            if (string.Equals(normalized, "arch", StringComparison.OrdinalIgnoreCase))
            {
                profile = NetworkElevationProfile.Arch;
                if (hasEaseStart || hasEaseEnd)
                {
                    error = "profile=arch does not take easeStart or easeEnd";
                    return false;
                }
                if (!hasArchHeight || !TryParseRange(rawArchHeight, -80f, 80f, out archHeight))
                {
                    error = "profile=arch requires archHeight in meters (-80..80)";
                    return false;
                }
                if (hasArchAt && !TryParseRange(rawArchAt, 0.1f, 0.9f, out archAt))
                {
                    error = "archAt must be from 0.1 to 0.9";
                    return false;
                }
                return true;
            }

            error = "profile must be 'linear', 'ease', or 'arch'";
            return false;
        }

        private static string Normalize(string raw)
        {
            return (raw ?? string.Empty).Trim();
        }

        private static bool TryParseUnit(string raw, out float value)
        {
            return TryParseRange(raw, 0f, 1f, out value);
        }

        private static bool TryParseRange(string raw, float minimum, float maximum, out float value)
        {
            if (!TryParseFinite(raw, out value))
            {
                return false;
            }
            return value >= minimum && value <= maximum;
        }

        private static bool TryParseFinite(string raw, out float value)
        {
            return float.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value)
                && !float.IsNaN(value)
                && !float.IsInfinity(value);
        }
    }
}
