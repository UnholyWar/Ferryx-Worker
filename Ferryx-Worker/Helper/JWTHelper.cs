using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Ferryx_Worker.Helper
{
    public static class JWTHelper
    {
        public static string CreateJwtFromKey(string jwtKey)
        {
            // Hub artık base64 decode ile doğruluyor -> worker da base64 decode ile imzalamalı
            var keyBytes = Convert.FromBase64String(jwtKey);

            var signingKey = new SymmetricSecurityKey(keyBytes);
            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var jwt = new JwtSecurityToken(
                claims: new[] { new Claim("sub", "ferryx-worker") },
                expires: DateTime.UtcNow.AddYears(10),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}
