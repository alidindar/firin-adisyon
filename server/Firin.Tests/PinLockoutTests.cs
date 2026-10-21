using Firin.Api.Auth;
using Microsoft.Extensions.Time.Testing;

namespace Firin.Tests;

public class PinLockoutTests
{
    [Fact]
    public void Fail_during_active_lock_does_not_shorten_or_clear_it()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.Zero));
        var lockout = new PinLockout(clock);
        for (var i = 0; i < PinLockout.MaxAttempts; i++) lockout.Fail(1);
        var before = lockout.RemainingLock(1);
        Assert.NotNull(before);

        lockout.Fail(1);

        Assert.Equal(before, lockout.RemainingLock(1));
    }
}
