using System.Net;
using System.Net.Sockets;
using Encore.Hosting.Extensions;
using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Encore.Tests;

public class IntegrationTests
{
    public sealed class EchoController(TcpSession session) : CommandController<TcpSession>(session)
    {
        [CommandHandler]
        public EchoResponse Echo(EchoRequest request) => new() { Text = $"{request.Text}!" };

        [CommandHandler(Cmd.LogoutRequest)]
        public void Logout() => Session.Terminate();
    }

    private static async Task<(TcpClient Client, TcpSession Session)> ConnectAsync(ITcpServer<TcpSession> server)
    {
        var client = new TcpClient();
        var accept = server.AcceptSession(default);
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)server.Socket.LocalEndPoint!).Port).Within();

        return (client, await accept.Within());
    }

    private static async Task RunClientScenario(ITcpServer<TcpSession> server, ISessionManager<TcpSession> manager)
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Stopped += (_, _) => stopped.TrySetResult();

        var (client, session) = await ConnectAsync(server);
        using var connection = client;
        manager.StartSession(session);

        var framer = new SizePrefixedMessageFramer<ushort>(client.GetStream());

        await framer.WriteFrame(Wire.Encode(new PingRequest { Value = 21 }), default).Within();
        Assert.Equal(42, Wire.Decode<PingResponse>(await framer.ReadFrame().Within()).Value);

        await framer.WriteFrame(Wire.Encode(new EchoRequest { Text = "hello" }), default).Within();
        Assert.Equal("hello!", Wire.Decode<EchoResponse>(await framer.ReadFrame().Within()).Text);

        await framer.WriteFrame(Convert.FromHexString("3000"), default).Within();
        await stopped.Task.Within();

        Assert.Equal(0, await client.ReadAnyAsync());
    }

    [Fact]
    public async Task ServerSessionManagerAndDispatcher_ServeRequestsOverTcp()
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        dispatcher.Map<TcpSession, PingRequest, PingResponse>((_, request) =>
            Task.FromResult(new PingResponse { Value = request.Value * 2 }));
        dispatcher.Map<TcpSession, EchoController>(session => new EchoController(session));

        using var server  = new TcpServer(Options.Create(new TcpOptions { Port = 0 }), dispatcher: dispatcher);
        using var manager = new TcpSessionManager();
        server.Start();

        await RunClientScenario(server, manager);
    }

    [Fact]
    public async Task HostedServices_WireFramerDispatcherAndSessions()
    {
        using var host = new HostBuilder()
            .ConfigureRoutes(routes => routes
                .Map<TcpSession, PingRequest, PingResponse>((_, request) =>
                    Task.FromResult(new PingResponse { Value = request.Value * 2 }))
                .Map<TcpSession, EchoController>())
            .ConfigureTcpFramer(framer => framer.AddFramerFactory<SizePrefixedMessageFramer<ushort>>())
            .ConfigureTcpSessions(sessions => sessions.UseTcpSession<TcpSession>()
                .AddFactory<SessionFactory>()
                .AddManager<TcpSessionManager>())
            .Build();

        using var server = new TcpServer<TcpSession>(
            host.Services.GetRequiredService<ISessionFactory<TcpSession>>(),
            host.Services.GetRequiredService<IOptions<TcpOptions>>());
        server.Start();

        await RunClientScenario(server, host.Services.GetRequiredService<ISessionManager<TcpSession>>());
    }
}
