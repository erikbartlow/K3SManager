using K3SManager.Core.Abstractions;
using K3SManager.Core.Models;
using K3SManager.Web.Controllers;
using K3SManager.Web.Infrastructure;
using K3SManager.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace K3SManager.Web.Tests;

public sealed class NamespaceAliasTests
{
    [Theory]
    [InlineData("  Production apps  ", "Production apps")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public async Task Alias_edits_preserve_metadata_and_real_namespace(string? input, string? expected)
    {
        var original = new NamespaceProfile
        {
            NamespaceName = "apps", Alias = "Old alias", Owner = "Operations",
            Environment = "prod", Description = "Workloads", IsPinned = true
        };
        var repository = new Profiles { Profile = original };
        var controller = Controller(repository);
        var result = Assert.IsType<RedirectToActionResult>(await controller.SaveAlias(
            new NamespaceAliasEditModel { NamespaceName = "apps", Alias = input }, default));

        Assert.Equal(original with { Alias = expected }, repository.Profile);
        Assert.Equal("apps", result.RouteValues!["name"]);
        var summary = new NamespaceSummary { Name = "apps", Profile = repository.Profile };
        Assert.Equal(expected ?? "apps", summary.DisplayName);
        Assert.Equal("apps", summary.Name);

        await controller.SaveProfile(new NamespaceProfileEditModel { NamespaceName = "apps", Owner = "New owner" }, default);
        Assert.Equal(expected, repository.Profile!.Alias);
        Assert.Equal("New owner", repository.Profile.Owner);
    }

    [Fact]
    public async Task Invalid_or_disabled_alias_edits_do_not_write()
    {
        var repository = new Profiles();
        var controller = Controller(repository);
        controller.ModelState.AddModelError("Alias", "Too long");
        await controller.SaveAlias(new NamespaceAliasEditModel { NamespaceName = "apps", Alias = new string('a', 129) }, default);
        Assert.Null(repository.Profile);

        await Controller(repository, false).SaveAlias(new NamespaceAliasEditModel { NamespaceName = "apps", Alias = "Alias" }, default);
        Assert.Null(repository.Profile);
        Assert.Equal("apps", new NamespaceSummary { Name = "apps" }.DisplayName);
    }

    // Null cluster collaborators make any accidental Kubernetes access fail these tests.
    private static NamespacesController Controller(Profiles profiles, bool enabled = true) =>
        new(null!, profiles, null!, null!, null!, new PersistenceState(enabled), NullLogger<NamespacesController>.Instance)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new TempDataProvider())
        };

    private sealed class TempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class Profiles : INamespaceProfileRepository
    {
        public NamespaceProfile? Profile { get; set; }
        public Task<NamespaceProfile?> GetAsync(string namespaceName, CancellationToken cancellationToken) => Task.FromResult(Profile);
        public Task UpsertAsync(NamespaceProfile profile, CancellationToken cancellationToken)
        {
            Profile = profile;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyDictionary<string, NamespaceProfile>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string namespaceName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
