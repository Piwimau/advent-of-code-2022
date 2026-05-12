using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace RegolithReservoir;

internal sealed partial class RegolithReservoir {

    /// <summary>Represents a two-dimensional <see cref="Position"/>.</summary>
    /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
    /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
    private readonly partial record struct Position(int X, int Y) {

        [GeneratedRegex("^\\d+,\\d+$")]
        private static partial Regex PositionRegex();

        /// <summary>Parses a <see cref="Position"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must contain two positive, comma-separated integers
        /// as in "42,17".
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
            int commaIndex = s.IndexOf(',');
            int x = int.Parse(s[..commaIndex], CultureInfo.InvariantCulture);
            int y = int.Parse(s[(commaIndex + 1)..], CultureInfo.InvariantCulture);
            return new Position(x, y);
        }

    }

    /// <summary>Length of the separator (" -> ") for parsing lines of rocks.</summary>
    private const int SeparatorLength = 4;

    /// <summary>Source <see cref="Position"/> from which sand begins to fall.</summary>
    private static readonly Position Source = new(500, 0);


    [GeneratedRegex("^\\d+,\\d+(?: -> \\d+,\\d+)+$")]
    private static partial Regex LineRegex();

    /// <summary>Parses all rocks from a given string.</summary>
    /// <remarks>
    /// The string <paramref name="s"/> must contain two or more positions separated by " -> ".
    /// A <see cref="Position"/> is represented as two positive, comma-separated integers.<br/>
    /// For more information, see the <see cref="Position.Parse(ReadOnlySpan{char})"/> method.
    /// <para/>
    /// An example for a valid string might be "498,4 -> 498,6 -> 496,6".
    /// </remarks>
    /// <param name="s">String to parse the rocks from.</param>
    /// <returns>All rocks parsed from the given string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="s"/> has an invalid format.
    /// </exception>
    private static HashSet<Position> ParseRocks(ReadOnlySpan<char> s) {
        if (!LineRegex().IsMatch(s)) {
            throw new ArgumentOutOfRangeException(
                nameof(s),
                $"The string \"{s}\" has an invalid format."
            );
        }
        HashSet<Position> rocks = [];
        int separatorIndex = s.IndexOf(" -> ");
        Position start = Position.Parse(s[..separatorIndex]);
        while (separatorIndex >= 0) {
            s = s[(separatorIndex + SeparatorLength)..];
            separatorIndex = s.IndexOf(" -> ");
            Position end = Position.Parse((separatorIndex >= 0) ? s[..separatorIndex] : s);
            int xOffset = Math.Sign(end.X - start.X);
            int yOffset = Math.Sign(end.Y - start.Y);
            while (start != end) {
                rocks.Add(start);
                start = start with { X = start.X + xOffset, Y = start.Y + yOffset };
            }
            rocks.Add(end);
        }
        return rocks;
    }

    /// <summary>
    /// Returns the number of resting units of sand when the bottom of the cave is an open void.
    /// </summary>
    /// <param name="initialRocks">Set of initial rocks inside the cave.</param>
    /// <returns>The number of resting units of sand.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="initialRocks"/> is <see langword="null"/>.
    /// </exception>
    private static int RestingUnitsOfSandWithOpenVoid(IReadOnlySet<Position> initialRocks) {
        Guard.IsNotNull(initialRocks);
        int maxY = initialRocks.Max(initialRock => initialRock.Y);
        HashSet<Position> unitsOfSand = [];
        bool sandFallingIntoVoid = false;
        while (!sandFallingIntoVoid) {
            Position position = Source;
            bool sandStoppedFalling = false;
            while ((position.Y < maxY) && !sandStoppedFalling) {
                Position down = position with { Y = position.Y + 1 };
                if (!initialRocks.Contains(down) && !unitsOfSand.Contains(down)) {
                    position = down;
                    continue;
                }
                Position downLeft = position with { X = position.X - 1, Y = position.Y + 1 };
                if (!initialRocks.Contains(downLeft) && !unitsOfSand.Contains(downLeft)) {
                    position = downLeft;
                    continue;
                }
                Position downRight = position with { X = position.X + 1, Y = position.Y + 1 };
                if (!initialRocks.Contains(downRight) && !unitsOfSand.Contains(downRight)) {
                    position = downRight;
                }
                else {
                    sandStoppedFalling = true;
                }
            }
            if (sandStoppedFalling) {
                unitsOfSand.Add(position);
            }
            else {
                sandFallingIntoVoid = true;
            }
        }
        return unitsOfSand.Count;
    }

    /// <summary>
    /// Returns the number of resting units of sand when the bottom of the cave is an infinite
    /// floor of rocks.
    /// </summary>
    /// <param name="initialRocks">Set of initial rocks inside the cave.</param>
    /// <returns>The number of resting units of sand.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="initialRocks"/> is <see langword="null"/>.
    /// </exception>
    private static int RestingUnitsOfSandWithInfiniteFloor(IReadOnlySet<Position> initialRocks) {
        Guard.IsNotNull(initialRocks);
        // Infinite floor of rocks is placed two units below the maximum y coordinate.
        int maxY = initialRocks.Max(initialRock => initialRock.Y) + 2;
        HashSet<Position> rocks = [.. initialRocks];
        HashSet<Position> unitsOfSand = [];
        while (!unitsOfSand.Contains(Source)) {
            Position position = Source;
            bool sandStoppedFalling = false;
            while ((position.Y < maxY) && !sandStoppedFalling) {
                Position down = position with { Y = position.Y + 1 };
                if (!rocks.Contains(down) && !unitsOfSand.Contains(down)) {
                    position = down;
                    continue;
                }
                Position downLeft = position with { X = position.X - 1, Y = position.Y + 1 };
                if (!rocks.Contains(downLeft) && !unitsOfSand.Contains(downLeft)) {
                    position = downLeft;
                    continue;
                }
                Position downRight = position with { X = position.X + 1, Y = position.Y + 1 };
                if (!rocks.Contains(downRight) && !unitsOfSand.Contains(downRight)) {
                    position = downRight;
                }
                else {
                    sandStoppedFalling = true;
                }
            }
            if (sandStoppedFalling) {
                unitsOfSand.Add(position);
            }
            else {
                // Normally the sand would have fallen into the void, this cave however has an
                // infinite floor two units below the other rocks. By instead placing a rock at that
                // position, we can stop sand from falling in further steps. As such, the floor is
                // generated by the algorithm while simulating the falling sand.
                rocks.Add(position);
            }
        }
        return unitsOfSand.Count;
    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Solves the <see cref="RegolithReservoir"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        IReadOnlySet<Position> rocks = File.ReadLines(InputFile)
            .SelectMany(line => ParseRocks(line))
            .ToFrozenSet();
        int restingOpenVoid = RestingUnitsOfSandWithOpenVoid(rocks);
        int restingInfiniteFloor = RestingUnitsOfSandWithInfiniteFloor(rocks);
        textWriter.WriteLine(
            $"With an open void as the bottom, {restingOpenVoid} units of sand come to rest."
        );
        textWriter.WriteLine(
            $"With an infinite floor of rocks as the bottom, {restingInfiniteFloor} units of "
                + "sand come to rest."
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