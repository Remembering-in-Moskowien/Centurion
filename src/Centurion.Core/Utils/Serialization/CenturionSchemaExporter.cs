using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Centurion.Models.Schema;

namespace Centurion.Core.Utils.Serialization;

/// <summary>
/// Exports the IR's JSON Schema (JSON Schema 2020-12 draft) from <see cref="CenturionJsonContext"/>'s
/// metadata. The exported schema is committed to the repository at schemas/centurion-v1.json;
/// just regenerate it after a structural change to stay in sync.
/// </summary>
public static class CenturionSchemaExporter
{
    /// <summary>Exports the complete JSON Schema text for the current version (1.0).</summary>
    public static string ExportV1()
    {
        var options = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (ctx, schema) => schema
        };

        var typeInfo = CenturionJsonContext.Default.CenturionDocument;
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(typeInfo, options);
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        schema["title"] = "Centurion intermediate file (v1.0)";
        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
