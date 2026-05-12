using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Running;
using CommunityToolkit.Diagnostics;

namespace NoSpaceLeftOnDevice;

internal sealed class NoSpaceLeftOnDevice {

    /// <summary>Represents a single <see cref="File"/>.</summary>
    /// <param name="Name">Name of the <see cref="File"/>.</param>
    /// <param name="Size">Size of the <see cref="File"/>.</param>
    private readonly record struct File(string Name, long Size);

    /// <summary>
    /// Represents a <see cref="Directory"/> which may contain files and other subdirectories.
    /// </summary>
    private sealed class Directory {

        /// <summary>Files in this <see cref="Directory"/>.</summary>
        private readonly List<File> files = [];

        /// <summary>Subdirectories in this <see cref="Directory"/>.</summary>
        private readonly List<Directory> subdirectories = [];

        /// <summary>Gets the name of this <see cref="Directory"/>.</summary>
        public string Name { get; init; }

        /// <summary>Gets the parent of this <see cref="Directory"/>.</summary>
        public Directory Parent { get; init; }

        /// <summary>
        /// Initializes a new <see cref="Directory"/> with a given name and an optional parent.
        /// </summary>
        /// <remarks>
        /// If <paramref name="parent"/> is <see langword="null"/>, the <see cref="Directory"/>
        /// becomes a top-level directory (which is its own parent).
        /// </remarks>
        /// <param name="name">Name of the <see cref="Directory"/>.</param>
        /// <param name="parent">Optional parent of the <see cref="Directory"/>.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        public Directory(string name, Directory? parent = null) {
            Guard.IsNotNullOrEmpty(name);
            Name = name;
            Parent = parent ?? this;
        }

        /// <summary>Adds a given <see cref="File"/> to this <see cref="Directory"/>.</summary>
        /// <param name="file"><see cref="File"/> to add to this <see cref="Directory"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when this <see cref="Directory"/> already contains a <see cref="File"/> with the
        /// same name.
        /// </exception>
        public void AddFile(File file) {
            if (files.Any(otherFile => otherFile.Name == file.Name)) {
                throw new ArgumentOutOfRangeException(
                    nameof(file),
                    $"A file with the name \"{file.Name}\" already exists."
                );
            }
            files.Add(file);
        }

        /// <summary>Adds a given subdirectory to this <see cref="Directory"/>.</summary>
        /// <param name="subdirectory">Subdirectory to add to this <see cref="Directory"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="subdirectory"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when trying to add a <see cref="Directory"/> as its own subdirectory
        /// or when this <see cref="Directory"/> already contains a subdirectory with the same name.
        /// </exception>
        public void AddSubdirectory(Directory subdirectory) {
            Guard.IsNotNull(subdirectory);
            if (subdirectory == this) {
                throw new ArgumentOutOfRangeException(
                    nameof(subdirectory),
                    "A directory cannot be one of its own subdirectories."
                );
            }
            if (subdirectories.Any(s => s.Name == subdirectory.Name)) {
                throw new ArgumentOutOfRangeException(
                    nameof(subdirectory),
                    $"A subdirectory with the name \"{subdirectory.Name}\" already exists."
                );
            }
            subdirectories.Add(subdirectory);
        }

        /// <summary>
        /// Returns the size of this <see cref="Directory"/> including all files and subdirectories.
        /// </summary>
        /// <returns>
        /// The size of this <see cref="Directory"/> including all files and subdirectories.
        /// </returns>
        public long Size()
            => files.Sum(file => file.Size) + subdirectories.Sum(directory => directory.Size());

        /// <summary>Returns all subdirectories of this <see cref="Directory"/>.</summary>
        /// <param name="searchRecursively">
        /// Whether to search recursively, which includes indirect subdirectories on deeper levels
        /// in the resulting sequence as well.
        /// </param>
        /// <returns>All subdirectories of this <see cref="Directory"/>.</returns>
        public IEnumerable<Directory> Subdirectories(bool searchRecursively = false) {
            foreach (Directory subdirectory in subdirectories) {
                yield return subdirectory;
                if (searchRecursively) {
                    foreach (Directory indirectSubdirectory
                            in subdirectory.Subdirectories(searchRecursively)) {
                        yield return indirectSubdirectory;
                    }
                }
            }
        }

    }

    /// <summary>Size for searching directories in the first part of the puzzle.</summary>
    private const long SearchSize = 100_000L;

    /// <summary>Total available size of the file system.</summary>
    private const long TotalAvailableSize = 70_000_000L;

    /// <summary>Required free size for installing the update.</summary>
    private const long RequiredFreeSize = 30_000_000L;

    private static readonly string InputFile = Path.Combine(
        AppContext.BaseDirectory,
        "resources",
        "input.txt"
    );

    /// <summary>
    /// Parses a file structure from the command line output in a given sequence of lines.
    /// </summary>
    /// <param name="lines">Sequence of lines of the command line output.</param>
    /// <returns>
    /// The top-level directory of the file structure parsed from the given command line output.
    /// </returns>
    private static Directory ParseFileStructure(ReadOnlySpan<string> lines) {
        Directory root = new("/");
        Directory current = root;
        foreach (string line in lines) {
            ReadOnlySpan<string> parts = line.Split(' ');
            if (parts[0] == "$") {
                if (parts[1] == "cd") {
                    string name = parts[2];
                    current = name switch {
                        "/" => root,
                        ".." => current.Parent,
                        _ => current.Subdirectories().First(
                            subdirectory => subdirectory.Name == name
                        )
                    };
                }
            }
            else if (parts[0] == "dir") {
                string name = parts[1];
                current.AddSubdirectory(new Directory(name, current));
            }
            else {
                string name = parts[1];
                long size = long.Parse(parts[0], CultureInfo.InvariantCulture);
                current.AddFile(new File(name, size));
            }
        }
        return root;
    }

    /// <summary>Solves the <see cref="NoSpaceLeftOnDevice"/> puzzle.</summary>
    /// <param name="textWriter"><see cref="TextWriter"/> to write the results to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="textWriter"/> is <see langword="null"/>.
    /// </exception>
    internal static void Solve(TextWriter textWriter) {
        Guard.IsNotNull(textWriter);
        ReadOnlySpan<string> lines = [.. System.IO.File.ReadLines(InputFile)];
        Directory root = ParseFileStructure(lines);
        long sum = root.Subdirectories(true)
            .Sum(subdirectory => {
                long size = subdirectory.Size();
                return (size <= SearchSize) ? size : 0;
            });
        long currentFreeSize = TotalAvailableSize - root.Size();
        long smallestSufficientSize = root.Subdirectories(true)
            .MinBy(subdirectory => {
                long freeSize = currentFreeSize + subdirectory.Size();
                return (freeSize >= RequiredFreeSize) ? freeSize : long.MaxValue;
            })!
            .Size();
        textWriter.WriteLine($"The sum of the directory sizes in the initial search is {sum}.");
        textWriter.WriteLine(
            $"The smallest directory freeing enough space has a size of {smallestSufficientSize}."
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