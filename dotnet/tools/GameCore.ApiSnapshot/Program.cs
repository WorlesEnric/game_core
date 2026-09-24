// API snapshot console tool for the W0 reference seam (GC-002). Tooling only.
#nullable enable
using System;
using System.Globalization;
using System.IO;

namespace GameCore.ApiSnapshot
{
    /// <summary>Command-line entry point: generate the canonical seam listing into a file.</summary>
    public static class Program
    {
        private const string DefaultNamespace = "GameCore.Contracts";

        public static int Main(string[] args)
        {
            string? assemblyPath = null;
            string? outputPath = null;
            string namespaceFilter = DefaultNamespace;

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--assembly":
                            assemblyPath = Next(args, ref i, "--assembly");
                            break;
                        case "--output":
                            outputPath = Next(args, ref i, "--output");
                            break;
                        case "--namespace":
                            namespaceFilter = Next(args, ref i, "--namespace");
                            break;
                        default:
                            Console.Error.WriteLine("Unknown argument: " + args[i]);
                            Console.Error.WriteLine(Usage);
                            return 2;
                    }
                }
            }
            catch (ArgumentException error)
            {
                Console.Error.WriteLine(error.Message);
                Console.Error.WriteLine(Usage);
                return 2;
            }

            if (string.IsNullOrEmpty(assemblyPath))
            {
                Console.Error.WriteLine("--assembly is required.");
                Console.Error.WriteLine(Usage);
                return 2;
            }

            string listing;
            try
            {
                listing = ApiSnapshotGenerator.GenerateFromPath(assemblyPath, namespaceFilter);
            }
            catch (Exception error) when (error is IOException || error is BadImageFormatException || error is FileNotFoundException)
            {
                Console.Error.WriteLine("Failed to read assembly: " + error.Message);
                return 1;
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                Console.Out.Write(listing);
                return 0;
            }

            string fullOutput = Path.GetFullPath(outputPath);
            string? directory = Path.GetDirectoryName(fullOutput);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullOutput, listing);
            Console.Out.WriteLine("Wrote " + fullOutput + " (" +
                listing.Length.ToString(CultureInfo.InvariantCulture) + " characters)");
            return 0;
        }

        private const string Usage =
            "usage: GameCore.ApiSnapshot --assembly <path> [--output <path>] [--namespace " + DefaultNamespace + "]";

        private static string Next(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException("Missing value for " + option + ".");
            }

            index++;
            return args[index];
        }
    }
}
