namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// DAG pipeline definition: a collection of nodes that declare dependencies, conditions, retry policies, and timeouts.
/// Build it with <see cref="PipelineDag.Builder"/> or create a chained DAG from a linear operator list with <see cref="FromSequence"/>.
/// </summary>
public sealed class PipelineDag
{
    private readonly List<PipelineNode> _nodes = [];

    /// <summary>All nodes in the order they were added.</summary>
    public IReadOnlyList<PipelineNode> Nodes => _nodes;

    /// <summary>Gets a node, or null if it does not exist.</summary>
    public PipelineNode? Find(string name) =>
        _nodes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal));

    /// <summary>Creates a DAG builder.</summary>
    public static Builder CreateBuilder() => new();

    /// <summary>Creates a chained DAG from a linear operator list, where each node depends on the previous node, preserving sequential behavior.</summary>
    public static PipelineDag FromSequence(IEnumerable<IPipelineOperator> operators)
    {
        var builder = CreateBuilder();
        string? previous = null;
        var index = 0;
        foreach (var op in operators)
        {
            var name = op.Name;
            // Append an index to duplicate operator names to keep node names unique.
            while (builder.Contains(name))
                name = $"{op.Name}#{++index}";

            var node = new PipelineNode
            {
                Name = name,
                Operator = op,
                DependsOn = previous is null ? [] : [previous]
            };
            builder.Add(node);
            previous = name;
        }

        return builder.Build();
    }

    /// <summary>
    /// Validates that node names are unique, dependencies exist, and the graph has no cycles.
    /// Returns null when valid, or an error description otherwise.
    /// </summary>
    public string? Validate()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in _nodes)
        {
            if (!names.Add(node.Name))
                return $"Duplicate node name '{node.Name}'.";
        }

        foreach (var node in _nodes)
        {
            foreach (var dependency in node.DependsOn)
            {
                if (!names.Contains(dependency))
                    return $"Node '{node.Name}' depends on unknown node '{dependency}'.";
            }
        }

        // Detect cycles with Kahn's algorithm.
        var indegree = _nodes.ToDictionary(n => n.Name, _ => 0, StringComparer.Ordinal);
        var adjacency = _nodes.ToDictionary(n => n.Name, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var node in _nodes)
        {
            foreach (var dependency in node.DependsOn)
            {
                adjacency[dependency].Add(node.Name);
                indegree[node.Name]++;
            }
        }

        var queue = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var visited = 0;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;
            foreach (var next in adjacency[current])
            {
                if (--indegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        if (visited != _nodes.Count)
        {
            var cycle = indegree.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
            return $"Pipeline DAG contains a cycle involving: {string.Join(", ", cycle)}.";
        }

        return null;
    }

    /// <summary>DAG builder for declaring nodes fluently.</summary>
    public sealed class Builder
    {
        private readonly List<PipelineNode> _nodes = [];

        /// <summary>Adds a node.</summary>
        public Builder Add(PipelineNode node)
        {
            _nodes.Add(node ?? throw new ArgumentNullException(nameof(node)));
            return this;
        }

        /// <summary>Declaratively adds a node with its name, operator, dependencies, condition, retry policy, timeout, and degradation policy.</summary>
        public Builder Add(
            string name,
            IPipelineOperator @operator,
            IEnumerable<string>? dependsOn = null,
            Func<Centurion.Models.Workflow.SubtitleWorkflowContext, bool>? when = null,
            int maxRetries = 0,
            TimeSpan? timeout = null,
            bool degradeOnFailure = false,
            string? description = null)
        {
            return Add(new PipelineNode
            {
                Name = name,
                Operator = @operator ?? throw new ArgumentNullException(nameof(@operator)),
                DependsOn = dependsOn?.ToList() ?? [],
                When = when,
                MaxRetries = maxRetries,
                Timeout = timeout,
                DegradeOnFailure = degradeOnFailure,
                Description = description
            });
        }

        /// <summary>Checks whether a node with the specified name has already been added.</summary>
        public bool Contains(string name) => _nodes.Any(n => string.Equals(n.Name, name, StringComparison.Ordinal));

        /// <summary>Builds the DAG.</summary>
        public PipelineDag Build()
        {
            var dag = new PipelineDag();
            dag._nodes.AddRange(_nodes);
            return dag;
        }
    }
}
