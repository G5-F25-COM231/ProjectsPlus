using System;

namespace t5f25sdprojectone_projectsplus.Commons
{
    public static class IsoDateHelper
    {
        public static string UtcNowIso() => DateTimeOffset.UtcNow.ToString("o");
        public static DateTimeOffset ParseUtc(string iso) => DateTimeOffset.Parse(iso);
    }
}
