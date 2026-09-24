using Encore.Server;
using Encore.Sessions;

namespace Encore.Tests.Server;

public class AuthorizeAttributeTests
{
    [Authorize]
    public sealed class SecureController(ISession session) : CommandController(session)
    {
        [CommandHandler(Cmd.NotifyRequest)]
        public Task Secret()
        {
            ((TestSession)Session).Record("secret");
            return Task.CompletedTask;
        }

        [AllowAnonymous]
        [CommandHandler(Cmd.LogoutRequest)]
        public Task Public()
        {
            ((TestSession)Session).Record("public");
            return Task.CompletedTask;
        }
    }

    public sealed class CustomSecureController(ISession session) : CommandController(session)
    {
        [Authorize<UnauthorizedAccessException>]
        [CommandHandler(Cmd.NotifyRequest)]
        public Task Secret() => Task.CompletedTask;
    }

    private static ICommandDispatcher Map<TController>(Func<ISession, TController> factory)
        where TController : CommandController
    {
        ICommandDispatcher dispatcher = new CommandDispatcher();
        dispatcher.Map<ISession, TController>(factory);

        return dispatcher;
    }

    [Fact]
    public async Task AuthorizedSession_IsAllowed()
    {
        var dispatcher = Map(s => new SecureController(s));
        var session    = new TestTcpSession();
        session.Authorize("token");

        await dispatcher.Dispatch(session, Cmd.NotifyRequest, default);

        Assert.Equal(["secret"], session.Log);
    }

    [Fact]
    public async Task UnauthorizedSession_IsRejected()
    {
        var dispatcher = Map(s => new SecureController(s));
        var session    = new TestTcpSession();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.Dispatch(session, Cmd.NotifyRequest, default));

        Assert.Equal("Unauthorized access", ex.Message);
        Assert.Empty(session.Log);
    }

    [Fact]
    public async Task AllowAnonymous_BypassesAuthorization()
    {
        var dispatcher = Map(s => new SecureController(s));
        var session    = new TestTcpSession();

        await dispatcher.Dispatch(session, Cmd.LogoutRequest, default);

        Assert.Equal(["public"], session.Log);
    }

    [Fact]
    public async Task NonTcpSession_IsRejected()
    {
        var dispatcher = Map(s => new SecureController(s));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.Dispatch(new TestSession(), Cmd.NotifyRequest, default));

        Assert.Contains("non-TCP", ex.Message);
    }

    [Fact]
    public async Task GenericAuthorize_ThrowsConfiguredException()
    {
        var dispatcher = Map(s => new CustomSecureController(s));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            dispatcher.Dispatch(new TestTcpSession(), Cmd.NotifyRequest, default));
    }

    [Fact]
    public async Task Rejection_CanBeTranslatedByExceptionHandler()
    {
        var dispatcher = Map(s => new SecureController(s));
        var session    = new TestTcpSession();

        dispatcher.AddExceptionFilter(new DelegateExceptionHandler(c => c.Result = new FailureResponse { Reason = c.Exception.Message }));

        await dispatcher.Dispatch(session, Cmd.NotifyRequest, default);

        Assert.Equal("Unauthorized access", Wire.Decode<FailureResponse>(Assert.Single(session.Frames)).Reason);
    }
}
