using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace PyroclasticFlow;

internal sealed class PyroclasticFlow {

    /// <summary>Represents a <see cref="Jet"/> of hot gas that pushes rocks around.</summary>
    private enum Jet { Left, Right }

    /// <summary>Represents an enumeration of all possible shapes of a rock.</summary>
    private enum Shape { HorizontalLine, Plus, MirroredL, VerticalLine, Square }

    /// <summary>Represents a two-dimensional <see cref="Position"/>.</summary>
    /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
    /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
    private readonly record struct Position(int X, int Y);

    /// <summary>
    /// Represents a <see cref="State"/> visited while simulating rocks falling down a tall,
    /// narrow chamber.
    /// </summary>
    /// <param name="SettledRocks">Number of settled rocks at the time of recording.</param>
    /// <param name="Height">Height of the tower of settled rocks at the time of recording.</param>
    private readonly record struct State(int SettledRocks, int Height);

    /// <summary>Minimum x-coordinate a rock may have.</summary>
    private const int MinX = 0;

    /// <summary>Maximum x-coordinate a rock may have.</summary>
    private const int MaxX = 6;

    /// <summary>X-coordinate a rock starts with (from the left edge of the chamber).</summary>
    private const int StartX = 2;

    /// <summary>Minimum y-coordinate a rock may have.</summary>
    private const int MinY = 0;

    /// <summary>Y-coordinate a rock starts with (above the ground or other rocks).</summary>
    private const int StartY = 3;

    /// <summary>First checkpoint for checking the height of the tower of settled rocks.</summary>
    private const int FirstCheckpoint = 2022;

    /// <summary>Second checkpoint for checking the height of the tower of settled rocks.</summary>
    private const long SecondCheckpoint = 1_000_000_000_000L;

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Parses a <see cref="Jet"/> from a given character.</summary>
    /// <remarks>
    /// The character <paramref name="c"/> must be either '&lt;' (<see cref="Jet.Left"/>) or '&gt;'
    /// (<see cref="Jet.Right"/>).
    /// </remarks>
    /// <param name="c">Character to parse a <see cref="Jet"/> from.</param>
    /// <returns>A <see cref="Jet"/> parsed from the given character.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="c"/> does not represent a valid <see cref="Jet"/>.
    /// </exception>
    private static Jet ParseJet(char c) => c switch {
        '<' => Jet.Left,
        '>' => Jet.Right,
        _ => throw new ArgumentOutOfRangeException(
            nameof(c),
            $"The character '{c}' does not represent a valid jet."
        )
    };

    /// <summary>
    /// Returns all positions covered by a <see cref="Shape"/> at a given <see cref="Position"/>.
    /// </summary>
    /// <returns>
    /// All positions covered by a <see cref="Shape"/> at a given <see cref="Position"/>.
    /// </returns>
    private static IEnumerable<Position> CoveredPositions(Shape shape, Position position)
        => shape switch {
            Shape.HorizontalLine => [
                position,
                position with { X = position.X + 1 },
                position with { X = position.X + 2 },
                position with { X = position.X + 3 },
            ],
            Shape.Plus => [
                position with { X = position.X + 1 },
                position with { Y = position.Y + 1 },
                position with { X = position.X + 1, Y = position.Y + 1 },
                position with { X = position.X + 2, Y = position.Y + 1 },
                position with { X = position.X + 1, Y = position.Y + 2 }
            ],
            Shape.MirroredL => [
                position,
                position with { X = position.X + 1 },
                position with { X = position.X + 2 },
                position with { X = position.X + 2, Y = position.Y + 1 },
                position with { X = position.X + 2, Y = position.Y + 2 }
            ],
            Shape.VerticalLine => [
                position,
                position with { Y = position.Y + 1 },
                position with { Y = position.Y + 2 },
                position with { Y = position.Y + 3 }
            ],
            Shape.Square => [
                position,
                position with { X = position.X + 1 },
                position with { Y = position.Y + 1 },
                position with { X = position.X + 1, Y = position.Y + 1 }
            ],
            _ => throw new InvalidOperationException("Unreachable."),
        };

