using System.Security.Cryptography;
using Identity.Api.Data;
using Identity.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Auth;
using Shared.Contracts.Constants;
using Shared.Contracts.Events;
using Shared.Infrastructure.Outbox;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(IdentityDbContext db, JwtTokenService jwt) : ControllerBase
{
    private const int OtpMinutes = 5, OtpMaxFails = 5, LoginMaxFails = 5, LockMinutes = 15;

    // ---------- Đăng ký ----------
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (req.Role is not (Roles.Freelancer or Roles.Employer))
            return BadRequest(new { error = "Vai trò chỉ được là Freelancer hoặc Employer." });
        if (!PasswordRules.IsValid(req.Password))
            return BadRequest(new { error = PasswordRules.Message });
        if (await db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "Email đã được đăng ký. Nếu chưa xác minh, hãy dùng chức năng gửi lại OTP." });

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role,
            Profile = new Profile { FullName = req.FullName.Trim() }
        };
        db.Users.Add(user);
        IssueOtp(user.Id, email);
        await db.SaveChangesAsync();   // người dùng + OTP + Outbox cùng một transaction

        return StatusCode(201, new { userId = user.Id, message = "Đăng ký thành công. Vui lòng kiểm tra email để lấy mã OTP." });
    }

    // ---------- Xác minh OTP ----------
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(VerifyOtpRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null) return BadRequest(new { error = "Mã OTP không hợp lệ." });
        if (user.IsVerified) return Ok(new { message = "Tài khoản đã được xác minh." });

        var otp = await db.EmailOtps.Where(o => o.UserId == user.Id && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();
        if (otp is null || otp.ExpiresAt < DateTime.UtcNow)
            return BadRequest(new { error = "Mã OTP đã hết hạn. Vui lòng yêu cầu gửi lại." });

        if (!BCrypt.Net.BCrypt.Verify(req.Otp, otp.CodeHash))
        {
            otp.FailedCount++;
            if (otp.FailedCount >= OtpMaxFails) otp.IsUsed = true;
            await db.SaveChangesAsync();
            return BadRequest(new
            {
                error = otp.IsUsed ? "Nhập sai quá 5 lần. Vui lòng yêu cầu gửi lại mã." : "Mã OTP không đúng."
            });
        }

        otp.IsUsed = true;
        user.IsVerified = true;
        user.Status = UserStatus.Active;
        await db.SaveChangesAsync();
        return Ok(new { message = "Xác minh email thành công. Bạn có thể đăng nhập." });
    }

    // ---------- Gửi lại OTP ----------
    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp(ResendOtpRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null && !user.IsVerified)
        {
            await db.EmailOtps.Where(o => o.UserId == user.Id && !o.IsUsed)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsUsed, true));
            IssueOtp(user.Id, email);
            await db.SaveChangesAsync();
        }
        // Luôn trả cùng một thông báo để không lộ email nào đã đăng ký.
        return Ok(new { message = "Nếu email hợp lệ và chưa xác minh, mã OTP mới đã được gửi." });
    }

    // ---------- Đăng nhập ----------
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Email == email);
        const string bad = "Email hoặc mật khẩu không đúng.";
        if (user is null) return Unauthorized(new { error = bad });

        if (user.LockoutEnd > DateTime.UtcNow)
            return StatusCode(StatusCodes.Status423Locked,
                new { error = $"Tài khoản tạm khóa do đăng nhập sai nhiều lần. Thử lại sau {Math.Ceiling((user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes)} phút." });

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= LoginMaxFails)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(LockMinutes);
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync();
            return Unauthorized(new { error = bad });
        }

        if (user.Status == UserStatus.Locked)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Tài khoản đã bị khóa bởi quản trị viên." });
        if (!user.IsVerified)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Email chưa được xác minh." });

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        await db.SaveChangesAsync();

        var fullName = user.Profile?.FullName ?? user.Email;
        var (token, expires) = jwt.Create(user, fullName);
        return Ok(new AuthResponse
        {
            Token = token, ExpiresAt = expires, UserId = user.Id,
            Email = user.Email, FullName = fullName, Role = user.Role
        });
    }

    // Tạo OTP 6 chữ số: lưu băm trong EmailOtps, phát sự kiện qua Outbox (cùng transaction với SaveChanges).
    private void IssueOtp(Guid userId, string email)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.EmailOtps.Add(new EmailOtp
        {
            UserId = userId,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpMinutes)
        });
        db.AddOutbox(new UserRegisteredEvent(userId, email, code));
    }
}
