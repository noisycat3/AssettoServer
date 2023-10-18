using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using AssettoServer.Network.Tcp;
using AssettoServer.Shared.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssettoServer.Network.Http.Authentication;

public class ACClientAuthenticationHandler : AuthenticationHandler<ACClientAuthenticationSchemeOptions>
{
    private const string CarIdHeader = "X-Car-Id";
    private const string ApiKeyHeader = "X-Api-Key";
    
    private readonly IACServer _acServer;
    
    public ACClientAuthenticationHandler(IOptionsMonitor<ACClientAuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IACServer acServer) : base(options, logger, encoder)
    {
        _acServer = acServer;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(CarIdHeader, out var carIdHdr) 
            || !Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyHdr))
        {
            return Task.FromResult(AuthenticateResult.Fail("Header Not Found."));
        }

        if (!int.TryParse(carIdHdr, out var carId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid car id."));
        }
        
        string apiKey = apiKeyHdr.ToString();
        IEntryCar car = _acServer.GetCarBySessionId((byte)carId);
        if (car.Client is ACTcpClient { ApiKey: {} clientKey } acClient && clientKey == apiKey)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, acClient.Guid.ToString()),
                new(ClaimTypes.Name, acClient.Name!)
            };

            if (acClient.IsAdministrator)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
            }

            var claimsIdentity = new ACClientClaimsIdentity(claims, nameof(ACClientAuthenticationHandler)) { Client = acClient };
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        
        return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
    }
}
