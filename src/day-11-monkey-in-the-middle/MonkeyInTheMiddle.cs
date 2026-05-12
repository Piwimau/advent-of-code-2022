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

namespace MonkeyInTheMiddle;

internal sealed partial class MonkeyInTheMiddle {

    /// <summary>
    /// Represents a strategy for reducing the worry level when monkeys inspect certain items.
    /// </summary>
    private enum WorryLevelReduction { Normal, Advanced }

    /// <summary>Represents a <see cref="Monkey"/> that inspects and throws items.</summary>
    private sealed partial class Monkey {

        /// <summary>Index of the line containing the initial list of items.</summary>
        private const int ItemsLine = 1;

        /// <summary>Index at which the initial list of items starts.</summary>
        private const int ItemsStartIndex = 18;

        /// <summary>Index of the line containing the operation to perform for each item.</summary>
        private const int OperationLine = 2;

        /// <summary>Index at which the operator of the operation is present.</summary>
        private const int OperatorIndex = 23;

        /// <summary>Index at which the operand of the operation starts.</summary>
        private const int OperandStartIndex = 25;

        /// <summary>Index of the line containing the divisability test.</summary>
        private const int TestLine = 3;

        /// <summary>Index at which the divisor of the divisability test starts.</summary>
        private const int DivisorStartIndex = 21;

        /// <summary>Index of the line containing the first target monkey index.</summary>
        private const int FirstTargetLine = 4;

        /// <summary>Index at which the first target monkey index starts.</summary>
        private const int FirstTargetStartIndex = 29;

        /// <summary>Index of the line containing the second target monkey index.</summary>
        private const int SecondTargetLine = 5;

        /// <summary>Index at which the second target monkey index starts.</summary>
        private const int SecondTargetStartIndex = 30;

        /// <summary>Items this <see cref="Monkey"/> is currently holding.</summary>
        private readonly List<long> items;

        /// <summary>Operation to perform on each item during inspection.</summary>
        private readonly Func<long, long> operation;

        /// <summary>
        /// Gets the divisor this <see cref="Monkey"/> uses for testing the divisibility of an item.
        /// </summary>
        public long Divisor { get; init; }

        /// <summary>
        /// Index of the first target <see cref="Monkey"/> to throw items to if the divisibility
        /// for an item test succeeds.
        /// </summary>
        private readonly int firstTarget;

        /// <summary>
        /// Index of the second target <see cref="Monkey"/> to throw items to if the divisibility
        /// for an item test fails.
        /// </summary>
        private readonly int secondTarget;

        /// <summary>Gets the number of items this <see cref="Monkey"/> has inspected.</summary>
        public int InspectedItems { get; private set; }

        /// <summary>Initializes a new <see cref="Monkey"/>.</summary>
        /// <param name="items">Initial sequence of items the <see cref="Monkey"/> holds.</param>
        /// <param name="operation">Operation to perform on each item during inspection.</param>
        /// <param name="divisor">
        /// Divisor for testing the divisibility of an item, must be greater than or equal to one.
        /// </param>
        /// <param name="firstTarget">
        /// Positive index of the first target <see cref="Monkey"/> to throw items to if the
        /// divisibility test for an item succeeds.
        /// </param>
        /// <param name="secondTarget">
        /// Positive index of the second target <see cref="Monkey"/> to throw items to if the
        /// divisibility test for an item fails.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="items"/> or <paramref name="operation"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="divisor"/> is less than or equal to zero, or if
        /// <paramref name="firstTarget"/> or <paramref name="secondTarget"/> is negative.
        /// </exception>
        private Monkey(
            IEnumerable<long> items,
            Func<long, long> operation,
            long divisor,
            int firstTarget,
            int secondTarget
        ) {
            Guard.IsNotNull(items);
            Guard.IsNotNull(operation);
            Guard.IsGreaterThan(divisor, 0);
            Guard.IsGreaterThanOrEqualTo(firstTarget, 0);
            Guard.IsGreaterThanOrEqualTo(secondTarget, 0);
            this.items = [.. items];
            this.operation = operation;
            Divisor = divisor;
            this.firstTarget = firstTarget;
            this.secondTarget = secondTarget;
        }

        [GeneratedRegex(
            """
            ^Monkey \d+:
              Starting items: \d+(?:, \d+)*
              Operation: new = old (?:\+|\*) (?:old|\d+)
              Test: divisible by \d+
                If true: throw to monkey \d+
                If false: throw to monkey \d+$
            """
        )]
        private static partial Regex MonkeyRegex();

