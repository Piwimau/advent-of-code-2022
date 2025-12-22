# 🎄 Advent of Code 2022 🎄

This repository contains my solutions for [Advent of Code
2022](https://adventofcode.com/2022), my second year of participation.

## What Is Advent of Code?

[Advent of Code](https://adventofcode.com/) is a series of small programming
puzzles created by [Eric Wastl](http://was.tl/). Every day from December 1st to
25th, a puzzle is released alongside an engaging fictional Christmas story. Each
puzzle consists of two parts, the second of which usually contains some
interesting twist or changing requirements and is only unlocked after completing
the first one. The objective is to solve all parts and collect fifty stars ⭐
until December 25th to save Christmas.

Many users compete on the [global
leaderboard](https://adventofcode.com/2022/leaderboard) by solving the puzzles
in an unbelievably fast way in order to get some extra points. Personally, I see
Advent of Code as a fun exercise to do during the Advent season while waiting
for Christmas. I often use it to learn a new programming language (like I did in
2021 with `C#`) or some advanced programming concepts. I can only encourage you
to participate as well – of course in a way that you find fun. Just get started
and learn more about Advent of Code [here](https://adventofcode.com/2022/about).

## About This Project

The solutions for Advent of Code 2022 were originally developed using `.NET 7`
and `C# 11` at the time. Since then I have taken some time to update them to
more recent versions (`.NET 9` and `C# 13`), which allowed me to take advantage
of new language features and modern data structures that either did not exist
yet or I did not know about (as I was still learning the whole ecosystem). These
include expression bodies (`=>`), collection expressions (`[...]`), target-typed
`new()`, `Span<T>` and `ReadOnlySpan<T>`, types of the
`System.Collections.Immutable` or `System.Collections.Frozen` namespaces and
much more.

For this project and in general when developing software, I strive to produce
readable and well documented source code. However, I also enjoy benchmarking and
optimizing my code, which is why I sometimes implement a less idiomatic, yet
more efficient solution at the expense of readability. In those situations, I
try to document my design choices with analogies, possible alternative solutions
and sometimes little sketches to better illustrate the way a piece of code
works.

The general structure of this project is as follows:

```plaintext
src/
  Day-01-Calorie-Counting/
    Resources/
      .gitkeep
    Benchmark.cs
    CalorieCounting.cs
    Day-01-Calorie-Counting.csproj
  Day-02-Rock-Paper-Scissors/
    Resources/
      .gitkeep
    Benchmark.cs
    Day-02-Rock-Paper-Scissors.csproj
    RockPaperScissors.cs
  ...
  Day-25-Full-of-Hot-Air/
    ...
.gitignore
Advent-of-Code-2022.slnx
LICENSE
README.md
```

The [solution file](Advent-of-Code-2022.slnx) contains 25 standalone projects
for the days of the Advent calendar, organized into separate directories. Each
one provides a corresponding `.csproj` file that can be opened in Visual Studio.
In addition, there is a `Resources` directory which contains the puzzle
description and my personal input for that day. However, [as
requested](https://adventofcode.com/2022/about) by the creator of Advent of
Code, these are only present in my own private copy of the repository and
therefore not publicly available.

> If you're posting a code repository somewhere, please don't include parts of
> Advent of Code like the puzzle text or your inputs.

As a consequence, you will have to provide your own inputs for the days, as
described in more detail in the following section.

## Dependencies and Usage

If you want to try out one of my solutions, simply follow these steps below:

1. Make sure you have `.NET 9` or a later version installed on your machine.

2. Clone the repository (or download the source code) to a directory of your
   choice.

   ```shell
   git clone https://github.com/Piwimau/Advent-of-Code-2022 ./Advent-of-Code-2022
   cd ./Advent-of-Code-2022
   ```

3. Put your input for the day in a file called `input.txt` and copy it to the
   appropriate resources directory. You can get all inputs from the [official
   website](https://adventofcode.com/2022) if you have not downloaded them
   already.

   ```shell
   cp input.txt ./src/Day-01-Calorie-Counting/Resources
   ```

4. Nagivate into the appropriate day's directory.

   ```shell
   cd ./src/Day-01-Calorie-Counting
   ```

5. Finally, run the code in release mode to take advantage of all optimizations
   and achieve the best performance.

   ```shell
   dotnet run --configuration Release
   ```

   Optionally, specify an additional flag `--benchmark` to benchmark the
   relevant day on your machine. Note that in this mode no output for the
   results of the solved puzzle is produced.

   ```shell
   dotnet run --configuration Release --benchmark
   ```

If you have Visual Studio installed on your machine, you may also just open the
provided [solution file](Advent-of-Code-2022.slnx) and proceed from there.

## Benchmarks

Finally, here are some (non-scientific) benchmarks I created using the fantastic
[BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) package and my main
machine (Intel Core i9-13900HX, 32 GB DDR5-5600 RAM) running Windows 11 24H2.
All benchmarks include the time spent for reading the input from disk, as well
as printing the puzzle results (although the output is written to
`TextWriter.Null` when benchmarking, which is effectively a no-op and rather
fast).

| Day                               |         Min |         Max |        Mean |      Median | Standard Deviation |
|-----------------------------------|------------:|------------:|------------:|------------:|-------------------:|
| Day 1 – Calorie Counting          |    0.069 ms |    0.071 ms |    0.069 ms |    0.069 ms |           0.001 ms |
| Day 2 – Rock Paper Scissors       |    0.235 ms |    0.239 ms |    0.237 ms |    0.236 ms |           0.001 ms |
| Day 3 – Rucksack Reorganization   |    0.318 ms |    0.337 ms |    0.329 ms |    0.330 ms |           0.005 ms |
| Day 4 – Camp Cleanup              |    0.137 ms |    0.140 ms |    0.139 ms |    0.139 ms |           0.001 ms |
| Day 5 – Supply Stacks             |    0.170 ms |    0.174 ms |    0.173 ms |    0.173 ms |           0.001 ms |
| Day 6 – Tuning Trouble            |    0.028 ms |    0.029 ms |    0.029 ms |    0.029 ms |           0.000 ms |
| Day 7 – No Space Left On Device   |    0.185 ms |    0.188 ms |    0.186 ms |    0.187 ms |           0.001 ms |
| Day 8 – Treetop Tree House        |    0.513 ms |    0.539 ms |    0.523 ms |    0.521 ms |           0.008 ms |
| Day 9 – Rope Bridge               |    0.943 ms |    0.964 ms |    0.954 ms |    0.954 ms |           0.006 ms |
| Day 10 – Cathode-Ray Tube         |    0.022 ms |    0.023 ms |    0.022 ms |    0.022 ms |           0.000 ms |
| Day 11 – Monkey In The Middle     |    3.140 ms |    3.190 ms |    3.166 ms |    3.169 ms |           0.014 ms |
| Day 12 – Hill Climbing Algorithm  |   29.744 ms |   31.156 ms |   30.622 ms |   30.613 ms |           0.353 ms |
| Day 13 – Distress Signal          |    0.420 ms |    0.432 ms |    0.426 ms |    0.426 ms |           0.003 ms |
| Day 14 – Regolith Reservoir       |   58.409 ms |   61.027 ms |   59.449 ms |   59.324 ms |           0.764 ms |
| Day 15 – Beacon Exclusion Zone    |  133.735 ms |  140.129 ms |  136.489 ms |  135.957 ms |           1.777 ms |
| Day 16 – Proboscidea Volcanium    |  730.019 ms |  775.538 ms |  750.803 ms |  750.714 ms |          13.966 ms |
| Day 17 – Pyroclastic Flow         |    2.056 ms |    2.144 ms |    2.091 ms |    2.091 ms |           0.022 ms |
| Day 18 – Boiling Boulders         |    1.028 ms |    1.074 ms |    1.054 ms |    1.052 ms |           0.011 ms |
| Day 19 – Not Enough Minerals      |  131.870 ms |  141.179 ms |  136.107 ms |  136.157 ms |           2.409 ms |
| Day 20 – Grove Positioning System |   34.480 ms |   35.278 ms |   34.902 ms |   34.911 ms |           0.260 ms |
| Day 21 – Monkey Math              |    1.670 ms |    1.744 ms |    1.707 ms |    1.706 ms |           0.027 ms |
| Day 22 – Monkey Map               |    0.553 ms |    0.591 ms |    0.570 ms |    0.573 ms |           0.011 ms |
| Day 23 – Unstable Diffusion       |  425.963 ms |  436.287 ms |  430.321 ms |  430.350 ms |           2.572 ms |
| Day 24 – Blizzard Basin           |  193.843 ms |  208.269 ms |  201.307 ms |  201.106 ms |           3.405 ms |
| Day 25 – Full of Hot Air          |    0.037 ms |    0.038 ms |    0.038 ms |    0.038 ms |           0.000 ms |
| Total                             | 1749.587 ms | 1840.780 ms | 1791.713 ms | 1790.847 ms |          25.618 ms |

## License

This project is licensed under the [MIT License](LICENSE). Feel free to
experiment with the code, adapt it to your own preferences, and share it with
others.