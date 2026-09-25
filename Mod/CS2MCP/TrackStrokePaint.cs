namespace CS2MCP
{
    /// <summary>
    /// OSM Carto track symbol. One centerline is one symbol; pixel size follows
    /// meters per pixel of the frame, not the prefab width. A wider gray body
    /// with a narrower white dash leaves the two rails; the dash gaps are the sleepers.
    /// </summary>
    internal struct TrackStrokeRecipe
    {
        public MapRgb Body;
        public int BodyRadius;
        public int[] BodyPattern;
        public bool Hatch;
        public int HatchRadius;
        public int[] HatchPattern;
        public bool Deck;
        public int ShellRadius;
        public int DeckRadius;
    }

    internal static class TrackStrokePaint
    {
        private static readonly MapRgb RailBody = new MapRgb(0x70, 0x70, 0x70);
        private static readonly MapRgb RailHairline = new MapRgb(0x78, 0x78, 0x78);
        private static readonly MapRgb MetroBody = new MapRgb(0x99, 0x99, 0x99);
        private static readonly MapRgb TramBody = new MapRgb(0x6E, 0x6E, 0x6E);
        internal static readonly MapRgb White = new MapRgb(255, 255, 255);
        internal static readonly MapRgb Shell = new MapRgb(20, 20, 20);

        private static readonly int[] Dash88 = { 8, 8 };
        private static readonly int[] Sleeper = { 0, 8, 8, 1 };
        private static readonly int[] Tunnel52 = { 5, 2 };
        private static readonly int[] Tunnel64 = { 6, 4 };
        private static readonly int[] Tunnel86 = { 8, 6 };
        private static readonly int[] Tunnel53 = { 5, 3 };

        private const double DoubleTrackHalfM = 2.0;
        private const double DoubleTrackMinWidthM = 9.0;

        /// <summary>
        /// Half the gap between the two symbols of a double-track line.
        /// Below about one pixel the pair would stamp on itself and read as a border.
        /// </summary>
        internal static double LateralMeters(MapStrokeStyle style, double widthM, double scale)
        {
            if (style != MapStrokeStyle.Rail || widthM < DoubleTrackMinWidthM || scale <= 0)
            {
                return 0;
            }
            if (DoubleTrackHalfM * scale < 1.25)
            {
                return 0;
            }
            return DoubleTrackHalfM;
        }

        internal static bool IsTrack(MapStrokeStyle style)
        {
            return style == MapStrokeStyle.Rail
                || style == MapStrokeStyle.Metro
                || style == MapStrokeStyle.Tram;
        }

        internal static TrackStrokeRecipe For(MapStrokeStyle style, double metersPerPixel, MapGrade grade)
        {
            bool site = metersPerPixel < 1.0;
            bool bridge = grade == MapGrade.Bridge && metersPerPixel < 20.0;
            var recipe = new TrackStrokeRecipe();
            if (style == MapStrokeStyle.Rail)
            {
                PaintRail(ref recipe, metersPerPixel, grade, site);
            }
            else
            {
                recipe.Body = style == MapStrokeStyle.Metro ? MetroBody : TramBody;
                recipe.BodyRadius = site ? 1 : 0;
                if (grade == MapGrade.Tunnel)
                {
                    recipe.BodyPattern = Tunnel53;
                }
            }
            if (bridge)
            {
                recipe.Deck = true;
                recipe.ShellRadius = recipe.BodyRadius + 2;
                recipe.DeckRadius = recipe.BodyRadius + 1;
            }
            return recipe;
        }

        private static void PaintRail(ref TrackStrokeRecipe recipe, double metersPerPixel, MapGrade grade, bool site)
        {
            if (metersPerPixel >= 40.0)
            {
                recipe.Body = RailHairline;
                recipe.BodyRadius = 0;
                if (grade == MapGrade.Tunnel)
                {
                    recipe.BodyPattern = Tunnel52;
                }
                return;
            }

            if (grade == MapGrade.Tunnel)
            {
                recipe.Body = RailHairline;
                recipe.BodyRadius = site ? 2 : 1;
                recipe.BodyPattern = site ? Tunnel86 : Tunnel64;
                return;
            }

            recipe.Body = RailBody;
            recipe.Hatch = true;
            recipe.HatchPattern = metersPerPixel < 5.0 ? Sleeper : Dash88;
            if (site)
            {
                recipe.BodyRadius = 2;
                recipe.HatchRadius = 1;
            }
            else
            {
                recipe.BodyRadius = 1;
                recipe.HatchRadius = 0;
            }
        }
    }
}
