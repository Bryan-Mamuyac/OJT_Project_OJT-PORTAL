using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using ITPMS_OJT.Models;
using Dapper;

namespace ITPMS_OJT.Controllers
{
    internal class EmpTaskDateRow
    {
        public DateTime Date { get; set; }
        public int TaskId { get; set; }
    }

    [Authorize(Roles = "Employee")]
    public class EmployeeController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public EmployeeController(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        private SqlConnection GetConnection() =>
            new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        private int GetCurrentUserId() =>
            int.Parse(User.FindFirstValue("UserId")!);

        private string GetCurrentDepartment() =>
            User.FindFirstValue("Department") ?? "";
        private string GetCurrentBranch() =>
            User.FindFirstValue("Branch") ?? "";

        private async Task SetSidebarCounts(SqlConnection conn, int userId)
        {
            var tasks = (await conn.QueryAsync<TaskItem>(
                "SELECT Status FROM Tasks WHERE AssignedByUserId = @UserId",
                new { UserId = userId })).ToList();

            ViewBag.EmpPendingCount = tasks.Count(t => t.Status == "Pending");
            ViewBag.EmpCompletedCount = tasks.Count(t => t.Status == "Approved");
            ViewBag.EmpTotalCount = tasks.Count(t => t.Status != "Cancelled");
            ViewBag.EmpPendingReviewCount = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM TaskSubmissions ts
                  JOIN Tasks t ON ts.TaskId = t.TaskId
                  WHERE t.AssignedByUserId = @UserId AND ts.Status = 'Submitted'",
                new { UserId = userId });
            var pic = await conn.ExecuteScalarAsync<string>("SELECT ProfilePicture FROM Users WHERE UserId=@UserId", new { UserId = userId });
            ViewBag.ProfilePicUrl = string.IsNullOrEmpty(pic) ? null : $"/uploads/profiles/{pic}";
            ViewBag.UnreadChatCount = await ChatController.GetUnreadChatCount(conn, userId);
        }

        // ===================== ANALYTICS API =====================
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetAnalytics(string range = "overall", int ojtUserId = 0, string branch = "")
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var _todayPH = DateTime.Today;

            List<EmpTaskDateRow> allAssigned;
            List<EmpTaskDateRow> allCompleted;
            DateTime? firstDate;

            // Build branch JOIN clause if filtering by branch
            string branchJoin = string.IsNullOrEmpty(branch) ? "" :
                " JOIN Users ub ON ub.UserId = t.AssignedToUserId AND ub.Branch = @Branch";
            string branchAlias = string.IsNullOrEmpty(branch) ? "" : " t";

            if (ojtUserId > 0)
            {
                firstDate = await conn.QueryFirstOrDefaultAsync<DateTime?>(
                    "SELECT MIN(CreatedAt) FROM Tasks WHERE AssignedByUserId = @UserId AND AssignedToUserId = @OjtId",
                    new { UserId = userId, OjtId = ojtUserId, Branch = branch });
                allAssigned = (await conn.QueryAsync<EmpTaskDateRow>(
                    "SELECT CAST(CreatedAt AS DATE) AS Date, TaskId FROM Tasks WHERE AssignedByUserId = @UserId AND AssignedToUserId = @OjtId",
                    new { UserId = userId, OjtId = ojtUserId, Branch = branch })).ToList();
                allCompleted = (await conn.QueryAsync<EmpTaskDateRow>(
                    "SELECT CAST(UpdatedAt AS DATE) AS Date, TaskId FROM Tasks WHERE AssignedByUserId = @UserId AND AssignedToUserId = @OjtId AND Status = 'Approved'",
                    new { UserId = userId, OjtId = ojtUserId, Branch = branch })).ToList();
            }
            else
            {
                string bFilter = string.IsNullOrEmpty(branch) ? "" : " AND AssignedToUserId IN (SELECT UserId FROM Users WHERE Branch = @Branch)";
                firstDate = await conn.QueryFirstOrDefaultAsync<DateTime?>(
                    "SELECT MIN(CreatedAt) FROM Tasks WHERE AssignedByUserId = @UserId" + bFilter,
                    new { UserId = userId, Branch = branch });
                allAssigned = (await conn.QueryAsync<EmpTaskDateRow>(
                    "SELECT CAST(CreatedAt AS DATE) AS Date, TaskId FROM Tasks WHERE AssignedByUserId = @UserId" + bFilter,
                    new { UserId = userId, Branch = branch })).ToList();
                allCompleted = (await conn.QueryAsync<EmpTaskDateRow>(
                    "SELECT CAST(UpdatedAt AS DATE) AS Date, TaskId FROM Tasks WHERE AssignedByUserId = @UserId AND Status = 'Approved'" + bFilter,
                    new { UserId = userId, Branch = branch })).ToList();
            }

