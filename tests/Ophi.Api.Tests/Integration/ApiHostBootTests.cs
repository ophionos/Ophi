using FluentAssertions;
using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ophi.Api.Features.Auth;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace Ophi.Api.Tests.Integration;

/// <summary>
/// Boots the split API with the pre-generated handler code in <c>Ophi.Api/Internal/Generated</c>
/// (<c>TypeLoadMode.Static</c>, as in production) and attaches every handler's executor.
///
/// <para>
/// Every other API test compiles handlers at runtime (<see cref="WolverineOptionsConfigurator"/>), so
/// this is the only test that loads the checked-in code. It fails when a handler has no generated type
/// (<c>ExpectedTypeMissingException</c>) or when a generated constructor needs a service DI cannot
/// supply. It does not catch stale code for a changed handler: CI regenerates and diffs for that
/// (scripts/wolverine-codegen.sh).
/// </para>
/// </summary>
public class ApiHostBootTests(ApiHostBootTests.StaticCodegenFactory factory) : IClassFixture<ApiHostBootTests.StaticCodegenFactory>
{
    [Fact]
    public void ApiHost_AttachesEveryHandlerFromGeneratedCode()
    {
        var runtime = factory.Services.GetRequiredService<IWolverineRuntime>();
        var chains = factory.Services.GetRequiredService<HandlerGraph>().Chains;

        chains.Select(c => c.MessageType).Should().Contain(typeof(Login.Command),
            "the static handler registry must list the API handlers, or this sweep proves nothing");

        var failures = new List<string>();
        foreach (var chain in chains)
        {
            try
            {
                if (runtime.FindInvoker(chain.MessageType).GetType().Name == "NoHandlerExecutor")
                {
                    failures.Add($"{chain.MessageType.FullName}: no handler");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{chain.MessageType.FullName}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        failures.Should().BeEmpty("every API handler must load from the checked-in generated code");
    }

    public class StaticCodegenFactory : OphiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Registered after the base factory's WolverineOptionsConfigurator, so it runs last.
            builder.ConfigureServices(services => services.AddSingleton<IConfigureOptions<WolverineOptions>, ProductionCodegen>());
        }
    }

    private sealed class ProductionCodegen : IConfigureOptions<WolverineOptions>
    {
        public void Configure(WolverineOptions options)
        {
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
            options.ApplicationAssembly = typeof(Program).Assembly;
        }
    }
}
