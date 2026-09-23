using System.Numerics;

namespace RRSOS.PCC.Dashboard
{
    public enum PlanPart
    {
        Pod,
        Foundation,
        Platform,
        Dome,
        Lab,
        Aquarium,
        Tower,
        Other,

        // Walls, drawn as lines over the pieces.
        Wall,
        Glass,
        Door,
        Opening,

        // A ladder's hole in the roof, on the ladder's own floor; and where it comes out, on the floor above.
        Ladder,
        LadderAbove
    }

    /// <summary>A shape on a floor plan in (east, north) metres. Walls are two points (a line); everything else is a closed outline.</summary>
    public sealed record PlanShape(PlanPart Part, IReadOnlyList<Vector2> Points, string Label);

    /// <summary>One storey of a base: the pieces standing on it, their walls, ladder holes, and where its containers are.</summary>
    public sealed class PlanFloor
    {
        /// <summary>1 is the lowest floor.</summary>
        public int Number { get; init; }

        /// <summary>The height a pod standing on this floor has (its position's Y), for matching things to floors.</summary>
        public float Level { get; init; }

        public List<PlanShape> Pieces { get; } = new();
        public List<PlanShape> Walls { get; } = new();
        public List<PlanShape> Hatches { get; } = new();
        public List<Vector2> Containers { get; } = new();
    }

    /// <summary>
    /// A base drawn from above, one floor at a time. Built from the plugin's building pieces: each piece is outlined from
    /// the box the plugin measured in the game, or (from a save, or an older plugin) from a table of known sizes. Floors are
    /// found from the pieces' heights: a new floor starts wherever there is a clear gap (pods stack 6 m apart), so a base on
    /// a slope still reads as one floor, and nothing depends on a fixed storey height.
    /// </summary>
    public sealed class FloorPlan
    {
        /// <summary>
        /// A gap in height larger than this starts a new floor. Stacked pods are 6 m apart; a raised deck of foundations can
        /// sit 3 m above a floor (seen in a save), and that should read as a floor of its own, so this stays clear of 3.
        /// </summary>
        private const float FloorGap = 2.5f;

        /// <summary>How far above a floor's level something can sit and still be on it (a pod's floor is about 1 m above its position).</summary>
        private const float AboveFloor = 0.5f;

        /// <summary>A ladder stands on its pod's floor, this far above the pod's position (measured: 36.04 in a pod at 35).</summary>
        private const float PodFloorHeight = 1.04f;

        /// <summary>A foundation's top is this far above its position (foundations 2 m below the pods on them, in saves).</summary>
        private const float FoundationTop = 2f;

        /// <summary>
        /// A platform's deck (launch, trade and vehicle platforms) is this far above its position: in one save the
        /// foundations built as a walkway beside a launch platform top out exactly 5 m above it, and in another all three
        /// platforms stand 5 m below the main floor they are reached from.
        /// </summary>
        private const float PlatformDeck = 5f;

        /// <summary>Things this far outside the drawn pieces still count as "at this base" for the player marker and containers.</summary>
        private const float Margin = 6f;

        public IReadOnlyList<PlanFloor> Floors { get; }

        /// <summary>The corners of everything drawn, in (east, north) metres.</summary>
        public Vector2 Min { get; }
        public Vector2 Max { get; }

        /// <summary>True when every piece was measured in the game; false when some came from the table of sizes.</summary>
        public bool Measured { get; }

        private FloorPlan(IReadOnlyList<PlanFloor> floors, Vector2 min, Vector2 max, bool measured)
        {
            Floors = floors;
            Min = min;
            Max = max;
            Measured = measured;
        }

        /// <summary>The floor a height is on: the highest floor at or just below it (the lowest when it is below them all).</summary>
        public int FloorIndexFor(float y)
        {
            var index = 0;
            for (var i = 0; i < Floors.Count; i++)
            {
                if (Floors[i].Level <= y + AboveFloor)
                    index = i;
            }

            return index;
        }

        /// <summary>The floor a world point is on, or null when it is not over (or near) this base at all.</summary>
        public int? FloorIndexAt(Vector3 world)
        {
            var en = Compass.ToEastNorth(new Vector2(world.X, world.Z));
            return Contains(en, Margin) ? FloorIndexFor(world.Y) : null;
        }

        public bool Contains(Vector2 eastNorth, float margin) =>
            eastNorth.X >= Min.X - margin && eastNorth.X <= Max.X + margin &&
            eastNorth.Y >= Min.Y - margin && eastNorth.Y <= Max.Y + margin;

