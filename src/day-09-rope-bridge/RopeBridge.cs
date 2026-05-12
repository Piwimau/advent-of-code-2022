using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace RopeBridge;

internal sealed partial class RopeBridge {

    /// <summary>Represents an enumeration of all possible directions.</summary>
    private enum Direction { Left, Up, Right, Down }

    /// <summary>Represents a <see cref="Motion"/> for a rope of knots.</summary>
    /// <param name="Direction">
    /// <see cref="RopeBridge.Direction"/> of the <see cref="Motion"/>.
    /// </param>
    /// <param name="Amount">Amount by which <see cref="Motion"/> occurs.</param>
    private readonly partial record struct Motion(Direction Direction, int Amount) {

        [GeneratedRegex("^[LURD] \\d+$")]
        private static partial Regex MotionRegex();

        /// <summary>Parses a <see cref="Motion"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must begin with any of the characters 'L', 'U', 'R' or
        /// 'D' indicating the <see cref="RopeBridge.Direction"/> of the <see cref="Motion"/>.
        /// It is followed by a single space and a positive integer for the amount.<br/>
        /// Examples for valid strings might be the following:
        /// <example>
        /// <code>
        /// "L 42"
        /// "U 7"
        /// "R 0"
        /// "D 17"
        /// </code>
        /// </example>
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Motion"/> from.</param>
        /// <returns>A <see cref="Motion"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Motion Parse(ReadOnlySpan<char> s) {
            if (!MotionRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid motion."
                );
            }
            Direction direction = s[0] switch {
                'L' => Direction.Left,
                'U' => Direction.Up,
                'R' => Direction.Right,
                'D' => Direction.Down,
                _ => throw new InvalidOperationException("Unreachable.")
            };
            int amount = int.Parse(s[(s.IndexOf(' ') + 1)..], CultureInfo.InvariantCulture);
            return new Motion(direction, amount);
        }

    }

    /// <summary>Represents a two-dimensional <see cref="Position"/>.</summary>
    /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
    /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
    private readonly record struct Position(int X, int Y);

    /// <summary>Minimum number of knots required for a rope.</summary>
    private const int MinRequiredKnots = 2;

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>
    /// Returns the number of distinct positions visited by a tail of a rope with a specified number
    /// of knots using a given sequence of motions.
    /// </summary>
    /// <param name="motions">Sequence of motions to simulate.</param>
    /// <param name="numberOfKnots">
    /// Positive number of knots in the rope, which must be at least <see cref="MinRequiredKnots"/>.
    /// </param>
    /// <returns>
    /// The number of distinct positions visited by the tail of the rope with the specified number
    /// of knots.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="numberOfKnots"/> is less than <see cref="MinRequiredKnots"/>.
    /// </exception>
    private static int DistinctPositions(ReadOnlySpan<Motion> motions, int numberOfKnots) {
        Guard.IsGreaterThanOrEqualTo(numberOfKnots, MinRequiredKnots);
        Span<Position> knots = stackalloc Position[numberOfKnots];
        HashSet<Position> visited = [knots[^1]];
        foreach (Motion motion in motions) {
            for (int i = 0; i < motion.Amount; i++) {
                knots[0] = motion.Direction switch {
                    Direction.Left => knots[0] with { X = knots[0].X - 1 },
                    Direction.Up => knots[0] with { Y = knots[0].Y - 1 },
                    Direction.Right => knots[0] with { X = knots[0].X + 1 },
                    Direction.Down => knots[0] with { Y = knots[0].Y + 1 },
                    _ => throw new InvalidOperationException("Unreachable.")
                };
                for (int j = 1; j < knots.Length; j++) {
                    Position previous = knots[j - 1];
                    Position current = knots[j];
                    if ((Math.Abs(previous.X - current.X) > 1)
                            || (Math.Abs(previous.Y - current.Y) > 1)) {
                        knots[j] = current with {
                            X = current.X + Math.Sign(previous.X - current.X),
                            Y = current.Y + Math.Sign(previous.Y - current.Y)
                        };
                    }
                }
                visited.Add(knots[^1]);
            }
        }
        return visited.Count;
    }

    /// <summary>Solves the <see cref="RopeBridge"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ReadOnlySpan<Motion> motions = [
            .. File.ReadLines(InputFile).Select(line => Motion.Parse(line))
        ];
        int distinct2 = DistinctPositions(motions, 2);
        int distinct10 = DistinctPositions(motions, 10);
        textWriter.WriteLine(
            $"Using a rope of two knots, {distinct2} positions are visited at least once."
        );
        textWriter.WriteLine(
            $"Using a rope of ten knots, {distinct10} positions are visited at least once."
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