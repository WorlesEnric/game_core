#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            string path = Path.GetFullPath(args[0]);
            JObject config = JObject.Parse(File.ReadAllText(path));
            JObject request = (JObject)config["request"]!;
            string candidatePath = (string)config["candidate"]!;
            JObject candidate = JObject.Parse(File.ReadAllText(Path.Combine(candidatePath, "change-set.json")));
            JObject catalog = JObject.Parse(File.ReadAllText(Path.Combine((string)config["evidence"]!, "tool-catalog.json")));
            var bytes = new List<byte[]>();
            foreach (JToken artifact in (JArray)candidate["artifacts"]!)
            {
                string name = (string)artifact["name"]!;
                if (name != Path.GetFileName(name)) throw new InvalidOperationException("Invalid artifact basename");
                bytes.Add(File.ReadAllBytes(Path.Combine(candidatePath, "artifacts", name)));
            }
            // Only the production credential resolver reads pairing; no key is accessed or printed by this driver.
            EtosCredentials credentials = EtosCredentials.FromKeyFile((string)config["pairing"]!);
            using var client = new CompanionClient(new EtosClientOptions {
                NodeUrl = (string)config["nodeUrl"]!, ProjectId = (string)request["projectId"]!,
            }, credentials);
            StageJobInfo job = await client.StageAppCandidateAsync((string)request["changeSetId"]!,
                (string)request["projectId"]!, (string)request["sourceRevision"]!,
                (string)request["catalogRevision"]!, candidate, catalog, bytes);
            config["jobId"] = job.JobId;
            File.WriteAllText(path, config.ToString());
            Console.WriteLine("Submitted " + job.JobId + " after source Editor exit");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(EtosRedaction.Redact(error.Message));
            return 1;
        }
    }
}
