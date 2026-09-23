using System.ComponentModel.DataAnnotations;

namespace K3SManager.Web.Models;

public sealed class LoginViewModel
{
    [Required, StringLength(64), RegularExpression("[a-zA-Z0-9._-]+")]
    public string UserName { get; set; } = string.Empty;
    [Required, StringLength(128), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}
