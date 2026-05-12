using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace BoilingBoulders;

internal sealed partial class BoilingBoulders {

    /// <summary>Represents a three-dimensional <see cref="Position"/>.</summary>    
    /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
    /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
    /// <param name="Z">Z-coordinate of the <see cref="Position"/>.</param>
    private readonly partial record struct Position(int X, int Y, int Z) {

        [GeneratedRegex("^\\d+,\\d+,\\d+$")]
        private static partial Regex PositionRegex();

        /// <summary>Parses a <see cref="Position"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must contain three positive, comma-separated integers,
        /// as in "3,42,17".
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Position"/> from.</param>
        /// <returns>A <see cref="Position"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Position Parse(ReadOnlySpan<char> s) {
            if (!PositionRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid position."
                );
            }
            int firstCommaIndex = s.IndexOf(',');
            int secondCommaIndex = s.LastIndexOf(',');
            int x = int.Parse(s[..firstCommaIndex], CultureInfo.InvariantCulture);
            int y = int.Parse(
                s[(firstCommaIndex + 1)..secondCommaIndex],
                CultureInfo.InvariantCulture
            );
            int z = int.Parse(s[(secondCommaIndex + 1)..], CultureInfo.InvariantCulture);
            return new Position(x, y, z);
        }

        /// <summary>Returns all neighbors of this <see cref="Position"/>.</summary>
        /// <returns>All neighbors of this <see cref="Position"/>.</returns>
        public IEnumerable<Position> Neighbors() => [
            this with { X = X - 1 },
            this with { X = X + 1 },
            this with { Y = Y - 1 },
            this with { Y = Y + 1 },
            this with { Z = Z - 1 },
            this with { Z = Z + 1 }
        ];

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>
    /// Returns the total surface area of a given set of cubes forming a lava droplet.
    /// </summary>
    /// <remarks>
    /// Note that the total surface area does include any air pockets trapped inside the lava
    /// droplet.
    /// </remarks>
    /// <param name="cubes">Set of cubes for the calculation.</param>
    /// <returns>The total surface area of the given set of cubes forming a lava droplet.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="cubes"/> is <see langword="null"/>.
    /// </exception>
    private static int TotalSurfaceArea(FrozenSet<Position> cubes) {
        Guard.IsNotNull(cubes);
        return cubes.Sum(cube => cube.Neighbors().Count(neighbor => !cubes.Contains(neighbor)));
    }

    /// <summary>
    /// Returns the exterior surface area of a given set of cubes forming a lava droplet.
    /// </summary>
    /// <remarks>
    /// The exterior surface area does not include any air pockets trapped inside the lava droplet,
    /// in constrast to the <see cref="TotalSurfaceArea(FrozenSet{Position})"/> method.
    /// </remarks>
    /// <param name="cubes">Set of cubes for the calculation.</param>
    /// <returns>
    /// The exterior surface area of the given set of cubes forming a lava droplet.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="cubes"/> is <see langword="null"/>.
    /// </exception>
    private static int ExteriorSurfaceArea(FrozenSet<Position> cubes) {
        Guard.IsNotNull(cubes);
        if (cubes.Count == 0) {
            return 0;
        }
        // To restrict the search for the exterior surface area, we first calculate a bounding box
        // around the lava droplet. This bounding box needs to be one unit larger than the minimum
        // and maximum x-, y- or z-coordinate to include any cubes on the edges.
        Position min = new(int.MaxValue, int.MaxValue, int.MaxValue);
        Position max = new(int.MinValue, int.MinValue, int.MinValue);
        foreach (Position cube in cubes) {
            min = min with {
                X = Math.Min(min.X, cube.X),
                Y = Math.Min(min.Y, cube.Y),
                Z = Math.Min(min.Z, cube.Z)
            };
            max = max with {
                X = Math.Max(max.X, cube.X),
                Y = Math.Max(max.Y, cube.Y),
                Z = Math.Max(max.Z, cube.Z)
            };
        }
        min = min with { X = min.X - 1, Y = min.Y - 1, Z = min.Z - 1 };
        max = max with { X = max.X + 1, Y = max.Y + 1, Z = max.Z + 1 };
        HashSet<Position> visited = [min];
        Queue<Position> queue = new([min]);
        int exteriorSurfaceArea = 0;
        while (queue.Count > 0) {
            Position position = queue.Dequeue();
            foreach (Position neighbor in position.Neighbors()) {
                if ((neighbor.X < min.X) || (neighbor.X > max.X)
                        || (neighbor.Y < min.Y) || (neighbor.Y > max.Y)
                        || (neighbor.Z < min.Z) || (neighbor.Z > max.Z)) {
                    continue;
                }
                if (cubes.Contains(neighbor)) {
                    exteriorSurfaceArea++;
                }
                else if (visited.Add(neighbor)) {
                    queue.Enqueue(neighbor);
                }
            }
        }
        return exteriorSurfaceArea;
    }

    /// <summary>Solves the <see cref="BoilingBoulders"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        FrozenSet<Position> cubes = File.ReadLines(InputFile)
            .Select(line => Position.Parse(line))
            .ToFrozenSet();
        int totalSurfaceArea = TotalSurfaceArea(cubes);
        int exteriorSurfaceArea = ExteriorSurfaceArea(cubes);
        textWriter.WriteLine($"The total surface area is {totalSurfaceArea}.");
        textWriter.WriteLine($"The exterior surface area is {exteriorSurfaceArea}.");
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