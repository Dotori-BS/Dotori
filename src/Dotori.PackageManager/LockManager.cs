using System.Text;
using System.Text.Json;
using Dotori.Core;

namespace Dotori.PackageManager;

/// <summary>
/// Reads and writes dotori-lock.json (v2).
/// Automatically upgrades legacy .dotori.lock (v1) on load.
/// </summary>
public static class LockManager
{
    /// <summary>Load lock file from project directory. Returns empty if not found.</summary>
    public static LockFile Load(string projectDir)
    {
        var jsonPath = Path.Combine(projectDir, DotoriConstants.LockJsonFileName);
        if (File.Exists(jsonPath))
        {
            var json = File.ReadAllText(jsonPath, Encoding.UTF8);
            return JsonSerializer.Deserialize(json, LockJsonContext.Default.LockFile) ?? new LockFile();
        }

        // Auto-upgrade legacy v1 TOML lock file
        var legacyPath = Path.Combine(projectDir, DotoriConstants.LockFileName);
        if (File.Exists(legacyPath))
            return MigrateFromLegacy(File.ReadAllText(legacyPath, Encoding.UTF8));

        return new LockFile();
    }

    /// <summary>Write lock file to project directory.</summary>
    public static void Save(LockFile lockFile, string projectDir)
    {
        var path = Path.Combine(projectDir, DotoriConstants.LockJsonFileName);
        var json = JsonSerializer.Serialize(lockFile, LockJsonContext.Default.LockFile);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    // ─── Legacy migration ────────────────────────────────────────────────────

    internal static LockFile MigrateFromLegacy(string tomlText)
    {
        var result = new LockFile();
        string? name = null, version = null, source = null, hash = null;
        var deps = new List<string>();

        void Flush()
        {
            if (name is null || version is null || source is null) return;
            var key = $"{name}@{version}";
            var resolved = source;
            // Convert legacy source format to resolved URL
            // "git+https://..." stays as-is; "path:..." stays; "version:name@ver" → keep as-is
            var entry = new LockEntry
            {
                Version  = version,
                Resolved = resolved,
                Integrity = hash,
                Link     = resolved.StartsWith("path:", StringComparison.Ordinal),
                Dependencies = deps
                    .Select(d =>
                    {
                        var at = d.LastIndexOf('@');
                        return at > 0
                            ? (pkg: d[..at], ver: d[(at + 1)..])
                            : (pkg: d, ver: "*");
                    })
                    .ToDictionary(t => t.pkg, t => t.ver),
            };
            result.Packages[key] = entry;
            name = version = source = hash = null;
            deps.Clear();
        }

        foreach (var rawLine in tomlText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            if (line == "[[package]]") { Flush(); continue; }

            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var k = line[..eq].Trim();
            var v = Unquote(line[(eq + 1)..].Trim());

            switch (k)
            {
                case "name":    name    = v; break;
                case "version": version = v; break;
                case "source":  source  = v; break;
                case "hash":    hash    = v; break;
                case "deps":
                    var arr = v.TrimStart('[').TrimEnd(']');
                    foreach (var part in arr.Split(','))
                    {
                        var s = Unquote(part.Trim());
                        if (!string.IsNullOrEmpty(s)) deps.Add(s);
                    }
                    break;
            }
        }
        Flush();
        return result;
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
    }
}
