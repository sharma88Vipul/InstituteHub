namespace InstituteHub.Application.Users;

public sealed record UserOption(Guid Id, string Name);

/// <summary>Read access to Identity users of the current institute (implemented in Infrastructure).</summary>
public interface IUserDirectory
{
    /// <summary>Active users who can teach a batch (Teacher or Owner role).</summary>
    Task<IReadOnlyList<UserOption>> GetTeachersAsync(CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct = default);
}
