using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.AuthEndpoints;
using static Firin.Api.Endpoints.UserAdminEndpoints;

namespace Firin.Tests;

public sealed class UserAdminTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static async Task<int> MyIdAsync(HttpClient c) => (await (await c.GetAsync("/api/auth/me")).ReadAs<MeDto>()).Id;

    [Fact]
    public async Task Owner_creates_staff_who_can_log_in()
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "4321", role = "Staff" });
        var u = await res.ReadAs<UserDto>();
        Assert.Equal(Role.Staff, u.Role);
        Assert.True(u.IsActive);

        var staff = await _app.LoginAsync(u.Id, "4321");
        Assert.Equal(Role.Staff, (await (await staff.GetAsync("/api/auth/me")).ReadAs<MeDto>()).Role);
    }

    [Fact]
    public async Task Duplicate_user_name_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "4321", role = "Staff" });
        var res = await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "1111", role = "Staff" });
        Assert.Equal("kullanici_adi_var", await res.ProblemCode());
    }

    [Fact]
    public async Task Bad_pin_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "12", role = "Staff" });
        Assert.Equal("pin_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Last_owner_cannot_be_demoted_or_deactivated()
    {
        var owner = await _app.OwnerAsync();
        var id = await MyIdAsync(owner);

        var demote = await owner.PutAsJsonAsync($"/api/admin/users/{id}", new { name = "Sahip", role = "Staff", isActive = true, pin = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal("son_sahip", await demote.ProblemCode());

        var deactivate = await owner.PutAsJsonAsync($"/api/admin/users/{id}", new { name = "Sahip", role = "Owner", isActive = false, pin = (string?)null });
        Assert.Equal("son_sahip", await deactivate.ProblemCode());

        await owner.PostAsJsonAsync("/api/admin/users", new { name = "Ortak", pin = "2222", role = "Owner" });
        var ok = await owner.PutAsJsonAsync($"/api/admin/users/{id}", new { name = "Sahip", role = "Staff", isActive = true, pin = (string?)null });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Pin_can_be_changed()
    {
        var owner = await _app.OwnerAsync();
        var u = await (await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "4321", role = "Staff" })).ReadAs<UserDto>();

        await owner.PutAsJsonAsync($"/api/admin/users/{u.Id}", new { name = "Mehmet", role = "Staff", isActive = true, pin = "9999" });

        var old = await _app.NewClient().PostAsJsonAsync("/api/auth/login", new { userId = u.Id, pin = "4321" });
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        await _app.LoginAsync(u.Id, "9999");
    }

    [Fact]
    public async Task Staff_cannot_manage_users()
    {
        var staff = await _app.StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_cross_demotions_leave_an_active_owner()
    {
        var a = await _app.OwnerAsync();
        var aId = await MyIdAsync(a);
        var b = await (await a.PostAsJsonAsync("/api/admin/users", new { name = "Ortak", pin = "2222", role = "Owner" })).ReadAs<UserDto>();
        var bClient = await _app.LoginAsync(b.Id, "2222");

        var t1 = a.PutAsJsonAsync($"/api/admin/users/{b.Id}", new { name = "Ortak", role = "Staff", isActive = true, pin = (string?)null });
        var t2 = bClient.PutAsJsonAsync($"/api/admin/users/{aId}", new { name = "Sahip", role = "Staff", isActive = true, pin = (string?)null });
        await Task.WhenAll(t1, t2);

        var owners = _app.Db(db => db.Users.Count(u => u.Role == Role.Owner && u.IsActive));
        Assert.True(owners >= 1);
    }

    [Fact]
    public async Task Create_without_role_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "4321" });
        Assert.Equal("rol_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Update_without_role_or_is_active_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        var u = await (await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "4321", role = "Staff" })).ReadAs<UserDto>();

        var noRole = await owner.PutAsJsonAsync($"/api/admin/users/{u.Id}", new { name = "Mehmet", isActive = true });
        Assert.Equal("rol_gecersiz", await noRole.ProblemCode());

        var noActive = await owner.PutAsJsonAsync($"/api/admin/users/{u.Id}", new { name = "Mehmet", role = "Staff" });
        Assert.Equal("aktiflik_gecersiz", await noActive.ProblemCode());

        var list = await (await owner.GetAsync("/api/admin/users")).ReadAs<List<UserDto>>();
        var after = list.Single(x => x.Id == u.Id);
        Assert.True(after.IsActive);
        Assert.Equal(Role.Staff, after.Role);
    }
}