            if (range == "overall")
            {
                if (firstDate == null || !allAssigned.Any())
                    return Json(new { labels = new List<string>(), totalData = new List<int>(), completedData = new List<int>(), summary = new { total = 0, completed = 0 } });

                var start = firstDate.Value.Date;
                var today = _todayPH;
                int spanDays = (int)(today - start).TotalDays + 1;
                int step = spanDays <= 14 ? 1 : spanDays <= 60 ? 3 : 7;

                var labels = new List<string>(); var totalData = new List<int>(); var completedData = new List<int>();
                var empBuckets = new List<DateTime>();
                for (var d = start; d <= today; d = d.AddDays(step)) empBuckets.Add(d);
                if (empBuckets.Count == 0 || empBuckets[empBuckets.Count - 1] < today) empBuckets.Add(today);
                for (int i = 0; i < empBuckets.Count; i++)
                {
                    var d = empBuckets[i];
                    var end = (i + 1 < empBuckets.Count) ? empBuckets[i + 1] : today.AddDays(1);
                    labels.Add(d.ToString("MMM dd"));
                    totalData.Add(allAssigned.Count(r => r.Date >= d && r.Date < end));
                    completedData.Add(allCompleted.Count(r => r.Date >= d && r.Date < end));
                }
                return Json(new { labels, totalData, completedData, summary = new { total = allAssigned.Count, completed = allCompleted.Count } });
            }

            if (!int.TryParse(range, out int days) || !new[] { 7, 14, 30, 60, 90 }.Contains(days)) days = 7;
            var rangeStart = _todayPH.AddDays(-days);

            var assigned = allAssigned.Where(r => r.Date >= rangeStart).ToList();
            var completed = allCompleted.Where(r => r.Date >= rangeStart).ToList();

