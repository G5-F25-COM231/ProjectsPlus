using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.Commons
{
    public class ApiEnvelope<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("data")] public T? Data { get; set; }
        [JsonPropertyName("correlationId")] public string? CorrelationId { get; set; }

        public static ApiEnvelope<T> Ok(T data, string? correlationId = null)
            => new() { Success = true, Code = "ok", Message = null, Data = data, CorrelationId = correlationId };

        public static ApiEnvelope<T> Error(string code, string message, string? correlationId = null)
            => new() { Success = false, Code = code, Message = message, Data = default, CorrelationId = correlationId };
    }
}
