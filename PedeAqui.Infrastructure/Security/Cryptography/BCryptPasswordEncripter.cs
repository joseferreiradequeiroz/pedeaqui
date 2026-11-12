using PedeAqui.Domain.Security;
using BC = BCrypt.Net.BCrypt;

namespace PedeAqui.Infrastructure.Security.Cryptography;

internal class BCryptPasswordEncripter : IPasswordEncripter
{
    public string Encrypt(string password) => BC.HashPassword(password);

    public bool Verify(string password, string passwordHash) => BC.Verify(password, passwordHash);
}
