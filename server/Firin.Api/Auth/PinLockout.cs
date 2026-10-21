using System.Collections.Concurrent;

namespace Firin.Api.Auth;

/// <summary>5 yanlış PIN'den sonra kullanıcıyı 1 dakika kilitler. Bellekte tutulur; yeniden başlatma sıfırlar.</summary>
public sealed class PinLockout(TimeProvider clock)
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(1);

    private sealed record State(int Fails, DateTimeOffset? LockedUntil);
    private readonly ConcurrentDictionary<int, State> _states = new();

    public TimeSpan? RemainingLock(int userId)
    {
        if (!_states.TryGetValue(userId, out var s) || s.LockedUntil is not { } until) return null;
        var left = until - clock.GetUtcNow();
        if (left > TimeSpan.Zero) return left;
        _states.TryRemove(userId, out _);
        return null;
    }

    public void Fail(int userId) => _states.AddOrUpdate(userId,
        _ => new State(1, null),
        (_, s) => s.LockedUntil > clock.GetUtcNow()
            ? s // aktif kilit hiçbir hatayla kısalmaz ya da silinmez
            : s.Fails + 1 >= MaxAttempts
            ? new State(0, clock.GetUtcNow() + LockDuration)
            : new State(s.Fails + 1, null));

    public void Success(int userId) => _states.TryRemove(userId, out _);

    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    /// <summary>Kullanıcı başına sıraya sokar: kilit kontrolü, PIN doğrulama ve Fail/Success tek parça çalışır.</summary>
    public async Task<IDisposable> AcquireAsync(int userId)
    {
        var gate = _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
