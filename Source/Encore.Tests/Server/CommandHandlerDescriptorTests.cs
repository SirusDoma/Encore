using System.Reflection;
using Encore.Server;
using Encore.Sessions;

namespace Encore.Tests.Server;

public class CommandHandlerDescriptorTests
{
    [LogFilter("class")]
    public sealed class DescriptorController(ISession session) : CommandController(session)
    {
        [CommandHandler]
        public Task<PingResponse> Ping(PingRequest request) => throw new NotSupportedException();

        [CommandHandler(Cmd.PingRequest, Cmd.PingResponse)]
        public Task<PingResponse> PingCancelable(PingRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        [CommandHandler]
        public PingResponse PingSync(PingRequest request) => throw new NotSupportedException();

        [CommandHandler]
        public Task PingNoResponse(PingRequest request) => throw new NotSupportedException();

        [LogFilter("method")]
        [AllowAnonymous]
        [CommandHandler(Cmd.NotifyRequest)]
        public Task Notify(CancellationToken cancellationToken) => throw new NotSupportedException();

        [CommandHandler(Cmd.LogoutRequest, Cmd.LogoutResponse)]
        public void Logout() => throw new NotSupportedException();

        [CommandHandler(Cmd.NotifyRequest)]
        public static Task Static() => throw new NotSupportedException();

        [CommandHandler(Cmd.NotifyRequest)]
        public Task Generic<T>() => throw new NotSupportedException();

        [CommandHandler]
        public Task NoCommand() => throw new NotSupportedException();

        [CommandHandler(Cmd.NotifyRequest)]
        public Task TooMany(PingRequest request, int value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        [CommandHandler(Cmd.NotifyRequest)]
        public Task WrongParameter(int value) => throw new NotSupportedException();

        [CommandHandler]
        public Task WrongSecondParameter(PingRequest request, int value) => throw new NotSupportedException();

        [CommandHandler(Cmd.NotifyRequest)]
        public Task OutParameter(out int value) => throw new NotSupportedException();

        [CommandHandler(Cmd.NotifyRequest)]
        public int WrongReturn() => throw new NotSupportedException();

        [CommandHandler(Cmd.NotifyRequest)]
        public Task<int> WrongTaskReturn() => throw new NotSupportedException();

        [CommandHandler(Cmd.NotifyRequest)]
        public PingResponse SyncCancelable(CancellationToken cancellationToken) => throw new NotSupportedException();

        [CommandHandler(Cmd.PingResponse)]
        public Task RequestMismatch(PingRequest request) => throw new NotSupportedException();

        [CommandHandler(Cmd.PingRequest, Cmd.EchoResponse)]
        public Task<PingResponse> ResponseMismatch(PingRequest request) => throw new NotSupportedException();

        public Task Unattributed() => throw new NotSupportedException();
    }

    public sealed class NotAController
    {
        [CommandHandler(Cmd.NotifyRequest)]
        public Task Handle() => throw new NotSupportedException();
    }

    private static CommandHandlerDescriptor Describe(string name, params ICommandFilter[] filters) =>
        Describe(typeof(DescriptorController), name, filters);

    private static CommandHandlerDescriptor Describe(Type type, string name, params ICommandFilter[] filters)
    {
        var method = type.GetMethod(name)!;
        return new CommandHandlerDescriptor(method,
            method.GetCustomAttribute<CommandHandlerAttribute>() ?? new CommandHandlerAttribute(), filters);
    }

    [Fact]
    public void RequestResponse_IsInferredFromSignature()
    {
        var descriptor = Describe(nameof(DescriptorController.Ping));

        Assert.Equal("DescriptorController:Ping", descriptor.Name);
        Assert.Equal(typeof(PingRequest), descriptor.RequestType);
        Assert.Equal<Enum>(Cmd.PingRequest, descriptor.RequestCommand);
        Assert.Equal(typeof(PingResponse), descriptor.ResponseType);
        Assert.Equal<Enum?>(Cmd.PingResponse, descriptor.ResponseCommand);
        Assert.True(descriptor.IsAsync);
        Assert.False(descriptor.IsCancelable);
    }

    [Fact]
    public void ExplicitMatchingCommands_AndCancellation_AreAccepted()
    {
        var descriptor = Describe(nameof(DescriptorController.PingCancelable));

        Assert.Equal<Enum>(Cmd.PingRequest, descriptor.RequestCommand);
        Assert.Equal<Enum?>(Cmd.PingResponse, descriptor.ResponseCommand);
        Assert.True(descriptor.IsCancelable);
    }

    [Fact]
    public void SyncHandler_IsNotAsync()
    {
        var descriptor = Describe(nameof(DescriptorController.PingSync));

        Assert.False(descriptor.IsAsync);
        Assert.Equal(typeof(PingResponse), descriptor.ResponseType);
    }

    [Fact]
    public void TaskHandler_HasNoResponse()
    {
        var descriptor = Describe(nameof(DescriptorController.PingNoResponse));

        Assert.Null(descriptor.ResponseType);
        Assert.Null(descriptor.ResponseCommand);
    }

    [Fact]
    public void CommandOnlyHandler_UsesAttributeCommand()
    {
        var descriptor = Describe(nameof(DescriptorController.Notify));

        Assert.Null(descriptor.RequestType);
        Assert.Equal<Enum>(Cmd.NotifyRequest, descriptor.RequestCommand);
        Assert.True(descriptor.IsAsync);
        Assert.True(descriptor.IsCancelable);
    }

    [Fact]
    public void VoidHandler_WithResponseCommand()
    {
        var descriptor = Describe(nameof(DescriptorController.Logout));

        Assert.Null(descriptor.ResponseType);
        Assert.Equal<Enum?>(Cmd.LogoutResponse, descriptor.ResponseCommand);
        Assert.True(descriptor.IsAsync);
        Assert.False(descriptor.IsCancelable);
    }

    [Theory]
    [InlineData(nameof(DescriptorController.Static))]
    [InlineData(nameof(DescriptorController.Generic))]
    [InlineData(nameof(DescriptorController.NoCommand))]
    [InlineData(nameof(DescriptorController.TooMany))]
    [InlineData(nameof(DescriptorController.WrongParameter))]
    [InlineData(nameof(DescriptorController.WrongSecondParameter))]
    [InlineData(nameof(DescriptorController.OutParameter))]
    [InlineData(nameof(DescriptorController.WrongReturn))]
    [InlineData(nameof(DescriptorController.WrongTaskReturn))]
    [InlineData(nameof(DescriptorController.SyncCancelable))]
    [InlineData(nameof(DescriptorController.Unattributed))]
    public void InvalidSignature_Throws(string method)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Describe(method));
    }

