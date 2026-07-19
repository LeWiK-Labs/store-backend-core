using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeWiK.Store.Api.Common;

// Money columns are numeric(14,4), so decimals carry their scale (150000.0000).
// Serialize trimmed (150000) so clients get clean numbers. Applies to decimal? too.
public sealed class TrimmedDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteRawValue(value.ToString("0.####", CultureInfo.InvariantCulture));
}
