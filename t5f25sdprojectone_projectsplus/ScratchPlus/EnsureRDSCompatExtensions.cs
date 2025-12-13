// src/IaC_ProjectsPlus/EnsureRdsCompatExtensions.cs
//
// Compatibility helpers for EnsureRDS.
// - If your EnsureRDS implementation lacks EnsureExistsAsync / EnsureDestroyAsync (older/newer file split),
//   these extension methods attempt to call same-named methods via reflection if present on the type.
// - If not present, they throw a descriptive MissingMethodException so the compiler/runtime error is clearer.
//
// Add this file to silence compilation errors and provide a clear runtime failure if the real methods are still missing.

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;

namespace t5f25sdprojectone_projectsplus.ScratchPlus
{
    public static class EnsureRDSCompatExtensions
    {
        // Try to invoke EnsureExistsAsync on the concrete EnsureRDS instance via reflection.
        // Expected signature on concrete class: Task<EnsureRdsExistsSummary> EnsureExistsAsync(string? idOrName = null, CancellationToken ct = default)
        public static Task<object?> EnsureExistsAsync(this EnsureRDS ensure, string? idOrName = null, CancellationToken ct = default)
        {
            if (ensure == null) throw new ArgumentNullException(nameof(ensure));
            var method = ensure.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => string.Equals(m.Name, "EnsureExistsAsync", StringComparison.Ordinal));
            if (method == null)
                throw new MissingMethodException("EnsureRDS does not implement EnsureExistsAsync(string? idOrName, CancellationToken). Add a method with that signature to Enable orchestration.");

            var parameters = method.GetParameters();
            object?[] args;
            if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(CancellationToken))
                args = new object?[] { idOrName, ct };
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(CancellationToken))
                args = new object?[] { ct };
            else if (parameters.Length == 0)
                args = Array.Empty<object?>();
            else
                args = new object?[] { idOrName, ct };

            var result = method.Invoke(ensure, args);
            if (result is Task t)
                return CastTaskToObjectAsync(t);

            throw new InvalidOperationException("EnsureExistsAsync reflected invocation did not return a Task.");
        }

        // Try to invoke EnsureDestroyAsync on the concrete EnsureRDS instance via reflection.
        // Expected signature: Task<EnsureRdsDestroyResult> EnsureDestroyAsync(string idOrName, bool skipFinalSnapshot = true, CancellationToken ct = default)
        public static Task<object?> EnsureDestroyAsync(this EnsureRDS ensure, string idOrName, bool skipFinalSnapshot = false, CancellationToken ct = default)
        {
            if (ensure == null) throw new ArgumentNullException(nameof(ensure));
            var method = ensure.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => string.Equals(m.Name, "EnsureDestroyAsync", StringComparison.Ordinal));
            if (method == null)
                throw new MissingMethodException("EnsureRDS does not implement EnsureDestroyAsync(string idOrName, bool skipFinalSnapshot, CancellationToken). Add a method with that signature to enable orchestration.");

            var parameters = method.GetParameters();

            object?[] args;
            if (parameters.Length == 3 &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType == typeof(bool) &&
                parameters[2].ParameterType == typeof(CancellationToken))
            {
                args = new object?[] { idOrName, skipFinalSnapshot, ct };
            }
            else if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(CancellationToken))
            {
                args = new object?[] { idOrName, ct };
            }
            else
            {
                // fallback: attempt to pass only idOrName
                args = new object?[] { idOrName };
            }

            var result = method.Invoke(ensure, args);
            if (result is Task t)
                return CastTaskToObjectAsync(t);

            throw new InvalidOperationException("EnsureDestroyAsync reflected invocation did not return a Task.");
        }

        // helper to convert any Task<T> to Task<object?>
        private static async Task<object?> CastTaskToObjectAsync(Task task)
        {
            await task.ConfigureAwait(false);
            var taskType = task.GetType();
            if (taskType.IsGenericType)
            {
                var resultProperty = taskType.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
                return resultProperty?.GetValue(task);
            }
            return null;
        }
    }
}
