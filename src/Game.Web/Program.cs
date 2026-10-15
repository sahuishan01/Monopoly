using Game.Web;
using Game.Web.Services;
using Game.WebClient;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped(sp => new ApiClient(sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped<Session>();
builder.Services.AddScoped<SettingsService>();
await builder.Build().RunAsync();
