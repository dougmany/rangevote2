using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using RangeVote2.Data;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Dapper;

// Configure Dapper to handle Guid type conversions with SQLite
SqlMapper.AddTypeHandler(new GuidTypeHandler());
SqlMapper.AddTypeHandler(new NullableGuidTypeHandler());

var builder = WebApplication.CreateBuilder(args);
var config = new ApplicationConfig
{
    DatabaseName = builder.Configuration["DatabaseName"],
    ElectionIds = builder.Configuration["ElectionIds"]?.Split(",") ?? Array.Empty<string>()
};

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<ApplicationConfig>(config);
builder.Services.AddSingleton<IDatabaseBootstrap, DatabaseBootstrap>();
builder.Services.AddSingleton<IRangeVoteRepository, RangeVoteRepository>();
builder.Services.AddSingleton<RangeVoteRepository>();

// Add authentication services
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<CustomAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(provider =>
    provider.GetRequiredService<CustomAuthenticationStateProvider>());
builder.Services.AddAuthorizationCore();

// Add new ballot system services
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddHostedService<BallotClosureService>();

var app = builder.Build();

app.UsePathBase(builder.Configuration["FolderName"]);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

var provider = new FileExtensionContentTypeProvider();
provider.Mappings["{EXTENSION}"] = "{CONTENT TYPE}"; 
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });
app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

try
{
    var dbBoostrap = new DatabaseBootstrap(config);
    dbBoostrap.Setup();
}
catch (Exception ex)
{
    // Log database setup error but don't crash the app
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Failed to initialize database");
}

if (app.Environment.IsDevelopment())
{
    app.Run();
}
else
{
    app.Run($"http://localhost:{builder.Configuration["RunPort"]}");
}

