using Firin.Api.Auth;
using Firin.Api.Data;

namespace Firin.Api.Endpoints;

public static class PhotoEndpoints
{
    public const long MaxBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    public static void MapPhotoEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/admin/products/{id:int}/photo").RequireAuthorization(AuthSetup.OwnerPolicy);

        // Cookie SameSite=Strict olduğu için antiforgery token'a gerek yok.
        g.MapPost("/", async (int id, IFormFile file, AppDb db, FirinPaths paths) =>
        {
            var p = await db.Products.FindAsync(id) ?? throw Validate.NotFound();
            if (!Extensions.TryGetValue(file.ContentType, out var ext))
                throw new DomainException("foto_tur", "Fotoğraf JPEG, PNG ya da WebP olmalı.");
            if (file.Length is 0 or > MaxBytes)
                throw new DomainException("foto_boyut", "Fotoğraf en fazla 2 MB olabilir.");

            var name = $"{p.Id}-{Guid.NewGuid():N}{ext}";
            await using (var fs = File.Create(Path.Combine(paths.PhotosDir, name)))
                await file.CopyToAsync(fs);

            var old = p.PhotoFile;
            p.PhotoFile = name;
            await db.SaveChangesAsync();
            DeleteFile(paths, old);
            return p.ToDto();
        }).DisableAntiforgery();

        g.MapDelete("/", async (int id, AppDb db, FirinPaths paths) =>
        {
            var p = await db.Products.FindAsync(id) ?? throw Validate.NotFound();
            var old = p.PhotoFile;
            p.PhotoFile = null;
            await db.SaveChangesAsync();
            DeleteFile(paths, old);
            return p.ToDto();
        });
    }

    private static void DeleteFile(FirinPaths paths, string? name)
    {
        if (name is null) return;
        try { File.Delete(Path.Combine(paths.PhotosDir, name)); } catch (IOException) { }
    }
}
