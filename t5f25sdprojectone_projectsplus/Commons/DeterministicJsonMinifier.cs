using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;

namespace t5f25sdprojectone_projectsplus.Commons
{
    public static class DeterministicJsonMinifier
    {
        public static string Minify(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "{}";

            JsonNode doc;
            try
            {
                doc = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                // If the input isn't valid JSON, treat as empty object to avoid throwing here
                return "{}";
            }

            if (doc is null) return "{}";

            var normalized = NormalizeNode(doc);
            var options = new JsonSerializerOptions { WriteIndented = false };
            return JsonSerializer.Serialize(normalized, options);
        }

        private static JsonNode NormalizeNode(JsonNode node)
        {
            if (node is JsonValue) return node.DeepClone();

            if (node is JsonArray arr)
            {
                var clone = new JsonArray();
                foreach (var el in arr)
                    clone.Add(NormalizeNode(el));
                return clone;
            }

            if (node is JsonObject obj)
            {
                var clone = new JsonObject();
                foreach (var kv in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    clone[kv.Key] = NormalizeNode(kv.Value);
                }
                return clone;
            }

            return node.DeepClone();
        }
    }
}
