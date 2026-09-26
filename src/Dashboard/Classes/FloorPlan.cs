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
        Console,

        // A trade rocket's landing circle, on a trade platform's deck.
        Rocket,

        // The arc of that circle where you go in.
        RocketEntrance,
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
                .SelectMany(VehiclePlatformConsole)
                .SelectMany(TradePlatformConsole)
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

        /// <summary>A vehicle platform's console, where vehicles are built: a small marker on its deck, at the spot found by standing at it.</summary>
        private static IEnumerable<StructureData> VehiclePlatformConsole(StructureData s)
        {
            yield return s;

            if (s.Group.StartsWith("VehicleCrafter", StringComparison.OrdinalIgnoreCase))
                yield return Part(s, "Vehicle console", s.Position!.Y + PlatformDeck, -10.85, -9.65, -2.1, -0.9, 1);
        }

        /// <summary>
        /// A trade platform's console (a small marker, 1.2 m square) and the circle where its rocket lands, both on its deck. Found by standing at it, facing it, on the
        /// deck at (2.49, 2.74) in the platform's own frame (facing +x, toward the south edge); the console is put 1 m ahead of that spot.
        /// </summary>
        private static IEnumerable<StructureData> TradePlatformConsole(StructureData s)
        {
            yield return s;

            if (!s.Group.StartsWith("TradePlatform", StringComparison.OrdinalIgnoreCase))
                yield break;

            // The rocket lands across the middle of four foundations: a circle of half a foundation's radius centred on the
            // corner they share, at z -4.01 (the owner stood 2.8 m from it, on the circle's edge, and the plugin's own
            // deck search found a flat 6 m slab there).
            yield return Part(s, "Trade rocket", s.Position!.Y + PlatformDeck, -3.045, 3.045, -7.055, -0.965, 1);
            yield return Part(s, "Trade rocket entrance", s.Position!.Y + PlatformDeck, -3.045, 3.045, -7.055, -0.965, 1);
            yield return Part(s, "Trade console", s.Position!.Y + PlatformDeck, 2.9, 4.1, 2.15, 3.35, 1);
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
            PlanPart.Dome or PlanPart.Rocket => 1,
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
                else if (IsVehiclePlatform)
                {
                    // Its shape is known too (see VehiclePlatformRamp): the deck is the slab the plugin measured.
                    (_minX, _maxX, _minZ, _maxZ, top) = (-12.72f, 5.55f, -10.92f, 8.56f, 0f);
                    Measured = true;
                }
                else if (IsTradePlatform)
                {
                    // Its shape is known too (see TradePlatformDeck): the deck only; the stairs are drawn as an extra.
                    (_minX, _maxX, _minZ, _maxZ, top) = (-6.09f, 6.09f, -10.1f, 8.17f, 0f);
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

            private bool IsVehiclePlatform => Data.Group.StartsWith("VehicleCrafter", StringComparison.OrdinalIgnoreCase);

            /// <summary>
            /// Where the trade rocket's circle is entered, as an angle round its centre in the platform's frame (0 is +x, toward the
            /// south; 90 is +z, toward the east), and how wide the opening is. It is at the east edge: the first guess, from the
            /// owner facing it from inside the circle (aim 80 degrees off -z), put it on the west side, and the owner said it is
            /// the exact opposite. The width is a guess (a doorway, about 2 m of the 3 m circle).
            /// </summary>
            private const float TradeRocketEntranceAngle = 90f, TradeRocketEntranceWidth = 40f;

            /// <summary>
            /// The 3x3 living compartment (Pod9x, seen as Pod9xA) is round, not square: 24 m across, centred on its position. Read
            /// from what the plugin measured on the owner's (standing in it): a box of -12 to +12 on both axes, a flat floor slab
            /// about 23.2 m wide on both, the four wall panels on a ring 11.4 m out, and a 3x3 grid of 6 m tiles (the corner ones
            /// the rounded ones) inside. So it is drawn as a circle of the box's radius, like a dome.
            /// </summary>
            private bool IsRoundPod => Data.Group.StartsWith("Pod9x", StringComparison.OrdinalIgnoreCase);

            private bool IsTradePlatform => Data.Group.StartsWith("TradePlatform", StringComparison.OrdinalIgnoreCase);

            /// <summary>
            /// A trade platform's deck, in its own frame (x, z; its yaw is 180, so x runs south and z runs east): 3 foundations
            /// of 6.09 m along z, from -10.1 to +8.17, and 2 across x, from -6.09 to +6.09, no cut corners. Found by standing
            /// on it: the owner's east corners and northwest corner read as the middles of foundations (about ±3.1 to ±3.4 m
            /// across, 5.2 m east, -7.8 m west), and the plugin's measured box (z -10.1 to +15.05) is exactly this deck plus the stairs.
            /// </summary>
            private static readonly Vector2[] TradePlatformDeck =
            {
                new(-6.09f, -10.1f), new(6.09f, -10.1f), new(6.09f, 8.17f), new(-6.09f, 8.17f)
            };

            /// <summary>
            /// Its stairs: off the middle of the deck's east edge (in line with its position), half a foundation wide (3 m) and
            /// 6.88 m long, down to the ground, out to where the measured box ends (15.05). Described by the owner and checked by
            /// standing on them (0.6 m off the middle line at the top, 11.1 m out and 2 m below the deck part way down).
            /// </summary>
            private static readonly Vector2[] TradePlatformStairs =
            {
                new(-1.5f, 8.17f), new(1.5f, 8.17f), new(1.5f, 15.05f), new(-1.5f, 15.05f)
            };

            /// <summary>
            /// A vehicle platform's deck, in its own frame (x, z): the slab the plugin measured (X -12.72 to +5.55, 3 tiles of
            /// 6.09 m; Z -10.92 to +8.56), with both of its -X corners cut on the diagonal, 6.09 m each way. Found by
            /// standing on it: the +Z one is a diagonal piece between the ramp and the deck, the -Z one a drop-off; the
            /// console stands in the middle of the -X edge left between them.
            /// </summary>
            private static readonly Vector2[] VehiclePlatformDeck =
            {
                new(-12.72f, -4.83f), new(-6.63f, -10.92f), new(5.55f, -10.92f), new(5.55f, 8.56f), new(-6.63f, 8.56f), new(-12.72f, 2.47f)
            };

            /// <summary>
            /// Its ramp, which the vehicle drives up: off the deck's +Z side across the two +X tiles, out to where the
            /// measured box ends. Found by standing on the ramp's far corners and its top.
            /// </summary>
            private static readonly Vector2[] VehiclePlatformRamp =
            {
                new(-6.63f, 8.56f), new(5.55f, 8.56f), new(5.55f, 19.78f), new(-6.63f, 19.78f)
            };

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
                // Where you go in to a trade rocket: a short arc on its circle, at TradeRocketEntranceAngle.
                if (Part == PlanPart.RocketEntrance)
                {
                    var (ex, ez, er) = ((_minX + _maxX) / 2, (_minZ + _maxZ) / 2, (_maxX - _minX) / 2);
                    const float Thickness = 0.25f, Steps = 8;
                    var half = TradeRocketEntranceWidth / 2 * MathF.PI / 180;
                    var centre = TradeRocketEntranceAngle * MathF.PI / 180;

                    IEnumerable<Vector2> Arc(float radius, bool forward) => Enumerable.Range(0, (int)Steps + 1)
                        .Select(i => forward ? i : (int)Steps - i)
                        .Select(i => centre - half + 2 * half * i / Steps)
                        .Select(a => ToEastNorth(ex + radius * MathF.Cos(a), ez + radius * MathF.Sin(a)));

                    return Arc(er + Thickness, true).Concat(Arc(er - Thickness, false)).ToList();
                }

                // A trade rocket's landing circle: centred in its box, which is its own square.
                if (Part == PlanPart.Rocket)
                {
                    var (cx, cz, radius) = ((_minX + _maxX) / 2, (_minZ + _maxZ) / 2, (_maxX - _minX) / 2);

                    return Enumerable.Range(0, 32)
                        .Select(i => i * MathF.PI * 2 / 32)
                        .Select(a => ToEastNorth(cx + radius * MathF.Cos(a), cz + radius * MathF.Sin(a)))
                        .ToList();
                }

                if (Part is PlanPart.Dome or PlanPart.Aquarium || IsRoundPod)
                {
                    // Domes and the T2 aquarium are round, centred on the middle of their box, which is NOT their position: a T2
                    // dome's box runs from -20 to +12 on its x, so its middle is 4 m off its position (the owner stood in the
                    // middle of the Butterfly dome: 3.95 m along, exactly the box's middle). The radius is the box's shorter
                    // half, and whatever the longer one reaches past the circle is an entrance annex, on both ends. Checked on
                    // the row of domes: centres 32 m apart, radius 13.5 each, annexes of 2.5 m meeting exactly; and the small
                    // biodome (radius 10.1, annexes 1.9 m) making up the 28 m to the next one.
                    float cx = (_minX + _maxX) / 2, cz = (_minZ + _maxZ) / 2;
                    float halfX = (_maxX - _minX) / 2, halfZ = (_maxZ - _minZ) / 2;
                    var r = MathF.Max(1f, MathF.Min(halfX, halfZ));

                    // A corridor-type wall panel (a doorway to a neighbouring piece) can sit flush with the circle even
                    // when the box itself has no overhang there — confirmed in the game between a Biodome and a Biodome2:
                    // both carry a corridor panel facing each other, with a real connector between them, but neither box
                    // reaches past the other's circle. Such a panel still gets a short stub, so the doorway always shows.
                    const float CorridorStub = 3f;
                    float reachMinusX = halfX, reachPlusX = halfX, reachMinusZ = halfZ, reachPlusZ = halfZ;

                    if (Data.PanelBoxes is { Count: > 0 } panelBoxes)
                    {
                        foreach (var b in panelBoxes)
                        {
                            if (b.Type != PanelCodes.TypeWall || b.Sub != PanelCodes.WallCorridor || b.Min is null || b.Max is null)
                                continue;

                            float x0 = (float)b.Min.X - cx, x1 = (float)b.Max.X - cx, z0 = (float)b.Min.Z - cz, z1 = (float)b.Max.Z - cz;
                            var stubReach = r + CorridorStub;

                            // The panel's short side (its thickness) says which of the four sides it sits on.
                            if (x1 - x0 < z1 - z0)
                            {
                                if (x0 + x1 > 0) reachPlusX = MathF.Max(reachPlusX, stubReach);
                                else reachMinusX = MathF.Max(reachMinusX, stubReach);
                            }
                            else
                            {
                                if (z0 + z1 > 0) reachPlusZ = MathF.Max(reachPlusZ, stubReach);
                                else reachMinusZ = MathF.Max(reachMinusZ, stubReach);
                            }
                        }
                    }

                    AddAnnex(reachMinusX, r, cx, cz, (a, b) => (-a, b));
                    AddAnnex(reachPlusX, r, cx, cz, (a, b) => (a, b));
                    AddAnnex(reachMinusZ, r, cx, cz, (a, b) => (b, -a));
                    AddAnnex(reachPlusZ, r, cx, cz, (a, b) => (b, a));

                    return Enumerable.Range(0, 32)
                        .Select(i => i * MathF.PI * 2 / 32)
                        .Select(a => ToEastNorth(cx + r * MathF.Cos(a), cz + r * MathF.Sin(a)))
                        .ToList();
                }
                if (IsVehiclePlatform)
                {
                    Extras.Add(VehiclePlatformRamp.Select(p => ToEastNorth(p.X, p.Y)).ToList());
                    return VehiclePlatformDeck.Select(p => ToEastNorth(p.X, p.Y)).ToList();
                }

                if (IsTradePlatform)
                {
                    Extras.Add(TradePlatformStairs.Select(p => ToEastNorth(p.X, p.Y)).ToList());
                    return TradePlatformDeck.Select(p => ToEastNorth(p.X, p.Y)).ToList();
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

            // An annex from just inside the circle out to how far this side reaches, 2 m wide (halved from 4 m: full width
            // read as an odd slab rather than a doorway/tunnel). "place" turns (along, across) into (x, z) from the circle's centre.
            private void AddAnnex(float reach, float r, float cx, float cz, Func<float, float, (float X, float Z)> place)
            {
                const float HalfWidth = 1f;
                if (reach <= r + 0.5f)
                    return;

                var corners = new[] { place(r - 0.5f, -HalfWidth), place(reach, -HalfWidth), place(reach, HalfWidth), place(r - 0.5f, HalfWidth) };
                Extras.Add(corners.Select(c => ToEastNorth(cx + c.X, cz + c.Z)).ToList());
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
                if (group.Equals("Vehicle console", StringComparison.OrdinalIgnoreCase) || group.Equals("Trade console", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.Console;
                if (group.Equals("Trade rocket", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.Rocket;
                if (group.Equals("Trade rocket entrance", StringComparison.OrdinalIgnoreCase))
                    return PlanPart.RocketEntrance;
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

                // Nine floor tiles under one round roof, 24 m across and centred on its position (measured in the game, and now
                // drawn as a circle); the old guess was a 12 m square.
                if (group.StartsWith("Pod9x", StringComparison.OrdinalIgnoreCase))
                    return (-12f, 12f, -12f, 12f, 6f);
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
