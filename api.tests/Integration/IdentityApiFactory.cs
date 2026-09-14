using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The Google account <see cref="TestGoogleHandler"/> will present on the next
/// callback. Mutable so one factory can drive several sign-ins in sequence, which is
/// what the "same subject twice" and "same e-mail, different subject" cases need.
/// </summary>
internal sealed class TestGoogleAccount
{
    public string Subject { get; set; } = "";

    public string Email { get; set; } = "";

    public string? DisplayName { get; set; }

    public bool EmailVerified { get; set; } = true;
}

/// <summary>
/// Stands in for the half of the Google flow that needs Google: the authorization
/// code exchange. Everything after it — the <c>email_verified</c> check, the lookup
/// by external login, the lookup by e-mail, the linking, the user creation and the
/// cookie — is the real production code, because this is still a
/// <see cref="GoogleHandler"/> driving the real <see cref="GoogleOptions"/> and its
/// <c>OnTicketReceived</c> callback.
/// </summary>
internal sealed class TestGoogleHandler(
    IOptionsMonitor<GoogleOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TestGoogleAccount account)
    : GoogleHandler(options, logger, encoder)
{
    protected override Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        // Google reports a refused consent, or a flow that broke on its side, as a
        // query parameter on the callback — before any code exchange. The real handler
        // turns that into a failed result, and so does this one, so the production
        // OnRemoteFailure runs against it.
        var error = Request.Query["error"].ToString();

        if (!string.IsNullOrEmpty(error))
        {
            return Task.FromResult(HandleRequestResult.Fail($"Google returned {error}."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Subject),
            new(ClaimTypes.Email, account.Email),

            // Google sends this as a JSON boolean; GoogleOptions maps it to a claim
            // whose value is the lowercase string, so that is what is asserted on.
            new("email_verified", account.EmailVerified ? "true" : "false"),
        };

        if (account.DisplayName is not null)
        {
            claims.Add(new Claim(ClaimTypes.Name, account.DisplayName));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { RedirectUri = "/" },
            Scheme.Name);

        return Task.FromResult(HandleRequestResult.Success(ticket));
    }
}

/// <summary>
/// Drives the real pipeline against a real PostgreSQL, with the Google scheme's
/// handler swapped for <see cref="TestGoogleHandler"/>.
/// </summary>
/// <remarks>
/// The base address is <c>https://</c> on purpose: the session cookie is issued with
/// <c>Secure</c>, and a cookie container will not send one back over <c>http</c>. The
/// alternative — relaxing <c>CookieSecurePolicy</c> under test — would leave the
/// production cookie policy untested.
/// </remarks>
internal sealed class IdentityApiFactory(
    string connectionString,
    string? keysPath = null,
    string environment = "Development")
    : WebApplicationFactory<Program>
{
    /// <summary>The origin the Origin check is configured to accept.</summary>
    public const string AppOrigin = "https://localhost";

    public const string SessionCookieName = ".AspNetCore.Identity.Application";

    private readonly string _keysPath = keysPath
        ?? Path.Combine(Path.GetTempPath(), "finance-app-tests", Guid.NewGuid().ToString("N"));

    public TestGoogleAccount GoogleAccount { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["App:Origin"] = AppOrigin,
                ["DataProtection:KeysPath"] = _keysPath,

                // The real handler is never asked to talk to Google, but OAuthOptions
                // validates that both are present before it will run at all.
                ["Google:ClientId"] = "test-client-id",
                ["Google:ClientSecret"] = "test-client-secret",
            }));

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(GoogleAccount);

            // Constructed by a delegate rather than by type so the Development-time
            // ValidateOnBuild pass does not need UrlEncoder in the container. It is
            // the same instance AddWebEncoders would hand over for default options.
            services.AddTransient(serviceProvider => new TestGoogleHandler(
                serviceProvider.GetRequiredService<IOptionsMonitor<GoogleOptions>>(),
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                UrlEncoder.Default,
                serviceProvider.GetRequiredService<TestGoogleAccount>()));

            // Only the handler type is replaced. The scheme, its options and its
            // events stay exactly as Program.cs registered them.
            services.Configure<AuthenticationOptions>(options =>
                options.SchemeMap[GoogleDefaults.AuthenticationScheme].HandlerType =
                    typeof(TestGoogleHandler));
        });
    }

    /// <summary>
    /// A client that does not follow redirects, because the redirect target is the
    /// assertion in most of these tests.
    /// </summary>
    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(AppOrigin),
            AllowAutoRedirect = false,
        });

    /// <summary>Signs a client in through the development-only endpoint.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(
        string email,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var client = CreateApiClient();

        using var response = await client.SendAsync(
            AuthRequests.DevLogin(email, displayName),
            cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);

        return client;
    }

    /// <summary>
    /// The <c>name=value</c> pair of the session cookie in a response, without its
    /// attributes — ready to be replayed as a <c>Cookie</c> request header.
    /// </summary>
    public static string ReadSessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Select(header => header.Split(';', 2)[0])
            .Single(pair => pair.StartsWith(SessionCookieName + "=", StringComparison.Ordinal));
}

internal static class AuthRequests
{
    public static HttpRequestMessage DevLogin(
        string email,
        string? displayName,
        string? origin = IdentityApiFactory.AppOrigin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/dev-login")
        {
            Content = JsonContent.Create(new { email, displayName }),
        };

        return WithOrigin(request, origin);
    }

    public static HttpRequestMessage Logout(string? origin = IdentityApiFactory.AppOrigin) =>
        WithOrigin(new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"), origin);

    private static HttpRequestMessage WithOrigin(HttpRequestMessage request, string? origin)
    {
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }
}
