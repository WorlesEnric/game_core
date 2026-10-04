// GameCore.Studio.Edit - optional services the built-in tools call (docs/studio/03-authoring-contracts.md s5):
// the agent gateway (asset.generate, mechanism.propose; filled by the etos client, P2.x), the build lane
// (project.build/launch; P3/P4), explain sources (inspect.explain; gameplay packages) and live-op translators (Play-mode
// world edits; gameplay packages). Nothing here is required: an absent service makes its tool answer NotConfigured.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;

namespace GameCore.Studio.Edit
{
    /// <summary>Per-runtime service bindings.</summary>
    public sealed class StudioServiceRegistry
    {
        private readonly List<IExplainSource> _explainSources = new List<IExplainSource>();
        private readonly List<ILiveOpTranslator> _translators = new List<ILiveOpTranslator>();

        /// <summary>The etos agent gateway (null: asset.generate and mechanism.propose answer NotConfigured).</summary>
        public IAgentGateway? AgentGateway { get; set; }

        /// <summary>The build lane (null: project.build and project.launch answer NotConfigured).</summary>
        public IBuildLane? BuildLane { get; set; }

        public IReadOnlyList<IExplainSource> ExplainSources => _explainSources;

        public IReadOnlyList<ILiveOpTranslator> LiveTranslators => _translators;

        public void RegisterExplainSource(IExplainSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            _explainSources.RemoveAll(existing => string.Equals(existing.Id, source.Id, StringComparison.Ordinal));
            _explainSources.Add(source);
        }

        public void RegisterLiveTranslator(ILiveOpTranslator translator)
        {
            if (translator == null)
            {
                throw new ArgumentNullException(nameof(translator));
            }

            if (!_translators.Contains(translator))
            {
                _translators.Add(translator);
            }
        }

        public bool UnregisterLiveTranslator(ILiveOpTranslator translator) => _translators.Remove(translator);

        /// <summary>The first translator that accepts the operation, or null (the op then changes authored data only).</summary>
        public ILiveOpTranslator? FindLiveTranslator(EditContext context)
        {
            foreach (ILiveOpTranslator translator in _translators)
            {
                if (translator.CanTranslate(context))
                {
                    return translator;
                }
            }

            return null;
        }

        /// <summary>The first explain source that can explain <paramref name="target"/>, or null.</summary>
        public IExplainSource? FindExplainSource(AuthoringRef target, Newtonsoft.Json.Linq.JObject? args)
        {
            foreach (IExplainSource source in _explainSources)
            {
                if (source.CanExplain(target, args))
                {
                    return source;
                }
            }

            return null;
        }
    }
}
