// tests/Infrastructure/SqlCrashRecoveryTests.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Tests that exercise crash-recovery and retry/idempotency semantics against the
    /// in-process test double (SqlInProcessRedis). These tests simulate the kinds of
    /// races and retries that production SQL-backed components must tolerate:
    ///  - idempotent enqueue / forward operations
    ///  - concurrent idempotency marker creation
    ///  - concurrent attempt increments that should only produce a single dead-letter
    ///  - retrying operations is safe and deterministic
    /// </summary>
    public class SqlCrashRecoveryTests : IDisposable
    {
        private readonly SqlInProcessRedis _store;

        public SqlCrashRecoveryTests()
        {
            _store = new SqlInProcessRedis();
        }

        public void Dispose()
        {
            _store.Dispose();
        }

        [Fact(DisplayName = "Enqueue retry is idempotent when idempotency key provided")]
        public async Task Enqueue_Retry_IsIdempotent()
        {
            var channel = "crash-room";
            var messageId = "retry-msg";
            var payload = "payload";
            var idemKey = "idem-retry-1";

            // First enqueue
            var id1 = await _store.EnqueueMessageAsync(channel, messageId, payload, idempotencyKey: idemKey, retention: null);
            Assert.False(string.IsNullOrWhiteSpace(id1));

            // Retry enqueue with same idempotency key (simulating client retry after crash)
            var id2 = await _store.EnqueueMessageAsync(channel, messageId, payload, idempotencyKey: idemKey, retention: null);
            Assert.Equal(id1, id2);

            // Ensure only one message exists in pending
            var pending = await _store.QueryPendingMessagesAsync(limit: 100);
            var matches = pending.Where(m => m.MessageId == messageId && m.Channel == channel).ToList();
            Assert.Single(matches);
        }

        [Fact(DisplayName = "Concurrent idempotency marker creation yields single winner")]
        public async Task Concurrent_IdempotencyMarker_CreatesSingleWinner()
        {
            var key = "concurrent-idem";
            var attempts = 16;
            var createdResults = new ConcurrentBag<(bool Created, string? Existing, string MessageId)>();
            var tasks = new List<Task>();

            for (var i = 0; i < attempts; i++)
            {
                var msgId = $"m-{i}";
                tasks.Add(Task.Run(async () =>
                {
                    var res = await _store.TryCreateIdempotencyMarkerAsync(key, msgId, TimeSpan.FromMinutes(5));
                    createdResults.Add((res.Created, res.ExistingMessageId, msgId));
                }));
            }

            await Task.WhenAll(tasks);

            // Exactly one of the callers should have Created == true for its message id (the winner).
            var winners = createdResults.Where(r => r.Created).ToList();
            Assert.NotEmpty(winners);
            Assert.Single(winners);

            var winnerMessageId = winners.Single().MessageId;

            // All other callers should have Created == false and ExistingMessageId equal to the winner
            var losers = createdResults.Where(r => !r.Created).ToList();
            Assert.All(losers, l => Assert.Equal(winnerMessageId, l.Existing));

            // TryGet should return the winner message id
            var got = await _store.TryGetIdempotencyMessageIdAsync(key);
            Assert.Equal(winnerMessageId, got);
        }

        [Fact(DisplayName = "Concurrent attempt increments produce a single dead-letter when threshold reached")]
        public async Task Concurrent_IncrementAttempts_OnlyOneDeadLetter()
        {
            var channel = "crash-attempts";
            var messageId = "flaky";
            var payload = "x";
            var id = await _store.EnqueueMessageAsync(channel, messageId, payload);
            Assert.False(string.IsNullOrWhiteSpace(id));

            var maxAttempts = 5;
            var concurrentWorkers = 8;
            var tasks = new List<Task>();

            // Each worker will repeatedly call IncrementAttemptsAsync until the message disappears (moved to dead-letter)
            var movedToDl = new ConcurrentBag<bool>();
            for (var w = 0; w < concurrentWorkers; w++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    while (true)
                    {
                        var attempts = await _store.IncrementAttemptsAsync(id, maxAttempts, failureReason: "simulated");
                        if (attempts == null)
                        {
                            // message no longer exists (likely moved to dead-letter)
                            movedToDl.Add(true);
                            break;
                        }

                        if (attempts >= maxAttempts)
                        {
                            // threshold reached by this caller; after this call message should be moved
                            movedToDl.Add(true);
                            break;
                        }

                        // small jitter to increase interleaving
                        await Task.Delay(5);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Ensure message is removed from messages store
            var msg = await _store.TryGetMessageAsync(id);
            Assert.Null(msg);

            // Exactly one dead-letter entry for this original message id should exist
            var dls = await _store.QueryDeadLettersAsync(limit: 100);
            var matches = dls.Where(dl => dl.OriginalMessageId == messageId).ToList();
            Assert.Single(matches);

            // At least one worker observed the move-to-dead-letter
            Assert.NotEmpty(movedToDl);
        }

        [Fact(DisplayName = "Forward enqueue is idempotent when retried")]
        public async Task Forward_Enqueue_Retry_IsIdempotent()
        {
            var channel = "fwd-room";
            var messageId = "fwd-msg";
            var payload = "payload-fwd";
            var message = new PatchMessageEntity
            {
                Id = $"msg:{channel}:{messageId}",
                Channel = channel,
                MessageId = messageId,
                Payload = payload,
                CreatedAtUtcTicks = DateTime.UtcNow.Ticks,
                Attempts = 0,
                TtlUnixSeconds = null,
                Status = null
            };

            var target = "remote-1";

            // First enqueue
            var f1 = await _store.EnqueueForwardAsync(message, target, idempotencyKey: null, retention: null);
            Assert.False(string.IsNullOrWhiteSpace(f1));

            // Retry enqueue (simulating crash/retry)
            var f2 = await _store.EnqueueForwardAsync(message, target, idempotencyKey: null, retention: null);
            Assert.Equal(f1, f2);

            // Ensure only one forward exists for the target/message
            var pending = await _store.QueryPendingForwardsAsync(target, limit: 100);
            var matches = pending.Where(p => p.Id == f1).ToList();
            Assert.Single(matches);
        }
    }
}
