using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace DistressSignal;

internal sealed class DistressSignal {

    /// <summary>Represents the common interface of all packets.</summary>
    private interface IPacket : IComparable<IPacket> { }

    /// <summary>Represents a simple <see cref="ValuePacket"/> containing just a value.</summary>
    /// <param name="Value">Value of the <see cref="ValuePacket"/>.</param>
    private readonly record struct ValuePacket(int Value) : IPacket {

        /// <summary>
        /// Compares this <see cref="ValuePacket"/> to a given other <see cref="IPacket"/>.
        /// </summary>
        /// <param name="other">Other <see cref="IPacket"/> to compare this instance to.</param>
        /// <returns>
        /// <list type="bullet">
        ///     <item>
        ///     A negative value if this <see cref="ValuePacket"/> is considered smaller than the
        ///     given <see cref="IPacket"/>.
        ///     </item>
        ///     <item>
        ///     0 if this <see cref="ValuePacket"/> is considered equal to the given
        ///     <see cref="IPacket"/>.
        ///     </item>
        ///     <item>
        ///     A positive value if this <see cref="ValuePacket"/> is considered bigger than the
        ///     given <see cref="IPacket"/>.
        ///     </item>
        /// </list>
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="other"/> is <see langword="null"/>.
        /// </exception>
        public int CompareTo(IPacket? other) {
            Guard.IsNotNull(other);
            switch (other) {
                case ValuePacket valuePacket:
                    return Value.CompareTo(valuePacket.Value);
                case ListPacket listPacket: {
                    ListPacket packet = new();
                    packet.AddSubPacket(this);
                    return packet.CompareTo(listPacket);
                }
                default:
                    throw new InvalidOperationException("Unreachable.");
            }
        }

        /// <summary>Returns a string representation of this <see cref="ValuePacket"/>.</summary>
        /// <returns>A string representation of this <see cref="ValuePacket"/>.</returns>
        public override string ToString() => $"{Value}";

    }

    /// <summary>Represents a <see cref="ListPacket"/> with zero or more sub-packets.</summary>
    private sealed class ListPacket : IPacket {

        /// <summary>
        /// Used for efficiently searching digits when parsing a <see cref="ListPacket"/> from a
        /// string.
        /// </summary>
        private static readonly SearchValues<char> Digits = SearchValues.Create("0123456789");

        /// <summary>Sub-packets of this <see cref="ListPacket"/>.</summary>
        private readonly List<IPacket> subPackets = [];

        /// <summary>Initializes a new <see cref="ListPacket"/>.</summary>
        public ListPacket() { }

