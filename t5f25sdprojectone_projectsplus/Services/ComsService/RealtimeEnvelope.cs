// src/ProjectsPlus.Comms/Realtime/RealtimeEnvelope.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public sealed class RealtimeEnvelope
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; } = Guid.NewGuid().ToString("D");

        [JsonPropertyName("type")]
        public string Type { get; set; } = "message"; // e.g., "message", "presence", "ack", "control"

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; } // connectionId, userId, roomId

        [JsonPropertyName("payload")]
        public IDictionary<string, object?>? Payload { get; set; }

        [JsonPropertyName("meta")]
        public IDictionary<string, object?>? Meta { get; set; }

        [JsonPropertyName("ts")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
