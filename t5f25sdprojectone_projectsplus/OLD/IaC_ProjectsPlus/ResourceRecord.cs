using System;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus
{
    // Single-line, serializable record for appinfralog.txt
    public sealed class ResourceRecord
    {
        public string ResourceType { get; init; } = default!; // e.g., S3Bucket, DynamoDBTable
        public string Name { get; init; } = default!;
        public string Id { get; init; } = default!; // e.g., ARN or resource id
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public string Region { get; init; } = "us-east-2";

        public override string ToString()
        {
            // single-line key=value pairs; easy parse later
            return $"ResourceType={ResourceType};Name={Name};Id={Id};Region={Region};CreatedAt={CreatedAt:O}";
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
                    if (kv.Length == 2) dict[kv[0].Trim()] = kv[1].Trim();
                }

                rec = new ResourceRecord
                {
                    ResourceType = dict.TryGetValue("ResourceType", out var rt) ? rt : "",
                    Name = dict.TryGetValue("Name", out var nm) ? nm : "",
                    Id = dict.TryGetValue("Id", out var id) ? id : "",
                    Region = dict.TryGetValue("Region", out var r) ? r : "us-east-2",
                    CreatedAt = dict.TryGetValue("CreatedAt", out var ca) && DateTime.TryParse(ca, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow
                };

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
