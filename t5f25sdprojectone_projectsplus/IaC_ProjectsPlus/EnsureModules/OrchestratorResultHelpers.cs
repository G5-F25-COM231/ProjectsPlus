// src/IaC_ProjectsPlus/OrchestratorResultHelpers.cs
using System;
using System.Reflection;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus
{
    public static class OrchestratorResultHelpers
    {
        public static bool WasDestroyed(this object? result)
        {
            if (result == null) return false;
            try
            {
                var t = result.GetType();
                var p = t.GetProperty("Destroyed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (p != null && p.PropertyType == typeof(bool))
                {
                    var v = (bool?)p.GetValue(result);
                    if (v == true) return true;
                }

                var removedCountProp = t.GetProperty("RemovedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
                                     ?? t.GetProperty("Removed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (removedCountProp != null)
                {
                    var val = removedCountProp.GetValue(result);
                    if (val is int i && i > 0) return true;
                    if (val is long l && l > 0) return true;
                    if (val is System.Collections.ICollection c && c.Count > 0) return true;
                }

                var msgProp = t.GetProperty("Message", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (msgProp != null)
                {
                    var msg = msgProp.GetValue(result) as string;
                    if (!string.IsNullOrWhiteSpace(msg) && (msg.IndexOf("deleted", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("destroyed", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }
            }
            catch
            {
                // swallow and treat as not destroyed
            }
            return false;
        }

        public static bool WasNotFound(this object? result)
        {
            if (result == null) return true;
            try
            {
                var t = result.GetType();
                var p = t.GetProperty("NotFound", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (p != null && p.PropertyType == typeof(bool))
                {
                    var v = (bool?)p.GetValue(result);
                    if (v == true) return true;
                }

                var msgProp = t.GetProperty("Message", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (msgProp != null)
                {
                    var msg = msgProp.GetValue(result) as string;
                    if (!string.IsNullOrWhiteSpace(msg) && msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
