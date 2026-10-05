// GameCore.Studio.UI - the project's Studio selection state (a ScriptableSingleton, so it survives domain reloads and
// Play Mode transitions). The live SelectionModel is rebuilt after every reload from the serialized refs; the refs keep
// their stamps, so a target that changed meanwhile shows as stale in the badges.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using SelectionMode = GameCore.Studio.Model.SelectionMode;

namespace GameCore.Studio.UI
{
    /// <summary>Owner of the persisted selection (03 s2 SelectionSnapshot state between snapshots).</summary>
    public sealed class StudioSelection : ScriptableSingleton<StudioSelection>
    {
        [SerializeField]
        private string selectionJson = string.Empty;

        [SerializeField]
        private long savedVersion;

        /// <summary>The serialized selection (targets, parts, location, marquee) as last persisted.</summary>
        public string SelectionJson => selectionJson;

        public long SavedVersion => savedVersion;

        /// <summary>Restores <paramref name="model"/> from the persisted state and keeps persisting its changes.</summary>
        public void Bind(SelectionModel model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            Load(model, selectionJson);
            model.Changed += () => Persist(model);
        }

        /// <summary>Serializes a model's selection (JSON of the 03 s2 members).</summary>
        public static string Serialize(SelectionModel model)
        {
            JObject json = new JObject();
            JArray targets = new JArray();
            foreach (AuthoringRef target in model.Targets)
            {
                targets.Add(StudioJson.ToToken(target));
            }

            json["targets"] = targets;
            if (model.Parts.Count > 0)
            {
                JArray parts = new JArray();
                foreach (PartRef part in model.Parts)
                {
                    parts.Add(StudioJson.ToToken(part));
                }

                json["parts"] = parts;
            }

            if (model.Location != null)
            {
                json["location"] = StudioJson.ToToken(model.Location);
            }

            if (model.RegionRect != null)
            {
                json["regionRect"] = StudioJson.ToToken(model.RegionRect);
            }

            return json.ToString(Formatting.None);
        }

        /// <summary>Restores a model from <see cref="Serialize"/> output; malformed text leaves it empty.</summary>
        public static void Load(SelectionModel model, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                JObject json = JObject.Parse(text);
                List<AuthoringRef> targets = new List<AuthoringRef>();
                if (json["targets"] is JArray rows)
                {
                    foreach (JToken row in rows)
                    {
                        targets.Add(StudioJson.Deserialize<AuthoringRef>(row.ToString(Formatting.None)));
                    }
                }

                List<PartRef>? parts = null;
                if (json["parts"] is JArray partRows)
                {
                    parts = new List<PartRef>();
                    foreach (JToken row in partRows)
                    {
                        parts.Add(StudioJson.Deserialize<PartRef>(row.ToString(Formatting.None)));
                    }
                }

                AuthoringRef? location = json["location"] is JObject locationJson ? StudioJson.Deserialize<AuthoringRef>(locationJson.ToString(Formatting.None)) : null;
                RegionRect? rect = json["regionRect"] is JObject rectJson ? StudioJson.Deserialize<RegionRect>(rectJson.ToString(Formatting.None)) : null;
                model.Restore(targets, parts, location, rect);
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is InvalidOperationException)
            {
                Debug.LogWarning(StudioStyles.Safe("GameCore Studio: the persisted selection could not be read and was dropped: " + error.Message));
            }
        }

        private void Persist(SelectionModel model)
        {
            selectionJson = Serialize(model);
            savedVersion = model.Version;
        }
    }
}
