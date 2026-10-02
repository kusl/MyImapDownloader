using System.Text.Json.Serialization;

namespace MyEmailSearch.Data;

[JsonSerializable(typeof(List<string>))]
internal partial class EmailDocumentJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SearchResultSet))]
internal partial class SearchOutputJsonContext : JsonSerializerContext
{
}
