using Encore.Hosting.Extensions;
using Encore.Messaging;
using Encore.Server;
using Encore.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Encore.Tests.Hosting;

public class CommandHostExtensionsTests
{
    public sealed class Journal
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public void Add(string entry)
        {
            lock (_entries)
                _entries.Add(entry);
        }
    }

    public sealed class ScopedProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    public sealed class ProbeController(ISession session, Journal journal, ScopedProbe probe, List<ScopedProbe> probes)
        : CommandController(session)
    {
        [CommandHandler]
        public Task<PingResponse> Ping(PingRequest request)
        {
            journal.Add("ping");
            probes.Add(probe);
            return Task.FromResult(new PingResponse { Value = request.Value + 1 });
        }
    }

    public sealed class TypedController(TestSession session) : CommandController<TestSession>(session)
    {
        [CommandHandler(Cmd.NotifyRequest)]
        public Task Notify()
        {
            Session.Record("typed");
            return Task.CompletedTask;
        }
    }

    public sealed class JournalFilter(Journal journal) : CommandFilter
    {
        public string Name { get; init; } = "di";

        public override void OnActionExecuting(CommandExecutingContext context) => journal.Add($"{Name}:executing");
    }

    public sealed class JournalExceptionHandler(Journal journal) : CommandExceptionHandler
    {
        public override void Handle(CommandExceptionHandlerContext context)
        {
            journal.Add($"handled:{context.Exception.GetType().Name}");
            context.Handled = true;
        }
    }

    public sealed class JournalExceptionLogger(Journal journal) : CommandExceptionLogger
    {
        public string Name { get; init; } = "logged";

        public override void Log(CommandExceptionLoggerContext context) =>
            journal.Add($"{Name}:{context.Exception.GetType().Name}");
    }

    private static IHost Build(Action<IHostBuilder> configure)
    {
        var builder = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<Journal>();
            services.AddSingleton<List<ScopedProbe>>(_ => []);
            services.AddScoped<ScopedProbe>();
        });

        configure(builder);
        return builder.Build();
    }

    private static async Task<TestSession> Dispatch(IHost host, IMessage message)
    {
        var session = new TestSession();
        await host.Services.GetRequiredService<ICommandDispatcher>()
            .Dispatch(session, host.Services.GetRequiredService<IMessageCodec>().Encode(message), default);

        return session;
    }

    [Fact]
    public async Task ConfigureRoutes_MapsFunctionHandlers()
    {
        using var host = Build(builder => builder.ConfigureRoutes(routes =>
        {
            routes.Map<ISession, PingRequest, PingResponse>((_, request) =>
                Task.FromResult(new PingResponse { Value = request.Value * 2 }));
            routes.Map<ISession, EchoRequest, EchoResponse>((_, request, _) =>
                Task.FromResult(new EchoResponse { Text = request.Text }));
            routes.Map<TestSession, PingRequest>((session, _) =>
            {
                session.Record("request");
                return Task.CompletedTask;
            });
            routes.Map<TestSession, Cmd>(Cmd.NotifyRequest, session =>
            {
                session.Record("notify");
                return Task.CompletedTask;
            });
            routes.Map<TestSession, Cmd>(Cmd.LogoutRequest, (session, _) =>
            {
                session.Record("logout");
                return Task.CompletedTask;
            });
        }));

        var dispatcher = host.Services.GetRequiredService<ICommandDispatcher>();
        var session    = new TestSession();

        await dispatcher.Dispatch(session, new PingRequest { Value = 4 }, default);
        await dispatcher.Dispatch(session, new EchoRequest { Text = "hi" }, default);
        await dispatcher.Dispatch(session, Cmd.NotifyRequest, default);
        await dispatcher.Dispatch(session, Cmd.LogoutRequest, default);

        Assert.Equal(8, Wire.Decode<PingResponse>(session.Frames[0]).Value);
        Assert.Equal("hi", Wire.Decode<EchoResponse>(session.Frames[1]).Text);
        Assert.Equal(["request", "notify", "logout"], session.Log);
    }

    [Fact]
    public void DefaultServices_AreSharedSingletons()
    {
        using var host = Build(builder => builder.ConfigureRoutes((context, routes) =>
        {
            Assert.NotNull(context);
            routes.Map<ISession, PingRequest>((_, _) => Task.CompletedTask);
        }));

        var dispatcher = host.Services.GetRequiredService<ICommandDispatcher>();
        var codec      = host.Services.GetRequiredService<IMessageCodec>();

        Assert.Same(dispatcher, host.Services.GetRequiredService<ICommandDispatcher>());
        Assert.Same(dispatcher, host.Services.GetRequiredService<CommandDispatcher>());
        Assert.Same(codec, host.Services.GetRequiredService<DefaultMessageCodec>());
        Assert.Equal(typeof(PingRequest), codec.GetRegisteredType(typeof(PingRequest)));
    }

    [Fact]
    public async Task ConfigureRoutes_MapsOntoPreRegisteredDispatcherInstance()
    {
        ICommandDispatcher existing = new CommandDispatcher();
        using var host = Build(builder => builder
            .ConfigureServices(services => services.AddSingleton(existing))
            .ConfigureRoutes(routes => routes.Map<TestSession, Cmd>(Cmd.NotifyRequest, session =>
            {
                session.Record("notify");
                return Task.CompletedTask;
            })));
        var session = new TestSession();

        Assert.Same(existing, host.Services.GetRequiredService<ICommandDispatcher>());

        await existing.Dispatch(session, Cmd.NotifyRequest, default);
        Assert.Equal(["notify"], session.Log);
    }

    [Fact]
    public async Task MapController_ResolvesDependenciesInNewScopePerDispatch()
    {
        using var host = Build(builder => builder.ConfigureRoutes(routes => routes.Map<ProbeController>()));

        var first  = await Dispatch(host, new PingRequest { Value = 1 });
        var second = await Dispatch(host, new PingRequest { Value = 2 });

        var probes = host.Services.GetRequiredService<List<ScopedProbe>>();
        Assert.Equal(2, Wire.Decode<PingResponse>(Assert.Single(first.Frames)).Value);
        Assert.Equal(3, Wire.Decode<PingResponse>(Assert.Single(second.Frames)).Value);
        Assert.Equal(2, probes.Distinct().Count());
        Assert.All(probes, p => Assert.True(p.Disposed));
        Assert.Equal(["ping", "ping"], host.Services.GetRequiredService<Journal>().Entries);
    }

    [Fact]
    public async Task MapController_WithTypedSession()
    {
        using var host = Build(builder => builder.ConfigureRoutes(routes => routes.Map<TestSession, TypedController>()));
        var session = new TestSession();

        await host.Services.GetRequiredService<ICommandDispatcher>().Dispatch(session, Cmd.NotifyRequest, default);

        Assert.Equal(["typed"], session.Log);
    }

    [Fact]
    public async Task MapController_WithFactory()
    {
        var sessions = new List<ISession>();
        using var host = Build(builder => builder.ConfigureRoutes(routes =>
            routes.Map<TypedController>((provider, session) =>
            {
                Assert.NotNull(provider.GetRequiredService<ScopedProbe>());
                sessions.Add(session);
                return new TypedController((TestSession)session);
            })));
        var session = new TestSession();

        await host.Services.GetRequiredService<ICommandDispatcher>().Dispatch(session, Cmd.NotifyRequest, default);

        Assert.Equal([session], sessions);
        Assert.Equal(["typed"], session.Log);
    }

    [Fact]
    public async Task MapController_WithControllerFilters()
    {
        using var host = Build(builder => builder.ConfigureRoutes(routes =>
            routes.Map<ProbeController>(options => options
                .AddFilter<JournalFilter>()
                .AddFilter(provider => new JournalFilter(provider.GetRequiredService<Journal>()) { Name = "factory" })
                .AddFilter(new DelegateFilter { Executing = c => ((TestSession)c.Session).Record("instance") }))));

        var session = await Dispatch(host, new PingRequest());

        Assert.Equal(["di:executing", "factory:executing", "ping"], host.Services.GetRequiredService<Journal>().Entries);
        Assert.Equal(["instance"], session.Log);
    }

    [Fact]
    public void MapController_NullFilter_Throws()
    {
        using var host = Build(builder => builder.ConfigureRoutes(routes =>
            routes.Map<ProbeController>(options => options.AddFilter((ICommandFilter)null!))));

        Assert.Throws<ArgumentNullException>(() => host.Services.GetRequiredService<ICommandDispatcher>());
    }

    [Fact]
    public async Task UseCodec_ReplacesCodecForDispatcher()
    {
        using var host = Build(builder => builder.ConfigureRoutes(routes =>
            routes.UseCodec<TrackingCodec>()
                .Map<ISession, PingRequest, PingResponse>((_, r) => Task.FromResult(new PingResponse { Value = r.Value }))));

        var codec = Assert.IsType<TrackingCodec>(host.Services.GetRequiredService<IMessageCodec>());
        Assert.Same(codec, host.Services.GetRequiredService<TrackingCodec>());

        var session = await Dispatch(host, new PingRequest { Value = 5 });

        Assert.Equal(1, codec.DecodeCount);
        Assert.Equal(5, Wire.Decode<PingResponse>(Assert.Single(session.Frames)).Value);
    }

    [Fact]
    public async Task ConfigureFilters_RegistersFiltersAndExceptionFiltersFromContainer()
    {
        using var host = Build(builder => builder
            .ConfigureRoutes(routes => routes.Map<ISession, PingRequest>((_, _) => throw new InvalidDataException()))
            .ConfigureFilters(filters => filters
                .AddFilter<JournalFilter>()
                .AddFilter(provider => new JournalFilter(provider.GetRequiredService<Journal>()) { Name = "factory" })
                .AddExceptionLogger<JournalExceptionLogger>()
                .AddExceptionHandler<JournalExceptionHandler>()));

        await Dispatch(host, new PingRequest());

        Assert.Equal(
            ["di:executing", "factory:executing", "logged:InvalidDataException", "handled:InvalidDataException"],
            host.Services.GetRequiredService<Journal>().Entries);
    }

    [Fact]
    public async Task ConfigureFilters_FactoryExceptionFilters()
    {
        using var host = Build(builder => builder
            .ConfigureRoutes(routes => routes.Map<ISession, PingRequest>((_, _) => throw new InvalidDataException()))
            .ConfigureFilters((context, filters) =>
            {
                Assert.NotNull(context);
                filters
                    .AddExceptionLogger(provider => new JournalExceptionLogger(provider.GetRequiredService<Journal>()))
                    .AddExceptionHandler(provider => new JournalExceptionHandler(provider.GetRequiredService<Journal>()));
            }));

        await Dispatch(host, new PingRequest());

        Assert.Equal(
            ["logged:InvalidDataException", "handled:InvalidDataException"],
            host.Services.GetRequiredService<Journal>().Entries);
    }

    [Fact]
    public void ConfigureFilters_SecondExceptionHandler_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build(builder => builder.ConfigureFilters(filters => filters
            .AddExceptionHandler<JournalExceptionHandler>()
            .AddExceptionHandler<JournalExceptionHandler>())));

        Assert.Throws<InvalidOperationException>(() => Build(builder => builder.ConfigureFilters(filters => filters
            .AddExceptionHandler<JournalExceptionHandler>()
            .AddExceptionHandler(provider => new JournalExceptionHandler(provider.GetRequiredService<Journal>())))));
    }

    [Fact]
    public async Task ConfigureFilters_MultipleExceptionLoggers_AllRunOnce()
    {
        using var host = Build(builder => builder
            .ConfigureRoutes(routes => routes.Map<ISession, PingRequest>((_, _) => throw new InvalidDataException()))
            .ConfigureFilters(filters => filters
                .AddExceptionLogger<JournalExceptionLogger>()
                .AddExceptionLogger(provider => new JournalExceptionLogger(provider.GetRequiredService<Journal>()) { Name = "factory" })
                .AddExceptionLogger<JournalExceptionLogger>()
                .AddExceptionHandler<JournalExceptionHandler>()));

        await Dispatch(host, new PingRequest());

        Assert.Equal(
            ["logged:InvalidDataException", "factory:InvalidDataException", "logged:InvalidDataException", "handled:InvalidDataException"],
            host.Services.GetRequiredService<Journal>().Entries);
    }

    [Fact]
    public async Task ConfigureFilters_WithoutRoutes_StillRegistersDispatcher()
    {
        using var host = Build(builder => builder.ConfigureFilters(filters => filters.AddFilter<JournalFilter>()));

        var dispatcher = host.Services.GetRequiredService<ICommandDispatcher>();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            dispatcher.Dispatch(new TestSession(), Convert.FromHexString("9999"), default));
    }
}
