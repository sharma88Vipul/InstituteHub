using InstituteHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace InstituteHub.Web.Security;

/// <summary>
/// In an interactive Blazor Server circuit there is no HttpContext, so copy the authenticated user
/// into the circuit-scoped <see cref="UserContextAccessor"/>. TenantProvider and CurrentUser read from it.
/// </summary>
internal sealed class UserCircuitHandler(
    AuthenticationStateProvider authenticationStateProvider,
    UserContextAccessor userContext) : CircuitHandler, IDisposable
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationChanged;
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        userContext.User = state.User;
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> task)
    {
        _ = UpdateAsync(task);

        async Task UpdateAsync(Task<AuthenticationState> stateTask)
        {
            var state = await stateTask;
            userContext.User = state.User;
        }
    }

    public void Dispose() => authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationChanged;
}
