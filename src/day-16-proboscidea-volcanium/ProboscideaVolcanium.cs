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

namespace ProboscideaVolcanium;

internal sealed partial class ProboscideaVolcanium {

    /// <summary>Represents a <see cref="Valve"/> for releasing pressure inside a volcano.</summary>
    private sealed class Valve {

        /// <summary>Determines if this <see cref="Valve"/> is the source.</summary>
        public bool IsSource { get; init; }

        /// <summary>Gets the flow rate of this <see cref="Valve"/>.</summary>
        public int FlowRate { get; init; }

        /// <summary>
        /// Gets the mask of this <see cref="Valve"/>, which is used for checking if it's already
        /// been opened.
        /// </summary>
        /// <remarks>
        /// The mask is a power of two for valves with a <see cref="FlowRate"/> greater than zero
        /// and zero otherwise (as there is no point in trying to open jammed valves that release no
        /// pressure).
        /// </remarks>
        public uint Mask { get; init; }

        /// <summary>Initializes a new <see cref="Valve"/>.</summary>
        /// <param name="isSource">Whether the <see cref="Valve"/> is the source.</param>
        /// <param name="flowRate">Positive flow rate of the <see cref="Valve"/>.</param>
        /// <param name="mask">
        /// Mask of the <see cref="Valve"/>, which is used for checking if it's already been opened.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown in the following circumstances:
        /// <list type="bullet">
        ///     <item>The <paramref name="flowRate"/> is negative.</item>
        ///     <item>
        ///     <paramref name="isSource"/> is <see langword="true"/> and
        ///     the <paramref name="flowRate"/> or <paramref name="mask"/> is not zero.
        ///     </item>
        ///     <item>
        ///     The <paramref name="flowRate"/> is greater than zero, but the
        ///     <paramref name="mask"/> is not a power of two.
        ///     </item>
        ///     <item>
        ///     The <paramref name="flowRate"/> is zero, but the <paramref name="mask"/> is not.
        ///     </item>
        /// </list>
        /// </exception>
        public Valve(bool isSource, int flowRate, uint mask) {
            Guard.IsGreaterThanOrEqualTo(flowRate, 0);
            if (isSource) {
                if (flowRate != 0) {
                    throw new ArgumentOutOfRangeException(
                        nameof(flowRate),
                        $"The source valve must have a flow rate ({flowRate}) of zero."
                    );
                }
                if (mask != 0U) {
                    throw new ArgumentOutOfRangeException(
                        nameof(mask),
                        $"The source valve must have a mask ({mask}) of zero."
                    );
                }
            }
            else if (((flowRate > 0) && !uint.IsPow2(mask)) || ((flowRate == 0) && (mask != 0))) {
                throw new ArgumentOutOfRangeException(
                    nameof(mask),
                    $"The mask ({mask}) must be a power of two if the flow rate is greater "
                        + "than zero or zero otherwise."
                );
            }
            IsSource = isSource;
            FlowRate = flowRate;
            Mask = mask;
        }

        /// <summary>Returns a string representation of this <see cref="Valve"/>.</summary>
        /// <returns>A string representation of this <see cref="Valve"/>.</returns>
        public override string ToString()
            => $"Valve {{ IsSource = {IsSource}, FlowRate = {FlowRate}, Mask = {Mask} }}";

    }

    /// <summary>
    /// Represents a <see cref="Destination"/> consisting of a
    /// <see cref="ProboscideaVolcanium.Valve"/> and an associated distance to reach it.
    /// </summary>
    /// <param name="Valve">
    /// <see cref="ProboscideaVolcanium.Valve"/> at the <see cref="Destination"/>.
    /// </param>
    /// <param name="Distance">
    /// Distance to reach the <see cref="ProboscideaVolcanium.Valve"/> at the
    /// <see cref="Destination"/>.
    /// </param>
    private readonly record struct Destination(Valve Valve, int Distance);