        /// <summary>Parses a <see cref="Monkey"/> from a given string.</summary>
        /// <remarks>
        /// The string <paramref name="s"/> must match the format described by
        /// <see cref="MonkeyRegex"/>. A valid example might be the following:
        /// <example>
        /// <code>
        /// Monkey 0:
        ///   Starting items: 79, 98
        ///   Operation: new = old * 19
        ///   Test: divisible by 23
        ///     If true: throw to monkey 2
        ///     If false: throw to monkey 3
        /// </code>
        /// </example>
        /// </remarks>
        /// <param name="s">String to parse a <see cref="Monkey"/> from.</param>
        /// <returns>A <see cref="Monkey"/> parsed from the given string.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="s"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="s"/> has an invalid format.
        /// </exception>
        public static Monkey Parse(string s) {
            Guard.IsNotNull(s);
            if (!MonkeyRegex().IsMatch(s)) {
                throw new ArgumentOutOfRangeException(
                    nameof(s),
                    $"The string \"{s}\" does not represent a valid monkey."
                );
            }
            ReadOnlySpan<string> lines = s.Split(Environment.NewLine);
            IEnumerable<long> items = lines[ItemsLine][ItemsStartIndex..]
                .Split(", ")
                .Select(long.Parse);
            // Parsing of the operand is done here to prevent it from happening multiple times.
            // Note that the operand can be either an integer or "old", in which case parsing fails.
            // We then just ignore that since it is not actually needed in the operation anyway.
            _ = long.TryParse(lines[OperationLine][OperandStartIndex..], out long operand);
            Func<long, long> operation = lines[OperationLine][OperatorIndex] switch {
                '+' => lines[OperationLine][OperandStartIndex..] switch {
                    "old" => (item) => item + item,
                    _ => (item) => item + operand
                },
                _ => lines[OperationLine][OperandStartIndex..] switch {
                    "old" => (item) => item * item,
                    _ => (item) => item * operand
                }
            };
            long divisor = long.Parse(
                lines[TestLine][DivisorStartIndex..],
                CultureInfo.InvariantCulture
            );
            int firstTarget = int.Parse(
                lines[FirstTargetLine][FirstTargetStartIndex..],
                CultureInfo.InvariantCulture
            );
            int secondTarget = int.Parse(
                lines[SecondTargetLine][SecondTargetStartIndex..],
                CultureInfo.InvariantCulture
            );
            return new Monkey(items, operation, divisor, firstTarget, secondTarget);
        }

        /// <summary>Returns a deep copy of this <see cref="Monkey"/>.</summary>
        /// <returns>A deep copy of this <see cref="Monkey"/>.</returns>
        public Monkey Clone() => new(items, operation, Divisor, firstTarget, secondTarget);

        /// <summary>
        /// Lets this <see cref="Monkey"/> take a turn by inspecting and throwing its items.
        /// </summary>
        /// <param name="monkeys">Sequence of all monkeys in the simulation.</param>
        /// <param name="worryLevelReduction">Worry level reduction strategy to use.</param>
        /// <param name="productOfDivisors">
        /// Product of the divisors of all monkeys, necessary and used if
        /// <paramref name="worryLevelReduction"/> is <see cref="WorryLevelReduction.Advanced"/>.
        /// </param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TakeTurn(
            ReadOnlySpan<Monkey> monkeys,
            WorryLevelReduction worryLevelReduction,
            long productOfDivisors
        ) {
            foreach (long item in items) {
                long worryLevel = operation(item);
                if (worryLevelReduction == WorryLevelReduction.Normal) {
                    worryLevel /= 3;
                }
                else {
                    worryLevel %= productOfDivisors;
                }
                int target = ((worryLevel % Divisor) == 0) ? firstTarget : secondTarget;
                monkeys[target].items.Add(worryLevel);
                InspectedItems++;
            }
            items.Clear();
        }

    }

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Returns the monkey business after a specified number of rounds.</summary>
    /// <remarks>
    /// The monkey business is defined as the product of the number of inspected items of the two
    /// most active monkeys.
    /// </remarks>
    /// <param name="monkeys">Sequence of monkeys for the simulation.</param>
    /// <param name="rounds">Positive number of rounds to simulate.</param>
    /// <param name="worryLevelReduction">Worry level reduction strategy to use.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="rounds"/> is negative.
    /// </exception>
    private static long MonkeyBusiness(
        ReadOnlySpan<Monkey> monkeys,
        int rounds,
        WorryLevelReduction worryLevelReduction
    ) {
        Guard.IsGreaterThanOrEqualTo(rounds, 0);
        long productOfDivisors = 1L;
        foreach (Monkey monkey in monkeys) {
            productOfDivisors *= monkey.Divisor;
        }
        for (int i = 0; i < rounds; i++) {
            foreach (Monkey monkey in monkeys) {
                monkey.TakeTurn(monkeys, worryLevelReduction, productOfDivisors);
            }
        }
        Span<int> inspectedItems = stackalloc int[monkeys.Length];
        for (int i = 0; i < monkeys.Length; i++) {
            inspectedItems[i] = monkeys[i].InspectedItems;
        }
        inspectedItems.Sort();
        return ((long) inspectedItems[^1]) * inspectedItems[^2];
    }

    /// <summary>Solves the <see cref="MonkeyInTheMiddle"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ImmutableArray<Monkey> monkeys = [
            .. File.ReadAllText(InputFile)
                .Split($"{Environment.NewLine}{Environment.NewLine}")
                .Select(Monkey.Parse)
        ];
        ReadOnlySpan<Monkey> monkeysCloned = [.. monkeys.Select(monkey => monkey.Clone())];
        long monkeyBusiness20 = MonkeyBusiness(monkeys.AsSpan(), 20, WorryLevelReduction.Normal);
        long monkeyBusiness10000 = MonkeyBusiness(
            monkeysCloned,
            10000,
            WorryLevelReduction.Advanced
        );
        textWriter.WriteLine($"After 20 rounds, the monkey business is {monkeyBusiness20}.");
        textWriter.WriteLine($"After 10000 rounds, the monkey business is {monkeyBusiness10000}.");
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