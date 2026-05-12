using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace HillClimbingAlgorithm;

internal sealed class HillClimbingAlgorithm {

    /// <summary>Represents a <see cref="Map"/> of heights.</summary>
    private sealed class Map {

        /// <summary>Represents a two-dimensional <see cref="Position"/>.</summary>
        /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
        /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
        private readonly record struct Position(int X, int Y);

        /// <summary>Minimum allowed height at any <see cref="Position"/>.</summary>
        private const int MinHeight = 0;

        /// <summary>Maximum allowed height at any <see cref="Position"/>.</summary>
        private const int MaxHeight = 25;

        /// <summary>Array of all possible direction offsets, cached for efficiency.</summary>
        private static readonly ImmutableArray<(int XOffset, int YOffset)> Offsets = [
            (-1, 0), (0, -1), (1, 0), (0, 1)
        ];

        /// <summary>Heights of this <see cref="Map"/>.</summary>
        /// <remarks>
        /// Note that this is actually a two-dimensional array stored as a one-dimensional one (in
        /// row-major order) for reasons of improved cache locality and performance.
        /// </remarks>
        private readonly ImmutableArray<int> heights;

        /// <summary>Width of this <see cref="Map"/> of heights.</summary>
        private readonly int width;

        /// <summary>Height of this <see cref="Map"/> of heights.</summary>
        private readonly int height;

        /// <summary>Start <see cref="Position"/> used for pathfinding.</summary>
        private readonly Position start;

        /// <summary>End <see cref="Position"/> used for pathfinding.</summary>
        private readonly Position end;

        /// <summary>Initializes a new <see cref="Map"/> with a given array of heights.</summary>
        /// <param name="heights">Array of heights for the initialization.</param>
        /// <param name="start">Start <see cref="Position"/> used for pathfinding.</param>
        /// <param name="end">End <see cref="Position"/> used for pathfinding.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="heights"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="heights"/> contains an invalid height (which is not in the
        /// range [<see cref="MinHeight"/>; <see cref="MaxHeight"/>]) or <paramref name="start"/>
        /// or <paramref name="end"/> is not a valid position.
        /// </exception>
        private Map(int[][] heights, Position start, Position end) {
            Guard.IsNotNull(heights);
            this.heights = [.. heights.SelectMany(row => row)];
            if (this.heights.Any(height => (height < MinHeight) || (height > MaxHeight))) {
                throw new ArgumentOutOfRangeException(
                    nameof(heights),
                    $"All heights must be in the range [{MinHeight}; {MaxHeight}]."
                );
            }
            width = heights[0].Length;
            height = heights.Length;
            if (!IsValidPosition(start)) {
                throw new ArgumentOutOfRangeException(
                    nameof(start),
                    $"Start (X = {start.X}, Y = {start.Y}) is not a valid position."
                );
            }
            if (!IsValidPosition(end)) {
                throw new ArgumentOutOfRangeException(
                    nameof(end),
                    $"End (X = {end.X}, Y = {end.Y}) is not a valid position."
                );
            }
            this.start = start;
            this.end = end;
        }

        /// <summary>Returns the height for a given character.</summary>
        /// <remarks>
        /// The character <paramref name="c"/> must be the start position ('S'), the end position
        /// ('E') or a lowercase letter ('a' through 'z').
        /// </remarks>
        /// <param name="c">Character to determine the height for.</param>
        /// <returns>The height for the given character.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="c"/> does not represent a valid height.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Height(char c) => c switch {
            'S' => MinHeight,
            'E' => MaxHeight,
            (>= 'a') and (<= 'z') => c - 'a',
            _ => throw new ArgumentOutOfRangeException(
                nameof(c),
                $"The character '{c}' does not represent a valid height."
            )
        };

        /// <summary>Parses a <see cref="Map"/> of heights from a given string.</summary>
        /// <remarks>
        /// The given string <paramref name="s"/> must consist of zero or more lines of only letters
        /// 'a' through 'z', 'S' or 'E' representing the heights of the <see cref="Map"/>. 'S' and
        /// 'E' mark the start and end position and must only be present exactly once. All lines
        /// must be of the same length. An example might be the following (actual newlines
        /// rendered):
        /// <example>
        /// <code>
        /// Sabqponm
        /// abcryxxl
        /// accszExk
        /// acctuvwj
        /// abdefghi
        /// </code>
        /// </example>
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Map"/> from.</param>
        /// <returns>A <see cref="Map"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="s"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> contains an invalid height, or if the start or end
        /// position is missing or present more than once.
        /// </exception>
        public static Map Parse(string s) {
            Guard.IsNotNull(s);
            ReadOnlySpan<string> lines = [.. s.Split(Environment.NewLine)];
            int width = lines[0].Length;
            int height = lines.Length;
            int[][] heights = new int[height][];
            Position? start = null;
            Position? end = null;
            for (int y = 0; y < height; y++) {
                heights[y] = new int[width];
                for (int x = 0; x < width; x++) {
                    if (lines[y][x] == 'S') {
                        if (start == null) {
                            start = new Position(x, y);
                        }
                        else {
                            throw new ArgumentOutOfRangeException(
                                nameof(s),
                                "Start must only be present exactly once."
                            );
                        }
                    }
                    else if (lines[y][x] == 'E') {
                        if (end == null) {
                            end = new Position(x, y);
                        }
                        else {
                            throw new ArgumentOutOfRangeException(
                                nameof(s),
                                "End must only be present exactly once."
                            );
                        }
                    }
                    heights[y][x] = Height(lines[y][x]);
                }
            }
            if ((start == null) || (end == null)) {
                throw new ArgumentOutOfRangeException(nameof(s), "Start or end is missing.");
            }
            return new Map(heights, start.Value, end.Value);
        }