    /// <summary>
    /// Represents a <see cref="State"/> visited while releasing pressure inside a volcano.
    /// </summary>
    /// <param name="Valve">
    /// Current <see cref="ProboscideaVolcanium.Valve"/> at the time of recording.
    /// </param>
    /// <param name="Minutes">
    /// Positive number of minutes remaining for opening valves at the time of recording.
    /// </param>
    /// <param name="Path">Path of already opened valves at the time of recording.</param>
    private readonly record struct State(Valve Valve, int Minutes, uint Path);

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    [GeneratedRegex(
        "^Valve (?<name>[A-Z]{2}) has flow rate=(?<flowRate>\\d+); tunnels? leads? to valves? "
            + "(?<neighbors>[A-Z]{2}(?:, [A-Z]{2})*)$"
    )]
    private static partial Regex ValveRegex();

    /// <summary>Parses all valves from a given sequence of lines.</summary>
    /// <remarks>
    /// Each line in <paramref name="lines"/> must have one of the following two formats,
    /// depending on whether a <see cref="Valve"/> has one or more neighbors (lines shown are only
    /// examples):
    /// <example>
    /// <code>
    /// "Valve AA has flow rate=0; tunnels lead to valves DD, II, BB"
    /// "Valve HH has flow rate=22; tunnel leads to valve GG"
    /// </code>
    /// </example>
    /// The valve's name must consist of exactly two uppercase letters, just like the name(s) of the
    /// neighbor(s). The special <see cref="Valve"/> "AA" is considered the source, which must have
    /// a flow rate of zero. All other valves may have arbitrary, positive flow rates.
    /// </remarks>
    /// <param name="lines">Sequence of lines to parse the valves from.</param>
    /// <returns>A <see cref="FrozenDictionary"/> of valves to their neighbors.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="lines"/> contains a line with an invalid format.
    /// </exception>
    private static FrozenDictionary<Valve, ImmutableArray<Valve>> ParseValves(
        ReadOnlySpan<string> lines
    ) {
        Dictionary<string, Valve> valvesByName = [];
        // Neighbors are temporarily stored using their names, as we would have to create "dummy"
        // valves otherwise (for which we don't know the flow rate yet). To break this cycle,
        // we do the initialization and linking of the graph in two separate steps.
        Dictionary<Valve, ImmutableArray<string>> neighborsByValve = [];
        uint mask = 1U;
        foreach (string line in lines) {
            Match match = ValveRegex().Match(line);
            if (!match.Success) {
                throw new ArgumentOutOfRangeException(
                    nameof(lines),
                    $"The line \"{line}\" has an invalid format."
                );
            }
            string name = match.Groups["name"].Value;
            int flowRate = int.Parse(
                match.Groups["flowRate"].ValueSpan,
                CultureInfo.InvariantCulture
            );
            bool isSource = name == "AA";
            Valve valve = new(isSource, flowRate, (flowRate > 0) ? mask : 0);
            if (flowRate > 0) {
                mask <<= 1;
            }
            valvesByName[name] = valve;
            neighborsByValve[valve] = [.. match.Groups["neighbors"].Value.Split(", ")];
        }
        Dictionary<Valve, ImmutableArray<Valve>> graph = [];
        foreach ((Valve valve, ImmutableArray<string> neighbors) in neighborsByValve) {
            graph[valve] = [.. neighbors.Select(neighbor => valvesByName[neighbor])];
        }
        return graph.ToFrozenDictionary();
    }

