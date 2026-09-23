using K3SManager.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace K3SManager.Web.Infrastructure;

[HtmlTargetElement("*", Attributes = "admin-only")]
public sealed class AdminOnlyTagHelper : TagHelper
{
    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("admin-only");
        if (!ViewContext.HttpContext.User.IsInRole(UserRoles.Admin)) output.SuppressOutput();
    }
}
