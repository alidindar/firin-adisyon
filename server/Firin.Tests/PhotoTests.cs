using System.Net;
using System.Net.Http.Headers;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.CatalogEndpoints;

namespace Firin.Tests;

public sealed class PhotoTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static readonly byte[] FakeJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    private static MultipartFormDataContent Upload(byte[] bytes, string contentType = "image/jpeg")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", "foto.jpg" } };
    }

    private async Task<(HttpClient owner, ProductDto product)> ProductAsync()
    {
        var owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(owner, "Pasta");
        return (owner, await Seed.ProductAsync(owner, cat.Id, "Yaş pasta", UnitType.Piece, 9000));
    }

    private string PhotoPath(string url) => Path.Combine(_app.DataDir, "photos", url["/photos/".Length..]);

    [Fact]
    public async Task Upload_saves_file_and_serves_it()
    {
        var (owner, p) = await ProductAsync();
        var res = await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var dto = await res.ReadAs<ProductDto>();
        Assert.StartsWith("/photos/", dto.PhotoUrl);
        Assert.EndsWith(".jpg", dto.PhotoUrl);
        Assert.True(File.Exists(PhotoPath(dto.PhotoUrl!)));

        var served = await _app.Client.GetAsync(dto.PhotoUrl);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(FakeJpeg, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Replacing_photo_deletes_old_file()
    {
        var (owner, p) = await ProductAsync();
        var first = await (await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg))).ReadAs<ProductDto>();
        var second = await (await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg, "image/png"))).ReadAs<ProductDto>();

        Assert.NotEqual(first.PhotoUrl, second.PhotoUrl);
        Assert.EndsWith(".png", second.PhotoUrl);
        Assert.False(File.Exists(PhotoPath(first.PhotoUrl!)));
        Assert.True(File.Exists(PhotoPath(second.PhotoUrl!)));
    }

    [Fact]
    public async Task Delete_photo_removes_file()
    {
        var (owner, p) = await ProductAsync();
        var up = await (await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg))).ReadAs<ProductDto>();
        var del = await (await owner.DeleteAsync($"/api/admin/products/{p.Id}/photo")).ReadAs<ProductDto>();
        Assert.Null(del.PhotoUrl);
        Assert.False(File.Exists(PhotoPath(up.PhotoUrl!)));
    }

    [Fact]
    public async Task Wrong_type_is_rejected()
    {
        var (owner, p) = await ProductAsync();
        var res = await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg, "text/plain"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("foto_tur", await res.ProblemCode());
    }

    [Fact]
    public async Task Too_big_is_rejected()
    {
        var (owner, p) = await ProductAsync();
        var res = await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(new byte[2 * 1024 * 1024 + 1]));
        Assert.Equal("foto_boyut", await res.ProblemCode());
    }

    [Fact]
    public async Task Staff_cannot_upload()
    {
        var (_, p) = await ProductAsync();
        var staff = await _app.StaffAsync();
        var res = await staff.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
