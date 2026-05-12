using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace CalorieCounting;

internal sealed class CalorieCounting {

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>Solves the <see cref="CalorieCounting"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ImmutableArray<int> calories = [
            .. File.ReadAllText(InputFile)
                .Split($"{Environment.NewLine}{Environment.NewLine}")
                .Select(lines => lines.Split(Environment.NewLine).Sum(int.Parse))
                .OrderDescending()
        ];
        int top1 = calories.Take(1).Sum();
        int top3 = calories.Take(3).Sum();
        textWriter.WriteLine($"The maximum calories carried by a single elf is {top1}.");
        textWriter.WriteLine($"The top three elves carry {top3} calories in total.");
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