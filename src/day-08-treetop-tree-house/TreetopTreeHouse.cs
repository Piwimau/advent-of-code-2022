using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace TreetopTreeHouse;

internal sealed class TreetopTreeHouse {

    /// <summary>Represents a <see cref="Map"/> of tree heights.</summary>
    private sealed class Map {

        /// <summary>Represents a two-dimensional <see cref="Position"/>.</summary>
        /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
        /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
        private readonly record struct Position(int X, int Y);

        /// <summary>Minimum allowed height of a tree.</summary>
        private const int MinHeight = 0;

        /// <summary>Maximum allowed height of a tree.</summary>
        private const int MaxHeight = 9;

        /// <summary>Array of all possible direction offsets, cached for efficiency.</summary>
        private static readonly ImmutableArray<(int XOffset, int YOffset)> Offsets = [
            (-1, 0), (0, -1), (1, 0), (0, 1)
        ];

        /// <summary>Tree heights of this <see cref="Map"/>.</summary>
        /// <remarks>
        /// Note that this is actually a two-dimensional array stored as a one-dimensional one (in
        /// row-major order) for reasons of improved cache locality and performance.
        /// </remarks>
        private readonly ImmutableArray<int> treeHeights;

        /// <summary>Width of this <see cref="Map"/> of tree heights.</summary>
        private readonly int width;

        /// <summary>Height of this <see cref="Map"/> of tree heights.</summary>
        private readonly int height;

        /// <summary>
        /// Array of the positions of all visible trees, lazily initialized upon first use.
        /// </summary>
        private readonly Lazy<ImmutableArray<Position>> visibleTrees;

        /// <summary>
        /// Initializes a new <see cref="Map"/> with a given array of tree heights.
        /// </summary>
        /// <param name="treeHeights">Array of tree heights for the initialization.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="treeHeights"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="treeHeights"/> contains an invalid tree height (which is not
        /// in the range [<see cref="MinHeight"/>; <see cref="MaxHeight"/>]).
        /// </exception>
        private Map(int[][] treeHeights) {
            Guard.IsNotNull(treeHeights);
            this.treeHeights = [.. treeHeights.SelectMany(row => row)];
            if (this.treeHeights.Any(height => height < MinHeight || height > MaxHeight)) {
                throw new ArgumentOutOfRangeException(
                    nameof(treeHeights),
                    $"All tree heights must be in the range [{MinHeight}; {MaxHeight}]."
                );
            }
            width = treeHeights[0].Length;
            height = treeHeights.Length;
            visibleTrees = new Lazy<ImmutableArray<Position>>(
                VisibleTrees,
                LazyThreadSafetyMode.None
            );
        }

        /// <summary>Parses a <see cref="Map"/> of tree heights from a given string.</summary>
        /// <remarks>
        /// The given string <paramref name="s"/> must consist of zero or more lines of only digits
        /// '0' through '9' representing the tree heights of the <see cref="Map"/>. All lines must
        /// be of the same length. An example might be the following (actual newlines rendered):
        /// <example>
        /// <code>
        /// 30373
        /// 25512
        /// 65332
        /// 33549
        /// 35390
        /// </code>
        /// </example>
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Map"/> from.</param>
        /// <returns>A <see cref="Map"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="s"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> contains an invalid tree height.
        /// </exception>
        public static Map Parse(string s) {
            Guard.IsNotNull(s);
            int[][] treeHeights = [
                .. s.Split(Environment.NewLine).Select(line => line.Select(c => c - '0').ToArray())
            ];
            return new Map(treeHeights);
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

        /// <summary>Returns the index of a tree height at a given <see cref="Position"/>.</summary>
        /// <param name="position">
        /// <see cref="Position"/> of the tree height to get the index of.
        /// </param>
        /// <returns>The index of the tree height at the given <see cref="Position"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Index(Position position) => (position.Y * width) + position.X;

        /// <summary>
        /// Determines the positions of all visible trees in this <see cref="Map"/>.
        /// </summary>
        /// <remarks>
        /// This method is used to lazily initialize the <see cref="visibleTrees"/> field of this
        /// <see cref="Map"/>. It should not be necessary to call it manually.
        /// </remarks>
        /// <returns>
        /// An array containing the positions of all visible trees in this <see cref="Map"/>.
        /// </returns>
        private ImmutableArray<Position> VisibleTrees() {
            ImmutableArray<Position>.Builder visibleTrees =
                ImmutableArray.CreateBuilder<Position>();
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    Position position = new(x, y);
                    int index = Index(position);
                    foreach ((int xOffset, int yOffset) in Offsets) {
                        bool isVisibleFromDirection = true;
                        Position neighbor = position with {
                            X = position.X + xOffset,
                            Y = position.Y + yOffset
                        };
                        while (IsValidPosition(neighbor)) {
                            if (treeHeights[Index(neighbor)] >= treeHeights[index]) {
                                isVisibleFromDirection = false;
                                break;
                            }
                            neighbor = neighbor with {
                                X = neighbor.X + xOffset,
                                Y = neighbor.Y + yOffset
                            };
                        }
                        if (isVisibleFromDirection) {
                            visibleTrees.Add(position);
                            break;
                        }
                    }
                }
            }
            return visibleTrees.DrainToImmutable();
        }

        /// <summary>Returns the total number of visible trees in this <see cref="Map"/>.</summary>
        public int TotalVisibleTrees() => visibleTrees.Value.Length;

        /// <summary>
        /// Returns the maximum scenic score of any of the visible trees in this <see cref="Map"/>.
        /// </summary>
        /// <returns>
        /// The maximum scenic score of any of the visible trees in this <see cref="Map"/>.
        /// </returns>
        public int MaxScenicScore() {
            int maxScenicScore = int.MinValue;
            foreach (Position visibleTree in visibleTrees.Value) {
                int index = Index(visibleTree);
                int scenicScore = 1;
                foreach ((int xOffset, int yOffset) in Offsets) {
                    int score = 0;
                    Position neighbor = visibleTree with {
                        X = visibleTree.X + xOffset,
                        Y = visibleTree.Y + yOffset
                    };
                    while (IsValidPosition(neighbor)) {
                        score++;
                        if (treeHeights[Index(neighbor)] >= treeHeights[index]) {
                            break;
                        }
                        neighbor = neighbor with {
                            X = neighbor.X + xOffset,
                            Y = neighbor.Y + yOffset
                        };
                    }
                    scenicScore *= score;
                }
                maxScenicScore = Math.Max(maxScenicScore, scenicScore);
            }
            return maxScenicScore;
        }

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Solves the <see cref="TreetopTreeHouse"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        Map map = Map.Parse(File.ReadAllText(InputFile));
        int totalVisibleTrees = map.TotalVisibleTrees();
        int maxScenicScore = map.MaxScenicScore();
        textWriter.WriteLine($"{totalVisibleTrees} total trees are visible from the outside.");
        textWriter.WriteLine($"The maximum scenic score of any visible tree is {maxScenicScore}.");
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