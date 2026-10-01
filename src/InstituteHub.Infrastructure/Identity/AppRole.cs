using Microsoft.AspNetCore.Identity;

namespace InstituteHub.Infrastructure.Identity;

public class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
        Id = Guid.CreateVersion7();
    }

    public AppRole(string roleName) : this()
    {
        Name = roleName;
    }
}
