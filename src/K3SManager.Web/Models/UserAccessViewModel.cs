using System.ComponentModel.DataAnnotations;
using K3SManager.Core.Models;

namespace K3SManager.Web.Models;

public sealed class UserAccessViewModel
{
    [Required, StringLength(64)]
    public string UserName { get; set; } = string.Empty;
    [Required]
    public string Role { get; set; } = UserRoles.ReadOnly;
    public bool Enabled { get; set; }
}
