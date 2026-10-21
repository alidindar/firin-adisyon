using Microsoft.AspNetCore.Hosting;
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

    public TestApp()
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Firin:DataDir", DataDir);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(Clock);
            });
        });
        Client = Factory.CreateClient();
    }

    public HttpClient NewClient() => Factory.CreateClient();

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDir, recursive: true); } catch (IOException) { }
    }
}
