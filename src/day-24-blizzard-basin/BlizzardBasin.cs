using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace BlizzardBasin;

internal sealed partial class BlizzardBasin {

    /// <summary>Represents a <see cref="Map"/> of blizzards used for pathfinding.</summary>
    private sealed partial class Map {

        /// <summary>Represents a two-dimensional <see cref="Position"/>.</summary>
        /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
        /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
        private readonly record struct Position(int X, int Y);

        /// <summary>
        /// Represents an enumeration of all possible directions for moving across the
        /// <see cref="Map"/>.
        /// </summary>
        private enum Direction { None, Left, Up, Right, Down }

        /// <summary>
        /// Minimum number of checkpoints required for pathfinding (at least one start and end).
        /// </summary>
        private const int MinRequiredCheckpoints = 2;

        /// <summary>Array of all directions, cached for efficiency.</summary>
        private static readonly ImmutableArray<Direction> Directions = [
            .. Enum.GetValues<Direction>()
        ];

        /// <summary>
        /// States of the blizzards in this <see cref="Map"/> at different points in time.
        /// </summary>
        /// <remarks>
        /// Blizzards move in a predictable pattern, resulting in a finite number of different
        /// states. The length of this array indicates the number of states before a cycle repeats.
        /// </remarks>
        private readonly ImmutableArray<FrozenSet<Position>> states;

        /// <summary>Width of this <see cref="Map"/>.</summary>
        private readonly int width;

        /// <summary>Height of this <see cref="Map"/>.</summary>
        private readonly int height;

        /// <summary><see cref="Position"/> to start the pathfinding at.</summary>
        private readonly Position start;

        /// <summary><see cref="Position"/> to end the pathfinding at.</summary>
        private readonly Position end;

        /// <summary>Initializes a new <see cref="Map"/>.</summary>
        /// <param name="states">
        /// States of the blizzards in the <see cref="Map"/> at different points in time.
        /// </param>
        /// <param name="width">Width of the <see cref="Map"/>.</param>
        /// <param name="height">Height of the <see cref="Map"/>.</param>
        /// <param name="start"><see cref="Position"/> to start the pathfinding at.</param>
        /// <param name="end"><see cref="Position"/> to end the pathfinding at.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="width"/> or <paramref name="height"/> is negative.
        /// </exception>
        private Map(
            ImmutableArray<FrozenSet<Position>> states,
            int width,
            int height,
            Position start,
            Position end
        ) {
            Guard.IsGreaterThanOrEqualTo(width, 0);
            Guard.IsGreaterThanOrEqualTo(height, 0);
            this.states = states;
            this.width = width;
            this.height = height;
            this.start = start;
            this.end = end;
        }

        [GeneratedRegex("^#+\\.#+\\r?\\n(?:#[\\^v<>.]+#\\r?\\n)*#[\\^v<>.]+#\\r?\\n#+\\.#+$")]
        private static partial Regex MapRegex();

        /// <summary>Returns the lowest common multiple of two given numbers.</summary>
        /// <typeparam name="T">Type of the numbers.</typeparam>
        /// <param name="a">First number for the calculation.</param>
        /// <param name="b">Second number for the calculation.</param>
        /// <returns>
        /// The lowest common multiple of <paramref name="a"/> and <paramref name="b"/>.
        /// </returns>
        private static T LowestCommonMultiple<T>(T a, T b) where T : INumber<T> {
            T absoluteProduct = T.Abs(a * b);
            while (b != T.Zero) {
                (a, b) = (b, a % b);
            }
            return absoluteProduct / a;
        }

