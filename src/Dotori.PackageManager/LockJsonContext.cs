using System.Text.Json.Serialization;

namespace Dotori.PackageManager;

[JsonSerializable(typeof(LockFile))]
[JsonSerializable(typeof(LockEntry))]
[JsonSerializable(typeof(Dictionary<string, LockEntry>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class LockJsonContext : JsonSerializerContext;
