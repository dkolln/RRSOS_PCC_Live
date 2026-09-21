using System.Text.Json;
using System.Text.Json.Serialization;

namespace RRSOS.PCC.Dashboard
{
    // The plugin writes null for a number it could not read (NaN is not valid JSON). One odd value must never make
    // the whole reading unusable, so numbers are read leniently: null and text become 0 (or null for optional ones).

    public sealed class TolerantDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.GetDouble();
                case JsonTokenType.String when double.TryParse(reader.GetString(),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
                default:
                    reader.Skip();
                    return 0;
            }
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    public sealed class TolerantNullableDoubleConverter : JsonConverter<double?>
    {
        public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.GetDouble();
                case JsonTokenType.String when double.TryParse(reader.GetString(),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteNumberValue(value.Value); else writer.WriteNullValue();
        }
    }

    public sealed class TolerantIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number when reader.TryGetInt32(out var whole):
                    return whole;
                case JsonTokenType.Number:
                    return (int)Math.Round(reader.GetDouble());
                default:
                    reader.Skip();
                    return 0;
            }
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }
}