    /// <summary>Computes useful destinations for valves in a given graph.</summary>
    /// <remarks>
    /// Note that the resulting dictionary only contains two types of valves as keys:
    /// <list type="bullet">
    ///     <item>
    ///     The source <see cref="Valve"/> "AA", which is always required as the starting point.
    ///     </item>
    ///     <item>Useful valves with a flow rate greater than zero.</item>
    /// </list>
    /// Similarly, it only maps to destination valves to which traveling actually makes sense (i. e.
    /// flow rate greater than zero). In addition, it does not contain distances of valves to
    /// themselves (which are always zero by definition).
    /// </remarks>
    /// <param name="graph">Graph of valves and their neighbors for computing destinations.</param>
    /// <returns>
    /// A <see cref="FrozenDictionary"/> of valves to useful destination valves and the
    /// corresponding distances.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="graph"/> is <see langword="null"/>.
    /// </exception>
    private static FrozenDictionary<Valve, ImmutableArray<Destination>> Destinations(
        FrozenDictionary<Valve, ImmutableArray<Valve>> graph
    ) {
        Guard.IsNotNull(graph);
        Dictionary<Valve, ImmutableArray<Destination>> destinationsByValve = [];
        foreach (Valve valve in graph.Keys) {
            // As mentioned above, only the source valve (required as the starting point) and other
            // useful valves with a flow rate greater than zero are considered valid keys. Other
            // blocked valves with a flow rate of zero wouldn't hurt us technically, but as we do
            // not travel to them anyway, they can just be ignored (which results in a slightly
            // reduced memory footprint).
            if (valve.IsSource || (valve.FlowRate > 0)) {
                ImmutableArray<Destination>.Builder destinations =
                    ImmutableArray.CreateBuilder<Destination>();
                HashSet<Valve> visited = [valve];
                Queue<(Valve Valve, int Distance)> queue = [];
                queue.Enqueue((valve, 0));
                while (queue.Count > 0) {
                    (Valve current, int distance) = queue.Dequeue();
                    foreach (Valve neighbor in graph[current]) {
                        if (visited.Add(neighbor)) {
                            queue.Enqueue((neighbor, distance + 1));
                            // We only care about traveling to valves that may actually help us
                            // in the process of releasing pressure. This is only the case for
                            // valves which are not blocked (flow rate greater than zero).
                            if (neighbor.FlowRate > 0) {
                                destinations.Add(new Destination(neighbor, distance + 1));
                            }
                        }
                    }
                }
                // Move to avoid an additional, unnecessary copy of the array.
                destinations.Capacity = destinations.Count;
                destinationsByValve[valve] = destinations.MoveToImmutable();
            }
        }
        return destinationsByValve.ToFrozenDictionary();
    }

    /// <summary>Returns the maximum pressure released by opening valves inside a volcano.</summary>
    /// <param name="state">Current <see cref="State"/> to continue the process at.</param>
    /// <param name="destinations">Dictionary of valves to useful destinations.</param>
    /// <param name="visitedStates">Cache of already visited states.</param>
    /// <returns>The maximum pressure released by opening valves inside a volcano.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="valve"/>, <paramref name="destinations"/> or
    /// <paramref name="visitedStates"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="minutes"/> is negative.
    /// </exception>
    private static int MaxReleasedPressure(
        State state,
        FrozenDictionary<Valve, ImmutableArray<Destination>> destinations,
        Dictionary<State, int> visitedStates
    ) {
        Guard.IsNotNull(destinations);
        Guard.IsNotNull(visitedStates);
        // A lot of the possible states in the search space would be calculated redundantly using
        // this method. We try to avoid that and catch already visited states early.
        if (visitedStates.TryGetValue(state, out int maxReleasedPressure)) {
            return maxReleasedPressure;
        }
        foreach ((Valve destination, int distance) in destinations[state.Valve]) {
            int remainingMinutes = state.Minutes - distance - 1;
            // If we haven't got enough time left to even reach the destination and open its valve
            // or if we have already opened the valve, there is no point in traveling to that
            // destination again.
            if ((remainingMinutes <= 0) || ((state.Path & destination.Mask) != 0U)) {
                continue;
            }
            int releasedPressure = destination.FlowRate * remainingMinutes;
            // Another small, but significant optimization can be made if we have at most two
            // minutes of time remaining. Within these two minutes, we could travel to a destination
            // that is at most one step away and open its valve. However, this takes another minute
            // (leaving us with no remaining time at all), in which case we have effectively visited
            // the valve for nothing. It is therefore not worth traveling to other valves if the
            // remaining time is less than or equal to two minutes.
            if (remainingMinutes <= 2) {
                maxReleasedPressure = Math.Max(maxReleasedPressure, releasedPressure);
                continue;
            }
            maxReleasedPressure = Math.Max(
                maxReleasedPressure,
                releasedPressure + MaxReleasedPressure(
                    state with {
                        Valve = destination,
                        Minutes = remainingMinutes,
                        Path = state.Path | destination.Mask
                    },
                    destinations,
                    visitedStates
                )
            );
        }
        visitedStates[state] = maxReleasedPressure;
        return maxReleasedPressure;
    }

