using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace NotEnoughMinerals;

internal sealed partial class NotEnoughMinerals {

    /// <summary>
    /// Represents a <see cref="Blueprint"/> for building mineral harvesting robots.
    /// </summary>
    private sealed partial class Blueprint {

        /// <summary>Represents an enumeration of all existing minerals.</summary>
        private enum Mineral { Ore, Clay, Obsidian, Geode }

        /// <summary>Represents an <see cref="Inventory"/> of minerals.</summary>
        /// <remarks>
        /// An <see cref="Inventory"/> is an inline array that directly stores the quantities of
        /// the four different types of minerals. Note that it is a mutable value type and only
        /// allows mineral quantities of up to <see cref="byte.MaxValue"/> to be stored.
        /// </remarks>
        [InlineArray(4)]
        private struct Inventory : IEquatable<Inventory> {

            [SuppressMessage(
                "Style",
                "IDE0044:Add readonly modifier",
                Justification = "Field of an inline array cannot be readonly."
            )]
            [SuppressMessage(
                "CodeQuality",
                "IDE0051:Remove unused private members",
                Justification = "Field of an inline array might be unused."
            )]
            private byte firstElement;

            /// <summary>
            /// Initializes a new <see cref="Inventory"/> with all mineral quantities initially set
            /// to zero.
            /// </summary>
            public Inventory() { }

            /// <summary>
            /// Initializes a new <see cref="Inventory"/> with a given set of mineral quantities.
            /// </summary>
            /// <param name="ore">
            /// Amount of <see cref="Mineral.Ore"/> in the <see cref="Inventory"/>.
            /// </param>
            /// <param name="clay">
            /// Amount of <see cref="Mineral.Clay"/> in the <see cref="Inventory"/>.
            /// </param>
            /// <param name="obsidian">
            /// Amount of <see cref="Mineral.Obsidian"/> in the <see cref="Inventory"/>.
            /// </param>
            /// <param name="geode">
            /// Amount of <see cref="Mineral.Geode"/> in the <see cref="Inventory"/>.
            /// </param>
            public Inventory(byte ore, byte clay, byte obsidian, byte geode) {
                this[Mineral.Ore] = ore;
                this[Mineral.Clay] = clay;
                this[Mineral.Obsidian] = obsidian;
                this[Mineral.Geode] = geode;
            }

            /// <summary>Gets or sets the quantity of a given <see cref="Mineral"/>.</summary>
            /// <param name="mineral"><see cref="Mineral"/> to get or set the quantity of.</param>
            /// <returns>The quantity of the given <see cref="Mineral"/>.</returns>
            public byte this[Mineral mineral] {
                readonly get => this[(int) mineral];
                set => this[(int) mineral] = value;
            }

            /// <summary>Determines if two inventories are equal.</summary>
            /// <param name="first">First <see cref="Inventory"/> for the comparison.</param>
            /// <param name="second">Second <see cref="Inventory"/> for the comparison.</param>
            /// <returns>
            /// <see langword="True"/> if the two inventories are equal,
            /// otherwise <see langword="false"/>.
            /// </returns>
            public static bool operator ==(Inventory first, Inventory second)
                => first.Equals(second);

            /// <summary>Determines if two inventories are not equal.</summary>
            /// <param name="first">First <see cref="Inventory"/> for the comparison.</param>
            /// <param name="second">Second <see cref="Inventory"/> for the comparison.</param>
            /// <returns>
            /// <see langword="True"/> if the two inventories are not equal,
            /// otherwise <see langword="false"/>.
            /// </returns>
            public static bool operator !=(Inventory first, Inventory second)
                => !first.Equals(second);

            /// <summary>
            /// Determines if this <see cref="Inventory"/> is equal to a given one.
            /// </summary>
            /// <param name="other">
            /// Other <see cref="Inventory"/> to compare this <see cref="Inventory"/> to.
            /// </param>
            /// <returns>
            /// <see langword="True"/> if this <see cref="Inventory"/> is equal to the given one,
            /// otherwise <see langword="false"/>.
            /// </returns>
            public readonly bool Equals(Inventory other)
                => (this[Mineral.Ore] == other[Mineral.Ore])
                    && (this[Mineral.Clay] == other[Mineral.Clay])
                    && (this[Mineral.Obsidian] == other[Mineral.Obsidian])
                    && (this[Mineral.Geode] == other[Mineral.Geode]);

