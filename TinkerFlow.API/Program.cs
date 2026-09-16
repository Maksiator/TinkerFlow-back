using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.RateLimiting;
using Scalar.AspNetCore;
using System.Text;
using TinkerFlow.API.Extensions;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Infrastructure;
using TinkerFlow.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost", "http://127.0.0.1", "http://localhost:5173", "http://localhost:3000", "http://localhost:4200" };
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});







builder.Services.AddMemoryCache();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.GetClientIpAddress(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 300, // 300 zapytan na minute
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<TinkerFlowDbContext>(options =>
    // options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IGroupAccessService, GroupAccessService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

builder.Services.AddIdentityCore<User>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<TinkerFlowDbContext>()
.AddDefaultTokenProviders()
.AddErrorDescriber<TinkerFlow.API.Localization.PolishIdentityErrorDescriber>();

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Brak klucza JWT w appsettings.json!");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true
            
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Sprawdzamy czy przeglądarka podesłała nam ciastko o nazwie tinkerflow_token
                if (context.Request.Cookies.ContainsKey("tinkerflow_token"))
                {
                    // Jeśli tak, "wyciągamy" je i wrzucamy do strumienia logowania tak, 
                    // jakby to był normalny nagłówek Bearer.
                    context.Token = context.Request.Cookies["tinkerflow_token"];
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// RUTHLESS SECURITY & STABILITY CHECK: Automatyczna migracja przy starcie
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<TinkerFlowDbContext>();
        
        // Ta linijka rozwiązuje Twój problem. Automatycznie aplikuje
        // wszystkie brakujące migracje z folderu Migrations do pliku tinkerflow.db
        await context.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogCritical(ex, "Krytyczny błąd podczas automatycznej migracji bazy danych. Aplikacja zatrzymana.");
        throw; // Zatrzymujemy start aplikacji - jeśli baza jest niespójna, system nie ma prawa działać.
    }
}




app.UseMiddleware<TinkerFlow.API.Middlewares.GlobaLExceptionMiddleware>();

app.UseRateLimiter();
app.UseCors("CorsPolicy"); 

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/auth"))
    {
        await next();
        return;
    }

    if (context.User.Identity?.IsAuthenticated == true)
    {
        // Jeśli zapytanie ma ważny token JWT
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (userId != null)
            {
                var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
                var cacheKey = $"UserActiveStatus_{userId}";

                // Pobieramy z cache'u, jeśli nie ma - uderzamy do bazy (na 5 minut!)
                var isActive = await cache.GetOrCreateAsync(cacheKey, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                    var dbContext = context.RequestServices.GetRequiredService<TinkerFlowDbContext>();
                    var user = await dbContext.Users.FindAsync(Guid.Parse(userId));
                    return user?.IsActive ?? false;
                });

                // Jeśli ktoś usunął usera z bazy lub zmienił mu IsActive na false
                if (!isActive)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { message = "Konto zostało zablokowane." });
                    return; // Zwarcie! Ucinamy żądanie w tym miejscu, nie wpuszczamy do kontrolerów.
                }
            }
        }
    }

    await next(); // Jeśli wszystko ok, puszczamy żądanie dalej
});


app.MapControllers();

// --- AUTOMATYCZNA MIGRACJA I SEEDOWANIE PIERWSZEGO ADMINA ---
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<TinkerFlowDbContext>();
    try
    {
        await context.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Wystąpił błąd podczas automatycznej migracji bazy danych.");
    }

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    
    var adminEmail = "admin@tinkerflow.com";
    
    // Sprawdzamy, czy admin już istnieje
    if (await userManager.FindByEmailAsync(adminEmail) == null)
    {
        var adminUser = new User
        {
            UserName = adminEmail, // Identity tego wymaga
            Email = adminEmail,
            FirstName = "Główny",
            LastName = "Administrator",
            // Upewnij się, że UserRole.Admin to poprawna nazwa z Twojego enuma!
            Role = TinkerFlow.Domain.Enums.UserRole.Admin, 
            IsActive = true
        };

        // Tworzymy konto z silnym hasłem (Identity samo je zahashuje)
        await userManager.CreateAsync(adminUser, "Admin123!");
    }

    // --- UZUPEŁNIENIE DANYCH WYKONAWCY W LOGACH AUDYTU ---
    try
    {
        // 1. Uzupełnienie logów z UserId, którym brakowało imienia/nazwiska lub emaila
        var logsToFix = await context.AuditLogs
            .Where(l => l.UserId != null && (l.UserName == null || l.UserEmail == null))
            .ToListAsync();

        if (logsToFix.Any())
        {
            var userIds = logsToFix.Select(l => l.UserId!.Value).Distinct().ToList();
            var usersMap = await context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            foreach (var log in logsToFix)
            {
                if (usersMap.TryGetValue(log.UserId!.Value, out var u))
                {
                    log.UserName ??= $"{u.FirstName} {u.LastName}".Trim();
                    log.UserEmail ??= u.Email;
                    log.UserRole ??= u.Role.ToString();
                }
            }
            await context.SaveChangesAsync();
        }

        // 2. Uzupełnienie logów bez UserId (np. dodawanie uczniów), które pochodzą z tego samego IP co logi z przypisanym użytkownikiem
        var logsWithoutUser = await context.AuditLogs
            .Where(l => l.UserId == null && l.IpAddress != null)
            .ToListAsync();

        if (logsWithoutUser.Any())
        {
            var knownLogsWithUser = await context.AuditLogs
                .Where(l => l.UserId != null && l.IpAddress != null)
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            foreach (var orphanLog in logsWithoutUser)
            {
                var matchingLog = knownLogsWithUser
                    .FirstOrDefault(k => k.IpAddress == orphanLog.IpAddress &&
                                         Math.Abs((k.Timestamp - orphanLog.Timestamp).TotalMinutes) <= 60);

                if (matchingLog != null)
                {
                    orphanLog.UserId = matchingLog.UserId;
                    orphanLog.UserName = matchingLog.UserName;
                    orphanLog.UserEmail = matchingLog.UserEmail;
                    orphanLog.UserRole = matchingLog.UserRole;
                }
            }
            await context.SaveChangesAsync();
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Ostrzeżenie przy uzupełnianiu historycznych logów audytu.");
    }
}

app.Run();