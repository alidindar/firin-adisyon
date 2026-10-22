using System.Security.Claims;
using Firin.Api.Auth;
using Firin.Api.Orders;

namespace Firin.Api.Endpoints;

public static class OrderEndpoints
{
    public record AddItemRequest(int ProductId, int Quantity);
    public record SetQuantityRequest(int Quantity);

    public static void MapOrderEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/tables", (OrderService s) => s.ListTablesAsync()).RequireAuthorization();

        api.MapGet("/tables/{id:int}", (int id, OrderService s) => s.GetTableAsync(id)).RequireAuthorization();

        api.MapPost("/tables/{id:int}/items", (int id, AddItemRequest r, OrderService s, ClaimsPrincipal u) =>
            s.AddItemAsync(id, r.ProductId, r.Quantity, u.UserId())).RequireAuthorization();

        api.MapPut("/items/{id:int}", (int id, SetQuantityRequest r, OrderService s, ClaimsPrincipal u) =>
            s.SetQuantityAsync(id, r.Quantity, u.UserId())).RequireAuthorization();

        api.MapDelete("/items/{id:int}", (int id, OrderService s, ClaimsPrincipal u) =>
            s.VoidItemAsync(id, u.UserId())).RequireAuthorization();

        api.MapPost("/items/{id:int}/restore", (int id, OrderService s, ClaimsPrincipal u) =>
            s.RestoreItemAsync(id, u.UserId())).RequireAuthorization();

        api.MapPost("/tables/{id:int}/payments", (int id, PayRequest r, PaymentService s, ClaimsPrincipal u) =>
            s.PayAsync(id, r, u.UserId())).RequireAuthorization();

        api.MapGet("/orders/{id:int}", (int id, OrderService s) => s.GetOrderAsync(id)).RequireAuthorization();
    }
}
