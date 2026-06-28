using Microsoft.AspNetCore.SignalR;

namespace LeWiK.Store.Api.Realtime;

public sealed class StoreHub : Hub
{
    public Task<string> Ping() => Task.FromResult("pong");
}