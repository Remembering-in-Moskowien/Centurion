using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centurion.Models.Ass;

/// <summary>
/// <see cref="AssStyle"/> 的 System.Text.Json 转换器：保持与 Newtonsoft 契约一致的 JSON 字段名
/// （name/fontname/fontsize 等 Studio 前端 IAssStyle 契约），并支持主构造函数反序列化。
/// </summary>
public sealed class AssStyleJsonConverter : JsonConverter<AssStyle>
{
    /// <summary>按名称读取的可空占位，用于区分"字段缺失"与"默认值"。</summary>
    private static readonly float[] FloatDefaults =
    [
        100f, 100f, 0f, 0f, 0f, 0f, 0f
    ];

    /// <inheritdoc />
    public override AssStyle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("AssStyle must be a JSON object.");

        string name = string.Empty, fontName = string.Empty;
        int fontSize = 0;
        string primaryColour = string.Empty, secondaryColour = string.Empty,
               outlineColour = string.Empty, backColour = string.Empty;
        bool bold = false, italic = false, underline = false, strikeOut = false;
        float scaleX = 100f, scaleY = 100f, spacing = 0f, angle = 0f;
        int borderStyle = 1;
        float outline = 0f, shadow = 0f;
        int alignment = 2, marginL = 0, marginR = 0, marginV = 0, encoding = 1;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Unexpected token in AssStyle object.");

            var prop = reader.GetString();
            reader.Read();
            switch (prop)
            {
                case "name": name = reader.GetString() ?? string.Empty; break;
                case "fontname": fontName = reader.GetString() ?? string.Empty; break;
                case "fontsize": fontSize = reader.GetInt32(); break;
                case "primaryColour": primaryColour = reader.GetString() ?? string.Empty; break;
                case "secondaryColour": secondaryColour = reader.GetString() ?? string.Empty; break;
                case "outlineColour": outlineColour = reader.GetString() ?? string.Empty; break;
                case "backColour": backColour = reader.GetString() ?? string.Empty; break;
                case "bold": bold = reader.GetBoolean(); break;
                case "italic": italic = reader.GetBoolean(); break;
                case "underline": underline = reader.GetBoolean(); break;
                case "strikeOut": strikeOut = reader.GetBoolean(); break;
                case "scaleX": scaleX = reader.GetSingle(); break;
                case "scaleY": scaleY = reader.GetSingle(); break;
                case "spacing": spacing = reader.GetSingle(); break;
                case "angle": angle = reader.GetSingle(); break;
                case "borderStyle": borderStyle = reader.GetInt32(); break;
                case "outline": outline = reader.GetSingle(); break;
                case "shadow": shadow = reader.GetSingle(); break;
                case "alignment": alignment = reader.GetInt32(); break;
                case "marginL": marginL = reader.GetInt32(); break;
                case "marginR": marginR = reader.GetInt32(); break;
                case "marginV": marginV = reader.GetInt32(); break;
                case "encoding": encoding = reader.GetInt32(); break;
                default: reader.Skip(); break;
            }
        }

        return new AssStyle(
            name, fontName, fontSize,
            primaryColour, secondaryColour, outlineColour, backColour,
            bold, italic, underline, strikeOut,
            scaleX, scaleY, spacing, angle,
            borderStyle, outline, shadow,
            alignment, marginL, marginR, marginV,
            encoding);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AssStyle value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        writer.WriteString("fontname", value.FontName);
        writer.WriteNumber("fontsize", value.FontSize);
        writer.WriteString("primaryColour", value.PrimaryColour);
        writer.WriteString("secondaryColour", value.SecondaryColour);
        writer.WriteString("outlineColour", value.OutlineColour);
        writer.WriteString("backColour", value.BackColour);
        writer.WriteBoolean("bold", value.Bold);
        writer.WriteBoolean("italic", value.Italic);
        writer.WriteBoolean("underline", value.Underline);
        writer.WriteBoolean("strikeOut", value.StrikeOut);
        writer.WriteNumber("scaleX", value.ScaleX);
        writer.WriteNumber("scaleY", value.ScaleY);
        writer.WriteNumber("spacing", value.Spacing);
        writer.WriteNumber("angle", value.Angle);
        writer.WriteNumber("borderStyle", value.BorderStyle);
        writer.WriteNumber("outline", value.Outline);
        writer.WriteNumber("shadow", value.Shadow);
        writer.WriteNumber("alignment", value.Alignment);
        writer.WriteNumber("marginL", value.MarginL);
        writer.WriteNumber("marginR", value.MarginR);
        writer.WriteNumber("marginV", value.MarginV);
        writer.WriteNumber("encoding", value.Encoding);
        writer.WriteEndObject();
    }
}
