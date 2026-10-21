using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using Firin.Api.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _app.HubAsync(cookie: "firin_oturum=gecersiz"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
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
        await Task.Delay(300);
        Assert.Equal(0, count);

        await Seed.CategoryAsync(owner, "Börek");
        await got.Task.WaitAsync(Wait);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Failed_change_is_not_broadcast()
    {
        var id = _app.SeedUser("Sahip", "1234", Role.Owner);
        var owner = await _app.LoginAsync(id, "1234");
        await Seed.TableAsync(owner, "Masa 1");
        await using var hub = await _app.HubAsync(await _app.LoginCookieAsync(id, "1234"));
        var count = 0;
        hub.On("TablesChanged", () => Interlocked.Increment(ref count));

        var res = await owner.PostAsJsonAsync("/api/admin/tables", new { name = "Masa 1", sortOrder = 0, isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        await Task.Delay(300);
        Assert.Equal(0, count);
    }

    internal sealed class ThrowingRealtime : IRealtime
    {
        public Task TableChanged(int tableId) => throw new InvalidOperationException("yayın bozuk");
        public Task TablesChanged() => throw new InvalidOperationException("yayın bozuk");
        public Task CatalogChanged() => throw new InvalidOperationException("yayın bozuk");
    }

    [Fact]
    public async Task Broadcast_failure_does_not_fail_the_request()
    {
        var id = _app.SeedUser("Sahip", "1234", Role.Owner);
        using var factory = _app.Factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IRealtime>();
            s.AddSingleton<IRealtime, ThrowingRealtime>();
        }));
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "1234" })).EnsureSuccessStatusCode();

        var res = await client.PostAsJsonAsync("/api/admin/tables", new { name = "Masa 1", sortOrder = 0, isActive = true });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Single(_app.Db(db => db.Tables.ToList()));
    }
}
