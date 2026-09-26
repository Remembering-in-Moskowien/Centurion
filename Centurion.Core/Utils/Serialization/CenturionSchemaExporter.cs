using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Centurion.Models.Schema;

namespace Centurion.Core.Utils.Serialization;

/// <summary>
/// 从 <see cref="CenturionJsonContext"/> 的元数据导出 IR 的 JSON Schema（JSON Schema 2020-12 草案）。
/// 导出的 schema 提交到仓库 schemas/centurion-v1.json；结构变更后重新生成即可保持同步。
/// </summary>
public static class CenturionSchemaExporter
{
    /// <summary>导出当前版本（1.0）的完整 JSON Schema 文本。</summary>
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
