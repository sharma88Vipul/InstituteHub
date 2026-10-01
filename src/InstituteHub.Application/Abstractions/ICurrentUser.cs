namespace InstituteHub.Application.Abstractions;

/// <summary>The signed-in user for the current request, if any.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}
