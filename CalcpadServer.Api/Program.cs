using CalcpadServer.Api.Data;
using CalcpadServer.Api.Services;
using CalcpadServer.Api.Middleware;
using CalcpadServer.Api.Extensions;
using Microsoft.EntityFrameworkCore;
using Minio;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Entity Framework with SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=data/calcpad.db"));

// Configure MinIO
builder.Services.AddSingleton<IMinioClient>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var endpoint = configuration["MinIO:Endpoint"] ?? "localhost:9000";
    var accessKey = configuration["MinIO:AccessKey"] ?? "calcpad-admin";
    var secretKey = configuration["MinIO:SecretKey"] ?? "calcpad-password-123";
    var useSSL = configuration.GetValue<bool>("MinIO:UseSSL", false);

    return new MinioClient()
        .WithEndpoint(endpoint)
        .WithCredentials(accessKey, secretKey)
        .WithSSL(useSSL)
        .Build();
});

builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITagsService, TagsService>();

// Add configurable authentication
builder.Services.AddConfigurableAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Only use HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Add authentication middleware
app.UseMiddleware<AuthMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Database is initialized by Docker Compose sqlite-init service

app.Run();