        /// <summary>The plan for one base's pieces and container positions. Null when it has no pieces.</summary>
        public static FloorPlan? Build(IEnumerable<StructureData> structures, IEnumerable<Vec3> containers)
        {
            var pieces = structures
                .Where(s => s.Position is not null)
                .SelectMany(LaunchPlatformLevels)
                .Select(s => new Piece(s))
                .ToList();

            if (pieces.Count == 0)
                return null;

            // A ladder is on the floor of the room it stands in.
            var rooms = pieces.Where(p => p.Part is PlanPart.Pod or PlanPart.Lab or PlanPart.Dome or PlanPart.Aquarium).ToList();
            foreach (var ladder in pieces.Where(p => p.IsLadder))
            {
                var y = (float)ladder.Data.Position!.Y;
                var room = rooms
                    .Where(r => r.Level <= y + AboveFloor && y < r.Level + 5f && Inside(ladder.Center, r.Outline))
                    .OrderByDescending(r => r.Level)
                    .FirstOrDefault();

                ladder.Level = room?.Level ?? y - PodFloorHeight;
            }

            // Floors: sort by height and start a new one at every clear gap.
            var floors = new List<PlanFloor>();
            var byFloor = new List<List<Piece>>();
            float? previous = null;

            foreach (var piece in pieces.OrderBy(p => p.Level))
            {
                if (previous is null || piece.Level - previous.Value > FloorGap)
                {
                    floors.Add(new PlanFloor { Number = floors.Count + 1, Level = piece.Level });
                    byFloor.Add(new List<Piece>());
                }

                byFloor[^1].Add(piece);
                previous = piece.Level;
            }

            for (var i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];

                foreach (var piece in byFloor[i])
                {
                    if (piece.IsLadder)
                    {
                        floor.Hatches.Add(new PlanShape(PlanPart.Ladder, piece.Outline, "Ladder up"));
                        if (i + 1 < floors.Count)
                            floors[i + 1].Hatches.Add(new PlanShape(PlanPart.LadderAbove, piece.Outline, "Ladder from below"));
                        continue;
                    }

                    floor.Pieces.Add(new PlanShape(piece.Part, piece.Outline, piece.Data.Group));
                    floor.Pieces.AddRange(piece.Extras.Select(extra => new PlanShape(piece.Part, extra, piece.Data.Group)));
                    floor.Walls.AddRange(piece.Walls());
                }

                // Foundations and platforms first, so the pods standing on them are drawn on top.
                floor.Pieces.Sort((a, b) => Order(a.Part).CompareTo(Order(b.Part)));
            }

            var all = pieces.SelectMany(p => p.Outline.Concat(p.Extras.SelectMany(e => e))).ToList();
            var min = new Vector2(all.Min(p => p.X), all.Min(p => p.Y));
            var max = new Vector2(all.Max(p => p.X), all.Max(p => p.Y));
            var plan = new FloorPlan(floors, min, max, pieces.All(p => p.Measured));

            foreach (var spot in containers)
            {
                var en = Compass.ToEastNorth(new Vector2((float)spot.X, (float)spot.Z));
                if (plan.Contains(en, 1f))
                    floors[plan.FloorIndexFor((float)spot.Y)].Containers.Add(en);
            }

            return plan;
        }

        /// <summary>
        /// A launch platform has more than its deck: a landing reached by stairs at the deck's east end (10.6 m above its
        /// position), and a two-tile tower beside it, climbed by a ladder, whose top you can stand on (30 m above its
        /// position). All found by standing on them in the game; the landing's size is the slab the plugin measured. They
        /// become pieces of their own. The tower's top is a floor; the tower is outlined on the deck's floor, the landing's
        /// and two floors between the landing and the top, so the floor buttons can climb all the way up it.
        /// </summary>
        private static IEnumerable<StructureData> LaunchPlatformLevels(StructureData s)
        {
            yield return s;

            if (!s.Group.Equals("LaunchPlatform", StringComparison.OrdinalIgnoreCase))
                yield break;

            const double Landing = 10.58, Top = 30;
            var p = s.Position!;

            // Platforms count 5 m (PlatformDeck) above their position, so each is placed that much lower.
            yield return Part(s, "LaunchPlatformLanding", p.Y + Landing - PlatformDeck, 11.8, 18.34, -14, -10.71, Landing);
            yield return Part(s, "LaunchTowerPlatform", p.Y + Top - PlatformDeck, 3, 15, -21, -15, Top);

            foreach (var height in new[] { PlatformDeck, Landing, Landing + (Top - Landing) / 3, Landing + 2 * (Top - Landing) / 3 })
                yield return Part(s, "LaunchTower", p.Y + height, 3, 15, -21, -15, Top);
        }

