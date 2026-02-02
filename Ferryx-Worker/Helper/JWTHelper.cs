using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Ferryx_Worker.Helper
{
    public static class JWTHelper
    {
        public static string CreateJwtFromKey(string jwtKey)
        {
            var keyBytes = Encoding.UTF8.GetBytes(jwtKey); // Hub da aynı mantıkla doğruluyor
            var signingKey = new SymmetricSecurityKey(keyBytes);
            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var jwt = new JwtSecurityToken(
                claims: new[] { new Claim("sub", "ferryx-worker") },
                expires: DateTime.UtcNow.AddYears(1),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}
