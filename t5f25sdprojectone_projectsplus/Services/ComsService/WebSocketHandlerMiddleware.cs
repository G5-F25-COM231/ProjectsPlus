// src/ProjectsPlus.Comms/Realtime/WebSocketHandlerMiddleware.cs
using System;
using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public sealed class WebSocketHandlerOptions
    {
        public PathString Path { get; init; } = "/ws";
        public int ReceiveBufferSize { get; init; } = 4 * 1024;
        public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(30);
        // Authentication delegate: returns userId if authenticated, otherwise null
        public Func<HttpContext, Task<Guid?>>? AuthenticateAsync { get; init; }
    }

    public class WebSocketHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly WebSocketHandlerOptions _opts;
        private readonly IConnectionManager _connections;
        private readonly ILogger<WebSocketHandlerMiddleware> _logger;
        private readonly IMessageCenter? _messageCenter;
        private readonly IChatroomService? _chatroomService;

        public WebSocketHandlerMiddleware(RequestDelegate next,
            WebSocketHandlerOptions opts,
            IConnectionManager connections,
            ILogger<WebSocketHandlerMiddleware> logger,
            IMessageCenter? messageCenter = null,
            IChatroomService? chatroomService = null)
        {
            _next = next;
            _opts = opts ?? new WebSocketHandlerOptions();
            _connections = connections;
            _logger = logger;
            _messageCenter = messageCenter;
            _chatroomService = chatroomService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest || !context.Request.Path.StartsWithSegments(_opts.Path))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            // Authenticate (optional)
            Guid? userId = null;
            if (_opts.AuthenticateAsync != null)
            {
                try { userId = await _opts.AuthenticateAsync(context).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "Authentication delegate threw"); }
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            var connectionId = Guid.NewGuid().ToString("N");

            await _connections.RegisterAsync(connectionId, userId, socket).ConfigureAwait(false);
            _logger.LogInformation("WebSocket connected {ConnectionId} user={UserId}", connectionId, userId);

            try
            {
                await ReceiveLoopAsync(connectionId, socket, userId, context.RequestAborted).ConfigureAwait(false);
            }
            finally
            {
                await _connections.UnregisterAsync(connectionId).ConfigureAwait(false);
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None).ConfigureAwait(false); } catch { }
                _logger.LogInformation("WebSocket disconnected {ConnectionId}", connectionId);
            }
        }

        private async Task ReceiveLoopAsync(string connectionId, WebSocket socket, Guid? userId, CancellationToken ct)
        {
            var buffer = new byte[_opts.ReceiveBufferSize];
            var seg = new ArraySegment<byte>(buffer);

            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult? result = null;
                do
                {
                    result = await socket.ReceiveAsync(seg, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                ms.Position = 0;
                var text = Encoding.UTF8.GetString(ms.ToArray());
                RealtimeEnvelope? env = null;
                try
                {
                    env = JsonSerializer.Deserialize<RealtimeEnvelope>(text);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse envelope from {ConnectionId}", connectionId);
                    // optionally send error envelope back
                    var err = new RealtimeEnvelope { Type = "error", To = connectionId, Payload = new Dictionary<string, object?> { ["message"] = "invalid_envelope" } };
                    await _connections.SendToConnectionAsync(connectionId, err, ct).ConfigureAwait(false);
                    continue;
                }

                // Update presence last seen
                await _connections.SetPresenceAsync(connectionId, new PresenceState { Status = "online", LastSeenUtc = DateTime.UtcNow }).ConfigureAwait(false);

                // Route envelope by type
                await RouteEnvelopeAsync(connectionId, userId, env!, ct).ConfigureAwait(false);
            }
        }

        private async Task RouteEnvelopeAsync(string connectionId, Guid? userId, RealtimeEnvelope env, CancellationToken ct)
        {
            try
            {
                switch (env.Type?.ToLowerInvariant())
                {
                    case "ping":
                        var pong = new RealtimeEnvelope { Type = "pong", To = connectionId, Payload = new Dictionary<string, object?> { ["id"] = env.Id } };
                        await _connections.SendToConnectionAsync(connectionId, pong, ct).ConfigureAwait(false);
                        break;

                    case "presence":
                        // payload: { status: "away", meta: {...} }
                        var state = new PresenceState
                        {
                            Status = env.Payload != null && env.Payload.TryGetValue("status", out var s) ? s?.ToString() : "online",
                            Meta = env.Payload != null && env.Payload.TryGetValue("meta", out var m) && m is JsonElement je && je.ValueKind == JsonValueKind.Object
                                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText())
                                : env.Meta
                        };
                        await _connections.SetPresenceAsync(connectionId, state).ConfigureAwait(false);
                        break;

                    case "message":
                        // payload expected: { to: "<user|room|connection>", body: {...} }
                        // Forward to MessageCenter or ChatroomService
                        if (_messageCenter != null)
                        {
                            // Map envelope to NotificationDto or Message DTO as your MessageCenter expects
                            await _messageCenter.HandleRealtimeInboundAsync(env, connectionId, userId, ct).ConfigureAwait(false);
                        }
                        else if (_chatroomService != null)
                        {
                            await _chatroomService.HandleRealtimeMessageAsync(env, connectionId, userId, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            // default: echo back
                            var ack = new RealtimeEnvelope { Type = "ack", To = connectionId, Payload = new Dictionary<string, object?> { ["id"] = env.Id } };
                            await _connections.SendToConnectionAsync(connectionId, ack, ct).ConfigureAwait(false);
                        }
                        break;

                    case "subscribe":
                        // meta: { room: "roomId" }
                        if (_chatroomService != null && env.Payload != null && env.Payload.TryGetValue("room", out var roomObj) && roomObj is string roomId)
                        {
                            await _chatroomService.AddConnectionToRoomAsync(roomId, connectionId, userId).ConfigureAwait(false);
                        }
                        break;

                    case "unsubscribe":
                        if (_chatroomService != null && env.Payload != null && env.Payload.TryGetValue("room", out var roomObj2) && roomObj2 is string roomId2)
                        {
                            await _chatroomService.RemoveConnectionFromRoomAsync(roomId2, connectionId, userId).ConfigureAwait(false);
                        }
                        break;

                    default:
                        _logger.LogDebug("Unhandled envelope type {Type} from {ConnectionId}", env.Type, connectionId);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error routing envelope {EnvelopeId} from {ConnectionId}", env.Id, connectionId);
            }
        }
    }
}
