using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Firin.Tests;

public sealed class TestApp : IDisposable
{
    public string DataDir { get; } =
        Path.Combine(Path.GetTempPath(), "firin-test-" + Guid.NewGuid().ToString("N"));

    // 2026-10-21 09:00 İstanbul = 06:00 UTC
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.Zero));

    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; }

    public TestApp(Action<IServiceCollection>? configure = null, bool autoBackup = false)
    {
        // Gerçek wwwroot/index.html olmadan SPA fallback sınanamaz.
        var webRoot = Path.Combine(DataDir, "wwwroot");
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><title>test</title>");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Firin:DataDir", DataDir);
            b.UseSetting("Firin:AutoBackup", autoBackup ? "true" : "false");
            b.UseWebRoot(webRoot);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(Clock);
                configure?.Invoke(s);
            });
        });
        Client = Factory.CreateClient();
    }

    public HttpClient NewClient() => Factory.CreateClient();

    public static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public T Db<T>(Func<AppDb, T> work)
    {
        using var scope = Factory.Services.CreateScope();
        return work(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    public int SeedUser(string name, string pin, Role role) => Db(db =>
    {
        var hasher = Factory.Services.GetRequiredService<PinHasher>();
        var u = new User { Name = name, PinHash = hasher.Hash(pin), Role = role };
        db.Users.Add(u);
        db.SaveChanges();
        return u.Id;
    });

    public async Task<HttpClient> LoginAsync(int userId, string pin)
    {
        var c = NewClient();
        var res = await c.PostAsJsonAsync("/api/auth/login", new { userId, pin });
        res.EnsureSuccessStatusCode();
        return c;
    }

    /// <summary>Hub bağlantısı için oturum çerezini elle alır.</summary>
    public async Task<string> LoginCookieAsync(int userId, string pin)
    {
        using var c = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var res = await c.PostAsJsonAsync("/api/auth/login", new { userId, pin });
        res.EnsureSuccessStatusCode();
        return res.Headers.GetValues("Set-Cookie").First().Split(';')[0];
    }

    public async Task<HubConnection> HubAsync(string cookie)
    {
        var conn = new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "hubs/adisyon"), o =>
            {
                o.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.Headers["Cookie"] = cookie;
            })
            .Build();
        await conn.StartAsync();
        return conn;
    }

    public Task<HttpClient> OwnerAsync() => LoginAsync(SeedUser("Sahip", "1234", Role.Owner), "1234");
    public Task<HttpClient> StaffAsync(string name = "Garson") => LoginAsync(SeedUser(name, "5678", Role.Staff), "5678");

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDir, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

public static class HttpExt
{
    public static async Task<T> ReadAs<T>(this HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<T>(TestApp.Json))!;

    public static async Task<string?> ProblemDetail(this HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("title", out var c) ? c.GetString() : null;
    }

    public static async Task<string?> ProblemCode(this HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }
}
