using Amazon.Runtime;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus
{
    internal record AwsCredentials(string AccessKeyId, string SecretAccessKey);

    internal static class CredsReader
    {
        // Default path: <appdir>/credentials.csv (AWS console CSV export format)
        public static AWSCredentials ReadFromCsv(string? path = null)
        {
            string? projectRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.Parent?.Parent?.FullName;
            path ??= Path.Combine(projectRoot, "IaC_ProjectsPlus", "credentials.csv");
            if (!File.Exists(path))
                throw new FileNotFoundException("credentials.csv not found", path);

            var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (lines.Length < 2)
                throw new InvalidOperationException("credentials.csv must contain a header and one credential row.");

            var header = SplitCsv(lines[0]);
            var row = SplitCsv(lines[1]);

            int FindIndex(params string[] candidates)
            {
                for (int i = 0; i < header.Length; i++)
                {
                    var h = header[i].Trim().ToLowerInvariant();
                    foreach (var c in candidates)
                        if (h.Contains(c.ToLowerInvariant())) return i;
                }
                return -1;
            }

            var akIdx = FindIndex("access key id", "accesskeyid", "accesskey");
            var skIdx = FindIndex("secret access key", "secretaccesskey", "secretkey");
            if (akIdx < 0 || skIdx < 0)
                throw new InvalidOperationException("CSV header must contain Access key ID and Secret access key columns.");

            var access = row.Length > akIdx ? row[akIdx].Trim().Trim('"') : throw new InvalidOperationException("Access key missing in CSV.");
            var secret = row.Length > skIdx ? row[skIdx].Trim().Trim('"') : throw new InvalidOperationException("Secret key missing in CSV.");

            var _creds = new AwsCredentials(access, secret);
            return new BasicAWSCredentials(_creds.AccessKeyId, _creds.SecretAccessKey);
        }

        private static string[] SplitCsv(string line)
        {
            // Simple CSV split sufficient for AWS exported CSV (no embedded commas expected)
            return line.Split(',').Select(s => s.Trim()).ToArray();
        }
    }
}

