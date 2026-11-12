using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Services;
using PedeAqui.Domain.Tokens;
using PedeAqui.Infrastructure.Database;

namespace PedeAqui.Infrastructure.Services;

internal class LoggedUser : ILoggedUser
{
    private readonly PedeAquiDbContext _dbContext;
    private readonly ITokenProvider _tokenProvider;

    public LoggedUser(PedeAquiDbContext dbContext, ITokenProvider tokenProvider)
    {
        _dbContext = dbContext;
        _tokenProvider = tokenProvider;
    }

    public async Task<User> Get(CancellationToken cancellationToken)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(_tokenProvider.TokenOnRequest());
        var identifier = Guid.Parse(token.Claims.First(claim => claim.Type == ClaimTypes.Sid).Value);

        return await _dbContext.Users
            .AsNoTracking()
            .FirstAsync(user => user.UserIdentifier == identifier, cancellationToken);
    }
}
