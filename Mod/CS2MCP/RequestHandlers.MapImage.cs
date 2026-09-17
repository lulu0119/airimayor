using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using CitiesSkylines2Agent.Agent;
using Game.Simulation;
using Newtonsoft.Json.Linq;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CS2MCP
{
    /// <summary>
    /// Undistorted city map rasterized from the Carto mod's own export.
    /// Step 1 is rasterization only: no native overlay. The interface takes
    /// an optional map range (same convention as map_text); the
    /// implementation owns explicit Carto options (a default Options exports
    /// nothing), deterministic file names, a light cartographic style, and
    /// in-process rasterization of the returned files to PNG. Requested
    /// ranges are converted with Carto's own Transform, the exact function
    /// its writers use, so clipping lands in the right place.
    /// Read-only: runs on the simulation thread like other perception tools.
    /// Shares the vision switch with screenshot (gated in AgentToolSurface).
    /// Option shapes below are grounded in Carto's IO/Options.cs,
    /// IO/ExportResult.cs, IO/IO.cs, IO/GeoJson.cs, Domain/Enums.cs and
    /// Systems/NetworkSystem.cs (MIT).
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private const int kMapWidth = 1280;
        private const int kMapMaxHeight = 1920;
        private const int kMapMaxSegments = 60000;
        private const string kMapFileName = "map_{Feature}";

        private BridgeResponse MapImage(BridgeRequest request)
        {
            // TEMPORARY incident gate: the Carto export behind this path
            // freezes the game (under diagnosis). Text map_text is unaffected.
            // Remove when the hang is fixed.
            if (!CitiesSkylines2Agent.Setting.StaticEnableDevelopmentTools)
            {
                return BridgeResponse.Error(BridgeErrorKind.Unavailable,
                    "map image is temporarily disabled during diagnosis; use map_text");
            }
            DebugPhase("handler-enter");
            if (!TryGetCity(out _, out BridgeResponse cityError))
            {
                return cityError;
            }

            bool hasBounds = request.Query.ContainsKey("x") || request.Query.ContainsKey("z")
                || request.Query.ContainsKey("radius") || request.Query.ContainsKey("xMin")
                || request.Query.ContainsKey("zMin") || request.Query.ContainsKey("xMax")
                || request.Query.ContainsKey("zMax");
            float xMin = 0f;
            float zMin = 0f;
            float xMax = 0f;
            float zMax = 0f;
            if (hasBounds
                && !TryGetWorldBounds(request, out xMin, out zMin, out xMax, out zMax, out BridgeResponse boundsError))
            {
                return boundsError;
            }

            List<string> exported;
            CartoProjector projector;
            var exportTimer = Stopwatch.StartNew();
            DebugPhase("export-invoke-enter");
            BridgeResponse exportError = InvokeCartoExport(out exported, out projector);
            DebugPhase("export-invoke-returned");
            exportTimer.Stop();
            Mod.Log.Info($"[DEBUG-mapex1] export finished in {exportTimer.ElapsedMilliseconds}ms");
            if (exportError != null)
            {
                return exportError;
            }

            var files = new List<string>();
            foreach (string path in exported)
            {
                // Carto names vector output *.json (GeoJSON); accept *.geojson too.
                if (!string.IsNullOrWhiteSpace(path)
                    && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(path);
                }
            }
            if (files.Count == 0)
            {
                return BridgeResponse.Error(BridgeErrorKind.Internal,
                    "Carto export wrote no GeoJSON files; enable vector layers in Carto settings and retry");
            }

            var strokes = new List<MapStroke>();
            var fills = new List<MapPolygon>();
            var parseTimer = Stopwatch.StartNew();
            foreach (string file in files)
            {
                MapLayerKind? layer = ClassifyExportFile(file);
                if (layer == null)
                {
                    continue;
                }
                try
                {
                    ParseVectorFile(file, layer.Value, strokes, fills);
                }
                catch (Exception e)
                {
                    return BridgeResponse.Error(BridgeErrorKind.Internal,
                        $"map render rejected '{Path.GetFileName(file)}': {e.GetType().Name}: {e.Message}");
                }
            }
            if (strokes.Count == 0 && fills.Count == 0)
            {
                return BridgeResponse.Error(BridgeErrorKind.Internal,
                    "Carto export contained no renderable LineString/Polygon features");
            }
            Mod.Log.Info($"map_image: {files.Count} files, {strokes.Count} strokes, {fills.Count} footprints" +
                (hasBounds ? $" (extent {xMin},{zMin} to {xMax},{zMax})" : " (citywide)"));
            parseTimer.Stop();
            Mod.Log.Info($"[DEBUG-mapex1] parse finished in {parseTimer.ElapsedMilliseconds}ms");
            DebugPhase($"parse-done strokes={strokes.Count} fills={fills.Count}");

            MapLayering.Result layering = AssignRoadLayers(strokes);
            DebugPhase($"layers-done segments={layering.Segments} pairs={layering.PairChecks} " +
                $"constraints={layering.Constraints} skipped={layering.SkippedCells}");
            Mod.Log.Info("[DEBUG-mapex1] layering finished");

            MapFrame frame;
            if (hasBounds)
            {
                if (projector == null)
                {
                    return BridgeResponse.Error(BridgeErrorKind.Unavailable,
                        "bounded map_image needs Carto's Transform API; update Carto and retry (citywide still works)");
                }
                frame = ProjectExtent(projector, xMin, zMin, xMax, zMax);
                if (frame == null)
                {
                    return BridgeResponse.Error(BridgeErrorKind.Internal,
                        "requested extent could not be projected; retry citywide or check Carto projection settings");
                }
            }
            else
            {
                frame = MapFrame.FromData(strokes, fills);
                if (frame == null)
                {
                    return BridgeResponse.Error(BridgeErrorKind.Internal,
                        "vector features carry no usable coordinates");
                }
            }

            Texture2D texture = null;
            try
            {
                // Water sampling needs game meters: degree/UTM frames stay
                // land-only instead of painting water from wrong coordinates.
                Func<double, double, bool> isWater =
                    FrameIsMeters(frame) ? BuildWaterSampler() : null;
                var rasterTimer = Stopwatch.StartNew();
                DebugPhase("raster-enter");
                texture = Rasterize(strokes, fills, frame, isWater, layering.Merges);
                rasterTimer.Stop();
                Mod.Log.Info($"[DEBUG-mapex1] raster finished in {rasterTimer.ElapsedMilliseconds}ms");
                DebugPhase("raster-done");
                byte[] png = ImageConversion.EncodeToPNG(texture);
                if (png == null || png.Length == 0)
                {
                    return BridgeResponse.Error(BridgeErrorKind.Internal, "map PNG encode failed");
                }
                Mod.Log.Info("[DEBUG-mapex1] map_image complete");
                DebugPhase("complete");
                return BridgeResponse.Png(png, ToolPreview.EncodeThumbnail(texture));
            }
            catch (Exception e)
            {
                return BridgeResponse.Error(BridgeErrorKind.Internal,
                    $"map render failed: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        /// <summary>
        /// Grade separation for road strokes (bridges over ground). Features
        /// without elevation stay on the ground layer and never constrain.
        /// </summary>
        private static MapLayering.Result AssignRoadLayers(List<MapStroke> strokes)
        {
            var features = new List<MapLayering.Feature>(strokes.Count);
            foreach (MapStroke stroke in strokes)
            {
                features.Add(new MapLayering.Feature
                {
                    X = stroke.X,
                    Y = stroke.Y,
                    Elev = stroke.HasElev ? stroke.Elev : 0.0,
                    HasElev = stroke.HasElev,
                });
            }
            MapLayering.Result result = MapLayering.ComputeLayers(features);
            for (int i = 0; i < strokes.Count && i < result.Layers.Length; i++)
            {
                strokes[i].Layer = result.Layers[i];
            }
            if (!result.Converged)
            {
                Mod.Log.Warn("map_image: road layer constraints did not converge; " +
                    "deep stacks are clamped at the top layer");
            }
            if (result.SkippedCells > 0)
            {
                Mod.Log.Warn($"map_image: skipped {result.SkippedCells} overfull hash cells; " +
                    "nearby overpasses may draw flat");
            }
            return result;
        }
        /// <summary>
        /// Game XZ to export-CRS projector, replicating exactly what Carto's
        /// writers do (center shift, then Transform.Apply). No CRS math lives
        /// here: the installed Carto's own function does the conversion, so
        /// clipping lands in the right place under any projection settings.
        /// </summary>
        private sealed class CartoProjector
        {
            private readonly object m_Center;
            private readonly MethodInfo m_Shift;
            private readonly MethodInfo m_Apply;
            private readonly object m_SourceCrs;
            private readonly object m_SourceDef;
            private readonly object m_TargetCrs;
            private readonly object m_TargetDef;
            private readonly FieldInfo m_FieldX;
            private readonly FieldInfo m_FieldY;

            public CartoProjector(
                object center, MethodInfo shift, MethodInfo apply,
                object sourceCrs, object sourceDef, object targetCrs, object targetDef,
                FieldInfo fieldX, FieldInfo fieldY)
            {
                m_Center = center;
                m_Shift = shift;
                m_Apply = apply;
                m_SourceCrs = sourceCrs;
                m_SourceDef = sourceDef;
                m_TargetCrs = targetCrs;
                m_TargetDef = targetDef;
                m_FieldX = fieldX;
                m_FieldY = fieldY;
            }

            public bool TryProject(double x, double z, out double px, out double py)
            {
                px = 0.0;
                py = 0.0;
                try
                {
                    object shifted = m_Shift.Invoke(m_Center, new object[] { x, 0.0, z });
                    object result = m_Apply.Invoke(null, new object[]
                    {
                        shifted, m_SourceCrs, m_TargetCrs, m_SourceDef, m_TargetDef,
                    });
                    double rx = (double)m_FieldX.GetValue(result);
                    double ry = (double)m_FieldY.GetValue(result);
                    if (double.IsNaN(rx) || double.IsNaN(ry)
                        || double.IsInfinity(rx) || double.IsInfinity(ry)
                        || rx == double.MaxValue || ry == double.MaxValue)
                    {
                        return false;
                    }
                    px = rx;
                    py = ry;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static bool TryBuildCartoProjector(
            object options, Type optionsType, Assembly carto, out CartoProjector projector, out string reason)
        {
            projector = null;
            reason = "unknown Carto Transform shape";
            Type coordType = carto.GetType("Carto.Geodata.Coord");
            Type crsType = carto.GetType("Carto.Geodata.CRS");
            Type transformType = carto.GetType("Carto.Geodata.Transform");
            Type projDefType = carto.GetType("Carto.Geodata.ProjectionDefinition")
                ?? carto.GetType("Carto.IO.ProjectionDefinition");
            if (coordType == null || crsType == null || transformType == null || projDefType == null)
            {
                return false;
            }

            MethodInfo shift = null;
            foreach (MethodInfo method in coordType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!string.Equals(method.Name, "Shift", StringComparison.Ordinal))
                {
                    continue;
                }
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 3
                    && parameters[0].ParameterType == typeof(double)
                    && parameters[1].ParameterType == typeof(double)
                    && parameters[2].ParameterType == typeof(double))
                {
                    shift = method;
                    break;
                }
            }
            MethodInfo apply = null;
            foreach (MethodInfo method in transformType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "Apply", StringComparison.Ordinal))
                {
                    continue;
                }
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 5
                    && parameters[0].ParameterType == coordType
                    && parameters[1].ParameterType == crsType
                    && parameters[2].ParameterType == crsType
                    && parameters[3].ParameterType == projDefType
                    && parameters[4].ParameterType == projDefType)
                {
                    apply = method;
                    break;
                }
            }
            FieldInfo fieldX = coordType.GetField("x", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo fieldY = coordType.GetField("y", BindingFlags.Public | BindingFlags.Instance);
            if (shift == null || apply == null || fieldX == null || fieldY == null
                || fieldX.FieldType != typeof(double) || fieldY.FieldType != typeof(double))
            {
                return false;
            }

            object center = InvokeZeroArg(options, "GetTMCoord");
            object sourceCrs = InvokeZeroArg(options, "GetTMProjection");
            object sourceDef = InvokeZeroArg(options, "GetTMProjectionDefinition");
            if (center == null || sourceCrs == null || sourceDef == null
                || sourceCrs.GetType() != crsType)
            {
                reason = "unknown Carto projection members on Options";
                return false;
            }
            object targetCrs;
            object targetDef;
            if (!TryGetTargetProjection(options, optionsType, carto, out targetCrs, out targetDef))
            {
                reason = "unknown Carto target projection lookup";
                return false;
            }
            projector = new CartoProjector(
                center, shift, apply, sourceCrs, sourceDef, targetCrs, targetDef, fieldX, fieldY);
            reason = null;
            return true;
        }

        private static object InvokeZeroArg(object target, string name)
        {
            try
            {
                MethodInfo method = target.GetType().GetMethod(
                    name, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (method == null)
                {
                    return null;
                }
                return method.Invoke(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetTargetProjection(
            object options, Type optionsType, Assembly carto, out object targetCrs, out object targetDef)
        {
            targetCrs = null;
            targetDef = null;
            try
            {
                Type ioUtils = carto.GetType("Carto.Utils.IOUtils");
                if (ioUtils != null)
                {
                    foreach (MethodInfo method in ioUtils.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (!string.Equals(method.Name, "GetTargetProjections", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length != 3 || parameters[0].ParameterType != optionsType)
                        {
                            continue;
                        }
                        var args = new object[] { options, null, null };
                        method.Invoke(null, args);
                        if (args[1] != null && args[2] != null)
                        {
                            targetCrs = args[1];
                            targetDef = args[2];
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to the raw properties below.
            }
            targetCrs = ReadMemberObject(options, "TargetProjection");
            targetDef = ReadMemberObject(options, "TargetProjectionDefinition");
            return targetCrs != null && targetDef != null;
        }

        private static object ReadMemberObject(object target, string name)
        {
            try
            {
                PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanRead)
                {
                    return property.GetValue(target, null);
                }
            }
            catch
            {
                // Unknown member; reported by the caller.
            }
            return null;
        }

        /// <summary>
        /// Layer identity comes from the file names this module constructed
        /// (kMapFileName expands to map_{System}_{VectorKind}.geojson),
        /// not from guessing Carto's layout.
        /// </summary>
        private static MapLayerKind? ClassifyExportFile(string path)
        {
            string name = Path.GetFileName(path).ToLowerInvariant();
            if (name.Contains("_network_"))
            {
                return MapLayerKind.Network;
            }
            if (name.Contains("_building_"))
            {
                return MapLayerKind.Building;
            }
            if (name.Contains("_route_"))
            {
                return MapLayerKind.Route;
            }
            return null;
        }

        /// <summary>
        /// Returns null on success (files in the out list), else the error
        /// response to return. Silent export: never pops Carto's completion
        /// dialog or sound. Options are explicit: a default Options has
        /// Systems=Unknown and exports nothing.
        /// </summary>
        private static BridgeResponse InvokeCartoExport(out List<string> files, out CartoProjector projector)
        {
            files = new List<string>();
            projector = null;
            Type ioType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    ioType = assembly.GetType("Carto.IO.IO", throwOnError: false, ignoreCase: false);
                }
                catch
                {
                    ioType = null;
                }
                if (ioType != null)
                {
                    break;
                }
            }
            if (ioType == null)
            {
                return BridgeResponse.Error(BridgeErrorKind.Unavailable,
                    "Carto mod not found; install it from Paradox Mods to use map_image (screenshot still works)");
            }

            Assembly carto = ioType.Assembly;
            Type optionsType = carto.GetType("Carto.IO.Options");
            Type systemsType = carto.GetType("Carto.IO.System");
            Type featureType = carto.GetType("Carto.IO.Feature");
            Type vectorKindType = carto.GetType("Carto.IO.VectorKind");
            Type fileFormatType = carto.GetType("Carto.IO.FileFormat");
            Type propertyType = carto.GetType("Carto.IO.Property");
            if (optionsType == null || systemsType == null || featureType == null
                || vectorKindType == null || fileFormatType == null || propertyType == null)
            {
                return BridgeResponse.Error(BridgeErrorKind.Unavailable,
                    "installed Carto has an unknown IO shape; update Carto and retry");
            }

            MethodInfo export = null;
            foreach (MethodInfo method in ioType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "Export", StringComparison.Ordinal))
                {
                    continue;
                }
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == optionsType)
                {
                    export = method;
                    break;
                }
            }
            if (export == null)
            {
                return BridgeResponse.Error(BridgeErrorKind.Unavailable,
                    "installed Carto has an unknown Export shape (expected Carto.IO.IO.Export(Carto.IO.Options)); " +
                    "update Carto and retry");
            }

            object options;
            try
            {
                options = Activator.CreateInstance(optionsType);
            }
            catch (Exception e)
            {
                return BridgeResponse.Error(BridgeErrorKind.Internal,
                    $"Carto options construction failed: {e.GetType().Name}: {e.Message}");
            }

            BridgeResponse optionsError = ConfigureCartoOptions(
                options, systemsType, featureType, vectorKindType, fileFormatType, propertyType);
            if (optionsError != null)
            {
                return optionsError;
            }

            CartoProjector built;
            string projectorReason;
            if (TryBuildCartoProjector(options, optionsType, carto, out built, out projectorReason))
            {
                projector = built;
            }
            else
            {
                Mod.Log.Warn("map_image: bounded extents unavailable (" + projectorReason + ")");
            }

            object result;
            try
            {
                result = export.Invoke(null, new[] { options });
            }
            catch (TargetInvocationException e)
            {
                Exception inner = e.InnerException ?? e;
                return BridgeResponse.Error(BridgeErrorKind.Internal,
                    $"Carto export threw: {inner.GetType().Name}: {inner.Message}");
            }
            catch (Exception e)
            {
                return BridgeResponse.Error(BridgeErrorKind.Internal,
                    $"Carto export failed: {e.GetType().Name}: {e.Message}");
            }
            if (result == null)
            {
                return BridgeResponse.Error(BridgeErrorKind.Internal, "Carto export returned no result");
            }

            bool success = ReadBoolProperty(result, "Success");
            if (!success)
            {
                string message = ReadStringProperty(result, "ErrorMessage");
                return BridgeResponse.Error(BridgeErrorKind.Internal,
                    "Carto export failed" + (string.IsNullOrEmpty(message) ? "" : ": " + message));
            }
            foreach (string path in ReadStringArrayProperty(result, "FilesWritten"))
            {
                files.Add(path);
            }
            return null;
        }

        /// <summary>
        /// Explicit export request: GeoJSON centerlines for roads, tracks and
        /// transit routes plus building footprints, into this mod's own cache
        /// directory so a triggered export never clobbers the player's manual
        /// exports. Category selects the cartographic hierarchy; Volume rides
        /// along as the next overlay input.
        /// </summary>
        private static BridgeResponse ConfigureCartoOptions(
            object options,
            Type systemsType,
            Type featureType,
            Type vectorKindType,
            Type fileFormatType,
            Type propertyType)
        {
            if (!TrySetEnumFlags(options, "Systems", systemsType,
                new[] { "Network", "Building", "Route" }))
            {
                return UnknownOptionsShape("Systems");
            }
            if (!TrySetEnumFlags(options, "Features", featureType,
                new[] { "Road", "Track", "Building", "RoutePassenger" }))
            {
                return UnknownOptionsShape("Features");
            }
            if (!TrySetEnumValue(options, "VectorFormat", fileFormatType, "GeoJSON"))
            {
                return UnknownOptionsShape("VectorFormat");
            }

            object vectorKinds = NewDictionary(systemsType, vectorKindType);
            object centerline = CombineEnumFlags(vectorKindType, new[] { "Centerline" });
            object boundary = CombineEnumFlags(vectorKindType, new[] { "Boundary" });
            if (vectorKinds == null || centerline == null || boundary == null)
            {
                return UnknownOptionsShape("VectorKinds");
            }
            DictionaryAdd(vectorKinds,
                ParseEnum(systemsType, "Network"), centerline);
            DictionaryAdd(vectorKinds,
                ParseEnum(systemsType, "Building"), boundary);
            DictionaryAdd(vectorKinds,
                ParseEnum(systemsType, "Route"), centerline);
            if (!TrySetProperty(options, "VectorKinds", vectorKinds))
            {
                return UnknownOptionsShape("VectorKinds");
            }

            object networkProperties = NewHashSet(propertyType);
            object emptyProperties = NewHashSet(propertyType);
            if (networkProperties == null || emptyProperties == null)
            {
                return UnknownOptionsShape("Properties");
            }
            HashSetAdd(networkProperties, ParseEnum(propertyType, "Category"));
            HashSetAdd(networkProperties, ParseEnum(propertyType, "Volume"));
            HashSetAdd(networkProperties, ParseEnum(propertyType, "Elevation"));
            object properties = NewDictionary(systemsType, networkProperties.GetType());
            if (properties == null)
            {
                return UnknownOptionsShape("Properties");
            }
            DictionaryAdd(properties,
                ParseEnum(systemsType, "Network"), networkProperties);
            DictionaryAdd(properties,
                ParseEnum(systemsType, "Building"), emptyProperties);
            DictionaryAdd(properties,
                ParseEnum(systemsType, "Route"), emptyProperties);
            if (!TrySetProperty(options, "Properties", properties))
            {
                return UnknownOptionsShape("Properties");
            }

            // Category rendering reads one Display flag by direct index;
            // without it the export throws inside Carto.
            object display = NewDisplayFlag(systemsType, propertyType);
            if (display == null || !TrySetProperty(options, "Display", display))
            {
                return UnknownOptionsShape("Display");
            }

            if (!TrySetString(options, "FileName", kMapFileName))
            {
                return UnknownOptionsShape("FileName");
            }
            if (!TrySetString(options, "CustomDirectory",
                Path.Combine(ModPaths.RuntimeDataDirectory, "carto")))
            {
                return UnknownOptionsShape("CustomDirectory");
            }
            SetSilentFlag(options, "CompletionDialog", false);
            SetSilentFlag(options, "CompletionSound", false);
            // Fail closed: a model tool must never pop a modal dialog or play
            // sounds. If the silent flags cannot be confirmed, refuse instead
            // of exporting with unknown side effects.
            if (!ConfirmSilent(options, "CompletionDialog") || !ConfirmSilent(options, "CompletionSound"))
            {
                return BridgeResponse.Error(BridgeErrorKind.Unavailable,
                    "Carto completion side effects cannot be silenced on the installed version; update Carto and retry");
            }
            return null;
        }

        private static bool ConfirmSilent(object options, string name)
        {
            try
            {
                PropertyInfo property = options.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return property != null && property.CanRead && property.PropertyType == typeof(bool)
                    && !(bool)property.GetValue(options, null);
            }
            catch
            {
                return false;
            }
        }

        private static object NewDisplayFlag(Type systemsType, Type propertyType)
        {
            try
            {
                Type tupleType = typeof(ValueTuple<,>).MakeGenericType(propertyType, systemsType);
                Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(tupleType, typeof(bool));
                object display = Activator.CreateInstance(dictionaryType);
                object category = ParseEnum(propertyType, "Category");
                object network = ParseEnum(systemsType, "Network");
                if (category == null || network == null)
                {
                    return null;
                }
                object key = Activator.CreateInstance(tupleType, category, network);
                dictionaryType.GetMethod("Add").Invoke(display, new[] { key, (object)false });
                return display;
            }
            catch
            {
                return null;
            }
        }

        private static BridgeResponse UnknownOptionsShape(string member)
        {
            return BridgeResponse.Error(BridgeErrorKind.Unavailable,
                $"installed Carto has an unknown Options shape (member '{member}'); update Carto and retry");
        }

        private static object ParseEnum(Type enumType, string name)
        {
            try
            {
                return Enum.Parse(enumType, name, ignoreCase: false);
            }
            catch
            {
                return null;
            }
        }

        private static object CombineEnumFlags(Type enumType, string[] names)
        {
            long combined = 0;
            foreach (string name in names)
            {
                object value = ParseEnum(enumType, name);
                if (value == null)
                {
                    return null;
                }
                combined |= Convert.ToInt64(value);
            }
            try
            {
                return Enum.ToObject(enumType, combined);
            }
            catch
            {
                return null;
            }
        }

        private static bool TrySetEnumFlags(object target, string name, Type enumType, string[] flags)
        {
            object value = CombineEnumFlags(enumType, flags);
            return value != null && TrySetProperty(target, name, value);
        }

        private static bool TrySetEnumValue(object target, string name, Type enumType, string value)
        {
            object parsed = ParseEnum(enumType, value);
            return parsed != null && TrySetProperty(target, name, parsed);
        }

        private static bool TrySetString(object target, string name, string value)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
            {
                return false;
            }
            property.SetValue(target, value, null);
            return true;
        }

        private static bool TrySetProperty(object target, string name, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
            {
                return false;
            }
            if (value != null && !property.PropertyType.IsAssignableFrom(value.GetType()))
            {
                return false;
            }
            property.SetValue(target, value, null);
            return true;
        }

        private static object NewDictionary(Type keyType, Type valueType)
        {
            try
            {
                Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                return Activator.CreateInstance(dictionaryType);
            }
            catch
            {
                return null;
            }
        }

        private static void DictionaryAdd(object dictionary, object key, object value)
        {
            if (dictionary == null || key == null || value == null)
            {
                return;
            }
            try
            {
                dictionary.GetType().GetMethod("Add").Invoke(dictionary, new[] { key, value });
            }
            catch
            {
                // Leave the dictionary short; the shape check on set rejects it.
            }
        }

        private static object NewHashSet(Type elementType)
        {
            try
            {
                Type setType = typeof(HashSet<>).MakeGenericType(elementType);
                return Activator.CreateInstance(setType);
            }
            catch
            {
                return null;
            }
        }

        private static void HashSetAdd(object set, object value)
        {
            if (set == null || value == null)
            {
                return;
            }
            try
            {
                set.GetType().GetMethod("Add").Invoke(set, new[] { value });
            }
            catch
            {
                // Leave the set short; Carto exports fewer attributes.
            }
        }

        private static void SetSilentFlag(object options, string name, bool value)
        {
            PropertyInfo property = options.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
            {
                property.SetValue(options, value, null);
            }
        }

        private static bool ReadBoolProperty(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanRead && property.PropertyType == typeof(bool))
            {
                return (bool)property.GetValue(target, null);
            }
            return false;
        }

        private static string ReadStringProperty(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanRead && property.PropertyType == typeof(string))
            {
                return (string)property.GetValue(target, null);
            }
            return null;
        }

        private static List<string> ReadStringArrayProperty(object target, string name)
        {
            var paths = new List<string>();
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanRead)
            {
                return paths;
            }
            try
            {
                object value = property.GetValue(target, null);
                if (value is string[] array)
                {
                    paths.AddRange(array);
                }
                else if (value is IEnumerable enumerable)
                {
                    foreach (object item in enumerable)
                    {
                        if (item is string path && !string.IsNullOrWhiteSpace(path))
                        {
                            paths.Add(path);
                        }
                    }
                }
            }
            catch
            {
                // No file list; the caller reports no output.
            }
            return paths;
        }

        private enum MapLayerKind
        {
            Network,
            Building,
            Route,
        }

        private enum MapStrokeStyle
        {
            Minor,
            Medium,
            Large,
            Highway,
            Transit,
        }

        private sealed class MapStroke
        {
            public MapStrokeStyle Style;
            public double Elev = double.NaN;
            public bool HasElev;
            public int Layer;
            public readonly List<double> X = new List<double>();
            public readonly List<double> Y = new List<double>();
        }

        private sealed class MapPolygon
        {
            public readonly List<double> X = new List<double>();
            public readonly List<double> Y = new List<double>();
        }

        private sealed class MapFrame
        {
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
            public bool Clip;

            public static MapFrame FromData(List<MapStroke> strokes, List<MapPolygon> fills)
            {
                double minX = double.PositiveInfinity;
                double minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity;
                double maxY = double.NegativeInfinity;
                AccumulateBounds(strokes, fills, ref minX, ref minY, ref maxX, ref maxY);
                if (!(maxX > minX) || !(maxY > minY))
                {
                    return null;
                }
                return new MapFrame { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
            }
        }

        /// <summary>
        /// TEMPORARY diagnosis sink (remove with [DEBUG-mapex1]): synchronous
        /// write-through phase log that survives a process kill, unlike the
        /// buffered Player.log and timeline tails.
        /// </summary>
        private static void DebugPhase(string phase)
        {
            try
            {
                string directory = ModPaths.RuntimeDataDirectory;
                Directory.CreateDirectory(Path.Combine(directory, "logs"));
                string path = Path.Combine(directory, "logs", "map-export-debug.log");
                using (var stream = new FileStream(
                    path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite,
                    4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream))
                {
                    writer.WriteLine(DateTime.UtcNow.ToString("o") + " " + phase);
                    writer.Flush();
                    stream.Flush(true);
                }
            }
            catch
            {
                // Diagnosis must never break the tool.
            }
        }

        /// <summary>
        /// Game-meter exports span thousands with coordinates inside the
        /// world bounds; degree exports span fractions, UTM sits near
        /// 500000 easting. Only meter frames may sample native water.
        /// </summary>
        private static bool FrameIsMeters(MapFrame frame)
        {
            double span = Math.Max(frame.MaxX - frame.MinX, frame.MaxY - frame.MinY);
            double magnitude = Math.Max(
                Math.Max(Math.Abs(frame.MinX), Math.Abs(frame.MaxX)),
                Math.Max(Math.Abs(frame.MinY), Math.Abs(frame.MaxY)));
            return span > 1000.0 && magnitude < 30000.0;
        }

        /// <summary>
        /// Converts a requested game range to the export CRS with Carto's own
        /// function. Four corners because projections rotate slightly.
        /// </summary>
        private static MapFrame ProjectExtent(
            CartoProjector projector, float xMin, float zMin, float xMax, float zMax)
        {
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            double[] xs = { xMin, xMin, xMax, xMax };
            double[] zs = { zMin, zMax, zMin, zMax };
            for (int i = 0; i < 4; i++)
            {
                double px;
                double py;
                if (!projector.TryProject(xs[i], zs[i], out px, out py))
                {
                    return null;
                }
                if (px < minX) minX = px;
                if (py < minY) minY = py;
                if (px > maxX) maxX = px;
                if (py > maxY) maxY = py;
            }
            if (!(maxX > minX) || !(maxY > minY))
            {
                return null;
            }
            return new MapFrame { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY, Clip = true };
        }

        private static void ParseVectorFile(
            string path, MapLayerKind layer, List<MapStroke> strokes, List<MapPolygon> fills)
        {
            JObject document;
            using (StreamReader reader = new StreamReader(path))
            using (Newtonsoft.Json.JsonTextReader json = new Newtonsoft.Json.JsonTextReader(reader))
            {
                document = JObject.Load(json);
            }
            JToken features = document["features"];
            if (features == null || features.Type != JTokenType.Array)
            {
                throw new InvalidDataException("missing GeoJSON 'features' array");
            }
            foreach (JToken feature in features)
            {
                JToken geometry = feature["geometry"];
                if (geometry == null)
                {
                    continue;
                }
                string type = (geometry["type"] ?? "").ToString();
                JToken coordinates = geometry["coordinates"];
                if (coordinates == null)
                {
                    continue;
                }
                if (layer == MapLayerKind.Building)
                {
                    if (string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase))
                    {
                        AddPolygon(coordinates, fills);
                    }
                    else if (string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (JToken polygon in coordinates)
                        {
                            AddPolygon(polygon, fills);
                        }
                    }
                    continue;
                }
                MapStrokeStyle style = layer == MapLayerKind.Route
                    ? MapStrokeStyle.Transit
                    : ClassifyNetworkStroke(feature);
                double elev;
                bool hasElev = ReadElevation(feature, out elev);
                if (string.Equals(type, "LineString", StringComparison.OrdinalIgnoreCase))
                {
                    AddStroke(coordinates, style, elev, hasElev, strokes);
                }
                else if (string.Equals(type, "MultiLineString", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (JToken line in coordinates)
                    {
                        AddStroke(line, style, elev, hasElev, strokes);
                    }
                }
            }
        }

        /// <summary>Ordered first match, mirroring road_class upstream.</summary>
        private static MapStrokeStyle ClassifyNetworkStroke(JToken feature)
        {
            string category = "";
            JToken properties = feature["properties"];
            if (properties != null && properties.Type == JTokenType.Object)
            {
                foreach (JProperty property in properties.Children<JProperty>())
                {
                    if (string.Equals(property.Name, "Category", StringComparison.OrdinalIgnoreCase))
                    {
                        category = property.Value != null ? property.Value.ToString() : "";
                        break;
                    }
                }
            }
            if (category.IndexOf("Highway", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Highway;
            }
            if (category.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Large;
            }
            if (category.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Medium;
            }
            if (category.IndexOf("Tram", StringComparison.OrdinalIgnoreCase) >= 0
                || category.IndexOf("Subway", StringComparison.OrdinalIgnoreCase) >= 0
                || category.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MapStrokeStyle.Transit;
            }
            return MapStrokeStyle.Minor;
        }

        /// <summary>Per-feature average elevation in meters, when exported.</summary>
        private static bool ReadElevation(JToken feature, out double elev)
        {
            elev = double.NaN;
            JToken properties = feature["properties"];
            if (properties == null || properties.Type != JTokenType.Object)
            {
                return false;
            }
            foreach (JProperty property in properties.Children<JProperty>())
            {
                if (!string.Equals(property.Name, "Elevation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    elev = property.Value.ToObject<double>();
                    return !(double.IsNaN(elev) || double.IsInfinity(elev));
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        private static void AddStroke(
            JToken positions, MapStrokeStyle style, double elev, bool hasElev, List<MapStroke> strokes)
        {
            if (positions == null || positions.Type != JTokenType.Array)
            {
                return;
            }
            var stroke = new MapStroke { Style = style, Elev = elev, HasElev = hasElev };
            foreach (JToken position in positions)
            {
                var pair = position as JArray;
                if (pair == null || pair.Count < 2)
                {
                    continue;
                }
                stroke.X.Add(pair[0].ToObject<double>());
                stroke.Y.Add(pair[1].ToObject<double>());
            }
            if (stroke.X.Count >= 2)
            {
                strokes.Add(stroke);
            }
        }

        private static void AddPolygon(JToken rings, List<MapPolygon> fills)
        {
            if (rings == null || rings.Type != JTokenType.Array)
            {
                return;
            }
            // Outer ring only; holes stay uncut in v1 (overview use).
            JToken outer = rings.First;
            if (outer == null || outer.Type != JTokenType.Array)
            {
                return;
            }
            var polygon = new MapPolygon();
            foreach (JToken position in outer)
            {
                var pair = position as JArray;
                if (pair == null || pair.Count < 2)
                {
                    continue;
                }
                polygon.X.Add(pair[0].ToObject<double>());
                polygon.Y.Add(pair[1].ToObject<double>());
            }
            if (polygon.X.Count >= 3)
            {
                fills.Add(polygon);
            }
        }

        private static Texture2D Rasterize(
            List<MapStroke> strokes, List<MapPolygon> fills, MapFrame frame,
            Func<double, double, bool> isWater, List<MapLayering.Merge> merges)
        {
            double spanX = frame.MaxX - frame.MinX;
            double spanY = frame.MaxY - frame.MinY;
            int width = kMapWidth;
            int height = Mathf.Clamp(Mathf.RoundToInt((float)(width * spanY / spanX)), 1, kMapMaxHeight);
            double scale = Math.Min(width / spanX, height / spanY);
            double offsetX = (width - spanX * scale) * 0.5;
            double offsetY = (height - spanY * scale) * 0.5;

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            PaintBackground(texture, frame, scale, offsetX, offsetY, height, isWater);

            var buildingFill = new Color(0.77f, 0.73f, 0.64f);
            foreach (MapPolygon polygon in fills)
            {
                FillPolygon(texture, polygon, frame, scale, offsetX, offsetY, height, buildingFill);
            }

            int drawn = DrawStrokes(texture, strokes, merges, frame, scale, offsetX, offsetY, height, width);
            if (frame.Clip && drawn == 0)
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException("requested extent contains no exported features");
            }
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// World-space water test built on the simulation thread. Null when
        /// unavailable: the map stays land-only rather than failing.
        /// </summary>
        private Func<double, double, bool> BuildWaterSampler()
        {
            try
            {
                WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
                WaterSurfaceData<SurfaceWater> surface = water.GetSurfaceData(out JobHandle deps);
                deps.Complete();
                return (double worldX, double worldY) =>
                {
                    try
                    {
                        var position = new float3((float)worldX, 0f, (float)worldY);
                        return WaterUtils.SampleDepth(ref surface, position) > 0.05f;
                    }
                    catch
                    {
                        return false;
                    }
                };
            }
            catch
            {
                return null;
            }
        }

        private static void PaintBackground(
            Texture2D texture, MapFrame frame, double scale, double offsetX, double offsetY, int height,
            Func<double, double, bool> isWater)
        {
            int width = texture.width;
            var land = new Color(0.95f, 0.94f, 0.91f);
            var water = new Color(0.66f, 0.81f, 0.89f);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = land;
            }

            if (isWater != null)
            {
                for (int row = 0; row < height; row += 2)
                {
                    for (int col = 0; col < width; col += 2)
                    {
                        double worldX = frame.MinX + (col + 0.5 - offsetX) / scale;
                        double worldY = frame.MinY + (height - 1 - row + 0.5 - offsetY) / scale;
                        bool wet;
                        try
                        {
                            wet = isWater(worldX, worldY);
                        }
                        catch
                        {
                            wet = false;
                        }
                        if (!wet)
                        {
                            continue;
                        }
                        for (int dy = 0; dy < 2 && row + dy < height; dy++)
                        {
                            for (int dx = 0; dx < 2 && col + dx < width; dx++)
                            {
                                pixels[(row + dy) * width + col + dx] = water;
                            }
                        }
                    }
                }
            }
            texture.SetPixels(pixels);
        }

        /// <summary>
        /// Layer by layer, ground first: each layer gets a casing pass then
        /// a color pass, so bridges stack over ground roads. Merges redraw
        /// lower branch ends on top, flowing into the main road.
        /// </summary>
        private static int DrawStrokes(
            Texture2D texture, List<MapStroke> strokes, List<MapLayering.Merge> merges,
            MapFrame frame, double scale, double offsetX, double offsetY, int height, int width)
        {
            int top = 0;
            foreach (MapStroke stroke in strokes)
            {
                if (stroke.Layer > top) top = stroke.Layer;
            }
            int drawn = 0;
            for (int layer = 0; layer <= top; layer++)
            {
                foreach (MapStroke stroke in strokes)
                {
                    if (stroke.Layer != layer)
                    {
                        continue;
                    }
                    StrokePaint paint = PaintForStyle(stroke.Style);
                    if (paint.Casing)
                    {
                        drawn += DrawStroke(texture, stroke, frame, scale, offsetX, offsetY,
                            height, width, paint.CasingColor, paint.Radius + 1);
                    }
                }
                foreach (MapStroke stroke in strokes)
                {
                    if (stroke.Layer != layer)
                    {
                        continue;
                    }
                    StrokePaint paint = PaintForStyle(stroke.Style);
                    drawn += DrawStroke(texture, stroke, frame, scale, offsetX, offsetY,
                        height, width, paint.Color, paint.Radius);
                }
            }
            foreach (MapLayering.Merge merge in merges)
            {
                if (merge.Feature < 0 || merge.Feature >= strokes.Count)
                {
                    continue;
                }
                MapStroke stroke = strokes[merge.Feature];
                StrokePaint paint = PaintForStyle(stroke.Style);
                int x0 = (int)Math.Round((merge.EndX - frame.MinX) * scale + offsetX);
                int y0 = height - 1 - (int)Math.Round((merge.EndY - frame.MinY) * scale + offsetY);
                int x1 = (int)Math.Round((merge.AdjX - frame.MinX) * scale + offsetX);
                int y1 = height - 1 - (int)Math.Round((merge.AdjY - frame.MinY) * scale + offsetY);
                if (frame.Clip && IsOutsideFrame(x0, y0, x1, y1, width, height))
                {
                    continue;
                }
                DrawLine(texture, x0, y0, x1, y1, paint.Radius, paint.Color);
                drawn++;
            }
            return drawn;
        }

        private struct StrokePaint
        {
            public Color Color;
            public int Radius;
            public bool Casing;
            public Color CasingColor;
        }

        /// <summary>Palette mirrors cs2-carto-citymap style.py.</summary>
        private static StrokePaint PaintForStyle(MapStrokeStyle style)
        {
            switch (style)
            {
                case MapStrokeStyle.Highway:
                    return new StrokePaint { Color = new Color(0.95f, 0.58f, 0f), Radius = 1, Casing = true, CasingColor = new Color(0.75f, 0.44f, 0.07f) };
                case MapStrokeStyle.Large:
                    return new StrokePaint { Color = new Color(0.98f, 0.76f, 0.31f), Radius = 1, Casing = true, CasingColor = new Color(0.85f, 0.62f, 0.17f) };
                case MapStrokeStyle.Medium:
                    return new StrokePaint { Color = new Color(0.99f, 0.91f, 0.66f), Radius = 1, Casing = true, CasingColor = new Color(0.84f, 0.77f, 0.49f) };
                case MapStrokeStyle.Transit:
                    return new StrokePaint { Color = new Color(0.25f, 0.45f, 0.79f), Radius = 0 };
                default:
                    return new StrokePaint { Color = new Color(1f, 1f, 1f), Radius = 0, Casing = true, CasingColor = new Color(0.80f, 0.78f, 0.73f) };
            }
        }

        private static int DrawStroke(
            Texture2D texture, MapStroke stroke,
            MapFrame frame, double scale, double offsetX, double offsetY,
            int height, int width, Color color, int radius)
        {
            int stride = 1;
            int segments = Math.Max(0, stroke.X.Count - 1);
            if (!frame.Clip && segments > kMapMaxSegments)
            {
                stride = segments / kMapMaxSegments + 1;
            }
            int drawn = 0;
            for (int i = 0; i + 1 < stroke.X.Count; i += stride)
            {
                int x0 = (int)Math.Round((stroke.X[i] - frame.MinX) * scale + offsetX);
                int y0 = height - 1 - (int)Math.Round((stroke.Y[i] - frame.MinY) * scale + offsetY);
                int x1 = (int)Math.Round((stroke.X[i + 1] - frame.MinX) * scale + offsetX);
                int y1 = height - 1 - (int)Math.Round((stroke.Y[i + 1] - frame.MinY) * scale + offsetY);
                if (frame.Clip && IsOutsideFrame(x0, y0, x1, y1, width, height))
                {
                    continue;
                }
                DrawLine(texture, x0, y0, x1, y1, radius, color);
                drawn++;
            }
            return drawn;
        }

        private static bool IsOutsideFrame(int x0, int y0, int x1, int y1, int width, int height)
        {
            return (x0 < 0 && x1 < 0) || (x0 >= width && x1 >= width)
                || (y0 < 0 && y1 < 0) || (y0 >= height && y1 >= height);
        }

        private static void AccumulateBounds(
            List<MapStroke> strokes,
            List<MapPolygon> fills,
            ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            foreach (MapStroke stroke in strokes)
            {
                for (int i = 0; i < stroke.X.Count; i++)
                {
                    AccumulatePoint(stroke.X[i], stroke.Y[i], ref minX, ref minY, ref maxX, ref maxY);
                }
            }
            foreach (MapPolygon polygon in fills)
            {
                for (int i = 0; i < polygon.X.Count; i++)
                {
                    AccumulatePoint(polygon.X[i], polygon.Y[i], ref minX, ref minY, ref maxX, ref maxY);
                }
            }
        }

        private static void AccumulatePoint(
            double x, double y,
            ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            {
                return;
            }
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        private static void Plot(Texture2D texture, int x, int y, int radius, Color color)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int px = x + dx;
                    int py = y + dy;
                    if (px >= 0 && py >= 0 && px < texture.width && py < texture.height)
                    {
                        texture.SetPixel(px, py, color);
                    }
                }
            }
        }

        private static void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, int radius, Color color)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int error = dx - dy;
            while (true)
            {
                Plot(texture, x0, y0, radius, color);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }
                int doubled = 2 * error;
                if (doubled > -dy)
                {
                    error -= dy;
                    x0 += sx;
                }
                if (doubled < dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private static void FillPolygon(
            Texture2D texture, MapPolygon polygon,
            MapFrame frame, double scale, double offsetX, double offsetY, int height,
            Color color)
        {
            int n = polygon.X.Count;
            var xs = new int[n];
            var ys = new int[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = (int)Math.Round((polygon.X[i] - frame.MinX) * scale + offsetX);
                ys[i] = height - 1 - (int)Math.Round((polygon.Y[i] - frame.MinY) * scale + offsetY);
            }
            int rowMin = ys[0];
            int rowMax = ys[0];
            for (int i = 1; i < n; i++)
            {
                if (ys[i] < rowMin) rowMin = ys[i];
                if (ys[i] > rowMax) rowMax = ys[i];
            }
            rowMin = Math.Max(rowMin, 0);
            rowMax = Math.Min(rowMax, texture.height - 1);
            var crossings = new List<int>(8);
            for (int row = rowMin; row <= rowMax; row++)
            {
                crossings.Clear();
                for (int i = 0; i < n; i++)
                {
                    int x0 = xs[i];
                    int y0 = ys[i];
                    int x1 = xs[(i + 1) % n];
                    int y1 = ys[(i + 1) % n];
                    if ((y0 <= row && y1 > row) || (y1 <= row && y0 > row))
                    {
                        crossings.Add(x0 + (int)((long)(row - y0) * (x1 - x0) / (y1 - y0)));
                    }
                }
                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    int from = Math.Max(crossings[k], 0);
                    int to = Math.Min(crossings[k + 1], texture.width - 1);
                    for (int x = from; x <= to; x++)
                    {
                        texture.SetPixel(x, row, color);
                    }
                }
            }
        }
    }
}