        /// <summary>Parses a <see cref="Map"/> from a given sequence of lines.</summary>
        /// <remarks>
        /// The sequence of <paramref name="lines"/> must form a rectangle containing only walls
        /// ('#'), empty spots ('.') and blizzards ('&lt;', '^', '&gt;' or 'v'). In particular,
        /// the following requirements must be met:
        /// <list type="bullet">
        ///     <item>There must be at least three lines.</item>
        ///     <item>
        ///     The first line must only contain walls and exactly one empty spot, marking the start
        ///     <see cref="Position"/> for pathfinding.
        ///     </item>
        ///     <item>
        ///     The last line must only contain walls and exactly one empty spot, marking the end
        ///     <see cref="Position"/> for pathfinding.
        ///     </item>
        ///     <item>
        ///     Any lines between the first and last line must begin and end with exactly one wall,
        ///     the remaining characters must be empty spots or blizzards.
        ///     </item>
        /// </list>
        /// An example for a sequence of lines representing a valid <see cref="Map"/> may be the
        /// following:
        /// <example>
        /// <code>
        /// #.######
        /// #&gt;&gt;.&lt;^&lt;#
        /// #.&lt;..&lt;&lt;#
        /// #&gt;v.&gt;&lt;&gt;#
        /// #&lt;^v^^&gt;#
        /// ######.#
        /// </code>
        /// </example>
        /// </remarks>
        /// <param name="lines">Sequence of lines to parse a <see cref="Map"/> from.</param>
        /// <returns>A <see cref="Map"/> parsed from the given sequence of lines.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="lines"/> does not represent a valid <see cref="Map"/>.
        /// </exception>
        public static Map Parse(ReadOnlySpan<string> lines) {
            ReadOnlySpan<char> map = string.Join(Environment.NewLine, lines!);
            if (!MapRegex().IsMatch(map)) {
                throw new ArgumentOutOfRangeException(
                    nameof(lines),
                    $"The following map does not have a valid format:{Environment.NewLine}"
                        + $"{Environment.NewLine}{map}"
                );
            }
            List<(Position Position, Direction Direction)> blizzards = [];
            // As the whole map is surrounded by walls (except for the start and end position),
            // we can just ignore these and make the coordinates in the map zero-based.
            // This simplifies a lot of the computations (especially the wrapping behavior of
            // blizzards), but requires us to briefly translate between the coordinates in the
            // input and the actual map coordinates (i. e. a blizzard at (x, y) in the input is
            // actually situated at (x - 1, y - 1) in the map).
            for (int y = 1; y < (lines.Length - 1); y++) {
                for (int x = 1; x < (lines[0].Length - 1); x++) {
                    if (lines[y][x] != '.') {
                        Position position = new(x - 1, y - 1);
                        Direction direction = lines[y][x] switch {
                            '<' => Direction.Left,
                            '^' => Direction.Up,
                            '>' => Direction.Right,
                            'v' => Direction.Down,
                            _ => throw new InvalidOperationException("Unreachable.")
                        };
                        blizzards.Add((position, direction));
                    }
                }
            }
            // As stated above, the whole map is surrounded by a wall on each side, which needs to
            // be subtracted to get the actual width and height of the map.
            int width = lines[0].Length - 2;
            int height = lines.Length - 2;
            // Blizzards move in a predictable pattern, causing the same finite number of states
            // to repeat after a certain cycle length. We can use this to our advantage and
            // calculate the cycle length as the lowest common multiple of the width and height,
            // which allows us to only simulate and store one such cycle and reuse it later.
            int cycleLength = LowestCommonMultiple(width, height);
            ImmutableArray<FrozenSet<Position>>.Builder states =
                ImmutableArray.CreateBuilder<FrozenSet<Position>>(cycleLength);
            for (int i = 0; i < cycleLength; i++) {
                states.Add(blizzards.Select(blizzard => blizzard.Position).ToFrozenSet());
                for (int j = 0; j < blizzards.Count; j++) {
                    Position next = blizzards[j].Position;
                    next = blizzards[j].Direction switch {
                        Direction.Left => next with { X = (next.X + width - 1) % width },
                        Direction.Up => next with { Y = (next.Y + height - 1) % height },
                        Direction.Right => next with { X = (next.X + 1) % width },
                        Direction.Down => next with { Y = (next.Y + 1) % height },
                        _ => throw new InvalidOperationException("Unreachable.")
                    };
                    blizzards[j] = blizzards[j] with { Position = next };
                }
            }
            // Just like in the case of blizzards, one has to be subtracted from the x-coordinate
            // to account for the wall at the beginning of each line. Note that the start and end
            // position are actually placed outside the bounds of the map.
            Position start = new(lines[0].IndexOf('.') - 1, -1);
            Position end = new(lines[^1].IndexOf('.') - 1, height);
            return new Map(states.MoveToImmutable(), width, height, start, end);
        }

        /// <summary>
        /// Determines if a given <see cref="Position"/> is valid with respect to the bounds of this
        /// <see cref="Map"/>.
        /// </summary>
        /// <param name="position"><see cref="Position"/> to check.</param>
        /// <returns>
        /// <see langword="True"/> if the given <see cref="Position"/> is valid,
        /// otherwise <see langword="false"/>.
        /// </returns>
        private bool IsValidPosition(Position position)
            => (position.X >= 0) && (position.X < width)
                && (position.Y >= 0) && (position.Y < height);

