#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    // Candidate-output seam supplied by dialogue.setFact(authoringId). No gameplay assembly dependency.
    internal static class CandidateFactReferences
    {
        private static string? Text(JToken? token) => token?.Type == JTokenType.String ? (string?)token : null;

        internal static ChangeSet Normalize(ChangeSet changeSet, List<Diagnostic> diagnostics, HashSet<string> deferred)
        {
            List<Operation> operations = new List<Operation>();
            for (int consumerIndex = 0; consumerIndex < changeSet.Operations.Count; consumerIndex++)
            {
                Operation consumer = changeSet.Operations[consumerIndex];
                operations.Add(consumer);
                if (consumer.Tool != "dialogue.setFactCondition") continue;
                JToken? fact = consumer.Args?["fact"];
                string? id = Text(fact) ?? (fact is JObject referenceId ? Text(referenceId["authoringId"]) : null);
                if (string.IsNullOrEmpty(id)) continue;
                List<int> producers = new List<int>();
                for (int i = 0; i < changeSet.Operations.Count; i++)
                {
                    Operation producer = changeSet.Operations[i];
                    if (producer.Tool == "dialogue.setFact" && Text(producer.Args?["authoringId"]) == id) producers.Add(i);
                }
                if (producers.Count == 0) continue; // Existing/unknown references use normal typed binding.
                int producerIndex = producers[0];
                Operation source = changeSet.Operations[producerIndex];
                string? reason = producers.Count > 1 ? "candidate_fact_ambiguous_producer"
                    : producerIndex >= consumerIndex ? "candidate_fact_forward_reference"
                    : fact is JObject reference && Text(reference["kind"]) != "Definition" ? "candidate_fact_wrong_kind" : null;
                if (reason != null)
                {
                    string detail = reason == "candidate_fact_forward_reference"
                        ? "Forward fact reference: operation '" + consumer.OpId + "' precedes producer '" + source.OpId + "'."
                        : reason == "candidate_fact_ambiguous_producer" ? "More than one dialogue.setFact operation produces fact '" + id + "'."
                        : "Candidate fact '" + id + "' must use a Definition reference.";
                    diagnostics.Add(Diagnostic.AtOperation(DiagnosticCodes.InvalidArgs, consumer.OpId,
                        detail,
                        "Place the fact producer before its consumers; use its candidate authoringId.",
                        new JObject { ["reason"] = reason, ["authoringId"] = id, ["producer"] = source.OpId, ["consumer"] = consumer.OpId }));
                    continue;
                }
                List<string> dependencies = new List<string>(consumer.DependsOn ?? Array.Empty<string>());
                if (!dependencies.Contains(source.OpId)) dependencies.Add(source.OpId);
                operations[operations.Count - 1] = new Operation(consumer.OpId, consumer.Tool, consumer.Target,
                    consumer.Args, dependencies, consumer.Preconditions, consumer.ApplyRequirement);
                deferred.Add(consumer.OpId);
            }
            return new ChangeSet(changeSet.Id, changeSet.Schema, changeSet.Intent, operations, changeSet.Selection,
                changeSet.BaseVersions, changeSet.Artifacts, changeSet.Validation, changeSet.Requirements,
                changeSet.Links, changeSet.State, changeSet.Outcomes, changeSet.Policy, changeSet.Timestamps);
        }
    }
}
