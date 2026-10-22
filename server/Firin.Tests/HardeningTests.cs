using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Firin.Tests;

public sealed class HardeningTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Database_uses_wal_journal()
    {
        await _app.Client.GetAsync("/api/health");
        await using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(_app.DataDir, "firin.db"), Pooling = false }.ToString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", (string?)await cmd.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"name\":5}")]
    public async Task Malformed_json_gives_coded_400(string body)
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PostAsync("/api/admin/categories", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("istek_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public void Bad_time_zone_fails_at_startup()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var f = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Firin:DataDir", Path.Combine(_app.DataDir, "bad-tz"));
                b.UseSetting("Firin:TimeZone", "Yok/Boyle");
                b.UseSetting("Firin:AutoBackup", "false");
            });
            f.CreateClient();
        });
        Assert.Contains("Yok/Boyle", ex.ToString());
    }
}
