using Encore.Messaging;
using Encore.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Encore.Hosting.Extensions;

public static partial class CommandHostExtensions
{
    private static void RebuildSingleton<TService>(IServiceCollection services,
        Func<IServiceProvider, Func<IServiceProvider, object>, TService> newFactory)
        where TService : class
    {
        EnsureDefaultServicesRegistered(services);

        var descriptor = services.FirstOrDefault(d => !d.IsKeyedService && d.ServiceType == typeof(TService));
        var factory    = descriptor switch
        {
            { ImplementationFactory:  { } f } => f,
            { ImplementationInstance: { } i } => _ => i,
            { ImplementationType:     { } t } => p => ActivatorUtilities.CreateInstance(p, t),
            _                                 => p => ActivatorUtilities.CreateInstance<TService>(p),
        };

        services.Replace(ServiceDescriptor.Singleton<TService>(provider => newFactory(provider, factory)));
    }

    private static void EnsureDefaultServicesRegistered(IServiceCollection services)
    {
        services.TryAddSingleton<DefaultMessageCodec>();
        services.TryAddSingleton<IMessageCodec>(provider =>
            provider.GetRequiredService<DefaultMessageCodec>()
        );

        services.TryAddSingleton<CommandDispatcher>();
        if (services.All(d => d.ServiceType != typeof(ICommandDispatcher)))
        {
            services.AddSingleton<ICommandDispatcher, CommandDispatcher>(provider =>
                provider.GetRequiredService<CommandDispatcher>()
            );
        }
    }
}
