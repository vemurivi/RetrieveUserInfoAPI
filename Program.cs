using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Storage.Blobs;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Azure;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "RetrieveUserInfoAPI API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter into field the word 'Bearer' followed by a space and the JWT token",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
    {
        new OpenApiSecurityScheme {
            Reference = new OpenApiReference {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        },
        new string[] { }
    }});
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = $"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}/v2.0",
            ValidAudience = builder.Configuration["AzureAd:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["AzureAd:ClientSecret"]))
        };
    });

builder.Services.AddAuthorization();

// Retrieve environment variables from App Service
var blobServiceEndpoint = Environment.GetEnvironmentVariable("BlobServiceEndpoint");
var blobServiceSasToken = Environment.GetEnvironmentVariable("BlobServiceSasToken");
var storageAccountConnectionString = Environment.GetEnvironmentVariable("StorageAccountConnectionString");

if (string.IsNullOrEmpty(blobServiceEndpoint) || string.IsNullOrEmpty(blobServiceSasToken) || string.IsNullOrEmpty(storageAccountConnectionString))
{
    throw new InvalidOperationException("One or more environment variables are not set.");
}

builder.Services.AddSingleton(new BlobServiceClient(new Uri($"{blobServiceEndpoint}?{blobServiceSasToken}")));
builder.Services.AddSingleton(new TableServiceClient(storageAccountConnectionString));

var app = builder.Build();

// Conditionally use HTTPS redirection based on environment variable
var useHttpsRedirection = Environment.GetEnvironmentVariable("USE_HTTPS_REDIRECTION")?.ToLower() == "true";

if (useHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Retrieve Blob and Table clients from DI
var blobServiceClient = app.Services.GetRequiredService<BlobServiceClient>();
var tableServiceClient = app.Services.GetRequiredService<TableServiceClient>();
var tableClient = tableServiceClient.GetTableClient("careershotinformation");

app.MapGet("/api/user", [Authorize] async (string name) =>
{
    // Normalize the input name
    var normalizedRowKey = name.ToLower().Replace(" ", "");

    try
    {
        // Search for the user in the table
        var entities = tableClient.Query<UserData>(filter: $"RowKey eq '{normalizedRowKey}'");

        var userData = entities.FirstOrDefault();

        if (userData == null)
        {
            return Results.NotFound("User not found");
        }

        var blobContainerClient = blobServiceClient.GetBlobContainerClient("media-dev");
        var photoBlobClient = blobContainerClient.GetBlobClient($"{userData.RowKey}.jpg"); // Assuming photo is saved as RowKey.jpg
        var resumeBlobClient = blobContainerClient.GetBlobClient($"{userData.RowKey}.pdf"); // Assuming resume is saved as RowKey.pdf

        var photoUrl = photoBlobClient.Uri.ToString();
        var resumeUrl = resumeBlobClient.Uri.ToString();

        return Results.Ok(new
        {
            userData.Name,
            userData.Description,
            userData.LinkedIn,
            userData.GitHub,
            userData.Skills,
            PhotoUrl = photoUrl,
            ResumeUrl = resumeUrl
        });
    }
    catch (RequestFailedException)
    {
        return Results.StatusCode(500, "Internal server error");
    }
})
.WithName("GetUserData");

app.Run();

public class UserData : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LinkedIn { get; set; } = string.Empty;
    public string GitHub { get; set; } = string.Empty;
    public string Skills { get; set; } = string.Empty; // JSON string representing the skills
    public ETag ETag { get; set; }
}
