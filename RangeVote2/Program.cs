using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using RangeVote2.Data;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);
var config = new ApplicationConfig { DatabaseName = builder.Configuration["DatabaseName"], ElectionId = builder.Configuration["ElectionId"] };

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<ApplicationConfig>(config);
builder.Services.AddSingleton<IDatabaseBootstrap, DatabaseBootstrap>();
builder.Services.AddSingleton<RangeVoteRepository>();

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

app.UseStaticFiles(builder.Configuration["FolderName"]);

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

var dbBoostrap = new DatabaseBootstrap(config);
dbBoostrap.Setup();

if (app.Environment.IsDevelopment())
{
    app.Run();
}
else
{
    app.Run($"http://localhost:{builder.Configuration["RunPort"]}");
}

