using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Helper;
using NoQueryDB.Api.Service;
using System.Text;
using System.Threading.RateLimiting;
using static NoQueryDB.Api.Service.EncryptionService;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "NoQueryDB API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Description = "JWT Authorization header using Bearer scheme",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new()
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddDataProtection();
builder.Services.AddScoped<ISecretProtector, EncryptionService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IActiveDatasourceService, ActiveDatasourceService>();
builder.Services.AddScoped<IDatasourceRepository, DatasourceRepository>();
builder.Services.AddScoped<NoQueryDatabase.Data.Contract.IMetadataProvider, NoQueryDatabase.Data.Implementation.MetadataProvider>();
builder.Services.AddScoped<NoQueryDatabase.Data.Contract.ISchemaDiscoveryService, NoQueryDatabase.Data.Implementation.SchemaDiscoveryService>();
builder.Services.AddScoped<NoQueryDatabase.Data.Contract.IDataOperationService, NoQueryDatabase.Data.Implementation.DataOperationService>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)
            ),

            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowOrigin",
        policy =>
        {
            policy
                .WithOrigins(
      "https://noquerydbui-taupe.vercel.app"
   )
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});
builder.Services.AddHttpClient();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("LoginPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return ValueTask.CompletedTask;
    };
});
var app = builder.Build();

app.UseMiddleware<NoQueryDB.Api.Middleware.GlobalExceptionMiddleware>();
app.UseRouting();
app.UseCors("AllowOrigin");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.UseRateLimiter();



app.Run();
