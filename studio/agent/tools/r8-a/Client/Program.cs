#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static void Save(string path, JToken value) => File.WriteAllText(path, EtosRedaction.Redact(value.ToString(Formatting.Indented)) + "\n");
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Client queue|read|cancel CONFIG");
            JObject config = JObject.Parse(File.ReadAllText(args[1]));
            string output = (string)config["evidence"]!;
            // Only the production credential resolver opens the newly paired scratch file.
            EtosCredentials credentials = EtosCredentials.FromKeyFile((string)config["pairingFile"]!);
            using var client = new CompanionClient(new EtosClientOptions { NodeUrl = (string)config["nodeUrl"]!,
                AppName = "gamecore-unity", ProjectId = (string)config["projectId"]! }, credentials);
            if (args[0] == "queue")
            {
                JObject context = JObject.Parse(File.ReadAllText(Path.Combine(output, "stage-context.json")));
                JObject request = (JObject)context["request"]!;
                JObject candidate = (JObject)context["changeSet"]!;
                var files = new List<byte[]>();
                foreach (JObject artifact in (JArray)candidate["artifacts"]!)
                {
                    string name = (string)artifact["name"]!;
                    if (Path.GetFileName(name) != name) throw new InvalidOperationException("Invalid artifact basename");
                    files.Add(File.ReadAllBytes(Path.Combine((string)config["candidate"]!, "artifacts", name)));
                }
                StageJobInfo job = await client.StageAppCandidateAsync((string)request["changeSetId"]!, (string)request["projectId"]!,
                    (string)request["sourceRevision"]!, (string)request["catalogRevision"]!, candidate, (JObject)context["toolCatalog"]!, files);
                Save(Path.Combine(output, "stage-queued.json"), job.Raw);
            }
            else
            {
                string jobId = (string)JObject.Parse(File.ReadAllText(Path.Combine(output, "stage-queued.json")))["jobId"]!;
                StageJobInfo job = args[0] == "cancel" ? await client.CancelStageAsync(jobId) : await client.GetStageAsync(jobId);
                Save(Path.Combine(output, args[0] == "cancel" ? "cleanup-cancel.json" : "stage-durable-read.json"), job.Raw);
                if (args[0] == "read")
                {
                    if (job.State != "cancelled") throw new InvalidOperationException("Durable authenticated state is not cancelled");
                    try { await client.FetchTrustedVerdictAsync(jobId); throw new InvalidOperationException("Cancelled job issued a verdict"); }
                    catch (EtosException error) when (error.Error.Status == 404) { }
                }
            }
            Console.WriteLine("R8_A_CLIENT_OK " + args[0]);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(EtosRedaction.Redact(error.ToString()));
            return 1;
        }
    }
}
