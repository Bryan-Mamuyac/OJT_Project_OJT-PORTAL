using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using ITPMS_OJT.Models;
using Dapper;
using System.Security.Claims;

namespace ITPMS_OJT.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly IConfiguration _config;

        public AdminController(IConfiguration config)
        {
            _config = config;
        }

        private SqlConnection GetConnection() =>
            new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        private static readonly string[] Departments = {
            "IT","HR","GLL","SSDG","Accounting","Treasury","TMG",
            "Legal Service","Production","Electrical Engineer",
            "Mechanical Engineer","Agronomy"
        };
        private static readonly string[] Branches = {
            "Agoo", "Reina Mercedes", "Candon", "Pasig"
        };

        // ===================== DASHBOARD =====================
        public async Task<IActionResult> Index(string? department = null, string? branch = null)
        {
            using var conn = GetConnection();

            var allUsers = (await conn.QueryAsync<User>(
                "SELECT * FROM Users WHERE Role != 'Admin'")).ToList();

            var deptUsers = string.IsNullOrEmpty(branch)
                ? allUsers
                : allUsers.Where(u => u.Branch == branch).ToList();

            var deptStats = deptUsers
                .GroupBy(u => u.Department)
                .Select(g => new DepartmentStat
                {
                    Department = g.Key,
                    OJTCount = g.Count(u => u.Role == "OJT" && (u.Status == "Active" || u.Status == "Approved")),
                    EmployeeCount = g.Count(u => u.Role == "Employee" && (u.Status == "Active" || u.Status == "Approved"))
                })
                .OrderBy(d => d.Department)
                .ToList();

            int totalTasks = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Tasks");

            var recentUsers = allUsers
                .Where(u => string.IsNullOrEmpty(department) || u.Department == department)
                .Where(u => string.IsNullOrEmpty(branch) || u.Branch == branch)
                .OrderByDescending(u => u.CreatedAt)
                .Take(20)
                .ToList();

            await SetAdminProfilePic(conn);

            return View(new AdminDashboardViewModel
            {
                TotalOJTs = allUsers.Count(u => u.Role == "OJT" && ((u.Status == "Active" || u.Status == "Approved") || u.Status == "Approved")),
                TotalEmployees = allUsers.Count(u => u.Role == "Employee" && ((u.Status == "Active" || u.Status == "Approved") || u.Status == "Approved")),
                TotalTasks = totalTasks,
                PendingApprovals = 0,
                ApprovedUsers = allUsers.Count(u => ((u.Status == "Active" || u.Status == "Approved") || u.Status == "Approved")),
                RejectedUsers = allUsers.Count(u => u.Status == "Inactive"),
                DepartmentStats = deptStats,
                PendingUsers = new List<User>(),
                RecentUsers = recentUsers,
                SelectedDepartment = department,
                SelectedBranch = branch
            });
        }

        // ===================== ALL USERS =====================
        public async Task<IActionResult> AllUsers(
            string? department = null, string? role = null, string? status = null, string? branch = null)
        {
            using var conn = GetConnection();

            string sql = "SELECT * FROM Users WHERE Role != 'Admin'";
            var p = new DynamicParameters();

            if (!string.IsNullOrEmpty(department)) { sql += " AND Department = @Department"; p.Add("Department", department); }
            if (!string.IsNullOrEmpty(role)) { sql += " AND Role = @Role"; p.Add("Role", role); }
            if (!string.IsNullOrEmpty(status)) { sql += " AND (Status = @Status OR (@Status = 'Active' AND Status = 'Approved'))"; p.Add("Status", status); }
            if (!string.IsNullOrEmpty(branch)) { sql += " AND Branch = @Branch"; p.Add("Branch", branch); }

            sql += " ORDER BY CreatedAt DESC";

            var users = (await conn.QueryAsync<User>(sql, p)).ToList();

            ViewBag.Department = department;
            ViewBag.Role = role;
            ViewBag.Status = status;
            ViewBag.Branch = branch;
            ViewBag.Branches = Branches;

            return View(users);
        }

        // ===================== REGISTER USER (GET) =====================
        [HttpGet]
        public IActionResult RegisterUser()
        {
            ViewBag.Departments = Departments;
            ViewBag.Branches = Branches;
            return View(new AdminRegisterViewModel());
        }

        // ===================== REGISTER USER (POST) =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterUser(AdminRegisterViewModel model)
        {
            ViewBag.Departments = Departments;
            ViewBag.Branches = Branches;

            if (!ModelState.IsValid) return View(model);

            if (model.Role != "OJT" && model.Role != "Employee")
            {
                ModelState.AddModelError("Role", "Please select a valid role.");
                return View(model);
            }

            // Auto-generate username: firstname_lastname (lowercase, spaces→nothing)
            string baseUsername = (model.FirstName.Trim().ToLower().Replace(" ", "") + "_"
                                 + model.LastName.Trim().ToLower().Replace(" ", ""));
            string username = baseUsername;

            using var conn = GetConnection();

            // Handle duplicate username by appending a number
            int suffix = 1;
            while (await conn.QueryFirstOrDefaultAsync<User>(
                       "SELECT UserId FROM Users WHERE Username = @Username",
                       new { Username = username }) != null)
            {
                username = baseUsername + suffix++;
            }

            // Check email uniqueness
            var existingEmail = await conn.QueryFirstOrDefaultAsync<User>(
                "SELECT UserId FROM Users WHERE Email = @Email", new { model.Email });
            if (existingEmail != null)
            {
                ModelState.AddModelError("Email", "That email is already registered.");
                return View(model);
            }

            // Default password: "password123", user must change on first login
            string passwordHash = BCrypt.Net.BCrypt.HashPassword("password123");

            await conn.ExecuteAsync(
                @"INSERT INTO Users
                    (Username, PasswordHash, Email, FirstName, LastName, Role, Department, Branch, Status, MustChangePassword)
                  VALUES
                    (@Username, @PasswordHash, @Email, @FirstName, @LastName, @Role, @Department, @Branch, 'Active', 1)",
                new
                {
                    Username = username,
                    PasswordHash = passwordHash,
                    model.Email,
                    model.FirstName,
                    model.LastName,
                    model.Role,
                    model.Department,
                    model.Branch
                });

            TempData["SuccessMessage"] =
                $"Account created for {model.FirstName} {model.LastName}. " +
                $"Username: <strong>{username}</strong> · Default password: <strong>password123</strong>";

            return RedirectToAction("RegisterUser");
        }

        // ===================== EDIT USER (GET) =====================
        [HttpGet]
        public async Task<IActionResult> EditUser(int id)
        {
            using var conn = GetConnection();
            var user = await conn.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE UserId = @Id AND Role != 'Admin'",
                new { Id = id });
            if (user == null) return NotFound();

            ViewBag.Departments = Departments;
            ViewBag.Branches = Branches;
            return View(new AdminEditUserViewModel
            {
                UserId = user.UserId,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                Department = user.Department,
                Branch = user.Branch,
                Status = user.Status
            });
        }

        // ===================== EDIT USER (POST) =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(AdminEditUserViewModel model)
        {
            ViewBag.Departments = Departments;
            ViewBag.Branches = Branches;
            if (!ModelState.IsValid) return View(model);

            using var conn = GetConnection();

            // Check email uniqueness (excluding current user)
            var emailConflict = await conn.QueryFirstOrDefaultAsync<User>(
                "SELECT UserId FROM Users WHERE Email = @Email AND UserId != @UserId",
                new { model.Email, model.UserId });
            if (emailConflict != null)
            {
                ModelState.AddModelError("Email", "That email is already used by another account.");
                return View(model);
            }

            // Regenerate username if name changed
            string baseUsername = (model.FirstName.Trim().ToLower().Replace(" ", "") + "_"
                                 + model.LastName.Trim().ToLower().Replace(" ", ""));
            string username = baseUsername;
            int suffix = 1;
            while (await conn.QueryFirstOrDefaultAsync<User>(
                       "SELECT UserId FROM Users WHERE Username = @Username AND UserId != @UserId",
                       new { Username = username, model.UserId }) != null)
            {
                username = baseUsername + suffix++;
            }

            await conn.ExecuteAsync(
                @"UPDATE Users SET
                    FirstName  = @FirstName,
                    LastName   = @LastName,
                    Username   = @Username,
                    Email      = @Email,
                    Role       = @Role,
                    Department = @Department,
                    Branch     = @Branch,
                    Status     = @Status,
                    UpdatedAt  = GETDATE()
                  WHERE UserId = @UserId AND Role != 'Admin'",
                new
                {
                    model.FirstName,
                    model.LastName,
                    Username = username,
                    model.Email,
                    model.Role,
                    model.Department,
                    model.Branch,
                    model.Status,
                    model.UserId
                });

            TempData["SuccessMessage"] = $"User '{model.FirstName} {model.LastName}' updated successfully.";
            return RedirectToAction("AllUsers");
        }

        // ===================== RESET PASSWORD =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(int userId)
        {
            using var conn = GetConnection();
            var user = await conn.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE UserId = @UserId AND Role != 'Admin'",
                new { UserId = userId });
            if (user == null) return NotFound();

            string newHash = BCrypt.Net.BCrypt.HashPassword("password123");
            await conn.ExecuteAsync(
                @"UPDATE Users SET PasswordHash = @Hash, MustChangePassword = 1, UpdatedAt = GETDATE()
                  WHERE UserId = @UserId",
                new { Hash = newHash, UserId = userId });

            TempData["SuccessMessage"] =
                $"Password for '{user.FullName}' reset to <strong>password123</strong>. " +
                "They will be prompted to change it on next login.";
            return RedirectToAction("AllUsers");
        }

        // ===================== REMOVE USER =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveUser(int userId)
        {
            using var conn = GetConnection();
            var user = await conn.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE UserId = @UserId AND Role != 'Admin'",
                new { UserId = userId });
            if (user == null) return NotFound();

            await conn.ExecuteAsync(
                @"DELETE ts FROM TaskSubmissions ts
                  JOIN Tasks t ON ts.TaskId = t.TaskId
                  WHERE t.AssignedToUserId = @UserId OR t.AssignedByUserId = @UserId
                     OR ts.OJTUserId = @UserId",
                new { UserId = userId });
            await conn.ExecuteAsync(
                "DELETE FROM Tasks WHERE AssignedToUserId = @UserId OR AssignedByUserId = @UserId",
                new { UserId = userId });
            await conn.ExecuteAsync(
                "DELETE FROM Users WHERE UserId = @UserId AND Role != 'Admin'",
                new { UserId = userId });

            TempData["SuccessMessage"] = $"User '{user.FullName}' has been removed.";
            return RedirectToAction("AllUsers");
        }
        private async Task SetAdminProfilePic(SqlConnection conn)
        {
            int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
            var pic = await conn.ExecuteScalarAsync<string>(
                "SELECT ProfilePicture FROM Users WHERE UserId=@UserId", new { UserId = adminId });
            ViewBag.ProfilePicUrl = string.IsNullOrEmpty(pic) ? null : $"/uploads/profiles/{pic}";
        }

    }
}