            /// <summary>
            /// Determines if this <see cref="Inventory"/> is equal to a given object.
            /// </summary>
            /// <param name="obj">Object to compare this <see cref="Inventory"/> to.</param>
            /// <returns>
            /// <see langword="True"/> if the given object is an <see cref="Inventory"/> and equal
            /// to this one, otherwise <see langword="false"/>.
            /// </returns>
            public override readonly bool Equals([NotNullWhen(true)] object? obj)
                => (obj is Inventory other) && Equals(other);

            /// <summary>Returns a hash code for this <see cref="Inventory"/>.</summary>
            /// <returns>A hash code for this <see cref="Inventory"/>.</returns>
            public override readonly int GetHashCode()
                => HashCode.Combine(
                    this[Mineral.Ore],
                    this[Mineral.Clay],
                    this[Mineral.Obsidian],
                    this[Mineral.Geode]
                );

            /// <summary>Returns a string representation of this <see cref="Inventory"/>.</summary>
            /// <returns>A string representation of this <see cref="Inventory"/>.</returns>
            public override readonly string ToString()
                => $"Inventory {{ Ore = {this[Mineral.Ore]}, Clay = {this[Mineral.Clay]}, "
                    + $"Obsidian = {this[Mineral.Obsidian]}, Geode = {this[Mineral.Geode]} }}";

        }

        /// <summary>
        /// Represents a <see cref="State"/> visited while determining the maximum number of geodes
        /// that can be opened using a <see cref="Blueprint"/>.
        /// </summary>
        /// <param name="Minutes">Remaining minutes at the time of recording.</param>
        /// <param name="Minerals">
        /// <see cref="Inventory"/> of minerals at the time of recording.
        /// </param>
        /// <param name="Robots">
        /// <see cref="Inventory"/> of mineral harvesting robots at the time of recording.
        /// </param>
        private readonly record struct State(int Minutes, Inventory Minerals, Inventory Robots);

        /// <summary>Array of all minerals, cached for efficiency.</summary>
        private static readonly ImmutableArray<Mineral> Minerals = [.. Enum.GetValues<Mineral>()];

        /// <summary>Gets the id of this <see cref="Blueprint"/>.</summary>
        public int Id { get; init; }

        /// <summary>
        /// Costs for building mineral harvesting robots for each <see cref="Mineral"/>.
        /// </summary>
        private readonly ImmutableArray<Inventory> costs;

        /// <summary>
        /// Maximum useful number of mineral harvesting robots for each <see cref="Mineral"/>.
        /// </summary>
        private readonly Inventory maxRobots;

        /// <summary>Initializes a new <see cref="Blueprint"/>.</summary>
        /// <param name="id">Positive id of the <see cref="Blueprint"/>.</param>
        /// <param name="costs">
        /// Sequence of costs for building mineral harvesting robots, which must have exactly as
        /// many elements as the number of different minerals. The first element is treated as the
        /// costs for building a <see cref="Mineral.Ore"/> harvesting robot, the second for
        /// <see cref="Mineral.Clay"/> and so on.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="id"/> is negative or <paramref name="costs"/> does not have
        /// exactly as many elements as the number of different minerals.
        /// </exception>
        private Blueprint(int id, ReadOnlySpan<Inventory> costs) {
            Guard.IsGreaterThanOrEqualTo(id, 0);
            Guard.IsEqualTo(costs.Length, Minerals.Length);
            Id = id;
            this.costs = [.. costs];
            maxRobots = new Inventory(0, 0, 0, byte.MaxValue);
            foreach (Inventory robotCosts in costs) {
                foreach (Mineral mineral in Minerals) {
                    maxRobots[mineral] = Math.Max(maxRobots[mineral], robotCosts[mineral]);
                }
            }
        }

        [GeneratedRegex(
            "^Blueprint (\\d+): Each ore robot costs (\\d+) ore\\. "
                + "Each clay robot costs (\\d+) ore\\. "
                + "Each obsidian robot costs (\\d+) ore and (\\d+) clay\\. "
                + "Each geode robot costs (\\d+) ore and (\\d+) obsidian\\.$"
        )]
        private static partial Regex BlueprintRegex();

