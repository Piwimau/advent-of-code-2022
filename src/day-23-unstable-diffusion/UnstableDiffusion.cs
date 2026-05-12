using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace UnstableDiffusion;

internal sealed class UnstableDiffusion {

    /// <summary>Represents an enumeration of all possible directions.</summary>
    private enum Direction { N, NE, E, SE, S, SW, W, NW }

    /// <summary>Represents a two-dimensional <see cref="Position"/>.</summary>
    /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
    /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
    private readonly record struct Position(int X, int Y);

    /// <summary>Round after which the number of empty tiles should be checked.</summary>
    private const int Checkpoint = 10;

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Parses the elves from a given sequence of lines.</summary>
    /// <remarks>
    /// The sequence of <paramref name="lines"/> must form a rectangle containing only empty spots
    /// ('.') and elves ('#'). An example for a valid rectangle might be the following:
    /// <example>
    /// <code>
    /// ....#..
    /// ..###.#
    /// #...#.#
    /// .#...##
    /// #.###..
    /// ##.#.##
    /// .#..#..
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="lines">Sequence of lines to parse the elves from.</param>
    /// <returns>The elves parsed from the given sequence of lines.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="lines"/> contains an invalid character.
    /// </exception>
    private static HashSet<Position> ParseElves(ReadOnlySpan<string> lines) {
        HashSet<Position> elves = [];
        for (int y = 0; y < lines.Length; y++) {
            for (int x = 0; x < lines[0].Length; x++) {
                if (lines[y][x] == '#') {
                    elves.Add(new Position(x, y));
                }
                else if (lines[y][x] != '.') {
                    throw new ArgumentOutOfRangeException(
                        nameof(lines),
                        $"The character '{lines[y][x]}' is invalid."
                    );
                }
            }
        }
        return elves;
    }

    /// <summary>Simulates the movement of the elves.</summary>
    /// <param name="initialElves">Sequence of initial elf positions for the simulation.</param>
    /// <returns>
    /// A tuple containing the number of empty tiles after <see cref="Checkpoint"/> rounds and the
    /// round in which the elves stop moving. Note that the number of empty tiles may be
    /// <see langword="null"/> in case the elves stop before <see cref="Checkpoint"/> rounds could
    /// have been simulated.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="initialElves"/> is <see langword="null"/>.
    /// </exception>
    private static (int? EmptyTiles, int StopRound) SimulateElves(
        IReadOnlySet<Position> initialElves
    ) {
        Guard.IsNotNull(initialElves);
        HashSet<Position> elves = [.. initialElves];
        // The elves check for neighbors in each of the four orthogonal directions to determine
        // whether moving is required during the current round. Note that the check for a direction
        // actually also includes the two adjacent, diagonal directions, i. e. when checking if a
        // neighbor exists to the north, three tiles to the north west, north and north east are
        // considered by the elf. Also note that the order in which the four main directions are
        // checked changes: The direction first checked becomes the last one in the next round.
        Span<Direction> directions = [Direction.N, Direction.S, Direction.W, Direction.E];
        // We use compact and efficient bitmasks for checking the existence of neighbors in any of
        // the four main directions. The bit with index zero corresponds to north, index one to
        // north east and so on. Only the masks for the four main directions are actually required,
        // the other ones are just provided here for the sake of completeness.
        ReadOnlySpan<uint> adjacentDirections = [
            0b10000011U,
            0b00000111U,
            0b00001110U,
            0b00011100U,
            0b00111000U,
            0b01110000U,
            0b11100000U,
            0b11000001U
        ];
        int? emptyTiles = null;
        // During each round, we track which destinations elves propose to move to. This dictionary
        // is cleared after each round and reused to reduce the number of memory allocations.
        Dictionary<Position, List<Position>> proposals = [];
        for (int round = 1; ; round++) {
            foreach (Position elf in elves) {
                ReadOnlySpan<Position> neighbors = [
                    elf with { Y = elf.Y - 1 },
                    elf with { X = elf.X + 1, Y = elf.Y - 1 },
                    elf with { X = elf.X + 1 },
                    elf with { X = elf.X + 1, Y = elf.Y + 1 },
                    elf with { Y = elf.Y + 1 },
                    elf with { X = elf.X - 1, Y = elf.Y + 1 },
                    elf with { X = elf.X - 1 },
                    elf with { X = elf.X - 1, Y = elf.Y - 1 }
                ];
                uint existingNeighbors = 0U;
                for (int i = 0; i < neighbors.Length; i++) {
                    if (elves.Contains(neighbors[i])) {
                        existingNeighbors |= 1U << i;
                    }
                }
                // If no bits are set, we know that this elf has no adjacent neighbors in any of the
                // orthogonal and diagonal directions - it is therefore not required to move.
                if (existingNeighbors == 0U) {
                    continue;
                }
                // Otherwise, we need to find out which of the four main orthogonal directions is
                // free, so that the elf can propose to move there. Note that more than one elf may
                // propose to move to the same destination, which results in a collision that is
                // resolved at a later point in time.
                foreach (Direction direction in directions) {
                    if ((existingNeighbors & adjacentDirections[(int) direction]) == 0U) {
                        Position destination = neighbors[(int) direction];
                        List<Position> elvesWithSameDestination = proposals.GetValueOrDefault(
                            destination,
                            []
                        );
                        elvesWithSameDestination.Add(elf);
                        proposals[destination] = elvesWithSameDestination;
                        break;
                    }
                }
            }
            bool elvesMoved = false;
            foreach ((Position destination, List<Position> elvesWithSameDestination) in proposals) {
                // Moves only succeed if at most one elf proposed to move to a destination, as the
                // elves would be colliding otherwise. In the latter case, all elves with the same
                // destination remain at their original position.
                if (elvesWithSameDestination.Count == 1) {
                    elves.Remove(elvesWithSameDestination[0]);
                    elves.Add(destination);
                    elvesMoved = true;
                }
            }
            if (round == Checkpoint) {
                int minX = int.MaxValue;
                int maxX = int.MinValue;
                int minY = int.MaxValue;
                int maxY = int.MinValue;
                foreach (Position elf in elves) {
                    minX = Math.Min(minX, elf.X);
                    maxX = Math.Max(maxX, elf.X);
                    minY = Math.Min(minY, elf.Y);
                    maxY = Math.Max(maxY, elf.Y);
                }
                emptyTiles = (Math.Abs(maxX - minX + 1) * Math.Abs(maxY - minY + 1)) - elves.Count;
            }
            if (!elvesMoved) {
                return (emptyTiles, round);
            }
            Direction firstDirection = directions[0];
            directions[1..].CopyTo(directions);
            directions[^1] = firstDirection;
            proposals.Clear();
        }
    }

    /// <summary>Solves the <see cref="UnstableDiffusion"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        IReadOnlySet<Position> initialElves = ParseElves([.. File.ReadLines(InputFile)]);
        (int? emptyTiles, int stopRound) = SimulateElves(initialElves);
        textWriter.WriteLine($"After {Checkpoint} rounds, there are {emptyTiles} empty tiles.");
        textWriter.WriteLine($"The first round in which no elf moves is round {stopRound}.");
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