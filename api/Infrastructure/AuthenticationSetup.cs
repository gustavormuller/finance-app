using System.Threading.RateLimiting;
using Finance.Api.Application;
using Finance.Api.Domain.Identity;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure;

public static class AuthenticationSetup
{
    /// <summary>Rate limiting policy on the endpoint that starts the Google flow.</summary>
    public const string GoogleSignInPolicy = "google-sign-in";

    /// <summary>Where the browser lands after a completed sign-in.</summary>
    public const string SignedInPath = "/";

    /// <summary>Where the browser lands when Google will not vouch for the address.</summary>
    public const string UnverifiedEmailPath = "/login?error=unverified";

    /// <summary>Where the browser lands when the person refused the consent screen.</summary>
    public const string SignInCancelledPath = "/login?error=cancelled";

    /// <summary>Where the browser lands when the flow broke for any other reason.</summary>
    public const string SignInFailedPath = "/login?error=auth_failed";

    /// <summary>
    /// Identity with no roles and no local passwords, a cookie session, Google as the
    /// only sign-in provider, and a key ring that survives a restart.
    /// </summary>
    /// <remarks>
    /// Everything that reads configuration does so through a dependency rather than
    /// from the builder's configuration at registration time: sources added by the
    /// host — including the ones WebApplicationFactory injects in the integration
    /// tests — are only merged in during Build().
    /// </remarks>
    public static IServiceCollection AddFinanceAuthentication(this IServiceCollection services)
    {
        services.AddIdentityCore<AppUser>(options =>
            {
                // Two accounts sharing an address is exactly the state the linking
                // branch in ExternalSignIn exists to prevent. Enforce it in the store
                // too, so a bug there fails loudly instead of quietly forking a history.
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);

        authentication.AddIdentityCookies();
        authentication.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
        {
            // Supersedes the /signin-google in ARCHITECTURE.md: this path is already
            // covered by the Vite dev proxy, and /signin-google would need a rule of
            // its own. It must match the Google Cloud Console entry exactly.
            options.CallbackPath = "/api/auth/google/callback";

            // Never actually written: OnTicketReceived handles the response itself, so
            // the handler's own sign-in never runs. RemoteAuthenticationOptions still
            // requires the scheme to be named and registered.
            options.SignInScheme = IdentityConstants.ExternalScheme;

            // Google is identity only. Holding its tokens would mean holding a refresh
            // token that expires in seven days while the consent screen is in Testing.
            options.SaveTokens = false;

            // None of Google's default claim actions map email_verified, and
            // ExternalSignIn refuses to create anything without it, so it has to
            // survive the trip from the userinfo payload to the principal. The reading
            // itself is in GoogleUserInfo, where a unit test can reach it.
            options.Events.OnCreatingTicket = context =>
            {
                foreach (var claim in GoogleUserInfo.ReadUnmappedClaims(context.User))
                {
                    context.Identity?.AddClaim(claim);
                }

                return Task.CompletedTask;
            };

            // Without this the handler throws, and refusing the consent screen — a
            // button Google puts in front of everyone — answers 500.
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();

                // Classified from the parameter Google actually sent rather than from
                // the failure message, which is prose and version-dependent. A failure
                // with no error parameter at all is a broken flow: a missing or
                // tampered state, or a correlation cookie that never came back.
                var refused = string.Equals(
                    context.Request.Query["error"],
                    "access_denied",
                    StringComparison.Ordinal);

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Finance.Api.Authentication.Google");

                if (refused)
                {
                    // Somebody changing their mind is not an incident.
                    logger.LogInformation("A Google sign-in was refused at the consent screen.");
                }
                else
                {
                    logger.LogWarning(context.Failure, "The Google sign-in flow failed.");
                }

                context.Response.Redirect(refused ? SignInCancelledPath : SignInFailedPath);

                return Task.CompletedTask;
            };

            options.Events.OnTicketReceived = async context =>
            {
                // From here the response is ours. This also stops the handler signing
                // into the external cookie and redirecting to the ticket's RedirectUri.
                context.HandleResponse();

                var principal = context.Principal
                    ?? throw new InvalidOperationException("Google returned no principal.");

                var externalSignIn = context.HttpContext.RequestServices
                    .GetRequiredService<ExternalSignIn>();

                var outcome = await externalSignIn.SignInAsync(
                    GoogleDefaults.AuthenticationScheme,
                    principal);

                context.Response.Redirect(outcome switch
                {
                    ExternalSignInOutcome.SignedIn => SignedInPath,
                    _ => UnverifiedEmailPath,
                });
            };
        });

        services.AddOptions<GoogleOptions>(GoogleDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.ClientId = configuration["Google:ClientId"] ?? string.Empty;
                options.ClientSecret = configuration["Google:ClientSecret"] ?? string.Empty;
            });

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

            // Strict would break the top-level redirect back from Google — the browser
            // would arrive at the callback without the cookie. Lax still blocks
            // cross-site POSTs, and the Origin check covers the rest.
            options.Cookie.SameSite = SameSiteMode.Lax;

            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            // Identity's default is a 302 to /Account/Login. This is a JSON API: the
            // SPA needs a status code it can branch on, not a redirect to a page that
            // does not exist.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddScoped<ExternalSignIn>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // ADR-010 requires this: open registration means a bot loop on the endpoint
            // that creates rows.
            options.AddPolicy(GoogleSignInPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        services.AddDataProtection().SetApplicationName("finance-app");

        // Equivalent to PersistKeysToFileSystem, except the directory is resolved from
        // configuration when the key ring is first needed rather than at registration.
        // Without persistence every restart invalidates every session — the failure
        // that only ever shows up in production.
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var directory = new DirectoryInfo(ResolveKeysPath(configuration, environment));
            directory.Create();

            return new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new FileSystemXmlRepository(directory, loggerFactory));
        });

        return services;
    }

    /// <summary>
    /// Resolved against the content root so the same relative setting means the same
    /// directory whether the process was started by <c>dotnet run</c>, by Docker, or
    /// by a test host.
    /// </summary>
    private static string ResolveKeysPath(IConfiguration configuration, IHostEnvironment environment)
    {
        var keysPath = configuration[ConfigurationKeys.DataProtectionKeysPath]
            ?? throw new InvalidOperationException(
                $"{ConfigurationKeys.DataProtectionKeysPath} is not configured.");

        return Path.IsPathRooted(keysPath)
            ? keysPath
            : Path.Combine(environment.ContentRootPath, keysPath);
    }
}

public static class ConfigurationKeys
{
    public const string AppOrigin = "App:Origin";

    public const string DataProtectionKeysPath = "DataProtection:KeysPath";
}
