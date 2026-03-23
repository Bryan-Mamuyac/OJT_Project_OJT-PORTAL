using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using ITPMS_OJT.Models;
using Dapper;

namespace ITPMS_OJT.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AccountController> _logger;
        private readonly IWebHostEnvironment _env;

        public AccountController(IConfiguration config, ILogger<AccountController> logger, IWebHostEnvironment env)
        {
            _config = config;
            _logger = logger;
            _env = env;
        }

        private SqlConnection GetConnection() =>
            new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        // ===================== LOGIN =====================
        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToRoleDashboard();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            using var conn = GetConnection();
            var user = await conn.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Username = @Username",
                new { model.Username });

            if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            if (user.Status == "Inactive")
            {
                ModelState.AddModelError("", "Your account is inactive. Please contact the administrator.");
                return View(model);
            }

            // Sign in
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("FullName", user.FullName),
                new Claim("Department", user.Department),
                new Claim("Branch", user.Branch),
                new Claim("UserId", user.UserId.ToString())
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

            // Redirect to forced password change if admin-created account
            if (user.MustChangePassword)
                return RedirectToAction("ChangePassword");

            return RedirectToRoleDashboard();
        }

        // ===================== FORCED PASSWORD CHANGE =====================
        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            int userId = int.Parse(User.FindFirstValue("UserId") ?? "0");
            string newHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);

            using var conn = GetConnection();
            await conn.ExecuteAsync(
                @"UPDATE Users
                  SET PasswordHash = @Hash, MustChangePassword = 0, UpdatedAt = GETDATE()
                  WHERE UserId = @UserId",
                new { Hash = newHash, UserId = userId });

            TempData["SuccessMessage"] = "Password changed successfully. Welcome!";
            return RedirectToRoleDashboard();
        }

        // ===================== LOGOUT =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        public IActionResult AccessDenied() => View();

        // ===================== PROFILE PICTURE =====================
        [HttpPost, Authorize, ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadProfilePicture(IFormFile photo)
        {
            if (photo == null || photo.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select a valid image.";
                return Redirect(Request.Headers["Referer"].ToString());
            }
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
            {
                TempData["ErrorMessage"] = "Only JPG, PNG, GIF or WEBP images are allowed.";
                return Redirect(Request.Headers["Referer"].ToString());
            }
            if (photo.Length > 5 * 1024 * 1024)
            {
                TempData["ErrorMessage"] = "Image must be under 5MB.";
                return Redirect(Request.Headers["Referer"].ToString());
            }

            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
            var dir = Path.Combine(_env.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(dir);

            // Delete old picture if exists
            using var conn = GetConnection();
            var oldPic = await conn.ExecuteScalarAsync<string>(
                "SELECT ProfilePicture FROM Users WHERE UserId=@UserId", new { UserId = userId });
            if (!string.IsNullOrEmpty(oldPic))
            {
                var oldPath = Path.Combine(dir, oldPic);
                if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
            }

            // Save new picture
            var fileName = $"user_{userId}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(dir, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
                await photo.CopyToAsync(stream);

            await conn.ExecuteAsync(
                "UPDATE Users SET ProfilePicture=@Pic, UpdatedAt=GETDATE() WHERE UserId=@UserId",
                new { Pic = fileName, UserId = userId });

            TempData["SuccessMessage"] = "Profile picture updated!";
            return Redirect(Request.Headers["Referer"].ToString());
        }

        [HttpPost, Authorize, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProfilePicture()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
            using var conn = GetConnection();
            var oldPic = await conn.ExecuteScalarAsync<string>(
                "SELECT ProfilePicture FROM Users WHERE UserId=@UserId", new { UserId = userId });
            if (!string.IsNullOrEmpty(oldPic))
            {
                var path = Path.Combine(_env.WebRootPath, "uploads", "profiles", oldPic);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                await conn.ExecuteAsync(
                    "UPDATE Users SET ProfilePicture=NULL, UpdatedAt=GETDATE() WHERE UserId=@UserId",
                    new { UserId = userId });
            }
            TempData["SuccessMessage"] = "Profile picture removed.";
            return Redirect(Request.Headers["Referer"].ToString());
        }



        // ===================== HELPER =====================
        private IActionResult RedirectToRoleDashboard()
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            return role switch
            {
                "Admin" => RedirectToAction("Index", "Admin"),
                "Employee" => RedirectToAction("Index", "Employee"),
                "OJT" => RedirectToAction("Index", "OJT"),
                _ => RedirectToAction("Login", "Account")
            };
        }
    }
}