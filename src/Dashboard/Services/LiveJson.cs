using System.Text.Json;

namespace RRSOS.PCC.Dashboard
{
    /// <summary>How both of the plugin's files are read: names in any case, and odd numbers tolerated (see TolerantNumbers.cs).</summary>
    public static class LiveJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new TolerantDoubleConverter(), new TolerantNullableDoubleConverter(), new TolerantIntConverter() }
        };
    }
}
