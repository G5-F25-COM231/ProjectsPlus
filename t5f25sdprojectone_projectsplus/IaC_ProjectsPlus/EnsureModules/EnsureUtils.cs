using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public static class EnsureUtils
    {
        // canonical prefix (exact string you specified)
        public const string canonicalPrefix = "comp231003-91769-2025f-projectplus";

        /// <summary>
        /// normalizeName: trim, lowercase, replace non-alphanum/_/- with '-', collapse repeated '-' and trim.
        /// Returns a safe token suitable to append to canonical prefix or use as a logical fragment.
        /// When input is null/empty returns "default".
        /// </summary>
        public static string normalizeName(string? baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "default";
            var lowered = baseName.Trim().ToLowerInvariant();

            var sb = new StringBuilder(capacity: lowered.Length);
            foreach (var c in lowered)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else sb.Append('-');
            }

            var s = sb.ToString();
            while (s.Contains("--", StringComparison.Ordinal)) s = s.Replace("--", "-");
            s = s.Trim('-');
            if (s.Length == 0) s = "default";

            // keep safe length under common provider limits
            if (s.Length > 200) s = s.Substring(0, 200);
            return s;
        }

        /// <summary>
        /// buildCanonicalName: combine canonicalPrefix and a normalized fragment into a final canonical name:
        /// {canonicalPrefix}-{normalizedFragment}
        /// Ensures total length is within safe bounds.
        /// If fragment is null/empty, returns the canonicalPrefix itself.
        /// </summary>
        public static string buildCanonicalName(string? fragment)
        {
            var normalized = normalizeName(fragment ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized) || normalized == "default")
            {
                // return canonical prefix only when fragment absent/default to keep names concise
                return canonicalPrefix;
            }

            var candidate = $"{canonicalPrefix}-{normalized}";
            if (candidate.Length > 255) candidate = candidate.Substring(0, 255);
            return candidate;
        }

        /// <summary>
        /// makeResourceRecord: convenient factory for ResourceRecord entries created by Ensure modules.
        /// ensureIdentifier should be a short token like "EnsureDDB", "EnsureVPC", etc.
        /// </summary>
        public static ResourceRecord makeResourceRecord(string ensureIdentifier, string resourceType, string name, string id, string region)
        {
            return new ResourceRecord
            {
                EnsureIdentifier = ensureIdentifier ?? string.Empty,
                ResourceType = resourceType ?? string.Empty,
                Name = name ?? string.Empty,
                Id = id ?? string.Empty,
                Region = region ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Single-line, serializable resource record used by all Ensure modules.
    /// Includes EnsureIdentifier to allow readers to filter lines produced by a specific Ensure implementation.
    /// Format: key=value;key=value;...
    /// Keys (case-insensitive): EnsureIdentifier, ResourceType, Name, Id, Region, CreatedAt
    /// </summary>
    public sealed class ResourceRecord : IEquatable<ResourceRecord>
    {
        public string EnsureIdentifier { get; init; } = string.Empty; // e.g., "EnsureDDB"
        public string ResourceType { get; init; } = string.Empty;     // e.g., "DynamoDBTable"
        public string Name { get; init; } = string.Empty;             // logical name
        public string Id { get; init; } = string.Empty;               // arn or id
        public string Region { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        public override string ToString()
        {
            // Single-line ordered representation to keep deterministic ToString for hashing/tests.
            // Order: EnsureIdentifier;ResourceType;Name;Id;Region;CreatedAt
            return $"EnsureIdentifier={EnsureIdentifier};ResourceType={ResourceType};Name={Name};Id={Id};Region={Region};CreatedAt={CreatedAt.ToString("O", CultureInfo.InvariantCulture)}";
        }

        public static bool TryParse(string line, out ResourceRecord? rec)
        {
            rec = null;
            if (string.IsNullOrWhiteSpace(line)) return false;

            try
            {
                var parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in parts)
                {
                    var kv = p.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        dict[kv[0].Trim()] = kv[1].Trim();
                    }
                }

                if (dict.Count == 0) return false;

                dict.TryGetValue("EnsureIdentifier", out var ensureId);
                dict.TryGetValue("ResourceType", out var rtype);
                dict.TryGetValue("Name", out var name);
                dict.TryGetValue("Id", out var id);
                dict.TryGetValue("Region", out var region);
                dict.TryGetValue("CreatedAt", out var createdAtStr);

                DateTime createdAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(createdAtStr))
                {
                    if (!DateTime.TryParse(createdAtStr, null, DateTimeStyles.RoundtripKind, out createdAt))
                    {
                        createdAt = DateTime.UtcNow;
                    }
                }

                rec = new ResourceRecord
                {
                    EnsureIdentifier = ensureId ?? string.Empty,
                    ResourceType = rtype ?? string.Empty,
                    Name = name ?? string.Empty,
                    Id = id ?? string.Empty,
                    Region = region ?? string.Empty,
                    CreatedAt = createdAt
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Equals(ResourceRecord? other)
        {
            if (other == null) return false;
            return string.Equals(EnsureIdentifier, other.EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ResourceType, other.ResourceType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Region, other.Region, StringComparison.OrdinalIgnoreCase)
                && CreatedAt.ToUniversalTime() == other.CreatedAt.ToUniversalTime();
        }

        public override bool Equals(object? obj) => Equals(obj as ResourceRecord);
        public override int GetHashCode() => HashCode.Combine(EnsureIdentifier?.ToLowerInvariant(), ResourceType?.ToLowerInvariant(), Name?.ToLowerInvariant(), Id?.ToLowerInvariant(), Region?.ToLowerInvariant(), CreatedAt.ToUniversalTime());
    }

    /// <summary>
    /// Minimal Infralogger: file-backed append/read/exists/clear operations.
    /// - appendAsync writes a single-line ResourceRecord.ToString() and newline.
    /// - readAll parses existing lines via ResourceRecord.TryParse.
    /// - existsAsync returns true when an equal ResourceRecord is found.
    /// </summary>
    public sealed class Infralogger
    {
        private readonly string logPath;

        public Infralogger(string? path = null)
        {                    
            string relative_path = Path.Combine("../../../IaC_ProjectsPlus", "Infralogs.txt");
            logPath ??= Path.Combine(AppContext.BaseDirectory, relative_path);
            var dir = Path.GetDirectoryName(logPath);

            //var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            //logPath = string.IsNullOrWhiteSpace(path) ? Path.Combine(baseDir, "Infralogs.txt") : Path.GetFullPath(path);

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public string LogPath => logPath;

        public async Task appendAsync(ResourceRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var line = record.ToString() + Environment.NewLine;
            await File.AppendAllTextAsync(logPath, line, Encoding.UTF8).ConfigureAwait(false);
        }

        public IReadOnlyList<ResourceRecord> readAll()
        {
            if (!File.Exists(logPath)) return Array.Empty<ResourceRecord>();
            var lines = File.ReadAllLines(logPath, Encoding.UTF8);
            var list = new List<ResourceRecord>();
            foreach (var l in lines)
            {
                if (ResourceRecord.TryParse(l, out var rec) && rec != null) list.Add(rec);
            }
            return list;
        }

        public async Task<bool> existsAsync(ResourceRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!File.Exists(logPath)) return false;
            var lines = await File.ReadAllLinesAsync(logPath, Encoding.UTF8).ConfigureAwait(false);
            return lines.Any(l => ResourceRecord.TryParse(l, out var r) && r != null && r.Equals(record));
        }

        public void clear()
        {
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }
}


// src/IaC_ProjectsPlus/EnsureModules/EnsureUtils.cs
//
// Shared utilities for Ensure modules (name normalization, canonical naming, and infralog support).
// - normalizeName and buildCanonicalName return safe canonical defaults when input is empty.
// - ResourceRecord includes EnsureIdentifier so readers can filter records produced by a specific Ensure (e.g., "EnsureDDB").
// - makeResourceRecord factory and a small file-backed infralogger (append/read/exists/clear).
//
// NOTE: method names use camelCase per your preference for clarity (normalizeName, buildCanonicalName, makeResourceRecord).
//       Types remain PascalCase to remain idiomatic in C# while methods follow your requested style for clarity.
//
// Usage example:
//   var name = EnsureUtils.normalizeName(" Projects ");                // "projects"
//   var table = EnsureUtils.buildCanonicalName("ddb");                // "comp231003-91769-2025f-projectplus-ddb"
//   var rec = EnsureUtils.makeResourceRecord("EnsureDDB", "DynamoDBTable", table, tableArn, "us-east-2");
//   await logger.appendAsync(rec);
//