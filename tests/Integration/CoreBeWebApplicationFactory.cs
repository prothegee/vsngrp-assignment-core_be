using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VsngrpCoreBe.Tests.Integration;

public sealed class CoreBeWebApplicationFactory(string configPath) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("CONFIG_PATH", configPath);
    }
}
