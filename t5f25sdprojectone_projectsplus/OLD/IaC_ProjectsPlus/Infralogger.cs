using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus
{
    public sealed class Infralogger
    {
        private readonly string _logPath;

        public Infralogger(string? logPath = null)
        {
            // default to local folder of executing assembly
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _logPath = string.IsNullOrWhiteSpace(logPath) ? Path.Combine(baseDir, "Infralogs.txt") : Path.GetFullPath(logPath);
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }


        public string LogPath => _logPath;

        public async Task AppendAsync(ResourceRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var line = record.ToString() + Environment.NewLine;
            await File.AppendAllTextAsync(_logPath, line);
        }

        public IReadOnlyList<ResourceRecord> ReadAll()
        {
            if (!File.Exists(_logPath)) return Array.Empty<ResourceRecord>();
            var lines = File.ReadAllLines(_logPath);
            var list = new List<ResourceRecord>();
            foreach (var l in lines)
            {
                if (ResourceRecord.TryParse(l, out var rec) && rec != null) list.Add(rec);
            }
            return list;
        }

        public async Task<bool> ExistsAsync(ResourceRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!File.Exists(_logPath)) return false;

            var lines = await File.ReadAllLinesAsync(_logPath);
            return lines.Any(l => ResourceRecord.TryParse(l, out var r) && r != null && r.Equals(record));
        }


        public void Clear()
        {
            if (File.Exists(_logPath)) File.Delete(_logPath);
        }
    }
}
