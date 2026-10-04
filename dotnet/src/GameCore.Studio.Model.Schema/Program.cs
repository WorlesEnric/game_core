// GameCore.Studio.Model.Schema - command line: `--out <directory>` writes every Studio schema there.
// Invoked by tools/studio/emit_studio_schemas.py (normal and --check modes).
#nullable enable
using System;

namespace GameCore.Studio.Model.Schema
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string? output = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--out" && i + 1 < args.Length)
                {
                    output = args[i + 1];
                    i++;
                    continue;
                }

                Console.Error.WriteLine("unknown argument: " + args[i]);
                return 2;
            }

            if (output == null)
            {
                Console.Error.WriteLine("usage: GameCore.Studio.Model.Schema --out <directory>");
                return 2;
            }

            StudioSchemaEmitter.WriteAll(output);
            foreach (SchemaDocument document in StudioSchemaEmitter.EmitAll())
            {
                Console.WriteLine("wrote " + document.FileName + " (" + document.Root.Name + ")");
            }

            return 0;
        }
    }
}