        // A made-up piece that stands where the real one does and turns with it; "y" is chosen so it lands on the right floor.
        private static StructureData Part(StructureData s, string group, double y, double minX, double maxX, double minZ, double maxZ, double top) => new()
        {
            Id = s.Id,
            Group = group,
            Position = new Vec3 { X = s.Position!.X, Y = y, Z = s.Position.Z },
            Yaw = s.Yaw,
            Box = new BoxData { Min = new Vec3 { X = minX, Y = 0, Z = minZ }, Max = new Vec3 { X = maxX, Y = top, Z = maxZ } }
        };

        private static int Order(PlanPart part) => part switch
        {
            PlanPart.Foundation or PlanPart.Platform => 0,
            PlanPart.Dome => 1,
            _ => 2
        };

        // Even-odd ray test: is a point inside a closed outline?
        private static bool Inside(Vector2 point, IReadOnlyList<Vector2> outline)
        {
            var inside = false;
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                var a = outline[i];
                var b = outline[j];
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>One building piece, placed: its outline on the ground and the height of the floor it makes.</summary>
        private sealed class Piece
        {
            private readonly float _cos, _sin, _x, _z;
            private readonly float _minX, _maxX, _minZ, _maxZ;

            public Piece(StructureData data)
            {
                Data = data;
                Part = PartOf(data.Group);
                IsLadder = data.Group.Equals("Ladder", StringComparison.OrdinalIgnoreCase);

                var yaw = (float)(data.Yaw * Math.PI / 180.0);
                _cos = MathF.Cos(yaw);
                _sin = MathF.Sin(yaw);
                _x = (float)data.Position!.X;
                _z = (float)data.Position.Z;

                float top;
                if (IsLaunchPlatform)
                {
                    // Its shape is known (see LaunchPlatformOutline); its measured box reaches well past the deck.
                    (_minX, _maxX, _minZ, _maxZ, top) = (-21f, 27f, -27f, -3f, 0f);
                    Measured = true;
                }
                else if (MeasuredBox(data, Part) is { Min: { } bmin, Max: { } bmax })
                {
                    (_minX, _maxX, _minZ, _maxZ, top) = ((float)bmin.X, (float)bmax.X, (float)bmin.Z, (float)bmax.Z, (float)bmax.Y);
                    Measured = true;
                }
                else
                {
                    (_minX, _maxX, _minZ, _maxZ, top) = Table(data.Group, Part, IsLadder);
                }

                // Foundations and platforms are floors: what stands on them stands at their top. A measured top is only
                // trusted when it is about where the saves put it (a foundation 2 m up, a platform's deck 5 m up).
                var y = (float)data.Position.Y;
                Level = Part switch
                {
                    PlanPart.Foundation => y + (top is > 0.5f and < 3.5f ? top : FoundationTop),
                    PlanPart.Platform => y + (top is > 3f and < 7f ? top : PlatformDeck),
                    _ => y
                };

                Outline = MakeOutline();
                Center = ToEastNorth((_minX + _maxX) / 2, (_minZ + _maxZ) / 2);
            }

            /// <summary>
            /// The measured box to draw from. A platform's full box reaches over everything around its deck (a launch
            /// platform's is 48 by 31 m), so platforms use the deck box, or the full one only when it is platform-sized.
            /// </summary>
            private static BoxData? MeasuredBox(StructureData data, PlanPart part)
            {
                if (part != PlanPart.Platform)
                    return data.Box;

                // The deck search picks the widest flat slab, which is not always the deck (on a launch platform it found a
                // small slab 10 m up), so it is only trusted at a deck's height.
                if (data.DeckBox is { Min: not null, Max: { Y: > 3 and < 7 } } deck)
                    return deck;

                return data.Box is { Min: { } min, Max: { } max } && max.X - min.X <= MaxPlatformSide && max.Z - min.Z <= MaxPlatformSide
                    ? data.Box
                    : null;
            }

            private const double MaxPlatformSide = 16;

            /// <summary>More outline for the same piece: a dome's entrance, sticking out of its circle.</summary>
            public List<IReadOnlyList<Vector2>> Extras { get; } = new();

            public StructureData Data { get; }
            public PlanPart Part { get; }
            public bool IsLadder { get; }
            public bool Measured { get; }
            public float Level { get; set; }
            public IReadOnlyList<Vector2> Outline { get; }
            public Vector2 Center { get; }

            private bool IsTriangle => Data.Group.Equals("FoundationAngle", StringComparison.OrdinalIgnoreCase);

            private bool IsLaunchPlatform => Data.Group.Equals("LaunchPlatform", StringComparison.OrdinalIgnoreCase);

            /// <summary>
            /// A launch platform's deck, in its own frame (x, z): 6 m tiles, 8 along its X (from -21 to +27) and 4 along its
            /// -Z (from -3 to -27), with the corner tile at +X on the last row missing. Seen in the game as 3 rows of 8 and
            /// a row of 7, and checked by standing on its corners; the measured box agrees (47.8 m along X, and reaching
            /// 27.9 m along -Z).
            /// </summary>
            private static readonly Vector2[] LaunchPlatformOutline =
            {
                new(-21, -3), new(27, -3), new(27, -21), new(21, -21), new(21, -27), new(-21, -27)
            };

            /// <summary>Its staircase: one tile wide, in line with its position, from the deck's +Z edge out to where the measured box ends (standing on it confirmed).</summary>
            private static readonly Vector2[] LaunchPlatformStairs =
            {
                new(-3, -3), new(3, -3), new(3, 3.25f), new(-3, 3.25f)
            };

            // From the piece's own frame to (east, north): turn by its yaw (Unity: clockwise from above), move to its position.
            private Vector2 ToEastNorth(float x, float z)
            {
                var wx = _x + x * _cos + z * _sin;
                var wz = _z - x * _sin + z * _cos;
                return Compass.ToEastNorth(new Vector2(wx, wz));
            }

            private IReadOnlyList<Vector2> MakeOutline()
            {
                if (Part is PlanPart.Dome or PlanPart.Aquarium)
                {
                    // Domes and the T2 aquarium are round, centred on their position. Some have an entrance sticking out
                    // on one side (a T2 dome's box runs from -20 to +12), so the radius comes from each axis's nearer
                    // side, and a side that reaches past the circle gets a short annex drawn onto it.
                    var r = MathF.Max(1f, MathF.Max(MathF.Min(-_minX, _maxX), MathF.Min(-_minZ, _maxZ)));

                    AddAnnex(-_minX, r, (a, b) => (-a, b));
                    AddAnnex(_maxX, r, (a, b) => (a, b));
                    AddAnnex(-_minZ, r, (a, b) => (b, -a));
                    AddAnnex(_maxZ, r, (a, b) => (b, a));

                    return Enumerable.Range(0, 32)
                        .Select(i => i * MathF.PI * 2 / 32)
                        .Select(a => ToEastNorth(r * MathF.Cos(a), r * MathF.Sin(a)))
                        .ToList();
                }

                if (IsLaunchPlatform)
                {
                    Extras.Add(LaunchPlatformStairs.Select(p => ToEastNorth(p.X, p.Y)).ToList());
                    return LaunchPlatformOutline.Select(p => ToEastNorth(p.X, p.Y)).ToList();
                }

                // A triangular foundation is half of its square, cut corner to corner. Which half is a guess until checked in the game.
                if (IsTriangle)
                    return new[] { ToEastNorth(_minX, _minZ), ToEastNorth(_maxX, _minZ), ToEastNorth(_minX, _maxZ) };

                return new[] { ToEastNorth(_minX, _minZ), ToEastNorth(_maxX, _minZ), ToEastNorth(_maxX, _maxZ), ToEastNorth(_minX, _maxZ) };
            }

            // An annex from just inside the circle out to how far this side reaches, 4 m wide. "place" turns (along, across) into (x, z).
            private void AddAnnex(float reach, float r, Func<float, float, (float X, float Z)> place)
            {
                const float HalfWidth = 2f;
                if (reach <= r + 1f)
                    return;

                var corners = new[] { place(r - 0.5f, -HalfWidth), place(reach, -HalfWidth), place(reach, HalfWidth), place(r - 0.5f, HalfWidth) };
                Extras.Add(corners.Select(c => ToEastNorth(c.X, c.Z)).ToList());
            }

            public IEnumerable<PlanShape> Walls()
            {
                // Measured: each wall panel's own box, drawn as the line down its middle along its long side.
                if (Data.PanelBoxes is { Count: > 0 } boxes)
                {
                    for (var i = 0; i < boxes.Count; i++)
                    {
                        var b = boxes[i];
                        if (b.Type != PanelCodes.TypeWall || b.Min is null || b.Max is null)
                            continue;

                        float x0 = (float)b.Min.X, x1 = (float)b.Max.X, z0 = (float)b.Min.Z, z1 = (float)b.Max.Z;
                        var line = x1 - x0 >= z1 - z0
                            ? new[] { ToEastNorth(x0, (z0 + z1) / 2), ToEastNorth(x1, (z0 + z1) / 2) }
                            : new[] { ToEastNorth((x0 + x1) / 2, z0), ToEastNorth((x0 + x1) / 2, z1) };

                        yield return new PlanShape(WallPart(b.Sub), line, WallLabel(b.Sub));
                    }

                    yield break;
                }

                // From a save: a plain pod's first four panels are its sides, in the order +Z, -Z, +X, -X (worked out from
                // which sides of neighbouring pods are joined by corridors in two saves).
                if (Data.Group.Equals("pod", StringComparison.OrdinalIgnoreCase) && Data.Panels.Count >= 4)
                {
                    var sides = new[]
                    {
                        (_minX, _maxZ, _maxX, _maxZ),
                        (_minX, _minZ, _maxX, _minZ),
                        (_maxX, _minZ, _maxX, _maxZ),
                        (_minX, _minZ, _minX, _maxZ)
                    };

                    for (var i = 0; i < 4; i++)
                    {
                        var (ax, az, bx, bz) = sides[i];
                        yield return new PlanShape(WallPart(Data.Panels[i]), new[] { ToEastNorth(ax, az), ToEastNorth(bx, bz) }, WallLabel(Data.Panels[i]));
                    }
                }
            }

            private static PlanPart WallPart(int code) => code switch
            {
                PanelCodes.WallCorridor => PlanPart.Opening,
                PanelCodes.WallDoor => PlanPart.Door,
                PanelCodes.WallGlass or PanelCodes.WallWaterLife => PlanPart.Glass,
                _ => PlanPart.Wall
            };

            private static string WallLabel(int code) => code switch
            {
                PanelCodes.WallCorridor => "Corridor",
                PanelCodes.WallDoor => "Door",
                PanelCodes.WallGlass => "Window",
                PanelCodes.WallWaterLife => "Aquarium wall",
                PanelCodes.WallInside => "Inside wall",
                PanelCodes.WallLab => "Lab wall",
                _ => "Wall"
            };

            private static PlanPart PartOf(string group)
            {
                if (group.StartsWith("pod", StringComparison.OrdinalIgnoreCase) || group.Equals("EscapePod", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.Pod;
                if (group.StartsWith("Foundation", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.Foundation;
                if (group.Equals("LaunchTower", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.Tower;
                if (group.Contains("Platform", StringComparison.OrdinalIgnoreCase) || group.StartsWith("VehicleCrafter", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.Platform;
                if (group.StartsWith("Aquarium", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.Aquarium;
                if (group.Contains("dome", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.Dome;
                if (group.Contains("lab", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.Lab;
                return PlanPart.Other;
            }

            /// <summary>
            /// Sizes for when nothing was measured: (min X, max X, min Z, max Z, top) in the piece's own frame. The pod, the
            /// foundations, the lab and Pod4x come from how pieces sit next to each other in real saves: pods on an 8 m grid,
            /// foundations on a 6 m one and 2 m below the pods on them, and the lab and Pod4x (both four floor tiles under
            /// one roof) each filling one pod's square whose middle is 2.86 m along +Z from their position. The rest are
            /// rough until measured in the game.
            /// </summary>
            private static (float, float, float, float, float) Table(string group, PlanPart part, bool ladder)
            {
                if (ladder)
                    return (-0.6f, 0.6f, -0.6f, 0.6f, 0f);

                // Nine floor tiles; guessed as 12 m square with its position placed like Pod4x's (1.14 m in from its -Z side).
                if (group.StartsWith("Pod9x", StringComparison.OrdinalIgnoreCase))
                    return (-6f, 6f, -1.14f, 10.86f, 6f);
                if (group.StartsWith("Pod4x", StringComparison.OrdinalIgnoreCase))
                    return (-4f, 4f, -1.14f, 6.86f, 6f);
                if (group.Equals("EscapePod", StringComparison.OrdinalIgnoreCase))
                    return (-3f, 3f, -3f, 3f, 4f);
                if (group.StartsWith("VehicleCrafter", StringComparison.OrdinalIgnoreCase))
                    return (-3f, 3f, -3f, 3f, 0f);

                return part switch
                {
                    PlanPart.Pod => (-4f, 4f, -4f, 4f, 6f),
                    PlanPart.Foundation => (-3f, 3f, -3f, 3f, 2f),
                    PlanPart.Platform => (-2f, 2f, -4f, 4f, 0f),
                    PlanPart.Dome => (-11.5f, 11.5f, -11.5f, 11.5f, 10f),
                    PlanPart.Lab => (-4f, 4f, -1.14f, 6.86f, 6f),
                    PlanPart.Aquarium => (-4f, 4f, -4f, 4f, 6f),
                    _ => (-2f, 2f, -2f, 2f, 0f)
                };
            }
        }
    }
}
