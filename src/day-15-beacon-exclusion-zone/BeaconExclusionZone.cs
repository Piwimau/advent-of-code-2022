using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace BeaconExclusionZone;

internal sealed partial class BeaconExclusionZone {

    /// <summary>Represents a two-dimensional <see cref="Position"/>.</summary>
    /// <param name="X">X-coordinate of the <see cref="Position"/>.</param>
    /// <param name="Y">Y-coordinate of the <see cref="Position"/>.</param>
    private readonly record struct Position(int X, int Y) {

        /// <summary>
        /// Returns the manhattan distance of this <see cref="Position"/> to a given one.
        /// </summary>
        /// <param name="other">Other <see cref="Position"/> to calculate the distance to.</param>
        /// <returns>
        /// The manhattan distance of this <see cref="Position"/> to the given one.
        /// </returns>
        public int DistanceTo(Position other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    }

    /// <summary>
    /// Represents a <see cref="Measurement"/> containing the <see cref="Position"/> of a sensor and
    /// a beacon.
    /// </summary>
    /// <param name="Sensor"><see cref="Position"/> of the sensor.</param>
    /// <param name="Beacon"><see cref="Position"/> of the beacon.</param>
    private readonly partial record struct Measurement(Position Sensor, Position Beacon) {

        [GeneratedRegex("^Sensor at x=-?\\d+, y=-?\\d+: closest beacon is at x=-?\\d+, y=-?\\d+$")]
        private static partial Regex MeasurementRegex();

        [GeneratedRegex("-?\\d+")]
        private static partial Regex CoordinateRegex();

        /// <summary>Parses a <see cref="Measurement"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must have the following format, where x1, y1, x2 and y2
        /// represent integers:
        /// <example>
        /// <code>
        /// "Sensor at x=x1, y=y1: closest beacon is at x=x2, y=y2"
        /// </code>
        /// </example>
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Measurement"/> from.</param>
        /// <returns>A <see cref="Measurement"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="s"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Measurement Parse(string s) {
            Guard.IsNotNull(s);
            if (!MeasurementRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid measurement."
                );
            }
            ReadOnlySpan<int> coordinates = [
                .. CoordinateRegex().Matches(s).Select(
                    match => int.Parse(match.Value, CultureInfo.InvariantCulture)
                )
            ];
            Position sensor = new(coordinates[0], coordinates[1]);
            Position beacon = new(coordinates[2], coordinates[3]);
            return new Measurement(sensor, beacon);
        }

    }

    /// <summary>Target row for calculating the number of empty positions.</summary>
    private const int TargetRow = 2_000_000;

    /// <summary>Maximum x- or y-coordinate to be used for the search space.</summary>
    private const int MaxCoordinate = 4_000_000;

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>
    /// Counts the number of positions in a given row that cannot contain a beacon.
    /// </summary>
    /// <param name="measurements">Sequence of measurements for the search.</param>
    /// <param name="row">Positive row to inspect.</param>
    /// <returns>The number of positions in the given row that cannot contain a beacon.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="row"/> is negative.
    /// </exception>
    private static int CountNonBeaconPositions(ReadOnlySpan<Measurement> measurements, int row) {
        Guard.IsGreaterThanOrEqualTo(row, 0);
        HashSet<int> nonBeaconPositions = [];
        foreach ((Position sensor, Position beacon) in measurements) {
            int radius = sensor.DistanceTo(beacon);
            int yDistance = Math.Abs(sensor.Y - row);
            int xDistance = radius - yDistance;
            for (int xOffset = -xDistance; xOffset <= xDistance; xOffset++) {
                nonBeaconPositions.Add(sensor.X + xOffset);
            }
            if (beacon.Y == row) {
                nonBeaconPositions.Remove(beacon.X);
            }
        }
        return nonBeaconPositions.Count;
    }

    /// <summary>Determines if a given <see cref="Position"/> is valid.</summary>
    /// <remarks>
    /// A <see cref="Position"/> is considered valid if both its x- and y-coordinate are within the
    /// range [0; <see cref="MaxCoordinate"/>].
    /// </remarks>
    /// <param name="position"><see cref="Position"/> to check.</param>
    /// <returns>
    /// <see langword="True"/> if the given <see cref="Position"/> is valid,
    /// otherwise <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidPosition(Position position)
        => (position.X >= 0) && (position.X <= MaxCoordinate)
            && (position.Y >= 0) && (position.Y <= MaxCoordinate);

    /// <summary>
    /// Determines if a given <see cref="Position"/> is within the range of any sensor in a given
    /// sequence of measurements.
    /// </summary>
    /// <param name="position"><see cref="Position"/> to check.</param>
    /// <param name="measurements">Sequence of measurements for the check.</param>
    /// <returns>
    /// <see langword="True"/> if the given <see cref="Position"/> is within the range of any
    /// sensor in the given sequence of measurements, otherwise <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWithinRangeOfAnySensor(
        Position position,
        ReadOnlySpan<Measurement> measurements
    ) {
        foreach ((Position sensor, Position beacon) in measurements) {
            if (sensor.DistanceTo(position) <= sensor.DistanceTo(beacon)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>Tries to find the tuning frequency of the distress beacon.</summary>
    /// <param name="measurements">Sequence of measurements for the search.</param>
    /// <param name="tuningFrequency">
    /// Tuning frequency of the distress beacon if it was found (indicated by a return value of
    /// <see langword="true"/>), otherwise the <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="True"/> if the tuning frequency of the distress beacon was found,
    /// otherwise <see langword="false"/>.
    /// </returns>
    private static bool TryFindTuningFrequency(
        ReadOnlySpan<Measurement> measurements,
        out long tuningFrequency
    ) {
        foreach ((Position sensor, Position beacon) in measurements) {
            int radius = sensor.DistanceTo(beacon);
            for (int xOffset = 0; xOffset < (radius + 1); xOffset++) {
                int yOffset = radius + 1 - xOffset;
                ReadOnlySpan<Position> positions = [
                    sensor with { X = sensor.X + xOffset, Y = sensor.Y + yOffset },
                    sensor with { X = sensor.X + xOffset, Y = sensor.Y - yOffset },
                    sensor with { X = sensor.X - xOffset, Y = sensor.Y + yOffset },
                    sensor with { X = sensor.X - xOffset, Y = sensor.Y - yOffset }
                ];
                foreach (Position position in positions) {
                    if (IsValidPosition(position)
                            && !IsWithinRangeOfAnySensor(position, measurements)) {
                        // Cast to long is required to prevent an overflow.
                        tuningFrequency = (((long) position.X) * MaxCoordinate) + position.Y;
                        return true;
                    }
                }
            }
        }
        tuningFrequency = default;
        return false;
    }

    /// <summary>Solves the <see cref="BeaconExclusionZone"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ReadOnlySpan<Measurement> measurements = [
            .. File.ReadLines(InputFile).Select(Measurement.Parse)
        ];
        int nonBeaconPositions = CountNonBeaconPositions(measurements, TargetRow);
        _ = TryFindTuningFrequency(measurements, out long tuningFrequency);
        textWriter.WriteLine(
            $"In row {TargetRow}, {nonBeaconPositions} positions cannot contain a beacon."
        );
        textWriter.WriteLine($"The tuning frequency of the distress beacon is {tuningFrequency}.");
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