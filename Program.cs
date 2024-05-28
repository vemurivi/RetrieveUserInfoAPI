using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Storage.Blobs;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Azure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

// Retrieve Blob and Table clients from DI
var blobServiceClient = app.Services.GetRequiredService<BlobServiceClient>();
var tableServiceClient = app.Services.GetRequiredService<TableServiceClient>();
var tableClient = tableServiceClient.GetTableClient("careershotinformation");

app.MapGet("/api/user", async (string name) =>
{
    var partitionKey = name.Substring(0, 1).ToUpper();
    try
    {
        var userData = await tableClient.GetEntityAsync<UserData>(partitionKey, name);

        var blobContainerClient = blobServiceClient.GetBlobContainerClient("media-dev");
        var photoBlobClient = blobContainerClient.GetBlobClient($"{name}.jpg"); // Assuming photo is saved as name.jpg
        var resumeBlobClient = blobContainerClient.GetBlobClient($"{name}.pdf"); // Assuming resume is saved as name.pdf

        var photoUrl = photoBlobClient.Uri.ToString();
        var resumeUrl = resumeBlobClient.Uri.ToString();

        return Results.Ok(new
        {
            userData.Value.Name,
            userData.Value.Description,
            userData.Value.LinkedIn,
            userData.Value.GitHub,
            userData.Value.Skills,
            PhotoUrl = photoUrl,
            ResumeUrl = resumeUrl
        });
    }
    catch (RequestFailedException)
    {
        return Results.NotFound("User not found");
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
