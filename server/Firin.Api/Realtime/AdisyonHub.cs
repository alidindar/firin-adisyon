using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Firin.Api.Realtime;

/// <summary>İstemciler yalnızca dinler; sunucuya mesaj göndermezler.</summary>
[Authorize]
public sealed class AdisyonHub : Hub;

public interface IRealtime
{
    Task TableChanged(int tableId);
    Task TablesChanged();
    Task CatalogChanged();
}

public sealed class SignalRRealtime(IHubContext<AdisyonHub> hub) : IRealtime
{
    public Task TableChanged(int tableId) => hub.Clients.All.SendAsync("TableChanged", tableId);
    public Task TablesChanged() => hub.Clients.All.SendAsync("TablesChanged");
    public Task CatalogChanged() => hub.Clients.All.SendAsync("CatalogChanged");
}

public static class RealtimeExtensions
{
    /// <summary>Gruptaki GET dışı istekler başarıyla bitince yayın yapar. DomainException fırlarsa yayın olmaz.</summary>
    public static RouteGroupBuilder NotifiesOnChange(this RouteGroupBuilder group, Func<IRealtime, Task> notify)
    {
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var result = await next(ctx);
            if (!HttpMethods.IsGet(ctx.HttpContext.Request.Method))
                await notify(ctx.HttpContext.RequestServices.GetRequiredService<IRealtime>());
            return result;
        });
        return group;
    }
}