    [Theory]
    [InlineData(nameof(DescriptorController.RequestMismatch))]
    [InlineData(nameof(DescriptorController.ResponseMismatch))]
    public void CommandNotMatchingMessage_Throws(string method)
    {
        Assert.Throws<InvalidOperationException>(() => Describe(method));
    }

    [Fact]
    public void MethodOutsideController_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Describe(typeof(NotAController), nameof(NotAController.Handle)));
    }

    [Fact]
    public void Filters_ArePrioritizedThenMethodThenClass()
    {
        var prioritized = new DelegateFilter();
        var descriptor  = Describe(nameof(DescriptorController.Notify), prioritized);

        var filters = descriptor.GetCommandFilters().ToList();

        Assert.Equal(3, filters.Count);
        Assert.Same(prioritized, filters[0]);
        Assert.All(filters.Skip(1), f => Assert.IsType<LogFilterAttribute>(f));
        Assert.Same(filters[1], descriptor.GetCommandFilter<LogFilterAttribute>());
        Assert.True(descriptor.HasCommandFilter<DelegateFilter>());
        Assert.False(descriptor.HasCommandFilter<AuthorizeAttribute>());
        Assert.Null(descriptor.GetCommandFilter<AuthorizeAttribute>());
    }

    [Fact]
    public void CustomAttributes_AreReadFromMethod()
    {
        var descriptor = Describe(nameof(DescriptorController.Notify));

        Assert.True(descriptor.HasCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(descriptor.GetCustomAttribute<CommandHandlerAttribute>());
        Assert.Contains(descriptor.GetCustomAttributes(), a => a is AllowAnonymousAttribute);
        Assert.False(Describe(nameof(DescriptorController.Ping)).HasCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void ManualDescriptor_HasNoMethodMetadata()
    {
        var descriptor = new CommandHandlerDescriptor(Cmd.PingRequest, typeof(PingRequest), Cmd.PingResponse, typeof(PingResponse));

        Assert.Equal(string.Empty, descriptor.Name);
        Assert.Equal<Enum>(Cmd.PingRequest, descriptor.RequestCommand);
        Assert.Equal(typeof(PingRequest), descriptor.RequestType);
        Assert.Equal<Enum?>(Cmd.PingResponse, descriptor.ResponseCommand);
        Assert.Equal(typeof(PingResponse), descriptor.ResponseType);
        Assert.False(descriptor.IsAsync);
        Assert.False(descriptor.IsCancelable);
        Assert.Empty(descriptor.GetCustomAttributes());
        Assert.Null(descriptor.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.False(descriptor.HasCustomAttribute<AllowAnonymousAttribute>());
        Assert.Empty(descriptor.GetCommandFilters());
    }

    [Fact]
    public void CommandHandlerAttribute_ValidatesEnumArguments()
    {
        var empty = new CommandHandlerAttribute();
        var both  = new CommandHandlerAttribute(Cmd.PingRequest, Cmd.PingResponse);

        Assert.Null(empty.RequestCommand);
        Assert.Null(empty.ResponseCommand);
        Assert.Equal<Enum?>(Cmd.PingRequest, both.RequestCommand);
        Assert.Equal<Enum?>(Cmd.PingResponse, both.ResponseCommand);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandHandlerAttribute(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandHandlerAttribute(Cmd.PingRequest, 1));
    }
}
