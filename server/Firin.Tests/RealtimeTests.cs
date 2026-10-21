using Firin.Api.Domain;
using Microsoft.AspNetCore.SignalR.Client;

namespace Firin.Tests;

public sealed class RealtimeTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Hub_requires_login()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _app.HubAsync(cookie: ""));
    }

    [Fact]
    public async Task Table_admin_change_is_broadcast()
    {
        var id = _app.SeedUser("Sahip", "1234", Role.Owner);
        var owner = await _app.LoginAsync(id, "1234");
        await using var hub = await _app.HubAsync(await _app.LoginCookieAsync(id, "1234"));
        var got = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On("TablesChanged", () => got.TrySetResult());

        await Seed.TableAsync(owner, "Masa 1");
        await got.Task.WaitAsync(Wait);
    }

    [Fact]
    public async Task Catalog_change_is_broadcast_but_reads_are_not()
    {
        var id = _app.SeedUser("Sahip", "1234", Role.Owner);
        var owner = await _app.LoginAsync(id, "1234");
        await using var hub = await _app.HubAsync(await _app.LoginCookieAsync(id, "1234"));
        var count = 0;
        var got = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On("CatalogChanged", () => { Interlocked.Increment(ref count); got.TrySetResult(); });

        await owner.GetAsync("/api/admin/categories");
        await Seed.CategoryAsync(owner, "Börek");
        await got.Task.WaitAsync(Wait);
        Assert.Equal(1, count);
    }
}