        /// <summary>Parses a <see cref="Blueprint"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must have the following format (everything on one line
        /// and separated by single spaces, only wrapped for readability here):
        /// <example>
        /// <code>
        /// Blueprint &lt;Id&gt;:
        ///   Each ore robot costs &lt;N0&gt; ore.
        ///   Each clay robot costs &lt;N1&gt; ore.
        ///   Each obsidian robot costs &lt;N2&gt; ore and &lt;N3&gt; clay.
        ///   Each geode robot costs &lt;N4&gt; ore and &lt;N5&gt; obsidian.
        /// </code>
        /// </example>
        /// &lt;Id&gt; is a positive integer for the id of the <see cref="Blueprint"/>.
        /// Each placeholder &lt;NX&gt; is a positive integer indicating the cost of a certain
        /// <see cref="Mineral"/> for building a mineral harvesting robot.
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Blueprint"/> from.</param>
        /// <returns>A <see cref="Blueprint"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="s"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Blueprint Parse(string s) {
            Guard.IsNotNull(s);
            Match match = BlueprintRegex().Match(s);
            if (!match.Success) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid blueprint."
                );
            }
            int id = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            ReadOnlySpan<byte> values = [
                .. match.Groups.Values
                    .Skip(2)
                    .Select(group => byte.Parse(group.ValueSpan, CultureInfo.InvariantCulture))
            ];
            ReadOnlySpan<Inventory> costs = [
                new Inventory(values[0], 0, 0, 0),
                new Inventory(values[1], 0, 0, 0),
                new Inventory(values[2], values[3], 0, 0),
                new Inventory(values[4], 0, values[5], 0)
            ];
            return new Blueprint(id, costs);
        }

        /// <summary>
        /// Determines the maximum number of geodes that can be opened within a given number of
        /// minutes using this <see cref="Blueprint"/>.
        /// </summary>
        /// <param name="minutes">Positive number of minutes available for opening geodes.</param>
        /// <returns>
        /// The maximum number of geodes that can be opened within the given number of minutes using
        /// this <see cref="Blueprint"/>.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="minutes"/> is negative.
        /// </exception>
        public int MaxOpenedGeodes(int minutes) {
            Guard.IsGreaterThanOrEqualTo(minutes, 0);
            State start = new(minutes, new Inventory(), new Inventory(1, 0, 0, 0));
            Queue<State> queue = new([start]);
            HashSet<State> visited = [];
            int maxOpenedGeodes = 0;
            while (queue.Count > 0) {
                State state = queue.Dequeue();
                int minOpenedGeodes = state.Minerals[Mineral.Geode]
                    + (state.Minutes * state.Robots[Mineral.Geode]);
                int optimisticMaxOpenedGeodes = minOpenedGeodes
                    + (state.Minutes * (state.Minutes + 1) / 2);
                if ((optimisticMaxOpenedGeodes < maxOpenedGeodes) || !visited.Add(state)) {
                    continue;
                }
                maxOpenedGeodes = Math.Max(maxOpenedGeodes, minOpenedGeodes);
                foreach (Mineral robot in Minerals) {
                    if (state.Robots[robot] >= maxRobots[robot]) {
                        continue;
                    }
                    int waitingMinutes = 0;
                    bool canBuildRobot = true;
                    foreach (Mineral mineral in Minerals) {
                        int cost = costs[(int) robot][mineral];
                        if (cost > 0) {
                            if (state.Robots[mineral] == 0) {
                                canBuildRobot = false;
                                break;
                            }
                            waitingMinutes = Math.Max(
                                waitingMinutes,
                                (int) Math.Ceiling(
                                    ((double) (cost - state.Minerals[mineral]))
                                        / state.Robots[mineral]
                                )
                            );
                        }
                    }
                    if (!canBuildRobot) {
                        continue;
                    }
                    int remainingMinutes = state.Minutes - waitingMinutes - 1;
                    if (remainingMinutes <= 0) {
                        continue;
                    }
                    Inventory newMinerals = state.Minerals;
                    foreach (Mineral mineral in Minerals) {
                        newMinerals[mineral] = (byte) (
                            newMinerals[mineral] + ((waitingMinutes + 1) * state.Robots[mineral])
                                - costs[(int) robot][mineral]
                        );
                        if (mineral != Mineral.Geode) {
                            newMinerals[mineral] = (byte) Math.Min(
                                newMinerals[mineral],
                                remainingMinutes * maxRobots[mineral]
                            );
                        }
                    }
                    Inventory newRobots = state.Robots;
                    newRobots[robot]++;
                    queue.Enqueue(new State(remainingMinutes, newMinerals, newRobots));
                }
            }
            return maxOpenedGeodes;
        }

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Solves the <see cref="NotEnoughMinerals"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ImmutableArray<Blueprint> blueprints = [
            .. File.ReadLines(InputFile).Select(Blueprint.Parse)
        ];
        int sum = blueprints.Sum(blueprint => blueprint.Id * blueprint.MaxOpenedGeodes(24));
        int product = blueprints
            .Take(3)
            .Aggregate(1, (product, blueprint) => product * blueprint.MaxOpenedGeodes(32));
        textWriter.WriteLine($"The sum of all quality levels is {sum}.");
        textWriter.WriteLine($"The product of the top three maximum geodes is {product}.");
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