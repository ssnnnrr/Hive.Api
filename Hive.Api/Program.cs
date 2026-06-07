using Hive.Api.Data;
using Hive.Api.Entities;
using Hive.Api.Hubs;
using Hive.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Text;
using System.Text.Json.Serialization;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);




var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.Configure<FormOptions>(options => {
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = int.MaxValue;
    options.MemoryBufferThreshold = int.MaxValue;
});

builder.Services.Configure<KestrelServerOptions>(options => {
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024;
});

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<Hive.Api.Entities.TaskStatus>("task_status");
dataSourceBuilder.MapEnum<TaskPriority>("task_priority");
dataSourceBuilder.MapEnum<RequestStatus>("request_status");
dataSourceBuilder.MapEnum<SkillType>("skill_type");
dataSourceBuilder.MapEnum<UserRole>("user_role");
dataSourceBuilder.MapEnum<GoalType>("goal_type");
dataSourceBuilder.MapEnum<MaterialType>("material_type");
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<HiveDbContext>(options => options.UseNpgsql(dataSource));

builder.Services.AddControllers().AddJsonOptions(options => {
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true) // –азрешаем любые домены
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // ќЅя«ј“≈Ћ№Ќќ дл€ SignalR
    });
});

builder.Services.AddSingleton<ModerationService>();
builder.Services.AddScoped<AIService>();
builder.Services.AddHostedService<DeadlineWorker>();
builder.Services.AddSignalR();

var jwtSecret = builder.Configuration["JwtSettings:Secret"] ?? "default_very_long_secret_key_32_chars_min";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseStaticFiles();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/chatHub");
app.Run();