        /// <summary>
        /// Determines if a given <see cref="Position"/> is valid with respect to the width and
        /// height of this <see cref="Map"/>.
        /// </summary>
        /// <param name="position"><see cref="Position"/> to check.</param>
        /// <returns>
        /// <see langword="True"/> if the given <see cref="Position"/> is valid,
        /// otherwise <see langword="false"/>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidPosition(Position position)
            => (position.X >= 0) && (position.X < width)
                && (position.Y >= 0) && (position.Y < height);

        /// <summary>Returns the index of a height at a given <see cref="Position"/>.</summary>
        /// <param name="position"><see cref="Position"/> of the height to get the index of.</param>
        /// <returns>The index of the height at the given <see cref="Position"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Index(Position position) => (position.Y * width) + position.X;

        /// <summary>Returns all existing neighbors of a given <see cref="Position"/>.</summary>
        /// <param name="position">
        /// <see cref="Position"/> to determine all existing neighbors of.
        /// </param>
        /// <returns>All existing neighbors of the given <see cref="Position"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IEnumerable<Position> Neighbors(Position position) {
            foreach ((int xOffset, int yOffset) in Offsets) {
                Position neighbor = position with {
                    X = position.X + xOffset,
                    Y = position.Y + yOffset
                };
                if (IsValidPosition(neighbor)) {
                    yield return neighbor;
                }
            }
        }

        /// <summary>
        /// Tries to find the shortest path from a given start <see cref="Position"/> to the end.
        /// </summary>
        /// <param name="start"><see cref="Position"/> to start the pathfinding at.</param>
        /// <param name="distance">
        /// Distance or length of the shortest path if one was found (indicated by a return value of
        /// <see langword="true"/>), otherwise the <see langword="default"/>.
        /// </param>
        /// <returns>
        /// <see langword="True"/> if a shortest path was found, otherwise <see langword="false"/>.
        /// </returns>
        private bool TryFindShortestPathFrom(Position start, out int distance) {
            Dictionary<Position, int> distances = new() { [start] = 0 };
            HashSet<Position> visited = [start];
            PriorityQueue<Position, int> queue = new([(start, 0)]);
            while (queue.Count > 0) {
                Position position = queue.Dequeue();
                if (position == end) {
                    break;
                }
                // We can only continue our path to neighbors that are at most one higher than the
                // height of the current position.
                int allowedHeight = heights[Index(position)] + 1;
                int newDistance = distances[position] + 1;
                foreach (Position neighbor in Neighbors(position)) {
                    if (heights[Index(neighbor)] <= allowedHeight) {
                        if (!distances.TryGetValue(neighbor, out int previousDistance)
                                || (newDistance < previousDistance)) {
                            distances[neighbor] = newDistance;
                            if (visited.Add(neighbor)) {
                                queue.Enqueue(neighbor, newDistance);
                            }
                        }
                    }
                }
            }
            return distances.TryGetValue(end, out distance);
        }

        /// <summary>
        /// Tries to find the shortest path from the start <see cref="Position"/> to the end.
        /// </summary>
        /// <param name="distanceFromStart">
        /// Distance or length of the shortest path if one was found (indicated by a return value of
        /// <see langword="true"/>), otherwise the <see langword="default"/>.
        /// </param>
        /// <returns>
        /// <see langword="True"/> if a shortest path was found, otherwise <see langword="false"/>.
        /// </returns>
        public bool TryFindShortestPathFromStart(out int distanceFromStart)
            => TryFindShortestPathFrom(start, out distanceFromStart);

        /// <summary>
        /// Tries to find the shortest path from any of the lowest positions to the end.
        /// </summary>
        /// <param name="distanceFromAnyLowest">
        /// Distance or length of the shortest path if one was found (indicated by a return value of
        /// <see langword="true"/>), otherwise the <see langword="default"/>.
        /// </param>
        /// <returns>
        /// <see langword="True"/> if a shortest path was found, otherwise <see langword="false"/>.
        /// </returns>
        public bool TryFindShortestPathFromAnyLowest(out int distanceFromAnyLowest) {
            int minDistance = int.MaxValue;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    Position position = new(x, y);
                    if ((heights[Index(position)] == heights[Index(start)])
                            && TryFindShortestPathFrom(position, out distanceFromAnyLowest)) {
                        minDistance = Math.Min(minDistance, distanceFromAnyLowest);
                    }
                }
            }
            if (minDistance != int.MaxValue) {
                distanceFromAnyLowest = minDistance;
                return true;
            }
            distanceFromAnyLowest = default;
            return false;
        }

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Solves the <see cref="HillClimbingAlgorithm"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        Map map = Map.Parse(File.ReadAllText(InputFile));
        _ = map.TryFindShortestPathFromStart(out int distanceFromStart);
        _ = map.TryFindShortestPathFromAnyLowest(out int distanceFromAnyLowest);
        textWriter.WriteLine(
            $"The shortest distance from the start position is {distanceFromStart}."
        );
        textWriter.WriteLine(
            $"The shortest distance from any lowest position is {distanceFromAnyLowest}."
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