        /// <summary>Parses a <see cref="ListPacket"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must start with '[' and end with ']'.
        /// Additionally, it must only contain digits or the characters '[', ']', or ',' to
        /// represent any sub-packets.
        /// </remarks>
        /// <param name="s">String to parse a <see cref="ListPacket"/> from.</param>
        /// <returns>A <see cref="ListPacket"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static ListPacket Parse(ReadOnlySpan<char> s) {
            if (!s.StartsWith("[") || !s.EndsWith("]")) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid list packet."
                );
            }
            ListPacket current = new();
            Stack<ListPacket> stack = new([current]);
            for (int i = 1; i < s.Length - 1; i++) {
                char c = s[i];
                if (c == '[') {
                    ListPacket subPacket = new();
                    current.AddSubPacket(subPacket);
                    current = subPacket;
                    stack.Push(current);
                }
                else if (c == ']') {
                    stack.Pop();
                    current = stack.Peek();
                }
                else if (char.IsDigit(c)) {
                    int indexOfNextNonDigit = i + 1 + s[(i + 1)..].IndexOfAnyExcept(Digits);
                    ReadOnlySpan<char> value = s[i..indexOfNextNonDigit];
                    // Advance index for values with multiple digits to prevent them from being
                    // parsed more than once. Note that we set the index to one before the next
                    // non-digit character, as we need to process that next character and the index
                    // is incremented by one each round additionally.
                    i = indexOfNextNonDigit - 1;
                    current.AddSubPacket(
                        new ValuePacket(int.Parse(value, CultureInfo.InvariantCulture))
                    );
                }
                else if (c != ',') {
                    throw new ArgumentOutOfRangeException(
                        nameof(s),
                        $"Invalid character '{c}' found in the string \"{s}\"."
                    );
                }
            }
            return current;
        }

        /// <summary>
        /// Adds a given <see cref="IPacket"/> as a sub-packet of this <see cref="ListPacket"/>.
        /// </summary>
        /// <param name="subPacket"><see cref="IPacket"/> to add as a sub-packet.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="subPacket"/> is <see langword="null"/> or refers to the same
        /// <see cref="ListPacket"/> as this instance, as a packet cannot be one of its own
        /// sub-packets.
        /// </exception>
        public void AddSubPacket(IPacket subPacket) {
            Guard.IsNotNull(subPacket);
            if (subPacket == this) {
                throw new ArgumentOutOfRangeException(
                    nameof(subPacket),
                    $"A list packet cannot be one of its own sub-packets."
                );
            }
            subPackets.Add(subPacket);
        }

        /// <summary>
        /// Compares this <see cref="ListPacket"/> to a given other <see cref="IPacket"/>.
        /// </summary>
        /// <param name="other">Other <see cref="IPacket"/> to compare this instance to.</param>
        /// <returns>
        /// <list type="bullet">
        ///     <item>
        ///     A negative value if this <see cref="ListPacket"/> is considered smaller than the
        ///     given <see cref="IPacket"/>.
        ///     </item>
        ///     <item>
        ///     0 if this <see cref="ListPacket"/> is considered equal to the given
        ///     <see cref="IPacket"/>.
        ///     </item>
        ///     <item>
        ///     A positive value if this <see cref="ListPacket"/> is considered bigger than the
        ///     given <see cref="IPacket"/>.
        ///     </item>
        /// </list>
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="other"/> is <see langword="null"/>.
        /// </exception>
        public int CompareTo(IPacket? other) {
            Guard.IsNotNull(other);
            switch (other) {
                case ValuePacket valuePacket: {
                    ListPacket packet = new();
                    packet.AddSubPacket(valuePacket);
                    return CompareTo(packet);
                }
                case ListPacket listPacket: {
                    int count = Math.Min(subPackets.Count, listPacket.subPackets.Count);
                    for (int i = 0; i < count; i++) {
                        int comparison = subPackets[i].CompareTo(listPacket.subPackets[i]);
                        if (comparison != 0) {
                            return comparison;
                        }
                    }
                    return subPackets.Count.CompareTo(listPacket.subPackets.Count);
                }
                default:
                    throw new InvalidOperationException("Unreachable.");
            }
        }

        /// <summary>Returns a string representation of this <see cref="ListPacket"/>.</summary>
        /// <returns>A string representation of this <see cref="ListPacket"/>.</returns>
        public override string ToString() {
            StringBuilder result = new("[");
            for (int i = 0; i < subPackets.Count; i++) {
                result.Append(subPackets[i].ToString());
                if (i < subPackets.Count - 1) {
                    result.Append(',');
                }
            }
            return result.Append(']').ToString();
        }

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Solves the <see cref="DistressSignal"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ImmutableArray<ListPacket> packets = [
            .. File.ReadLines(InputFile)
                .Where(line => line.Length > 0)
                .Select(line => ListPacket.Parse(line))
        ];
        int sumOfIndices = packets
            .Chunk(2)
            .Select((packets, index) => (packets[0].CompareTo(packets[1]) <= 0) ? index + 1 : 0)
            .Sum();
        ListPacket firstDivider = ListPacket.Parse("[[2]]");
        ListPacket secondDivider = ListPacket.Parse("[[6]]");
        ReadOnlySpan<ListPacket> additionalPackets = [firstDivider, secondDivider];
        packets = packets.AddRange(additionalPackets).Sort();
        int decoderKey = (packets.IndexOf(firstDivider) + 1) * (packets.IndexOf(secondDivider) + 1);
        textWriter.WriteLine(
            $"The sum of the indices of the pairs in the right order is {sumOfIndices}."
        );
        textWriter.WriteLine($"The decoder key of the distress signal is {decoderKey}.");
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