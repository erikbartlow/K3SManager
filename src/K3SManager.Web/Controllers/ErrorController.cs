using System.Diagnostics;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace K3SManager.Web.Controllers;

// Exception re-execution preserves the original verb, including registration POSTs.
// This action has no database/cluster dependencies and performs no mutations.
public sealed class ErrorController : Controller
{
    [AllowAnonymous, Route("/error"), IgnoreAntiforgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Error()
    {
        if (HttpContext.Features.Get<IExceptionHandlerPathFeature>() is null) return NotFound();
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("Error", new ErrorViewModel
        {
            StatusCode = StatusCodes.Status500InternalServerError,
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            Message = "That request could not be completed. Please try again later or contact an administrator with the request id."
        });
    }
}
