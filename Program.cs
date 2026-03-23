using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.SqlClient;
using ITPMS_OJT.Hubs;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();                          // ← SignalR added
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
var app = builder.Build();
// Always seed / fix the Admin account on startup
await SeedAdminUser(builder.Configuration.GetConnectionString("DefaultConnection")!);
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");
app.MapHub<ChatHub>("/chatHub");                        // ← SignalR added
app.Run();
// ===================== SEED ADMIN =====================
async Task SeedAdminUser(string connectionString)
{
    try
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        var checkCmd = new SqlCommand(
            "SELECT COUNT(*) FROM Users WHERE Username = 'Admin'", conn);
        var count = (int)(await checkCmd.ExecuteScalarAsync())!;
        string correctHash = BCrypt.Net.BCrypt.HashPassword("admin987654321");
        if (count == 0)
        {
            var insert = new SqlCommand(
                @"INSERT INTO Users
                    (Username, PasswordHash, Email, FirstName, LastName, Role, Department, Status)
                  VALUES
                    ('Admin', @Hash, 'admin@itpms.com', 'System', 'Administrator', 'Admin', 'IT', 'Approved')",
                conn);
            insert.Parameters.AddWithValue("@Hash", correctHash);
            await insert.ExecuteNonQueryAsync();
            Console.WriteLine("? Admin account created. Username: Admin | Password: admin987654321");
        }
        else
        {
            var update = new SqlCommand(
                "UPDATE Users SET PasswordHash = @Hash, Status = 'Approved' WHERE Username = 'Admin'",
                conn);
            update.Parameters.AddWithValue("@Hash", correctHash);
            await update.ExecuteNonQueryAsync();
            Console.WriteLine("? Admin password re-synced. Username: Admin | Password: admin987654321");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"? Seed warning: {ex.Message}");
    }
}