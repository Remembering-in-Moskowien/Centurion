using Centurion.Models;

namespace Centurion.Models.Workflow;

internal static class CorrectionMetadata
{
    public static Dictionary<Sentence, Dictionary<string, object>> Get(WorkflowState state)
    {
        if (state.Extensions.TryGetValue("CorrectionMetadata", out var value) &&
            value is Dictionary<Sentence, Dictionary<string, object>> metadata)
            return metadata;

        var created = new Dictionary<Sentence, Dictionary<string, object>>();
        state.Extensions["CorrectionMetadata"] = created;
        return created;
    }
}
