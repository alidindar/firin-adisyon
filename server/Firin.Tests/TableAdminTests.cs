using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.TableAdminEndpoints;

namespace Firin.Tests;

public sealed class TableAdminTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Owner_creates_and_lists_tables_in_order()
    {
        var owner = await _app.OwnerAsync();
        await Seed.TableAsync(owner, "Masa 2", sortOrder: 2);
        await Seed.TableAsync(owner, "Masa 1", sortOrder: 1);
        await Seed.TableAsync(owner, "Tezgâh", sortOrder: 0);

        var list = await (await owner.GetAsync("/api/admin/tables")).ReadAs<List<TableDto>>();
        Assert.Equal(new[] { "Tezgâh", "Masa 1", "Masa 2" }, list.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task Duplicate_table_name_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        await Seed.TableAsync(owner, "Masa 1");
        var res = await owner.PostAsJsonAsync("/api/admin/tables", new { name = " Masa 1 ", sortOrder = 0, isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("masa_adi_var", await res.ProblemCode());
    }

    [Fact]
    public async Task Table_with_open_order_cannot_be_deactivated()
    {
        var owner = await _app.OwnerAsync();
        var t = await Seed.TableAsync(owner, "Masa 1");
        _app.Db(db =>
        {
            db.Orders.Add(new Order { TableId = t.Id, OpenedById = db.Users.First().Id, OpenedAt = _app.Clock.GetUtcNow().UtcDateTime });
            return db.SaveChanges();
        });

        var res = await owner.PutAsJsonAsync($"/api/admin/tables/{t.Id}", new { name = "Masa 1", sortOrder = 0, isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("masa_acik", await res.ProblemCode());

        var rename = await owner.PutAsJsonAsync($"/api/admin/tables/{t.Id}", new { name = "Bahçe 1", sortOrder = 0, isActive = true });
        Assert.Equal("Bahçe 1", (await rename.ReadAs<TableDto>()).Name);
    }

    [Fact]
    public async Task Staff_cannot_manage_tables()
    {
        var staff = await _app.StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/tables")).StatusCode);
    }

    [Fact]
    public async Task Table_without_is_active_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PostAsJsonAsync("/api/admin/tables", new { name = "Masa 9", sortOrder = 0 });
        Assert.Equal("aktiflik_gecersiz", await res.ProblemCode());
        var t = await Seed.TableAsync(owner, "Masa 1");
        var put = await owner.PutAsJsonAsync($"/api/admin/tables/{t.Id}", new { name = "Masa 1", sortOrder = 0 });
        Assert.Equal("aktiflik_gecersiz", await put.ProblemCode());
    }
}
