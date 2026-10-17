using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Ticketa.Core.DTOs;
using Ticketa.Core.Entities;
using Ticketa.Core.Interfaces.IServices;
using Ticketa.Core.Settings;
using Ticketa.Infrastructure.Data;

namespace Ticketa.Infrastructure.Service
{
  public class AuthApiService(
      IEmailService emailService,
      ITokenService tokenService,
      UserManager<AppUser> userManager,
      RoleManager<AppRole> roleManager,
      IOptions<JwtSettings> jwtSettings,
      IConfiguration config,
      ApplicationDbContext? context = null) : IAuthApiService
  {
    private readonly IEmailService _emailService = emailService;
    private readonly ITokenService _tokenService = tokenService;
    private readonly UserManager<AppUser> _userManager = userManager;
    private readonly RoleManager<AppRole> _roleManager = roleManager;
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;
    private readonly IConfiguration _config = config;
    private readonly ApplicationDbContext? _context = context;

    public async Task<AuthResultDto> ConfirmEmailAsync(ConfirmEmailDto dto, CancellationToken ct = default)
    {
      var user = await _userManager.FindByEmailAsync(dto.Email);

      if (user is null || user.VerificationCode != dto.Code || user.VerificationCodeExpiry < DateTime.UtcNow)
        return AuthResultDto.Failure("Invalid or expired verification code");

      user.EmailConfirmed = true;
      user.VerificationCode = null;
      user.VerificationCodeExpiry = null;
      await _userManager.UpdateAsync(user);

      var roles = await _userManager.GetRolesAsync(user);
      var token = _tokenService.GenerateAccessToken(user, roles);
      var refreshToken = _tokenService.GenerateRefreshToken();

      user.RefreshToken = refreshToken;
      user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDate);
      await _userManager.UpdateAsync(user);

      var userDto = new UserDto { Id = user.Id, Email = user.Email!, Name = user.UserName! };
      return AuthResultDto.Success(token, refreshToken);
    }

    public async Task<AuthResultDto> GoogleAuthAsync(string idToken, CancellationToken ct = default)
    {
      GoogleJsonWebSignature.Payload payload;
      try
      {
        payload = await GoogleJsonWebSignature.ValidateAsync(idToken, GetGoogleValidationSettings());
      }
      catch
      {
        return AuthResultDto.Failure("Invalid Google token.");
      }

      var user = await _userManager.FindByEmailAsync(payload.Email);

      if (user is null)
      {
        user = new AppUser
        {
          UserName = payload.Email,
          Email = payload.Email,
          EmailConfirmed = true,
          Theme = "light",
          FirstName = payload.GivenName ?? "",
          LastName = payload.FamilyName ?? ""
        };
        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
          return AuthResultDto.Failure("Failed to create user account.");
      }
      else
      {
        var namesChanged = false;
        if (!string.IsNullOrEmpty(payload.GivenName) && user.FirstName != payload.GivenName)
        {
          user.FirstName = payload.GivenName;
          namesChanged = true;
        }
        if (!string.IsNullOrEmpty(payload.FamilyName) && user.LastName != payload.FamilyName)
        {
          user.LastName = payload.FamilyName;
          namesChanged = true;
        }
        if (!user.EmailConfirmed || namesChanged)
        {
          user.EmailConfirmed = true;
          await _userManager.UpdateAsync(user);
        }
      }

      return await IssueTokensAsync(user);
    }

    public async Task<AuthResultDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
      var user = await _userManager.FindByEmailAsync(dto.Email);

      if (user is null || !await _userManager.CheckPasswordAsync(user, dto.Password))
        return AuthResultDto.Failure("Invalid email or password");

      if (!user.EmailConfirmed)
        return AuthResultDto.Failure("Email not confirmed. Please check your inbox.");

      return await IssueTokensAsync(user);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
      if (string.IsNullOrWhiteSpace(refreshToken)) return;

      var user = _userManager.Users.FirstOrDefault(u => u.RefreshToken == refreshToken);

      if (user is null)
        return;

