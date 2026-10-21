using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.AuthEndpoints;

namespace Firin.Tests;

public sealed class AuthTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Status_needs_setup_when_no_users()
    {
        var s = await (await _app.Client.GetAsync("/api/auth/status")).ReadAs<AuthStatusDto>();
        Assert.True(s.NeedsSetup);
        Assert.Empty(s.Users);
    }

    [Fact]
    public async Task Setup_creates_owner_and_signs_in()
    {
        var res = await _app.Client.PostAsJsonAsync("/api/auth/setup", new { name = "Ayşe", pin = "1234" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var me = await (await _app.Client.GetAsync("/api/auth/me")).ReadAs<MeDto>();
        Assert.Equal("Ayşe", me.Name);
        Assert.Equal(Role.Owner, me.Role);

        var s = await (await _app.Client.GetAsync("/api/auth/status")).ReadAs<AuthStatusDto>();
        Assert.False(s.NeedsSetup);
        Assert.Equal("Ayşe", Assert.Single(s.Users).Name);
    }

    [Fact]
    public async Task Setup_twice_is_conflict()
    {
        await _app.Client.PostAsJsonAsync("/api/auth/setup", new { name = "Ayşe", pin = "1234" });
        var res = await _app.NewClient().PostAsJsonAsync("/api/auth/setup", new { name = "Mehmet", pin = "9999" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("kurulum_yapilmis", await res.ProblemCode());
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("123")]
    [InlineData("12345")]
    public async Task Setup_rejects_bad_pin(string pin)
    {
        var res = await _app.Client.PostAsJsonAsync("/api/auth/setup", new { name = "Ayşe", pin });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("pin_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Me_without_login_is_401()
    {
        var res = await _app.Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_pin_is_401()
    {
        var id = _app.SeedUser("Garson", "5678", Role.Staff);
        var res = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "0000" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("pin_hatali", await res.ProblemCode());
    }

    [Fact]
    public async Task Five_wrong_pins_lock_user_for_a_minute()
    {
        var id = _app.SeedUser("Garson", "5678", Role.Staff);
        for (var i = 0; i < 5; i++)
            await _app.Client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "0000" });

        var locked = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "5678" });
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        Assert.Equal("kilitli", await locked.ProblemCode());

        _app.Clock.Advance(TimeSpan.FromSeconds(61));
        var ok = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "5678" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_session_is_rejected()
    {
        var staff = await _app.StaffAsync();
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/auth/me")).StatusCode);

        _app.Db(db => { db.Users.Single(u => u.Name == "Garson").IsActive = false; return db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_ends_session()
    {
        var owner = await _app.OwnerAsync();
        await owner.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/auth/me")).StatusCode);
    }
}