    /// <summary>Simulates rocks falling down a tall, narrow chamber.</summary>
    /// <param name="jets">Sequence of one or more jets pushing the rocks around.</param>
    /// <returns>
    /// A tuple containing the height of the tower of settled rocks at two checkpoints (i. e. after
    /// <see cref="FirstCheckpoint"/> and <see cref="SecondCheckpoint"/> rocks stopped falling).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="jets"/> has a length of zero.
    /// </exception>
    private static (int FirstHeight, long SecondHeight) SimulateFallingRocks(
        ReadOnlySpan<Jet> jets
    ) {
        if (jets.Length == 0) {
            throw new ArgumentOutOfRangeException(
                nameof(jets),
                "There must be at least one jet to push rocks around."
            );
        }
        int jetIndex = 0;
        int settledRocks = 0;
        int maxY = 0;
        Shape shape = Shape.HorizontalLine;
        Position position = new(StartX, StartY);
        int? firstHeight = null;
        long? secondHeight = null;
        HashSet<Position> settledRockPositions = [];
        Dictionary<(int JetIndex, int ShapeIndex), State> visited = [];
        while ((firstHeight == null) || (secondHeight == null)) {
            Position newPosition = position with {
                X = position.X + ((jets[jetIndex] == Jet.Left) ? -1 : 1)
            };
            jetIndex = (jetIndex + 1) % jets.Length;
            // Rocks may crash into the walls on both edges of the narrow chamber, as well as into
            // other already settled rocks. We therefore need to check if the rock can be pushed
            // around horizontally by the jet at all.
            if (!CoveredPositions(shape, newPosition).Any(position => (position.X < MinX)
                    || (position.X > MaxX) || settledRockPositions.Contains(position))) {
                position = newPosition;
            }
            newPosition = position with { Y = position.Y - 1 };
            // Rocks only fall down as long as they don't hit the ground of the chamber or other
            // already settled rocks.
            if (!CoveredPositions(shape, newPosition).Any(position => (position.Y < MinY)
                    || settledRockPositions.Contains(position))) {
                position = newPosition;
                continue;
            }
            foreach (Position coveredPosition in CoveredPositions(shape, position)) {
                settledRockPositions.Add(coveredPosition);
                maxY = Math.Max(maxY, coveredPosition.Y);
            }
            int height = maxY + 1;
            settledRocks++;
            if (settledRocks == FirstCheckpoint) {
                firstHeight = height;
            }
            int shapeIndex = (int) shape;
            shape = (Shape) ((shapeIndex + 1) % ((int) Shape.Square + 1));
            position = position with { X = StartX, Y = height + StartY };
            // Simulating a trillion rocks falling down the chamber simply isn't possible within a
            // reasonable time frame. We can however take advantage of the fact that the rocks are
            // pushed around and fall down in a predictable pattern. This allows us to effectively
            // detect a cycle in the simulation and skip the remaining rocks after that point.
            if (secondHeight == null
                    && visited.TryGetValue((jetIndex, shapeIndex), out State state)) {
                int settledRocksDifference = settledRocks - state.SettledRocks;
                long remainingFallingRocks = SecondCheckpoint - settledRocks - 1L;
                long remainingCycles = (remainingFallingRocks / settledRocksDifference) + 1L;
                if (settledRocks + (remainingCycles * settledRocksDifference) == SecondCheckpoint) {
                    secondHeight = height + (remainingCycles * (height - state.Height));
                }
            }
            else {
                visited[(jetIndex, shapeIndex)] = new State(settledRocks, height);
            }
        }
        return (firstHeight.Value, secondHeight.Value);
    }

    /// <summary>Solves the <see cref="PyroclasticFlow"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ReadOnlySpan<Jet> jets = [.. File.ReadAllText(InputFile).Select(ParseJet)];
        (int firstHeight, long secondHeight) = SimulateFallingRocks(jets);
        textWriter.WriteLine(
            $"After {FirstCheckpoint} rocks have stopped falling, the tower is {firstHeight} "
                + "units tall."
        );
        textWriter.WriteLine(
            $"After {SecondCheckpoint} rocks have stopped falling, the tower is {secondHeight} "
                + "units tall."
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