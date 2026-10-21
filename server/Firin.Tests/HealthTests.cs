using System.Net;

namespace Firin.Tests;

public sealed class HealthTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await _app.Client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("ok", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Data_dir_is_created()
    {
        Assert.True(Directory.Exists(Path.Combine(_app.DataDir, "photos")));
    }

    [Fact]
    public async Task Unknown_api_route_is_404_not_spa()
    {
        var res = await _app.Client.GetAsync("/api/olmayan-yol");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Spa_route_serves_index_html()
    {
        var res = await _app.Client.GetAsync("/masa/5");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<title>test</title>", await res.Content.ReadAsStringAsync());
    }
}