    /// <summary>
    /// Returns the maximum pressure released by opening valves inside a volcano without any help.
    /// </summary>
    /// <param name="source">Source <see cref="Valve"/> to start at.</param>
    /// <param name="destinations">Dictionary of valves to useful destinations.</param>
    /// <returns>
    /// The maximum pressure released by opening valves inside a volcano without any help.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="destinations"/> or is
    /// <see langword="null"/>.
    /// </exception>
    private static int MaxReleasedPressureWithoutHelp(
        Valve source,
        FrozenDictionary<Valve, ImmutableArray<Destination>> destinations
    ) {
        Guard.IsNotNull(source);
        Guard.IsNotNull(destinations);
        return MaxReleasedPressure(new State(source, 30, 0U), destinations, []);
    }

    /// <summary>
    /// Returns the maximum pressure released by opening valves inside a volcano with the help
    /// of a single elephant.
    /// </summary>
    /// <param name="source">Source <see cref="Valve"/> to start at.</param>
    /// <param name="destinations">Dictionary of valves to useful destinations.</param>
    /// <returns>
    /// The maximum pressure released by opening valves inside a volcano with the help of a single
    /// elephant.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="destinations"/> or is
    /// <see langword="null"/>.
    /// </exception>
    private static int MaxReleasedPressureWithHelp(
        Valve source,
        FrozenDictionary<Valve, ImmutableArray<Destination>> destinations
    ) {
        Guard.IsNotNull(source);
        Guard.IsNotNull(destinations);
        int maxReleasedPressureWithHelp = 0;
        Dictionary<State, int> visitedStates = [];
        // The bitmask for the completed path is constructed from all useful valves (excluding the
        // source valve, which is always jammed; therefore only (destination.Count - 1)).
        // By shifting the one bit left by the number of useful valves and subtracting one,
        // we generate a bitmask with exactly that many one bits, padded with leading zeros (i. e.
        // given eight useful valves, the bitmask 0000...000011111111 is generated).
        uint completedPath = (1U << (destinations.Count - 1)) - 1U;
        State state = new(source, 26, 0U);
        // We only need to check the first half of all paths, as the elephant always visits the
        // other half (see below for a more detailed explanation). In the second half, the order
        // would just be reversed, so we would not gain any useful information.
        for (uint path = 0; path < (completedPath / 2) + 1; path++) {
            state = state with { Path = path };
            // It only makes sense to calculate the maximum released pressure for paths where
            // our path does not overlap with the elephant's path (as valves can only be opened once
            // and therefore there is no point in visiting them twice). This can be avoided rather
            // easily by making use of the bitwise XOR operator, which computes the opposite bitmask
            // for the elephant given our own path. This effectively causes the elephant to visit
            // all remaining unopened valves we do not open ourselves. For example, given eight
            // useful valves:
            //   0000...000011111111 (completed path)
            // ^ 0000...000011010010 (our path)
            // ---------------------
            // = 0000...000000101101 (elephant's path)
            State elephantState = state with { Path = completedPath ^ path };
            maxReleasedPressureWithHelp = Math.Max(
                maxReleasedPressureWithHelp,
                MaxReleasedPressure(state, destinations, visitedStates)
                    + MaxReleasedPressure(elephantState, destinations, visitedStates)
            );
        }
        return maxReleasedPressureWithHelp;
    }

    /// <summary>Solves the <see cref="ProboscideaVolcanium"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        FrozenDictionary<Valve, ImmutableArray<Destination>> destinations = Destinations(
            ParseValves([.. File.ReadLines(InputFile)])
        );
        Valve source = destinations.Keys.Single(valve => valve.IsSource);
        int maxReleaseWithoutHelp = MaxReleasedPressureWithoutHelp(source, destinations);
        int maxReleaseWithHelp = MaxReleasedPressureWithHelp(source, destinations);
        textWriter.WriteLine(
            $"Without any help, the maximum pressure released is {maxReleaseWithoutHelp}."
        );
        textWriter.WriteLine(
            $"With the help of one elephant, the maximum pressure released is {maxReleaseWithHelp}."
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