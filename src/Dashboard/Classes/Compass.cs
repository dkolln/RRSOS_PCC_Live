using System.Numerics;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>
    /// The game's compass on the ground plane and a few display helpers.
    ///
    /// Measured in the game with the live-data plugin: running along world -X the in-game compass read
    /// south, and along world -Z it read east, so north is world +X and east is world -Z. Everything that
    /// draws a compass, a map or a bearing goes through <see cref="ToEastNorth"/>.
    /// </summary>
    public static class Compass
    {
        /// <summary>(east, north) for a world (X, Z) point or direction.</summary>
        public static Vector2 ToEastNorth(Vector2 worldXZ) => new(-worldXZ.Y, worldXZ.X);

        /// <summary>Compass bearing in degrees for an (east, north) direction: 0 = north, 90 = east.</summary>
        public static float BearingDegrees(Vector2 eastNorth)
        {
            var bearing = MathF.Atan2(eastNorth.X, eastNorth.Y) * (180f / MathF.PI);
            return bearing < 0 ? bearing + 360f : bearing;
        }

        /// <summary>
        /// Compass heading from Unity's yaw (0 = along +Z, clockwise from above): the forward vector is
        /// (sin yaw, cos yaw) in world (X, Z), which then goes through the same conversion as everything else.
        /// </summary>
        public static float HeadingFromYaw(double yawDegrees)
        {
            var yaw = (float)(yawDegrees * Math.PI / 180.0);
            return BearingDegrees(ToEastNorth(new Vector2(MathF.Sin(yaw), MathF.Cos(yaw))));
        }

        public static string GetCompassDirection(Vector2 from, Vector2 to) =>
            EightWay(BearingDegrees(ToEastNorth(to - from)));

        public static string EightWay(float bearing) => bearing switch
        {
            >= 337.5f or < 22.5f => "N",
            >= 22.5f and < 67.5f => "NE",
            >= 67.5f and < 112.5f => "E",
            >= 112.5f and < 157.5f => "SE",
            >= 157.5f and < 202.5f => "S",
            >= 202.5f and < 247.5f => "SW",
            >= 247.5f and < 292.5f => "W",
            >= 292.5f and < 337.5f => "NW",
            _ => "?"
        };

        /// <summary>1581821722624 becomes "1581.82B"; the planet dials show their totals this way.</summary>
        public static string Abbreviate(double n)
        {
            var abs = Math.Abs(n);

            if (abs >= 1_000_000_000)
                return (n / 1_000_000_000d).ToString("0.##") + "B";

            if (abs >= 1_000_000)
                return (n / 1_000_000d).ToString("0.##") + "M";

            if (abs >= 1_000)
                return (n / 1_000d).ToString("0.##") + "K";

            return n.ToString("N0");
        }
    }
}
