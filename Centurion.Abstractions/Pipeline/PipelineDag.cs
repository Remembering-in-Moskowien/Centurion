namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// DAG 管线定义：节点集合（节点内声明依赖、条件、重试与超时策略）。
/// 由 <see cref="PipelineDag.Builder"/> 构建，或由 <see cref="FromSequence"/> 从线性算子列表生成链式 DAG。
/// </summary>
public sealed class PipelineDag
{
    private readonly List<PipelineNode> _nodes = [];

    /// <summary>全部节点（按添加顺序）。</summary>
    public IReadOnlyList<PipelineNode> Nodes => _nodes;

    /// <summary>获取节点；不存在时返回 null。</summary>
    public PipelineNode? Find(string name) =>
        _nodes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal));

    /// <summary>创建一个 DAG 构建器。</summary>
    public static Builder CreateBuilder() => new();

    /// <summary>从线性算子列表生成链式 DAG（第 i 个节点依赖第 i-1 个），保持原串行语义。</summary>
    public static PipelineDag FromSequence(IEnumerable<IPipelineOperator> operators)
    {
        var builder = CreateBuilder();
        string? previous = null;
        var index = 0;
        foreach (var op in operators)
        {
            var name = op.Name;
            // 同名算子（如重复阶段）追加序号保证节点名唯一
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
    /// 校验 DAG 结构：节点名唯一、依赖存在、无环。
    /// 返回 null 表示合法；否则返回错误描述。
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

        // Kahn 环检测
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

    /// <summary>DAG 构建器：链式声明节点。</summary>
    public sealed class Builder
    {
        private readonly List<PipelineNode> _nodes = [];

        /// <summary>添加节点。</summary>
        public Builder Add(PipelineNode node)
        {
            _nodes.Add(node ?? throw new ArgumentNullException(nameof(node)));
            return this;
        }

        /// <summary>声明式添加节点：名称、算子、依赖、条件、重试、超时、降级。</summary>
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

        /// <summary>是否已包含同名节点。</summary>
        public bool Contains(string name) => _nodes.Any(n => string.Equals(n.Name, name, StringComparison.Ordinal));

        /// <summary>构建 DAG。</summary>
        public PipelineDag Build()
        {
            var dag = new PipelineDag();
            dag._nodes.AddRange(_nodes);
            return dag;
        }
    }
}
