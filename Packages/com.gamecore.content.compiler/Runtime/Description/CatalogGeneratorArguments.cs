// GameCore.Content.Compiler - command-line contract for the Editor entry point (GC-003).
// Unity-free on purpose: the argument parsing lives beside the generator core so a plain-dotnet test can
// exercise the same parsing the batchmode entry point uses, including the failure cases.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Content.Compiler
{
    /// <summary>
    /// Parsed generator invocation. The Editor entry point builds one of these from the Unity command line and
    /// the package tests build one directly, so both paths are the same code.
    /// </summary>
    public sealed class CatalogGeneratorArguments
    {
        private CatalogGeneratorArguments(string? descriptionPath, string? outputPath, bool printHelp, IReadOnlyList<string> errors)
        {
            DescriptionPath = descriptionPath;
            OutputPath = outputPath;
            PrintHelp = printHelp;
            Errors = errors;
        }

        /// <summary>Absolute or repository-relative path of the catalog description document.</summary>
        public string? DescriptionPath { get; }

        /// <summary>Absolute or repository-relative path of the generated C# file.</summary>
        public string? OutputPath { get; }

        /// <summary>True when the invocation asked for usage text instead of generating.</summary>
        public bool PrintHelp { get; }

        /// <summary>Argument errors in the order they were found; empty when the invocation is usable.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>True when the invocation is complete and did not request help.</summary>
        public bool IsValid => Errors.Count == 0 && !PrintHelp;

        /// <summary>Usage text of the batchmode entry point, mirrored by the Editor menu and the README.</summary>
        public const string Usage =
            "usage: -executeMethod GameCore.Content.Compiler.Editor.CatalogGeneratorMenu.GenerateFromCommandLine \\\n" +
            "         -catalogDescription <path to the description document> \\\n" +
            "         -catalogOutput <path of the generated C# file>\n" +
            "       -executeMethod GameCore.Content.Compiler.Editor.CatalogGeneratorMenu.GenerateFromCommandLine -catalogHelp\n" +
            "\n" +
            "Exit code contract: the process exits 0 only after the generated file was written and re-read with a\n" +
            "matching fingerprint; otherwise it exits nonzero and writes the diagnostics to the log.";

        /// <summary>Parses generator arguments from an argument array (Unity's <c>Environment.GetCommandLineArgs()</c>).</summary>
        public static CatalogGeneratorArguments Parse(string[] args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            string? descriptionPath = null;
            string? outputPath = null;
            bool printHelp = false;
            List<string> errors = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-catalogDescription":
                        descriptionPath = Next(args, ref i, errors, "-catalogDescription");
                        break;
                    case "-catalogOutput":
                        outputPath = Next(args, ref i, errors, "-catalogOutput");
                        break;
                    case "-catalogHelp":
                        printHelp = true;
                        break;
                    default:
                        break;
                }
            }

            if (!printHelp)
            {
                if (string.IsNullOrEmpty(descriptionPath))
                {
                    errors.Add("-catalogDescription <path> is required.");
                }

                if (string.IsNullOrEmpty(outputPath))
                {
                    errors.Add("-catalogOutput <path> is required.");
                }
            }

            return new CatalogGeneratorArguments(descriptionPath, outputPath, printHelp, errors);
        }

        private static string? Next(string[] args, ref int index, List<string> errors, string option)
        {
            if (index + 1 >= args.Length)
            {
                errors.Add(option + " requires a value.");
                return null;
            }

            index++;
            return args[index];
        }
    }
}