            int step2 = days <= 14 ? 1 : days <= 30 ? 2 : 7;
            var labels2 = new List<string>(); var totalData2 = new List<int>(); var completedData2 = new List<int>();
            var empBuckets2 = new List<DateTime>();
            for (var d = rangeStart; d <= _todayPH; d = d.AddDays(step2)) empBuckets2.Add(d);
            if (empBuckets2.Count == 0 || empBuckets2[empBuckets2.Count - 1] < _todayPH) empBuckets2.Add(_todayPH);
            for (int i = 0; i < empBuckets2.Count; i++)
            {
                var d = empBuckets2[i];
                var end = (i + 1 < empBuckets2.Count) ? empBuckets2[i + 1] : _todayPH.AddDays(1);
                labels2.Add(d.ToString("MMM dd"));
                totalData2.Add(assigned.Count(r => r.Date >= d && r.Date < end));
                completedData2.Add(completed.Count(r => r.Date >= d && r.Date < end));
            }
            return Json(new { labels = labels2, totalData = totalData2, completedData = completedData2, summary = new { total = assigned.Count, completed = completed.Count } });
        }

        // ===================== GET OJT LIST =====================
        [HttpGet]
        // ===================== MY CALENDAR =====================
        public async Task<IActionResult> MyCalendar()
        {
            int userId = GetCurrentUserId();
            string dept = GetCurrentDepartment();
            using var conn = GetConnection();
            await SetSidebarCounts(conn, userId);

            var ojts = (await conn.QueryAsync<dynamic>(
                @"SELECT DISTINCT u.UserId, u.FirstName + ' ' + u.LastName AS FullName, u.Branch
                  FROM Tasks t JOIN Users u ON t.AssignedToUserId = u.UserId
                  WHERE t.AssignedByUserId = @UserId
                  ORDER BY FullName",
                new { UserId = userId })).ToList();

            ViewBag.OJTs = ojts;
            ViewBag.Branches = new[] { "Agoo", "Reina Mercedes", "Candon", "Pasig" };
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetCalendarTasks(string branch = "", int ojtId = 0)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();

            string sql = @"
                SELECT t.TaskId, t.Title, t.Status, t.CreatedAt, t.Deadline, t.TaskType,
                       t.AssignedByUserId,
                       u.FirstName + ' ' + u.LastName AS AssignedToName,
                       u.Branch AS AssignedToBranch
                FROM Tasks t
                JOIN Users u ON t.AssignedToUserId = u.UserId
                WHERE t.AssignedByUserId = @UserId
                  AND t.Status != 'Cancelled'";

            if (ojtId > 0) sql += " AND t.AssignedToUserId = @OjtId";
            if (!string.IsNullOrEmpty(branch)) sql += " AND u.Branch = @Branch";

            var tasks = (await conn.QueryAsync<dynamic>(sql,
                new { UserId = userId, OjtId = ojtId, Branch = branch })).ToList();

            var events = new List<object>();
            foreach (var t in tasks)
            {
                bool approved = (string)t.Status == "Approved";
                string dueStr = t.Deadline != null ? ((DateTime)t.Deadline).ToString("MMM dd, yyyy") : null;

                // Assigned/Completed task event on CreatedAt
                events.Add(new
                {
                    id = $"task-{t.TaskId}",
                    title = (string)t.Title,
                    start = ((DateTime)t.CreatedAt).ToString("yyyy-MM-dd"),
                    color = approved ? "#16a34a" : "#2563ab",
                    textColor = "#fff",
                    extendedProps = new { type = approved ? "completed" : "assigned", assignedTo = (string)t.AssignedToName, branch = (string)(t.AssignedToBranch ?? ""), dueDate = dueStr, taskType = (string)(t.TaskType ?? "Individual") }
                });

                // Purple due date event — only for active (non-approved) tasks with a deadline
                if (t.Deadline != null && !approved)
                {
                    events.Add(new
                    {
                        id = $"due-{t.TaskId}",
                        title = $"Due: {(string)t.Title}",
                        start = ((DateTime)t.Deadline).ToString("yyyy-MM-dd"),
                        color = "#7c3aed",
                        textColor = "#fff",
                        extendedProps = new { type = "deadline", assignedTo = (string)t.AssignedToName, branch = (string)(t.AssignedToBranch ?? ""), dueDate = dueStr, taskType = (string)(t.TaskType ?? "Individual") }
                    });
                }
            }
            return Json(events);
        }

        // ===================== GET GROUP MEMBERS =====================
        [HttpGet]
        [ResponseCache(NoStore = true)]
        public async Task<IActionResult> GetGroupMembers(int taskId)
        {
            using var conn = GetConnection();
            var task = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Title, AssignedByUserId FROM Tasks WHERE TaskId=@TaskId AND TaskType='Group'",
                new { TaskId = taskId });
            if (task == null) return Json(new string[0]);

            var members = (await conn.QueryAsync<string>(
                @"SELECT u.FirstName + ' ' + u.LastName
                  FROM Tasks t JOIN Users u ON t.AssignedToUserId = u.UserId
                  WHERE t.Title = @Title AND t.AssignedByUserId = @AssignedBy AND t.TaskType = 'Group'
                    AND u.Status IN ('Active','Approved')
                  ORDER BY u.FirstName",
                new { Title = (string)task.Title, AssignedBy = (int)task.AssignedByUserId }))
                .ToList();
            return Json(members);
        }


        public async Task<IActionResult> GetMyOJTs(string branch = "")
        {
            string dept = GetCurrentDepartment();
            using var conn = GetConnection();
            string sql = @"SELECT UserId, FirstName, LastName, Branch
                           FROM Users
                           WHERE Role = 'OJT' AND Department = @Department AND Status IN ('Active','Approved')";
            if (!string.IsNullOrEmpty(branch))
                sql += " AND Branch = @Branch";
            sql += " ORDER BY FirstName, LastName";
            var ojts = (await conn.QueryAsync<User>(sql,
                new { Department = dept, Branch = branch })).ToList();
            var result = ojts.Select(u => new { userId = u.UserId, fullName = u.FirstName + " " + u.LastName, branch = u.Branch ?? "" });
            return Json(result);
        }

        // ===================== DASHBOARD =====================
        public async Task<IActionResult> Index()
        {
            int userId = GetCurrentUserId();
            string dept = GetCurrentDepartment();
            using var conn = GetConnection();

            var user = await conn.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE UserId = @UserId", new { UserId = userId });

            var tasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*,
                    u1.FirstName + ' ' + u1.LastName AS AssignedToName,
                    u1.Username AS AssignedToUsername,
                    u2.FirstName + ' ' + u2.LastName AS AssignedByName
                  FROM Tasks t
                  JOIN Users u1 ON t.AssignedToUserId = u1.UserId
                  JOIN Users u2 ON t.AssignedByUserId = u2.UserId
                  WHERE t.AssignedByUserId = @UserId AND t.Status != 'Cancelled'
                  ORDER BY t.CreatedAt DESC",
                new { UserId = userId })).ToList();

            var cancelledTasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*,
                    u1.FirstName + ' ' + u1.LastName AS AssignedToName,
                    u1.Username AS AssignedToUsername,
                    u2.FirstName + ' ' + u2.LastName AS AssignedByName
                  FROM Tasks t
                  JOIN Users u1 ON t.AssignedToUserId = u1.UserId
                  JOIN Users u2 ON t.AssignedByUserId = u2.UserId
                  WHERE t.AssignedByUserId = @UserId AND t.Status = 'Cancelled'
                  ORDER BY t.UpdatedAt DESC",
                new { UserId = userId })).ToList();

            // Active OJTs: same department, ALL branches
            var ojts = (await conn.QueryAsync<User>(
                @"SELECT * FROM Users
                  WHERE Role = 'OJT'
                    AND Department = @Department
                    AND Status IN ('Active','Approved')
                  ORDER BY Branch, FirstName, LastName",
                new { Department = dept })).ToList();

            await SetSidebarCounts(conn, userId);
            ViewBag.EmpBranch = GetCurrentBranch();

            return View(new EmployeeDashboardViewModel
            {
                CurrentUser = user!,
                Tasks = tasks,
                MyOJTs = ojts,
                CancelledTaskList = cancelledTasks,
                TotalTasks = tasks.Count,
                PendingTasks = tasks.Count(t => t.Status == "Pending"),
                CompletedTasks = tasks.Count(t => t.Status == "Approved"),
                CancelledTasks = cancelledTasks.Count
            });
        }

        // ===================== TASKS GIVEN =====================
        public async Task<IActionResult> TasksGiven()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var tasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*, u1.FirstName+' '+u1.LastName AS AssignedToName,
                    u1.Username AS AssignedToUsername,
                    u2.FirstName+' '+u2.LastName AS AssignedByName
                  FROM Tasks t
                  JOIN Users u1 ON t.AssignedToUserId=u1.UserId
                  JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.AssignedByUserId=@UserId AND t.Status != 'Cancelled'
                  ORDER BY t.CreatedAt DESC",
                new { UserId = userId })).ToList();
            var cancelledTasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*, u1.FirstName+' '+u1.LastName AS AssignedToName,
                    u1.Username AS AssignedToUsername,
                    u2.FirstName+' '+u2.LastName AS AssignedByName
                  FROM Tasks t
                  JOIN Users u1 ON t.AssignedToUserId=u1.UserId
                  JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.AssignedByUserId=@UserId AND t.Status = 'Cancelled'
                  ORDER BY t.UpdatedAt DESC",
                new { UserId = userId })).ToList();
            await SetSidebarCounts(conn, userId);
            ViewData["Title"] = "Tasks Given";
            ViewBag.CancelledTasks = cancelledTasks;
            return View("EmpTasksGiven", tasks);
        }

        // ===================== COMPLETED TASKS =====================
        public async Task<IActionResult> CompletedTasks()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var tasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*, ISNULL(u1.FirstName+' '+u1.LastName,'(Removed)') AS AssignedToName,
                    ISNULL(u1.Username,'(removed)') AS AssignedToUsername,
                    ISNULL(u2.FirstName+' '+u2.LastName,'Unknown') AS AssignedByName,
                    ISNULL(u1.Branch,'') AS AssignedToBranch
                  FROM Tasks t
                  LEFT JOIN Users u1 ON t.AssignedToUserId=u1.UserId
                  LEFT JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.AssignedByUserId=@UserId AND t.Status='Approved'
                  ORDER BY t.CreatedAt DESC",
                new { UserId = userId })).ToList();
            await SetSidebarCounts(conn, userId);
            ViewData["Title"] = "Completed Tasks";
            return View("EmpTasksGiven", tasks);
        }

        // ===================== HISTORY =====================
        public async Task<IActionResult> History()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var submissions = (await conn.QueryAsync<TaskSubmission>(
                @"SELECT ts.*, t.Title AS TaskTitle, ISNULL(u.FirstName+' '+u.LastName,'(Removed)') AS OJTName,
                    ISNULL(u.Branch,'') AS OJTBranch
                  FROM TaskSubmissions ts
                  JOIN Tasks t ON ts.TaskId=t.TaskId
                  LEFT JOIN Users u ON ts.OJTUserId=u.UserId
                  WHERE t.AssignedByUserId=@UserId AND ts.Status='Approved'
                  ORDER BY ts.SubmittedAt DESC",
                new { UserId = userId })).ToList();
            var cancelledTasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*,
                    ISNULL(u1.FirstName+' '+u1.LastName,'(Removed)') AS AssignedToName,
                    u1.Username AS AssignedToUsername,
                    ISNULL(u2.FirstName+' '+u2.LastName,'Unknown') AS AssignedByName,
                    ISNULL(u1.Branch,'') AS AssignedToBranch
                  FROM Tasks t
                  LEFT JOIN Users u1 ON t.AssignedToUserId=u1.UserId
                  LEFT JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.AssignedByUserId=@UserId AND t.Status='Cancelled'
                  ORDER BY t.UpdatedAt DESC",
                new { UserId = userId })).ToList();
            await SetSidebarCounts(conn, userId);
            ViewData["Title"] = "History";
            ViewBag.CancelledTasks = cancelledTasks;
            return View("EmpHistory", submissions);
        }

        // ===================== CREATE TASK (GET) =====================
        [HttpGet]
        public async Task<IActionResult> CreateTask()
        {
            string dept = GetCurrentDepartment();
            string branch = GetCurrentBranch();
            using var conn = GetConnection();
            // Active OJTs: same department, ALL branches — Employee can assign to any branch
            var ojts = (await conn.QueryAsync<User>(
                @"SELECT * FROM Users
                  WHERE Role = 'OJT'
                    AND Department = @Department
                    AND Status IN ('Active','Approved')
                  ORDER BY Branch, FirstName, LastName",
                new { Department = dept })).ToList();
            ViewBag.OJTs = ojts;
            ViewBag.Branches = new[] { "Agoo", "Reina Mercedes", "Candon", "Pasig" };
            ViewBag.DefaultBranch = branch;
            await SetSidebarCounts(conn, GetCurrentUserId());
            return View(new CreateTaskViewModel());
        }

        // ===================== CREATE TASK (POST) =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTask(CreateTaskViewModel model)
        {
            string dept = GetCurrentDepartment();
            string branch = GetCurrentBranch();
            int userId = GetCurrentUserId();
            string selectedBranch = Request.Form["SelectedBranch"].ToString();
            if (string.IsNullOrEmpty(selectedBranch)) selectedBranch = branch;
            using var conn = GetConnection();

            // Load ALL OJTs in dept for ViewBag fallback (all branches)
            var allOjts = (await conn.QueryAsync<User>(
                "SELECT * FROM Users WHERE Role = 'OJT' AND Department = @Department AND Status IN ('Active','Approved')",
                new { Department = dept })).ToList();

            // Filter to selected branch for actual assignment
            var ojts = allOjts.Where(u => u.Branch == selectedBranch).ToList();

            if (model.TaskType == "Individual" && (!model.AssignedToUserId.HasValue || model.AssignedToUserId == 0))
                ModelState.AddModelError("AssignedToUserId", "Please select an OJT trainee.");

            if (!ModelState.IsValid)
            {
                ViewBag.OJTs = allOjts;
                ViewBag.Branches = new[] { "Agoo", "Reina Mercedes", "Candon", "Pasig" };
                ViewBag.DefaultBranch = selectedBranch;
                await SetSidebarCounts(conn, userId);
                return View(model);
            }

            DateTime? deadlineUtc = model.GetDeadlineUtc();
            string? filePath = null, fileName = null;
            var allowed = new[] { ".pdf", ".xlsx", ".xls", ".docx", ".doc", ".pptx", ".ppt", ".png", ".jpg", ".jpeg", ".pbix", ".zip" };

            if (model.AttachedFiles != null && model.AttachedFiles.Any(f => f.Length > 0))
            {
                var validFiles = model.AttachedFiles.Where(f => f != null && f.Length > 0).ToList();
                foreach (var f in validFiles)
                {
                    var ext2 = Path.GetExtension(f.FileName).ToLower();
                    if (!allowed.Contains(ext2))
                    {
                        ModelState.AddModelError("AttachedFiles", $"'{f.FileName}' is not an allowed file type.");
                        ViewBag.OJTs = ojts; await SetSidebarCounts(conn, userId); return View(model);
                    }
                }
                var dir = Path.Combine(_env.WebRootPath, "uploads", "tasks");
                Directory.CreateDirectory(dir);
                var paths = new List<string>(); var names = new List<string>();
                foreach (var f in validFiles)
                {
                    var ext2 = Path.GetExtension(f.FileName).ToLower();
                    var sn = $"{Guid.NewGuid()}{ext2}";
                    using var stream = new FileStream(Path.Combine(dir, sn), FileMode.Create);
                    await f.CopyToAsync(stream);
                    paths.Add($"/uploads/tasks/{sn}"); names.Add(f.FileName);
                }
                filePath = System.Text.Json.JsonSerializer.Serialize(paths);
                fileName = System.Text.Json.JsonSerializer.Serialize(names);
            }

            if (model.TaskType == "Individual")
            {
                var ojtUser = await conn.QueryFirstOrDefaultAsync<User>(
                    "SELECT * FROM Users WHERE UserId = @Uid AND Role = 'OJT' AND Department = @Dept AND Branch = @Branch AND Status IN ('Active','Approved')",
                    new { Uid = model.AssignedToUserId!.Value, Dept = dept, Branch = selectedBranch });
                if (ojtUser == null) { ModelState.AddModelError("AssignedToUserId", "Invalid OJT selected."); ViewBag.OJTs = allOjts; ViewBag.Branches = new[] { "Agoo", "Reina Mercedes", "Candon", "Pasig" }; ViewBag.DefaultBranch = selectedBranch; await SetSidebarCounts(conn, userId); return View(model); }
                await InsertTask(conn, model.Title, model.Description, model.AssignedToUserId.Value, userId, "Individual", deadlineUtc, filePath, fileName);
                TempData["SuccessMessage"] = $"Task '{model.Title}' assigned to {ojtUser.FullName}.";
            }
            else
            {
                int count = 0;
                foreach (var ojt in ojts) { await InsertTask(conn, model.Title, model.Description, ojt.UserId, userId, "Group", deadlineUtc, filePath, fileName); count++; }
                TempData["SuccessMessage"] = $"Group task '{model.Title}' assigned to all {count} OJT trainee(s) in {dept} - {selectedBranch}.";
            }
            return RedirectToAction("Index");
        }

        private async Task InsertTask(SqlConnection conn, string title, string desc, int assignedTo, int assignedBy,
            string taskType, DateTime? deadline, string? filePath, string? fileName)
        {
            await conn.ExecuteAsync(
                @"INSERT INTO Tasks (Title, Description, AssignedToUserId, AssignedByUserId,
                    Status, TaskType, Deadline, AttachedFilePath, AttachedFileName, CreatedAt, UpdatedAt)
                  VALUES (@Title, @Description, @AssignedTo, @AssignedBy,
                    'New', @TaskType, @Deadline, @FilePath, @FileName, GETDATE(), GETDATE())",
                new
                {
                    Title = title,
                    Description = desc,
                    AssignedTo = assignedTo,
                    AssignedBy = assignedBy,
                    TaskType = taskType,
                    Deadline = deadline,
                    FilePath = filePath,
                    FileName = fileName
                });
        }


        // ===================== TASK DETAILS (view approved task) =====================
        [HttpGet]
        public async Task<IActionResult> TaskDetails(int id)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var task = await conn.QueryFirstOrDefaultAsync<TaskItem>(
                @"SELECT t.*,
                    ISNULL(u1.FirstName+' '+u1.LastName,'(Removed)') AS AssignedToName,
                    ISNULL(u1.Username,'(removed)') AS AssignedToUsername,
                    ISNULL(u2.FirstName+' '+u2.LastName,'Unknown') AS AssignedByName
                  FROM Tasks t
                  LEFT JOIN Users u1 ON t.AssignedToUserId=u1.UserId
                  LEFT JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.TaskId=@TaskId AND t.AssignedByUserId=@UserId",
                new { TaskId = id, UserId = userId });
            if (task == null) return NotFound();
            var submission = await conn.QueryFirstOrDefaultAsync<TaskSubmission>(
                "SELECT * FROM TaskSubmissions WHERE TaskId=@TaskId AND Status='Approved' ORDER BY ReviewedAt DESC",
                new { TaskId = id });
            // Load OJT progress logs for employee visibility
            var progressLogs = (await conn.QueryAsync<TaskProgressLog>(
                @"SELECT tpl.*, u.FirstName+' '+u.LastName AS OJTName
                  FROM TaskProgressLogs tpl
                  LEFT JOIN Users u ON tpl.OJTUserId=u.UserId
                  WHERE tpl.TaskId=@TaskId
                  ORDER BY tpl.LoggedAt DESC",
                new { TaskId = id })).ToList();
            await SetSidebarCounts(conn, userId);
            ViewBag.Submission = submission;
            ViewBag.ProgressLogs = progressLogs;
            ViewBag.Task = task;
            return View("EmpTaskDetails", task);
        }

        // ===================== EDIT TASK (GET) =====================
        [HttpGet]
        public async Task<IActionResult> EditTask(int id)
        {
            int userId = GetCurrentUserId();
            string dept = GetCurrentDepartment();
            using var conn = GetConnection();
            var task = await conn.QueryFirstOrDefaultAsync<TaskItem>(
                "SELECT * FROM Tasks WHERE TaskId=@TaskId AND AssignedByUserId=@UserId AND Status='New'",
                new { TaskId = id, UserId = userId });
            if (task == null) return NotFound();
            var ojts = (await conn.QueryAsync<User>(
                "SELECT * FROM Users WHERE Role='OJT' AND Department=@Department AND Status IN ('Active','Approved') ORDER BY Branch, FirstName, LastName",
                new { Department = dept })).ToList();
            ViewBag.OJTs = ojts;
            await SetSidebarCounts(conn, userId);
            // Map task back to edit model — including deadline fields
            var model = new CreateTaskViewModel
            {
                Title = task.Title,
                Description = task.Description,
                AssignedToUserId = task.AssignedToUserId,
                TaskType = task.TaskType ?? "Individual"
            };

            // Re-populate deadline fields so the form pre-fills correctly
            if (task.Deadline.HasValue)
            {
                // Deadline stored as UTC — convert to PH local time (UTC+8) for display
                var phTime = task.Deadline.Value.AddHours(8);
                model.DeadlineDate = phTime.ToString("yyyy-MM-dd");
                int hour = phTime.Hour;
                int minute = phTime.Minute;
                model.DeadlineAmPm = hour >= 12 ? "PM" : "AM";
                int displayHour = hour % 12;
                if (displayHour == 0) displayHour = 12;
                model.DeadlineTime = $"{displayHour:D2}:{minute:D2}";
            }

            ViewBag.TaskId = id;
            ViewBag.ExistingFilePath = task.AttachedFilePath;
            ViewBag.ExistingFileName = task.AttachedFileName;
            return View(model);
        }

        // ===================== EDIT TASK (POST) =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTask(int id, CreateTaskViewModel model)
        {
            int userId = GetCurrentUserId();
            string dept = GetCurrentDepartment();
            using var conn = GetConnection();
            var task = await conn.QueryFirstOrDefaultAsync<TaskItem>(
                "SELECT * FROM Tasks WHERE TaskId=@TaskId AND AssignedByUserId=@UserId AND Status='New'",
                new { TaskId = id, UserId = userId });
            if (task == null) return NotFound();

            var ojts = (await conn.QueryAsync<User>(
                "SELECT * FROM Users WHERE Role='OJT' AND Department=@Department AND Status='Active'",
                new { Department = dept })).ToList();

            if (model.TaskType == "Individual" && (!model.AssignedToUserId.HasValue || model.AssignedToUserId == 0))
                ModelState.AddModelError("AssignedToUserId", "Please select an OJT trainee.");

            if (!ModelState.IsValid)
            {
                ViewBag.OJTs = ojts; ViewBag.TaskId = id;
                await SetSidebarCounts(conn, userId); return View(model);
            }

            DateTime? deadlineUtc = model.GetDeadlineUtc();

            // ── File handling ──
            // Get current file values from DB
            string? filePath = task.AttachedFilePath;
            string? fileName = task.AttachedFileName;

            // If user wants to clear existing files
            bool clearFiles = Request.Form["ClearFiles"] == "true";
            if (clearFiles) { filePath = null; fileName = null; }

            // If new files uploaded, delete old files first then save new ones
            var allowed = new[] { ".pdf", ".xlsx", ".xls", ".docx", ".doc", ".pptx", ".ppt", ".png", ".jpg", ".jpeg", ".pbix", ".zip" };
            if (model.AttachedFiles != null && model.AttachedFiles.Any(f => f?.Length > 0))
            {
                var validFiles = model.AttachedFiles.Where(f => f != null && f.Length > 0).ToList();
                foreach (var f in validFiles)
                {
                    var ext2 = Path.GetExtension(f.FileName).ToLower();
                    if (!allowed.Contains(ext2))
                    {
                        ModelState.AddModelError("AttachedFiles", $"'{f.FileName}' is not an allowed file type.");
                        ViewBag.OJTs = ojts; ViewBag.TaskId = id;
                        ViewBag.ExistingFilePath = task.AttachedFilePath;
                        ViewBag.ExistingFileName = task.AttachedFileName;
                        await SetSidebarCounts(conn, userId); return View(model);
                    }
                }

                // Delete old task files from disk before saving new ones — prevents duplicates
                if (!string.IsNullOrEmpty(task.AttachedFilePath))
                {
                    foreach (var (oldFp, _) in task.GetAttachedFiles())
                    {
                        if (string.IsNullOrEmpty(oldFp)) continue;
                        var oldFullPath = Path.Combine(_env.WebRootPath, oldFp.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (System.IO.File.Exists(oldFullPath)) System.IO.File.Delete(oldFullPath);
                    }
                }

                var dir = Path.Combine(_env.WebRootPath, "uploads", "tasks");
                Directory.CreateDirectory(dir);
                var paths = new List<string>(); var names = new List<string>();
                foreach (var f in validFiles)
                {
                    var ext2 = Path.GetExtension(f.FileName).ToLower();
                    var sn = $"{Guid.NewGuid()}{ext2}";
                    using var stream = new FileStream(Path.Combine(dir, sn), FileMode.Create);
                    await f.CopyToAsync(stream);
                    paths.Add($"/uploads/tasks/{sn}"); names.Add(f.FileName);
                }
                filePath = System.Text.Json.JsonSerializer.Serialize(paths);
                fileName = System.Text.Json.JsonSerializer.Serialize(names);
            }

            await conn.ExecuteAsync(
                @"UPDATE Tasks SET Title=@Title, Description=@Description,
                    AssignedToUserId=@AssignedTo, TaskType=@TaskType,
                    Deadline=@Deadline, AttachedFilePath=@FilePath, AttachedFileName=@FileName,
                    UpdatedAt=GETDATE()
                  WHERE TaskId=@TaskId AND AssignedByUserId=@UserId AND Status='New'",
                new
                {
                    Title = model.Title,
                    Description = model.Description,
                    AssignedTo = model.AssignedToUserId,
                    TaskType = model.TaskType,
                    Deadline = deadlineUtc,
                    FilePath = filePath,
                    FileName = fileName,
                    TaskId = id,
                    UserId = userId
                });

            TempData["SuccessMessage"] = $"Task '{model.Title}' updated successfully.";
            return RedirectToAction("TasksGiven");
        }

        // ===================== DELETE TASK =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTask(int taskId, string? returnTo = null)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var task = await conn.QueryFirstOrDefaultAsync<TaskItem>(
                "SELECT * FROM Tasks WHERE TaskId = @TaskId AND AssignedByUserId = @UserId AND Status = 'New'",
                new { TaskId = taskId, UserId = userId });
            if (task == null) return NotFound();
            // Soft-cancel: keep record in history instead of hard delete
            await conn.ExecuteAsync(
                "UPDATE Tasks SET Status='Cancelled', UpdatedAt=GETDATE() WHERE TaskId=@TaskId",
                new { TaskId = taskId });
            TempData["SuccessMessage"] = $"Task '{task.Title}' has been cancelled.";
            return returnTo == "TasksGiven" ? RedirectToAction("TasksGiven") : RedirectToAction("Index");
        }

        // ===================== PENDING SUBMISSIONS =====================
        public async Task<IActionResult> PendingSubmissions()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var submissions = (await conn.QueryAsync<TaskSubmission>(
                @"SELECT ts.*, t.Title AS TaskTitle, u.FirstName+' '+u.LastName AS OJTName
                  FROM TaskSubmissions ts
                  JOIN Tasks t ON ts.TaskId=t.TaskId
                  JOIN Users u ON ts.OJTUserId=u.UserId
                  WHERE t.AssignedByUserId=@UserId AND ts.Status='Submitted'
                  ORDER BY ts.SubmittedAt DESC",
                new { UserId = userId })).ToList();
            await SetSidebarCounts(conn, userId);
            return View(submissions);
        }

        // ===================== APPROVE SUBMISSION =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveSubmission(int submissionId)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var sub = await conn.QueryFirstOrDefaultAsync<TaskSubmission>(
                @"SELECT ts.* FROM TaskSubmissions ts JOIN Tasks t ON ts.TaskId=t.TaskId
                  WHERE ts.SubmissionId=@SubmissionId AND t.AssignedByUserId=@UserId",
                new { SubmissionId = submissionId, UserId = userId });
            if (sub == null) return NotFound();

            // Approve — files are kept on disk and in DB for reference
            await conn.ExecuteAsync(
                "UPDATE TaskSubmissions SET Status='Approved', ReviewedAt=GETDATE() WHERE SubmissionId=@Id",
                new { Id = submissionId });
            await conn.ExecuteAsync(
                "UPDATE Tasks SET Status='Approved', UpdatedAt=GETDATE() WHERE TaskId=@TaskId",
                new { sub.TaskId });
            TempData["SuccessMessage"] = "Submission approved!";
            return RedirectToAction("PendingSubmissions");
        }

        // ===================== RESUBMIT (formerly Reject) =====================
        // Sets submission Status='Rejected', Task Status='New' so OJT can resubmit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectSubmission(int submissionId)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var sub = await conn.QueryFirstOrDefaultAsync<TaskSubmission>(
                @"SELECT ts.* FROM TaskSubmissions ts JOIN Tasks t ON ts.TaskId=t.TaskId
                  WHERE ts.SubmissionId=@SubmissionId AND t.AssignedByUserId=@UserId",
                new { SubmissionId = submissionId, UserId = userId });
            if (sub == null) return NotFound();

            // Mark submission as Rejected — ReviewedAt triggers OJT notification
            // Files are kept so OJT can reference their previous submission
            await conn.ExecuteAsync(
                "UPDATE TaskSubmissions SET Status='Rejected', ReviewedAt=GETDATE() WHERE SubmissionId=@Id",
                new { Id = submissionId });
            // Reset task to New so OJT can submit again
            await conn.ExecuteAsync("UPDATE Tasks SET Status='New', UpdatedAt=GETDATE() WHERE TaskId=@TaskId", new { sub.TaskId });
            TempData["SuccessMessage"] = "Resubmission requested. The OJT has been notified.";
            return RedirectToAction("PendingSubmissions");
        }

        // ===================== CLEANUP ORPHANED FILES (one-time fix) =====================
        // Call this once to delete files from already-approved tasks/submissions
        // that were approved before the auto-delete feature was added
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CleanupOrphanedFiles()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();

            int filesDeleted = 0;

            // 1. Clean up approved task files (tasks assigned by this employee)
            var approvedTasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT TaskId, AttachedFilePath, AttachedFileName
                  FROM Tasks
                  WHERE AssignedByUserId = @UserId
                    AND Status = 'Approved'
                    AND AttachedFilePath IS NOT NULL",
                new { UserId = userId })).ToList();

            foreach (var task in approvedTasks)
            {
                foreach (var (fp, _) in task.GetAttachedFiles())
                {
                    if (string.IsNullOrEmpty(fp)) continue;
                    var fullPath = Path.Combine(_env.WebRootPath, fp.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                        filesDeleted++;
                    }
                }
                await conn.ExecuteAsync(
                    "UPDATE Tasks SET AttachedFilePath=NULL, AttachedFileName=NULL WHERE TaskId=@TaskId",
                    new { task.TaskId });
            }

            // 2. Clean up approved submission files (submissions for tasks assigned by this employee)
            var approvedSubs = (await conn.QueryAsync<TaskSubmission>(
                @"SELECT ts.SubmissionId, ts.FilePath, ts.FileName
                  FROM TaskSubmissions ts
                  JOIN Tasks t ON ts.TaskId = t.TaskId
                  WHERE t.AssignedByUserId = @UserId
                    AND ts.Status = 'Approved'
                    AND ts.FilePath IS NOT NULL",
                new { UserId = userId })).ToList();

            foreach (var sub in approvedSubs)
            {
                foreach (var (fp, _) in sub.GetSubmittedFiles())
                {
                    if (string.IsNullOrEmpty(fp)) continue;
                    var fullPath = Path.Combine(_env.WebRootPath, fp.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                        filesDeleted++;
                    }
                }
                await conn.ExecuteAsync(
                    "UPDATE TaskSubmissions SET FilePath=NULL, FileName=NULL WHERE SubmissionId=@Id",
                    new { Id = sub.SubmissionId });
            }

            TempData["SuccessMessage"] = $"Cleanup complete. {filesDeleted} orphaned file(s) deleted from disk. {approvedTasks.Count} task(s) and {approvedSubs.Count} submission(s) cleared.";
            return RedirectToAction("History");
        }

        // ===================== DELETE HISTORY =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHistory(int submissionId)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var sub = await conn.QueryFirstOrDefaultAsync<TaskSubmission>(
                @"SELECT ts.* FROM TaskSubmissions ts JOIN Tasks t ON ts.TaskId=t.TaskId
                  WHERE ts.SubmissionId=@Id AND t.AssignedByUserId=@UserId AND ts.Status='Approved'",
                new { Id = submissionId, UserId = userId });
            if (sub != null)
                await conn.ExecuteAsync("DELETE FROM TaskSubmissions WHERE SubmissionId=@Id", new { Id = submissionId });
            TempData["SuccessMessage"] = "History entry removed.";
            return RedirectToAction("History");
        }

        // ===================== CLEAR ALL HISTORY =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearHistory()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            await conn.ExecuteAsync(
                @"DELETE ts FROM TaskSubmissions ts JOIN Tasks t ON ts.TaskId=t.TaskId
                  WHERE t.AssignedByUserId=@UserId AND ts.Status='Approved'",
                new { UserId = userId });
            TempData["SuccessMessage"] = "All completed task history cleared.";
            return RedirectToAction("History");
        }
    }
}
