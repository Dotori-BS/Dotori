using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Dotori.Core.Generators;

/// <summary>
/// Generates portable CMake configure, build, and test presets for IDEs and CI.
/// </summary>
public static class CMakePresetsGenerator
{
    public static string Generate(string config, string targetId)
    {
        var configurations = config == "both"
            ? new[] { "debug", "release" }
            : new[] { config };

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 6);
            writer.WriteStartObject("cmakeMinimumRequired");
            writer.WriteNumber("major", 3);
            writer.WriteNumber("minor", 21);
            writer.WriteNumber("patch", 0);
            writer.WriteEndObject();

            writer.WriteStartArray("configurePresets");
            foreach (var configuration in configurations)
            {
                var presetName = BuildPresetName(targetId, configuration);
                var displayConfiguration = DisplayConfiguration(configuration);
                writer.WriteStartObject();
                writer.WriteString("name", presetName);
                writer.WriteString("displayName", $"{targetId} {displayConfiguration}");
                writer.WriteString("generator", "Ninja");
                writer.WriteString("binaryDir", "${sourceDir}/build/cmake/${presetName}");
                writer.WriteStartObject("cacheVariables");
                writer.WriteString("CMAKE_BUILD_TYPE", displayConfiguration);
                writer.WriteBoolean("CMAKE_EXPORT_COMPILE_COMMANDS", true);
                writer.WriteEndObject();

                var architecture = ResolveArchitecture(targetId);
                if (architecture is not null)
                {
                    writer.WriteStartObject("architecture");
                    writer.WriteString("value", architecture);
                    writer.WriteString("strategy", "external");
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("buildPresets");
            foreach (var configuration in configurations)
            {
                var presetName = BuildPresetName(targetId, configuration);
                writer.WriteStartObject();
                writer.WriteString("name", presetName);
                writer.WriteString("configurePreset", presetName);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("testPresets");
            foreach (var configuration in configurations)
            {
                var presetName = BuildPresetName(targetId, configuration);
                writer.WriteStartObject();
                writer.WriteString("name", presetName);
                writer.WriteString("configurePreset", presetName);
                writer.WriteStartObject("output");
                writer.WriteBoolean("outputOnFailure", true);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + Environment.NewLine;
    }

    private static string BuildPresetName(string targetId, string configuration) =>
        $"{SanitizePresetName(targetId)}-{configuration}";

    private static string DisplayConfiguration(string configuration) =>
        configuration == "release" ? "Release" : "Debug";

    private static string SanitizePresetName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'
            ? char.ToLowerInvariant(ch)
            : '-');
        return string.Concat(chars);
    }

    private static string? ResolveArchitecture(string targetId)
    {
        if (targetId.EndsWith("-x64", StringComparison.OrdinalIgnoreCase)) return "x64";
        if (targetId.EndsWith("-x86", StringComparison.OrdinalIgnoreCase)) return "x86";
        if (targetId.EndsWith("-arm64", StringComparison.OrdinalIgnoreCase)) return "arm64";
        if (targetId.EndsWith("-arm", StringComparison.OrdinalIgnoreCase)) return "arm";
        return null;
    }
}