        /// <summary>
        /// Returns the fewest number of minutes required to reach a sequence of checkpoints by
        /// avoiding the blizzards in this <see cref="Map"/>.
        /// </summary>
        /// <param name="checkpoints">
        /// Sequence of checkpoints to reach, which must have at least
        /// <see cref="MinRequiredCheckpoints"/> elements. The first one is treated as the start
        /// for pathfinding, the last one as the end.
        /// </param>
        /// <returns>
        /// The fewest number of minutes required to reach the given sequence of checkpoints.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="checkpoints"/> has less than
        /// <see cref="MinRequiredCheckpoints"/> elements.
        /// </exception>
        private int FewestMinutes(ReadOnlySpan<Position> checkpoints) {
            Guard.IsGreaterThanOrEqualTo(checkpoints.Length, MinRequiredCheckpoints);
            int totalMinutes = 0;
            for (int i = 0; i < (checkpoints.Length - 1); i++) {
                Position start = checkpoints[i];
                Position end = checkpoints[i + 1];
                PriorityQueue<Position, int> queue = new([(start, 0)]);
                HashSet<(Position Position, int Minutes)> visited = [];
                while (queue.Count > 0) {
                    // Use of TryDequeue() instead of Dequeue() is required, as we need the priority
                    // (the number of minutes that have passed since the start) as well. It is
                    // always going to succeed, so we can just ignore the return value.
                    _ = queue.TryDequeue(out Position position, out int minutes);
                    if (!visited.Add((position, minutes))) {
                        continue;
                    }
                    if (position == end) {
                        totalMinutes += minutes;
                        break;
                    }
                    foreach (Direction direction in Directions) {
                        Position next = direction switch {
                            Direction.None => position,
                            Direction.Left => position with { X = position.X - 1 },
                            Direction.Up => position with { Y = position.Y - 1 },
                            Direction.Right => position with { X = position.X + 1 },
                            Direction.Down => position with { Y = position.Y + 1 },
                            _ => throw new InvalidOperationException("Unreachable.")
                        };
                        // There is no point in expanding the state to positions outside the map,
                        // except for the special start and end position, which are situated one
                        // unit away from the edge.
                        if (!IsValidPosition(next) && (next != start) && (next != end)) {
                            continue;
                        }
                        // We can only move to the next position if we aren't going to be hit by a
                        // blizzard in the coming minute.
                        if (!states[(totalMinutes + minutes + 1) % states.Length].Contains(next)) {
                            queue.Enqueue(next, minutes + 1);
                        }
                    }
                }
                if (queue.Count == 0) {
                    throw new InvalidOperationException(
                        $"Unable to reach checkpoint {end} from {start}."
                    );
                }
            }
            return totalMinutes;
        }

        /// <summary>
        /// Returns the fewest number of minutes required to go from the start to the end.
        /// </summary>
        /// <returns>
        /// The fewest number of minutes required to go from the start to the end.
        /// </returns>
        public int FewestMinutes() => FewestMinutes([start, end]);

        /// <summary>
        /// Returns the fewest number of minutes required to go from the start to the end, back to
        /// the start and then to the end again.
        /// </summary>
        /// <returns>
        /// The fewest number of minutes required to go from the start to the end, back to the start
        /// and then to the end again.
        /// </returns>
        public int FewestMinutesBackAndForth() => FewestMinutes([start, end, start, end]);

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Solves the <see cref="BlizzardBasin"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        Map map = Map.Parse([.. File.ReadLines(InputFile)]);
        int fewestMinutes = map.FewestMinutes();
        int fewestMinutesBackAndForth = map.FewestMinutesBackAndForth();
        textWriter.WriteLine(
            $"The fewest number of minutes required to go from the start to the end is "
                + $"{fewestMinutes}."
        );
        textWriter.WriteLine(
            "The fewest number of minutes required to go back and forth is "
                + $"{fewestMinutesBackAndForth}."
        );
    }

    private static void Main(string[] args) {
        if (args.Contains("--benchmark")) {
            BenchmarkRunner.Run<Benchmark>();
        }
        else {
            Solve(Console.Out);
        }
    }

}