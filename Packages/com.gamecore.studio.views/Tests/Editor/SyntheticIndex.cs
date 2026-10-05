// GameCore.Studio.Views.Tests - synthetic semantic indexes for the pure model and canvas tests.
#nullable enable
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Views.Tests
{
    internal sealed class SyntheticIndex
    {
        private readonly List<IndexNode> _nodes = new List<IndexNode>();
        private readonly List<IndexEdge> _edges = new List<IndexEdge>();

        public static AuthoringRef Ref(string id) => new AuthoringRef(AuthoringKind.Definition, authoringId: id);

        public static AuthoringRef Entity(string id) => new AuthoringRef(AuthoringKind.Entity, authoringId: id);

        public AuthoringRef Node(string id, string type, string? name = null, Dictionary<string, IndexField>? fields = null, List<IndexRef>? refs = null, AuthoringKind kind = AuthoringKind.Definition)
        {
            AuthoringRef reference = new AuthoringRef(kind, authoringId: id);
            _nodes.Add(new IndexNode(reference, type, name ?? id, fields, refs));
            return reference;
        }

        public void Edge(AuthoringRef from, AuthoringRef to, EdgeKind kind = EdgeKind.References)
        {
            _edges.Add(new IndexEdge(from, to, kind));
        }

        public SemanticIndex Build(long revision = 1) => new SemanticIndex(revision, "synthetic", _nodes, _edges);

        public static IndexField Text(string value) => new IndexField("string", new JValue(value));

        public static IndexField Number(int value) => new IndexField("int", new JValue(value));

        /// <summary>A tree of <paramref name="count"/> nodes, fan-out <paramref name="fanOut"/>, every node referencing its parent.</summary>
        public static SemanticIndex Tree(int count, int fanOut, out AuthoringRef root)
        {
            SyntheticIndex index = new SyntheticIndex();
            List<AuthoringRef> refs = new List<AuthoringRef>(count);
            for (int i = 0; i < count; i++)
            {
                string type = i % 3 == 0 ? "synthetic.a" : (i % 3 == 1 ? "synthetic.b" : "synthetic.c");
                refs.Add(index.Node("n" + i.ToString("0000", CultureInfo.InvariantCulture), type, "Node " + i));
                if (i > 0)
                {
                    index.Edge(refs[(i - 1) / fanOut], refs[i], i % 5 == 0 ? EdgeKind.Contains : EdgeKind.References);
                }
            }

            root = refs[0];
            return index.Build();
        }
    }
}
