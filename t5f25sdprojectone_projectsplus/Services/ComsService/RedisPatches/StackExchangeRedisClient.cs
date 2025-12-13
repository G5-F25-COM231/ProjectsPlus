using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches;

public sealed class StackExchangeRedisClient : IRedisClient, IDisposable
{
    private readonly IConnectionMultiplexer _mux;
    private readonly IDatabase _db;
    private bool _disposed;

    public StackExchangeRedisClient(IConnectionMultiplexer mux)
    {
        _mux = mux ?? throw new ArgumentNullException(nameof(mux));
        _db = _mux.GetDatabase();
    }

    public Task<bool> StringSetAsync(string key, string value, CancellationToken ct = default)
    {
        // StackExchange.Redis returns Task<bool>
        return _db.StringSetAsync(key, value);
    }

    public async Task<string?> StringGetAsync(string key, CancellationToken ct = default)
    {
        var v = await _db.StringGetAsync(key).ConfigureAwait(false);
        return v.IsNull ? null : v.ToString();
    }

    public Task<bool> KeyDeleteAsync(string key, CancellationToken ct = default)
    {
        return _db.KeyDeleteAsync(key);
    }

    public async Task<long> SetAddAsync(string setKey, string member, CancellationToken ct = default)
    {
        var added = await _db.SetAddAsync(setKey, member).ConfigureAwait(false);
        return added ? 1L : 0L;
    }

    public async Task<long> SetRemoveAsync(string setKey, string member, CancellationToken ct = default)
    {
        var removed = await _db.SetRemoveAsync(setKey, member).ConfigureAwait(false);
        return removed ? 1L : 0L;
    }

    public async Task<string[]> SetMembersAsync(string setKey, CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync(setKey).ConfigureAwait(false);
        return members.Select(x => x.ToString()).ToArray();
    }

    public Task<bool> SetContainsAsync(string setKey, string member, CancellationToken ct = default)
    {
        return _db.SetContainsAsync(setKey, member);
    }

    public Task<long> PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        // ISubscriber.PublishAsync returns Task<long>
        return _mux.GetSubscriber().PublishAsync(channel, message);
    }

    public IDisposable Subscribe(string channel, Action<string> handler)
    {
        var sub = _mux.GetSubscriber();
        Action<RedisChannel, RedisValue> wrapper = (_, val) =>
        {
            try { handler(val); } catch { /* swallow to match adapter behavior */ }
        };

        sub.Subscribe(channel, wrapper);

        return new Unsubscriber(sub, channel, wrapper);
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly ISubscriber _sub;
        private readonly RedisChannel _channel;
        private readonly Action<RedisChannel, RedisValue> _handler;
        private bool _disposed;

        public Unsubscriber(ISubscriber sub, string channel, Action<RedisChannel, RedisValue> handler)
        {
            _sub = sub;
            _channel = channel;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _sub.Unsubscribe(_channel, _handler); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _mux.Dispose(); } catch { /* ignore */ }
    }
}
