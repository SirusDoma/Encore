using System.Net.Sockets;
using Encore.Hosting.Extensions;
using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Encore.Tests.Hosting;

public class TcpHostExtensionsTests
{
    public sealed class Marker;

    public sealed class TaggedFramer(NetworkStream stream, Marker marker) : IMessageFramer
    {
        private readonly SizePrefixedMessageFramer<ushort> _inner = new(stream);

        public NetworkStream Stream => stream;

        public Marker Marker => marker;

        public Task<byte[]> ReadFrame(int bufferSize = 1024, CancellationToken cancellationToken = default) =>
            _inner.ReadFrame(bufferSize, cancellationToken);

        public ValueTask WriteFrame(byte[] payload, CancellationToken cancellationToken) =>
            _inner.WriteFrame(payload, cancellationToken);

        public ValueTask WriteFrame(Memory<byte> payload, CancellationToken cancellationToken) =>
            _inner.WriteFrame(payload, cancellationToken);
    }

    public sealed class CustomSession(TcpClient client, ICommandDispatcher dispatcher)
        : TcpSession(client, new TcpOptions(), new SizePrefixedMessageFramerFactory<ushort>(), dispatcher);

    public sealed class CustomSessionFactory(ICommandDispatcher dispatcher) : ISessionFactory<CustomSession>
    {
        public CustomSession CreateSession(TcpClient client, params object[] parameters) => new(client, dispatcher);
    }

    public sealed class CustomSessionManager : TcpSessionManager<CustomSession>;

    [Fact]
    public async Task ConfigureTcpFramer_CreatesFramerWithStreamAndServices()
    {
        using var pair = await Loopback.CreateAsync();
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddSingleton<Marker>())
            .ConfigureTcpFramer(framer => framer.AddFramerFactory<TaggedFramer>())
            .Build();

        var stream = pair.Local.GetStream();
        var framer = Assert.IsType<TaggedFramer>(
            host.Services.GetRequiredService<IMessageFramerFactory>().CreateFramer(stream));

        Assert.Same(stream, framer.Stream);
        Assert.Same(host.Services.GetRequiredService<Marker>(), framer.Marker);
    }

    [Fact]
    public async Task ConfigureTcpFramer_WithContext_RegistersBuiltInFramer()
    {
        using var pair = await Loopback.CreateAsync();
        using var host = new HostBuilder()
            .ConfigureTcpFramer((context, framer) =>
            {
                Assert.NotNull(context);
                framer.AddFramerFactory<SizePrefixedMessageFramer<uint>>();
            })
            .Build();

        var framer = host.Services.GetRequiredService<IMessageFramerFactory>().CreateFramer(pair.Local.GetStream());
        await framer.WriteFrame([5], default).Within();

        Assert.Equal(Convert.FromHexString("0500000005"), await pair.Remote.ReadBytesAsync(5));
    }

    [Fact]
    public void ConfigureTcpSessions_RegistersDefaultFactoryAndManager()
    {
        using var host = new HostBuilder()
            .ConfigureTcpSessions(sessions => sessions.UseTcpSession<TcpSession>()
                .AddFactory<SessionFactory>()
                .AddManager<TcpSessionManager>())
            .Build();

        var factory = host.Services.GetRequiredService<SessionFactory>();

        Assert.Same(factory, host.Services.GetRequiredService<ISessionFactory<TcpSession>>());
        Assert.Same(factory, host.Services.GetRequiredService<ISessionFactory>());
        Assert.IsType<TcpSessionManager>(host.Services.GetRequiredService<ISessionManager<TcpSession>>());
        Assert.Same(
            host.Services.GetRequiredService<TcpSessionManager>(),
            host.Services.GetRequiredService<ISessionManager<TcpSession>>());
    }

    [Fact]
    public void ConfigureTcpSessions_FactoryDelegateForTcpSession_AlsoRegistersNonGenericFactory()
    {
        var factory = new SessionFactory(Microsoft.Extensions.Options.Options.Create(new TcpOptions()));
        using var host = new HostBuilder()
            .ConfigureTcpSessions(sessions => sessions.UseTcpSession<TcpSession>().AddFactory(_ => factory))
            .Build();

        Assert.Same(factory, host.Services.GetRequiredService<ISessionFactory<TcpSession>>());
        Assert.Same(factory, host.Services.GetRequiredService<ISessionFactory>());
    }

    [Fact]
    public void ConfigureTcpSessions_WithFactoriesForCustomSession()
    {
        var dispatcher = new CommandDispatcher();
        using var host = new HostBuilder()
            .ConfigureTcpSessions((context, sessions) =>
            {
                Assert.NotNull(context);
                sessions.UseTcpSession<CustomSession>()
                    .AddFactory(_ => new CustomSessionFactory(dispatcher))
                    .AddManager(_ => new CustomSessionManager());
            })
            .Build();

        Assert.IsType<CustomSessionFactory>(host.Services.GetRequiredService<ISessionFactory<CustomSession>>());
        Assert.IsType<CustomSessionManager>(host.Services.GetRequiredService<ISessionManager<CustomSession>>());
        Assert.Null(host.Services.GetService<ISessionFactory>());
    }

    [Fact]
    public void UseTcpSession_SharesServiceCollection()
    {
        var services = new ServiceCollection();
        var provider = new TcpSessionExtensions.TcpSessionProvider(services);

        Assert.Same(services, provider.UseTcpSession<TcpSession>().Services);
    }
}