      user.RefreshToken = null;
      user.RefreshTokenExpiry = null;
      await _userManager.UpdateAsync(user);
    }

    public async Task<(bool success, string? Error)> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
      var existing = await _userManager.FindByEmailAsync(dto.Email);

      if (existing is not null)
      {
        if (existing.EmailConfirmed)
          return (false, "Email is already registered and confirmed.");

        await SendVerificationCodeAsync(existing);
        return (true, null);
      }

      var user = new AppUser
      {
        UserName = dto.Email,
        Email = dto.Email,
        FirstName = dto.FirstName,
        LastName = dto.LastName,
        DateOfBirth = dto.DateOfBirth,
        Theme = "light"
      };

      var result = await _userManager.CreateAsync(user, dto.Password);

      if (!result.Succeeded)
      {
        var errors = result.Errors.First().Description;
        return (false, errors);
      }

      await SendVerificationCodeAsync(user);
      return (true, null);
    }

    public async Task<AuthResultDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
      var user = _userManager.Users.FirstOrDefault(u => u.RefreshToken == refreshToken);

      if (user is null || user.RefreshTokenExpiry < DateTime.UtcNow)
        return AuthResultDto.Failure("Invalid or expired refresh token");

      return await IssueTokensAsync(user);
    }

    public async Task ResendEmailConfirmationAsync(string email, CancellationToken ct = default)
    {
      var user = await _userManager.FindByEmailAsync(email);

      if (user is null || user.EmailConfirmed) return;

      await SendVerificationCodeAsync(user);
    }

    public async Task<ExternalLoginResultDto> GoogleMobileAuthAsync(string idToken, CancellationToken ct = default)
    {
      GoogleJsonWebSignature.Payload payload;
      try
      {
        payload = await GoogleJsonWebSignature.ValidateAsync(idToken, GetGoogleValidationSettings());
      }
      catch
      {
        return new ExternalLoginResultDto { Message = "Invalid Google token." };
      }

      var user = await _userManager.FindByEmailAsync(payload.Email);
      var isNewUser = false;

      if (user is null)
      {
        user = new AppUser
        {
          UserName = payload.Email,
          Email = payload.Email,
          EmailConfirmed = true,
          Theme = "light",
          FirstName = payload.GivenName ?? "",
          LastName = payload.FamilyName ?? ""
        };
        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
          return new ExternalLoginResultDto { Message = "Failed to create user account." };
        isNewUser = true;
      }
      else
      {
        var namesChanged = false;
        if (!string.IsNullOrEmpty(payload.GivenName) && user.FirstName != payload.GivenName)
        {
          user.FirstName = payload.GivenName;
          namesChanged = true;
        }
        if (!string.IsNullOrEmpty(payload.FamilyName) && user.LastName != payload.FamilyName)
        {
          user.LastName = payload.FamilyName;
          namesChanged = true;
        }
        if (!user.EmailConfirmed || namesChanged)
        {
          user.EmailConfirmed = true;
          await _userManager.UpdateAsync(user);
        }
      }

      var (accessToken, refreshToken, refreshTokenExpiry) = await BuildTokensAsync(user);

      return new ExternalLoginResultDto
      {
        IsNewUser = isNewUser,
        HasPassword = await _userManager.HasPasswordAsync(user),
        Email = user.Email,
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        RefreshTokenExpiry = refreshTokenExpiry,
        Message = "Logged in successfully via Google"
      };
    }

    private async Task<AuthResultDto> IssueTokensAsync(AppUser user)
    {
      var (accessToken, refreshToken, _) = await BuildTokensAsync(user);
      return AuthResultDto.Success(accessToken, refreshToken);
    }

    private GoogleJsonWebSignature.ValidationSettings GetGoogleValidationSettings()
    {
      var audiences = new List<string>();
      if (!string.IsNullOrWhiteSpace(_config["Google:ClientId"]))
        audiences.Add(_config["Google:ClientId"]!);
      if (!string.IsNullOrWhiteSpace(_config["Google:IosClientId"]))
        audiences.Add(_config["Google:IosClientId"]!);
      if (!string.IsNullOrWhiteSpace(_config["Google:AndroidClientId"]))
        audiences.Add(_config["Google:AndroidClientId"]!);

      return new GoogleJsonWebSignature.ValidationSettings
      {
        Audience = audiences.Count > 0 ? audiences : null
      };
    }

    private async Task<(string AccessToken, string RefreshToken, DateTime RefreshTokenExpiry)> BuildTokensAsync(AppUser user)
    {
      List<string> roles;
      List<string> permissions;

      if (_context is not null)
      {
        roles = await (
            from ur in _context.UserRoles.AsNoTracking()
            where ur.UserId == user.Id
            join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
            select r.Name!
        ).ToListAsync();

        permissions = await (
            from ur in _context.UserRoles.AsNoTracking()
            where ur.UserId == user.Id
            join rc in _context.RoleClaims.AsNoTracking() on ur.RoleId equals rc.RoleId
            where rc.ClaimType == "permission" && rc.ClaimValue != null
            select rc.ClaimValue!
        ).Distinct().ToListAsync();
      }
      else
      {
        roles = (await _userManager.GetRolesAsync(user)).ToList();
        permissions = new List<string>();

        foreach (var roleName in roles)
        {
          var appRole = await _roleManager.FindByNameAsync(roleName);
          if (appRole is null) continue;
          var claims = await _roleManager.GetClaimsAsync(appRole);
          permissions.AddRange(claims.Where(c => c.Type == "permission").Select(c => c.Value));
        }
      }

      var accessToken = _tokenService.GenerateAccessToken(user, roles, permissions.Distinct().ToList());
      var refreshToken = _tokenService.GenerateRefreshToken();

      var refreshTokenExpiry = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDate);
      user.RefreshToken = refreshToken;
      user.RefreshTokenExpiry = refreshTokenExpiry;

      if (_context is not null)
      {
        await _context.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.RefreshToken, refreshToken)
                .SetProperty(u => u.RefreshTokenExpiry, refreshTokenExpiry));
      }
      else
      {
        await _userManager.UpdateAsync(user);
      }

      return (accessToken, refreshToken, refreshTokenExpiry);
    }

    private async Task SendVerificationCodeAsync(AppUser user)
    {
      var code = GenerateSixDigitCode();
      user.VerificationCode = code;
      user.VerificationCodeExpiry = DateTime.UtcNow.AddMinutes(15);
      await _userManager.UpdateAsync(user);

      var emailBody = EmailTemplates.VerificationCode(code);
      await _emailService.SendEmailAsync(user.Email!, "Verify Your Email", emailBody);
    }
    public async Task ForgetPasswordAsync(string email, CancellationToken ct = default)
    {
      var user = await _userManager.FindByEmailAsync(email);

      if (user is null || !user.EmailConfirmed) return;

      var token = await _userManager.GeneratePasswordResetTokenAsync(user);

      var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

      var clientUrl = _config["ClientSettings:BaseUrl"];

      var resetLink = $"{clientUrl}/reset-password" +
              $"?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(encodedToken)}";

      var html = EmailTemplates.PasswordReset(user.UserName!, resetLink);

      await _emailService.SendEmailAsync(user.Email!, "Reset Your Password", html);
    }

    public async Task<(bool Success, string? Error)> ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default)
    {
      var user = await _userManager.FindByEmailAsync(dto.Email);

      if (user is null)
        return (false, "Invalid email address.");

      string decodedToken;

      try
      {
        decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(dto.Token));
      }
      catch (Exception ex)
      {
        return (false, "Invalid token format.");
      }

      var result = await _userManager.ResetPasswordAsync(user, decodedToken, dto.NewPassword);

      if (!result.Succeeded)
      {
        return (false, result.Errors.First().Description);
      }

      return (true, null);
    }

    private static string GenerateSixDigitCode() => Random.Shared.Next(100_000, 999_999).ToString();

  }
}