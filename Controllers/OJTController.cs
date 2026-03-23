using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using ITPMS_OJT.Models;
using Dapper;

namespace ITPMS_OJT.Controllers
{
    [Authorize(Roles = "OJT")]
    public class OJTController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public OJTController(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        private SqlConnection GetConnection() =>
            new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        private int GetCurrentUserId() =>
            int.Parse(User.FindFirstValue("UserId")!);

        public static async Task<List<OJTNotification>> GetUnreadNotificationsStatic(SqlConnection conn, int userId)
        => await GetUnreadNotificationsInternal(conn, userId);

        private async Task<List<OJTNotification>> GetUnreadNotifications(SqlConnection conn, int userId)
        => await GetUnreadNotificationsInternal(conn, userId);

        private static async Task<List<OJTNotification>> GetUnreadNotificationsInternal(SqlConnection conn, int userId)
        {
            // New task notifications (last 7 days, unread)
            var newTasks = (await conn.QueryAsync<OJTNotification>(
                @"SELECT t.TaskId, t.Title AS TaskTitle, t.CreatedAt, t.Deadline,
                    u.FirstName + ' ' + u.LastName AS AssignedByName,
                    0 AS IsDueSoon
                  FROM Tasks t
                  JOIN Users u ON t.AssignedByUserId = u.UserId
                  WHERE t.AssignedToUserId = @UserId
                    AND t.Status = 'New'
                    AND t.CreatedAt >= DATEADD(day, -7, GETDATE())
                    AND t.TaskId NOT IN (
                        SELECT TaskId FROM NotificationReads WHERE OJTUserId = @UserId
                    )
                  ORDER BY t.CreatedAt DESC",
                new { UserId = userId })).ToList();

            // Due-soon notifications (deadline within 3 days, not yet approved)
            var dueSoon = (await conn.QueryAsync<OJTNotification>(
                @"SELECT t.TaskId, t.Title AS TaskTitle, t.CreatedAt, t.Deadline,
                    u.FirstName + ' ' + u.LastName AS AssignedByName,
                    1 AS IsDueSoon
                  FROM Tasks t
                  JOIN Users u ON t.AssignedByUserId = u.UserId
                  WHERE t.AssignedToUserId = @UserId
                    AND t.Status NOT IN ('Approved','Cancelled')
                    AND t.Deadline IS NOT NULL
                    AND t.Deadline <= DATEADD(day, 3, GETDATE())
                    AND t.Deadline >= GETDATE()
                  ORDER BY t.Deadline ASC",
                new { UserId = userId })).ToList();

            // Resubmit notifications — tasks rejected by employee (resubmission required)
            var resubmits = (await conn.QueryAsync<OJTNotification>(
                @"SELECT t.TaskId, t.Title AS TaskTitle, t.CreatedAt, t.Deadline,
                    u.FirstName + ' ' + u.LastName AS AssignedByName,
                    0 AS IsDueSoon, 1 AS IsResubmit, ts.SubmissionId
                  FROM TaskSubmissions ts
                  JOIN Tasks t ON ts.TaskId = t.TaskId
                  JOIN Users u ON t.AssignedByUserId = u.UserId
                  WHERE ts.OJTUserId = @UserId
                    AND ts.Status = 'Rejected'
                    AND t.Status = 'New'
                    AND ts.ReviewedAt >= DATEADD(day, -7, GETDATE())
                    AND ts.SubmissionId NOT IN (
                        SELECT ISNULL(SubmissionId,0) FROM NotificationReads
                        WHERE OJTUserId = @UserId AND SubmissionId IS NOT NULL
                    )
                  ORDER BY ts.ReviewedAt DESC",
                new { UserId = userId })).ToList();

            // Merge, deduplicate by TaskId
            var all = newTasks.ToList();
            foreach (var d in dueSoon)
                if (!all.Any(n => n.TaskId == d.TaskId))
                    all.Add(d);
            foreach (var r in resubmits)
                if (!all.Any(n => n.TaskId == r.TaskId && n.IsResubmit))
                    all.Add(r);

            return all.OrderByDescending(n => n.IsResubmit ? 2 : n.IsDueSoon ? 1 : 0)
                      .ThenByDescending(n => n.CreatedAt)
                      .ToList();
        }

        private async Task SetSidebarCounts(SqlConnection conn, int userId)
        {
            var tasks = (await conn.QueryAsync<TaskItem>(
                "SELECT Status FROM Tasks WHERE AssignedToUserId = @UserId",
                new { UserId = userId })).ToList();
            ViewBag.NewTaskCount = tasks.Count(t => t.Status == "New");
            ViewBag.PendingTaskCount = tasks.Count(t => t.Status == "Pending");
            ViewBag.CompletedTaskCount = tasks.Count(t => t.Status == "Approved");
            ViewBag.Notifications = await GetUnreadNotifications(conn, userId);
            var pic = await conn.ExecuteScalarAsync<string>("SELECT ProfilePicture FROM Users WHERE UserId=@UserId", new { UserId = userId });
            ViewBag.ProfilePicUrl = string.IsNullOrEmpty(pic) ? null : $"/uploads/profiles/{pic}";
            ViewBag.UnreadChatCount = await ChatController.GetUnreadChatCount(conn, userId);
        }

        // ===================== ANALYTICS API =====================
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetAnalytics(string range = "overall")
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var _todayPH = DateTime.Today;

            // ── "Overall" mode: span from first task ever to today ──
            if (range == "overall")
            {
                var firstDate = await conn.QueryFirstOrDefaultAsync<DateTime?>(
                    "SELECT MIN(CreatedAt) FROM Tasks WHERE AssignedToUserId = @UserId",
                    new { UserId = userId });

                if (firstDate == null)
                {
                    // No tasks at all — return empty
                    return Json(new
                    {
                        labels = new List<string>(),
                        totalData = new List<int>(),
                        completedData = new List<int>(),
                        summary = new { total = 0, completed = 0 }
                    });
                }

                var allAssigned = (await conn.QueryAsync<(DateTime Date, int TaskId)>(
                    "SELECT CAST(CreatedAt AS DATE) AS Date, TaskId FROM Tasks WHERE AssignedToUserId = @UserId",
                    new { UserId = userId })).ToList();

                var allCompleted = (await conn.QueryAsync<(DateTime Date, int TaskId)>(
                    "SELECT CAST(UpdatedAt AS DATE) AS Date, TaskId FROM Tasks WHERE AssignedToUserId = @UserId AND Status = 'Approved'",
                    new { UserId = userId })).ToList();

                var start = firstDate.Value.Date;
                var today = _todayPH;
                int spanDays = (int)(today - start).TotalDays + 1;

                // Choose step size based on span
                int step = spanDays <= 14 ? 1 : spanDays <= 60 ? 3 : 7;

                var labels = new List<string>();
                var totalData = new List<int>();
                var completedData = new List<int>();

                // Build buckets — each bucket covers [d, d+step)
                // We collect bucket start dates first, then always append today if not already included
                var bucketStarts = new List<DateTime>();
                for (var d = start; d <= today; d = d.AddDays(step))
                    bucketStarts.Add(d);

                // If today is not the last bucket start, add it explicitly
                if (bucketStarts.Count == 0 || bucketStarts[bucketStarts.Count - 1] < today)
                    bucketStarts.Add(today);

                for (int i = 0; i < bucketStarts.Count; i++)
                {
                    var d = bucketStarts[i];
                    var periodEnd = (i + 1 < bucketStarts.Count) ? bucketStarts[i + 1] : today.AddDays(1);
                    labels.Add(d.ToString("MMM dd"));
                    totalData.Add(allAssigned.Count(r => r.Date >= d && r.Date < periodEnd));
                    completedData.Add(allCompleted.Count(r => r.Date >= d && r.Date < periodEnd));
                }

                return Json(new
                {
                    labels,
                    totalData,
                    completedData,
                    summary = new
                    {
                        total = allAssigned.Count,
                        completed = allCompleted.Count
                    }
                });
            }

            // ── Date-range mode ──
            if (!int.TryParse(range, out int days) || !new[] { 7, 14, 30, 60, 90 }.Contains(days))
                days = 7;

            var rangeStart = _todayPH.AddDays(-days);

            var assigned = (await conn.QueryAsync<(DateTime Date, int TaskId)>(
                @"SELECT CAST(CreatedAt AS DATE) AS Date, TaskId
                  FROM Tasks
                  WHERE AssignedToUserId = @UserId AND CreatedAt >= @Start",
                new { UserId = userId, Start = rangeStart })).ToList();

            var completed = (await conn.QueryAsync<(DateTime Date, int TaskId)>(
                @"SELECT CAST(UpdatedAt AS DATE) AS Date, TaskId
                  FROM Tasks
                  WHERE AssignedToUserId = @UserId AND Status = 'Approved' AND UpdatedAt >= @Start",
                new { UserId = userId, Start = rangeStart })).ToList();

            var today2 = _todayPH;
            int step2 = days <= 14 ? 1 : days <= 30 ? 2 : 7;

            var labels2 = new List<string>();
            var totalData2 = new List<int>();
            var completedData2 = new List<int>();

            // Always start from rangeStart so graph fills the full period left-to-right
            var bucketStarts2 = new List<DateTime>();
            for (var d = rangeStart; d <= _todayPH; d = d.AddDays(step2))
                bucketStarts2.Add(d);
            if (bucketStarts2.Count == 0 || bucketStarts2[bucketStarts2.Count - 1] < _todayPH)
                bucketStarts2.Add(_todayPH);

            for (int i = 0; i < bucketStarts2.Count; i++)
            {
                var d = bucketStarts2[i];
                var periodEnd = (i + 1 < bucketStarts2.Count) ? bucketStarts2[i + 1] : _todayPH.AddDays(1);
                labels2.Add(d.ToString("MMM dd"));
                totalData2.Add(assigned.Count(r => r.Date >= d && r.Date < periodEnd));
                completedData2.Add(completed.Count(r => r.Date >= d && r.Date < periodEnd));
            }

            return Json(new
            {
                labels = labels2,
                totalData = totalData2,
                completedData = completedData2,
                summary = new
                {
                    total = assigned.Count,
                    completed = completed.Count
                }
            });
        }

        // ===================== MARK NOTIFICATION AS READ =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkNotificationRead(int taskId, int submissionId = 0)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            try
            {
                if (submissionId > 0)
                {
                    // Dismiss resubmit notification — insert read record
                    await conn.ExecuteAsync(
                        @"IF NOT EXISTS (SELECT 1 FROM NotificationReads WHERE OJTUserId=@UserId AND SubmissionId=@SubId)
                          INSERT INTO NotificationReads (OJTUserId, TaskId, SubmissionId, ReadAt) VALUES (@UserId, @TaskId, @SubId, GETDATE())",
                        new { UserId = userId, TaskId = taskId, SubId = submissionId });
                }
                else
                {
                    // Dismiss new task notification — insert read record
                    await conn.ExecuteAsync(
                        @"IF NOT EXISTS (SELECT 1 FROM NotificationReads WHERE OJTUserId=@UserId AND TaskId=@TaskId AND SubmissionId IS NULL)
                          INSERT INTO NotificationReads (OJTUserId, TaskId, ReadAt) VALUES (@UserId, @TaskId, GETDATE())",
                        new { UserId = userId, TaskId = taskId });
                }

                // Self-cleaning: delete NotificationReads rows older than 7 days for this user
                await conn.ExecuteAsync(
                    "DELETE FROM NotificationReads WHERE OJTUserId=@UserId AND ReadAt < DATEADD(day, -7, GETDATE())",
                    new { UserId = userId });
            }
            catch { }
            var remaining = (await GetUnreadNotificationsStatic(conn, userId)).Count;
            return Json(new { success = true, remaining });
        }

        // ===================== MARK ALL NOTIFICATIONS READ =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllNotificationsRead()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            // Dismiss new task notifications
            await conn.ExecuteAsync(
                @"INSERT INTO NotificationReads (OJTUserId, TaskId)
                  SELECT @UserId, t.TaskId FROM Tasks t
                  WHERE t.AssignedToUserId = @UserId AND t.Status = 'New'
                    AND t.CreatedAt >= DATEADD(day, -7, GETDATE())
                    AND t.TaskId NOT IN (
                        SELECT TaskId FROM NotificationReads WHERE OJTUserId = @UserId AND SubmissionId IS NULL
                    )",
                new { UserId = userId });
            // Dismiss resubmit notifications
            await conn.ExecuteAsync(
                @"INSERT INTO NotificationReads (OJTUserId, TaskId, SubmissionId)
                  SELECT @UserId, ts.TaskId, ts.SubmissionId
                  FROM TaskSubmissions ts
                  JOIN Tasks t ON ts.TaskId = t.TaskId
                  WHERE ts.OJTUserId = @UserId AND ts.Status = 'Rejected' AND t.Status = 'New'
                    AND ts.ReviewedAt >= DATEADD(day, -7, GETDATE())
                    AND ts.SubmissionId NOT IN (
                        SELECT ISNULL(SubmissionId, 0) FROM NotificationReads
                        WHERE OJTUserId = @UserId AND SubmissionId IS NOT NULL
                    )",
                new { UserId = userId });
            // Self-cleaning: delete old read records for this user
            await conn.ExecuteAsync(
                "DELETE FROM NotificationReads WHERE OJTUserId=@UserId AND ReadAt < DATEADD(day, -7, GETDATE())",
                new { UserId = userId });
            return Json(new { success = true, remaining = 0 });
        }

        // ===================== DASHBOARD =====================
        public async Task<IActionResult> Index()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();

            var user = await conn.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE UserId = @UserId", new { UserId = userId });

            var tasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*,
                    u1.FirstName + ' ' + u1.LastName AS AssignedToName,
                    u2.FirstName + ' ' + u2.LastName AS AssignedByName,
                    u2.Branch AS AssignedByBranch
                  FROM Tasks t
                  JOIN Users u1 ON t.AssignedToUserId = u1.UserId
                  JOIN Users u2 ON t.AssignedByUserId = u2.UserId
                  WHERE t.AssignedToUserId = @UserId AND t.Status != 'Cancelled'
                  ORDER BY t.CreatedAt DESC",
                new { UserId = userId })).ToList();

            var cancelledCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Tasks WHERE AssignedToUserId = @UserId AND Status = 'Cancelled'",
                new { UserId = userId });

            var history = (await conn.QueryAsync<TaskSubmission>(
                @"SELECT ts.*, t.Title AS TaskTitle,
                    u.FirstName + ' ' + u.LastName AS EmployeeName
                  FROM TaskSubmissions ts
                  JOIN Tasks t ON ts.TaskId = t.TaskId
                  JOIN Users u ON t.AssignedByUserId = u.UserId
                  WHERE ts.OJTUserId = @UserId AND ts.Status = 'Approved'
                  ORDER BY ts.SubmittedAt DESC",
                new { UserId = userId })).ToList();

            await SetSidebarCounts(conn, userId);

            return View(new OJTDashboardViewModel
            {
                CurrentUser = user!,
                Tasks = tasks,
                SubmissionHistory = history,
                TotalTasks = tasks.Count,
                NewTasks = tasks.Count(t => t.Status == "New"),
                PendingTasks = tasks.Count(t => t.Status == "Pending"),
                CompletedTasks = tasks.Count(t => t.Status == "Approved"),
                CancelledTasks = cancelledCount
            });
        }

        // ===================== NEW TASKS =====================
        public async Task<IActionResult> NewTasks()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var tasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*, u1.FirstName+' '+u1.LastName AS AssignedToName, u2.FirstName+' '+u2.LastName AS AssignedByName, u2.Branch AS AssignedByBranch
                  FROM Tasks t JOIN Users u1 ON t.AssignedToUserId=u1.UserId JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.AssignedToUserId=@UserId AND t.Status='New' ORDER BY t.CreatedAt DESC",
                new { UserId = userId })).ToList();
            await SetSidebarCounts(conn, userId);
            ViewBag.FilterLabel = "New Tasks";
            return View("TaskFilter", tasks);
        }

        // ===================== PENDING TASKS =====================
        public async Task<IActionResult> PendingTasks()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var tasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*, u1.FirstName+' '+u1.LastName AS AssignedToName, u2.FirstName+' '+u2.LastName AS AssignedByName, u2.Branch AS AssignedByBranch
                  FROM Tasks t JOIN Users u1 ON t.AssignedToUserId=u1.UserId JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.AssignedToUserId=@UserId AND t.Status='Pending' ORDER BY t.CreatedAt DESC",
                new { UserId = userId })).ToList();
            await SetSidebarCounts(conn, userId);
            ViewBag.FilterLabel = "Pending Tasks";
            return View("TaskFilter", tasks);
        }

        // ===================== COMPLETED TASKS =====================
        public async Task<IActionResult> CompletedTasks()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var tasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*, ISNULL(u1.FirstName+' '+u1.LastName,'(Removed)') AS AssignedToName, ISNULL(u2.FirstName+' '+u2.LastName,'Unknown') AS AssignedByName, ISNULL(u2.Branch,'') AS AssignedByBranch
                  FROM Tasks t LEFT JOIN Users u1 ON t.AssignedToUserId=u1.UserId LEFT JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.AssignedToUserId=@UserId AND t.Status='Approved' ORDER BY t.CreatedAt DESC",
                new { UserId = userId })).ToList();
            await SetSidebarCounts(conn, userId);
            ViewBag.FilterLabel = "Completed Tasks";
            return View("TaskFilter", tasks);
        }

        // ===================== HISTORY =====================
        public async Task<IActionResult> History()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var history = (await conn.QueryAsync<TaskSubmission>(
                @"SELECT ts.*, t.Title AS TaskTitle, ISNULL(u.FirstName+' '+u.LastName,'Unknown') AS EmployeeName, ISNULL(u.Branch,'') AS EmployeeBranch
                  FROM TaskSubmissions ts JOIN Tasks t ON ts.TaskId=t.TaskId LEFT JOIN Users u ON t.AssignedByUserId=u.UserId
                  WHERE ts.OJTUserId=@UserId AND ts.Status='Approved' ORDER BY ts.SubmittedAt DESC",
                new { UserId = userId })).ToList();
            var cancelledTasks = (await conn.QueryAsync<TaskItem>(
                @"SELECT t.*,
                    ISNULL(u1.FirstName+' '+u1.LastName,'(Removed)') AS AssignedToName,
                    ISNULL(u2.FirstName+' '+u2.LastName,'Unknown') AS AssignedByName, ISNULL(u2.Branch,'') AS AssignedByBranch
                  FROM Tasks t
                  LEFT JOIN Users u1 ON t.AssignedToUserId=u1.UserId
                  LEFT JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.AssignedToUserId=@UserId AND t.Status='Cancelled'
                  ORDER BY t.UpdatedAt DESC",
                new { UserId = userId })).ToList();
            await SetSidebarCounts(conn, userId);
            ViewBag.CancelledTasks = cancelledTasks;
            return View("HistoryView", history);
        }

        // ===================== START TASK (POST) — saves work status + optionally submits task =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartTask(int TaskId, string WorkStatus, string? Description,
            string? returnUrl, string? goSubmit,
            string? SubmissionTitle, string? SubmissionDescription, List<IFormFile>? Files, string? LinkUrl)
        {
            int userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(WorkStatus))
            { TempData["ErrorMessage"] = "Please select a work status."; return RedirectToAction("Index"); }

            using var conn = GetConnection();
            var task = await conn.QueryFirstOrDefaultAsync<TaskItem>(
                "SELECT * FROM Tasks WHERE TaskId=@TaskId AND AssignedToUserId=@UserId",
                new { TaskId, UserId = userId });
            if (task == null) return NotFound();

            // Always save work status + log
            await conn.ExecuteAsync(
                "UPDATE Tasks SET WorkStatus=@WorkStatus, UpdatedAt=GETDATE() WHERE TaskId=@TaskId",
                new { WorkStatus, TaskId });
            await conn.ExecuteAsync(
                @"INSERT INTO TaskProgressLogs(TaskId, OJTUserId, WorkStatus, Description, LoggedAt)
                  VALUES(@TaskId, @OJTUserId, @WorkStatus, @Description, GETDATE())",
                new { TaskId, OJTUserId = userId, WorkStatus, Description });

            // If "Submit Task" was clicked, also create the submission
            if (goSubmit == "true" && !string.IsNullOrEmpty(SubmissionDescription))
            {
                string? filePath = null, fileName = null;
                var _allowed = new[] { ".pdf", ".xlsx", ".xls", ".docx", ".doc", ".pptx", ".ppt", ".png", ".jpg", ".jpeg", ".pbix", ".zip" };
                if (Files != null && Files.Any(f => f?.Length > 0))
                {
                    var validFiles = Files.Where(f => f != null && f.Length > 0).ToList();
                    var dir = Path.Combine(_env.WebRootPath, "uploads", "submissions");
                    Directory.CreateDirectory(dir);
                    var paths = new List<string>(); var names = new List<string>();
                    foreach (var f in validFiles)
                    {
                        var ext2 = Path.GetExtension(f.FileName).ToLower();
                        if (!_allowed.Contains(ext2)) continue;
                        var sn = $"{Guid.NewGuid()}{ext2}";
                        using var s = new FileStream(Path.Combine(dir, sn), FileMode.Create);
                        await f.CopyToAsync(s);
                        paths.Add($"/uploads/submissions/{sn}"); names.Add(f.FileName);
                    }
                    if (paths.Any())
                    {
                        filePath = System.Text.Json.JsonSerializer.Serialize(paths);
                        fileName = System.Text.Json.JsonSerializer.Serialize(names);
                    }
                }
                var existingRejected = await conn.QueryFirstOrDefaultAsync<TaskSubmission>(
                    "SELECT * FROM TaskSubmissions WHERE TaskId=@TaskId AND OJTUserId=@UserId AND Status='Rejected' ORDER BY SubmittedAt DESC",
                    new { TaskId, UserId = userId });
                if (existingRejected != null)
                    await conn.ExecuteAsync(
                        @"UPDATE TaskSubmissions SET SubmissionTitle=@Title, SubmissionDescription=@Desc,
                          FilePath=@FilePath, FileName=@FileName, LinkUrl=@Link,
                          Status='Submitted', SubmittedAt=GETDATE() WHERE SubmissionId=@SubmissionId",
                        new { Title = task.Title, Desc = SubmissionDescription, FilePath = filePath, FileName = fileName, Link = LinkUrl, SubmissionId = existingRejected.SubmissionId });
                else
                    await conn.ExecuteAsync(
                        @"INSERT INTO TaskSubmissions(TaskId,OJTUserId,SubmissionTitle,SubmissionDescription,FilePath,FileName,LinkUrl,Status,SubmittedAt)
                          VALUES(@TaskId,@OJTUserId,@Title,@Desc,@FilePath,@FileName,@Link,'Submitted',GETDATE())",
                        new { TaskId, OJTUserId = userId, Title = task.Title, Desc = SubmissionDescription, FilePath = filePath, FileName = fileName, Link = LinkUrl });
                await conn.ExecuteAsync("UPDATE Tasks SET Status='Pending',UpdatedAt=GETDATE() WHERE TaskId=@TaskId", new { TaskId });
                TempData["SuccessMessage"] = $"'{task.Title}' submitted! Waiting for employee review.";
                return RedirectToAction("Index");
            }

            TempData["SuccessMessage"] = $"Progress saved — status: {WorkStatus}.";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index");
        }

        // ===================== SUBMIT TASK (GET) — merged with Start Task =====================
        [HttpGet]
        public async Task<IActionResult> SubmitTask(int id)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var task = await conn.QueryFirstOrDefaultAsync<TaskItem>(
                @"SELECT t.*, u.FirstName+' '+u.LastName AS AssignedByName, u.Branch AS AssignedByBranch
                  FROM Tasks t LEFT JOIN Users u ON t.AssignedByUserId=u.UserId
                  WHERE t.TaskId=@TaskId AND t.AssignedToUserId=@UserId",
                new { TaskId = id, UserId = userId });
            if (task == null) return NotFound();
            if (task.Status == "Pending" || task.Status == "Approved")
            { TempData["ErrorMessage"] = "This task has already been submitted or approved."; return RedirectToAction("Index"); }

            var logs = (await conn.QueryAsync<TaskProgressLog>(
                @"SELECT tpl.*, u.FirstName+' '+u.LastName AS OJTName
                  FROM TaskProgressLogs tpl LEFT JOIN Users u ON tpl.OJTUserId=u.UserId
                  WHERE tpl.TaskId=@TaskId AND tpl.OJTUserId=@UserId
                  ORDER BY tpl.LoggedAt DESC",
                new { TaskId = id, UserId = userId })).ToList();

            await SetSidebarCounts(conn, userId);
            return View("SubmitTask", new SubmitTaskViewModel
            {
                TaskId = task.TaskId,
                TaskTitle = task.Title,
                AssignedByName = task.AssignedByName ?? "",
                DeadlineDisplay = task.DeadlinePhDisplay,
                SubmissionTitle = task.Title,
                CurrentWorkStatus = task.WorkStatus,
                RecentLogs = logs
            });
        }

        // ===================== SUBMIT TASK (POST) =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitTask(SubmitTaskViewModel model)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var task = await conn.QueryFirstOrDefaultAsync<TaskItem>(
                @"SELECT t.*, u.FirstName+' '+u.LastName AS AssignedByName, u.Branch AS AssignedByBranch FROM Tasks t JOIN Users u ON t.AssignedByUserId=u.UserId
                  WHERE t.TaskId=@TaskId AND t.AssignedToUserId=@UserId",
                new { TaskId = model.TaskId, UserId = userId });
            if (task == null) return NotFound();
            model.SubmissionTitle = task.Title; model.TaskTitle = task.Title;
            model.AssignedByName = task.AssignedByName ?? ""; model.DeadlineDisplay = task.DeadlinePhDisplay;
            ModelState.Remove(nameof(model.SubmissionTitle));
            if (!ModelState.IsValid) return View(model);
            string? filePath = null, fileName = null;
            var allowed2 = new[] { ".pdf", ".xlsx", ".xls", ".docx", ".doc", ".pptx", ".ppt", ".png", ".jpg", ".jpeg", ".pbix", ".zip" };
            if (model.Files != null && model.Files.Any(f => f?.Length > 0))
            {
                var validFiles = model.Files.Where(f => f != null && f.Length > 0).ToList();
                foreach (var f in validFiles)
                {
                    var ext2 = Path.GetExtension(f.FileName).ToLower();
                    if (!allowed2.Contains(ext2)) { ModelState.AddModelError("Files", $"'{f.FileName}' is not an allowed file type."); return View(model); }
                }
                var dir = Path.Combine(_env.WebRootPath, "uploads", "submissions");
                Directory.CreateDirectory(dir);
                var paths = new List<string>(); var names = new List<string>();
                foreach (var f in validFiles)
                {
                    var ext2 = Path.GetExtension(f.FileName).ToLower();
                    var sn = $"{Guid.NewGuid()}{ext2}";
                    using var s = new FileStream(Path.Combine(dir, sn), FileMode.Create);
                    await f.CopyToAsync(s);
                    paths.Add($"/uploads/submissions/{sn}"); names.Add(f.FileName);
                }
                filePath = System.Text.Json.JsonSerializer.Serialize(paths);
                fileName = System.Text.Json.JsonSerializer.Serialize(names);
            }
            // If there's a rejected submission, update it; otherwise insert fresh
            var existingRejected = await conn.QueryFirstOrDefaultAsync<TaskSubmission>(
                "SELECT * FROM TaskSubmissions WHERE TaskId=@TaskId AND OJTUserId=@UserId AND Status='Rejected' ORDER BY SubmittedAt DESC",
                new { TaskId = model.TaskId, UserId = userId });
            if (existingRejected != null)
            {
                await conn.ExecuteAsync(
                    @"UPDATE TaskSubmissions SET SubmissionTitle=@Title, SubmissionDescription=@Desc,
                        FilePath=@FilePath, FileName=@FileName, LinkUrl=@Link,
                        Status='Submitted', SubmittedAt=GETDATE()
                      WHERE SubmissionId=@SubmissionId",
                    new { Title = task.Title, Desc = model.SubmissionDescription, FilePath = filePath, FileName = fileName, Link = model.LinkUrl, SubmissionId = existingRejected.SubmissionId });
            }
            else
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO TaskSubmissions(TaskId,OJTUserId,SubmissionTitle,SubmissionDescription,FilePath,FileName,LinkUrl,Status,SubmittedAt)
                      VALUES(@TaskId,@OJTUserId,@Title,@Desc,@FilePath,@FileName,@Link,'Submitted',GETDATE())",
                    new { TaskId = model.TaskId, OJTUserId = userId, Title = task.Title, Desc = model.SubmissionDescription, FilePath = filePath, FileName = fileName, Link = model.LinkUrl });
            }
            await conn.ExecuteAsync("UPDATE Tasks SET Status='Pending',UpdatedAt=GETDATE() WHERE TaskId=@TaskId", new { TaskId = model.TaskId });
            TempData["SuccessMessage"] = $"'{task.Title}' submitted! Waiting for employee review.";
            return RedirectToAction("Index");
        }

        // ===================== TASK DETAILS =====================
        public async Task<IActionResult> TaskDetails(int id)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var task = await conn.QueryFirstOrDefaultAsync<TaskItem>(
                @"SELECT t.*, u1.FirstName+' '+u1.LastName AS AssignedToName, u2.FirstName+' '+u2.LastName AS AssignedByName
                  FROM Tasks t JOIN Users u1 ON t.AssignedToUserId=u1.UserId JOIN Users u2 ON t.AssignedByUserId=u2.UserId
                  WHERE t.TaskId=@TaskId AND t.AssignedToUserId=@UserId",
                new { TaskId = id, UserId = userId });
            if (task == null) return NotFound();
            // Fetch most recent submission of ANY status so TaskDetails can show rejected ones
            var submission = await conn.QueryFirstOrDefaultAsync<TaskSubmission>(
                "SELECT * FROM TaskSubmissions WHERE TaskId=@TaskId AND OJTUserId=@UserId ORDER BY SubmittedAt DESC",
                new { TaskId = id, UserId = userId });
            await SetSidebarCounts(conn, userId);
            ViewBag.Submission = submission;
            return View(task);
        }

        // ===================== DELETE HISTORY =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHistory(int submissionId)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var sub = await conn.QueryFirstOrDefaultAsync<TaskSubmission>(
                "SELECT * FROM TaskSubmissions WHERE SubmissionId=@Id AND OJTUserId=@UserId AND Status='Approved'",
                new { Id = submissionId, UserId = userId });
            if (sub != null) await conn.ExecuteAsync("DELETE FROM TaskSubmissions WHERE SubmissionId=@Id", new { Id = submissionId });
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
            await conn.ExecuteAsync("DELETE FROM TaskSubmissions WHERE OJTUserId=@UserId AND Status='Approved'", new { UserId = userId });
            TempData["SuccessMessage"] = "All completed task history cleared.";
            return RedirectToAction("History");
        }
        // ===================== MY CALENDAR =====================
        public async Task<IActionResult> MyCalendar()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            await SetSidebarCounts(conn, userId);

            // Load all employees who assigned tasks to this OJT
            var taskers = (await conn.QueryAsync<dynamic>(
                @"SELECT DISTINCT u.UserId, u.FirstName + ' ' + u.LastName AS FullName
                  FROM Tasks t JOIN Users u ON t.AssignedByUserId = u.UserId
                  WHERE t.AssignedToUserId = @UserId
                  ORDER BY FullName",
                new { UserId = userId })).ToList();

            ViewBag.Taskers = taskers;
            ViewBag.Branches = new[] { "Agoo", "Reina Mercedes", "Candon", "Pasig" };
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetCalendarTasks(string branch = "", int taskerId = 0)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();

            string sql = @"
                SELECT t.TaskId, t.Title, t.Status, t.CreatedAt, t.Deadline, t.TaskType,
                       u.FirstName + ' ' + u.LastName AS AssignedByName,
                       u.Branch AS AssignedByBranch
                FROM Tasks t
                JOIN Users u ON t.AssignedByUserId = u.UserId
                WHERE t.AssignedToUserId = @UserId
                  AND t.Status != 'Cancelled'";

            if (taskerId > 0) sql += " AND t.AssignedByUserId = @TaskerId";
            if (!string.IsNullOrEmpty(branch)) sql += " AND u.Branch = @Branch";

            var tasks = (await conn.QueryAsync<dynamic>(sql,
                new { UserId = userId, TaskerId = taskerId, Branch = branch })).ToList();

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
                    extendedProps = new { type = approved ? "completed" : "assigned", assignedBy = (string)t.AssignedByName, branch = (string)(t.AssignedByBranch ?? ""), dueDate = dueStr, taskType = (string)(t.TaskType ?? "Individual") }
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
                        extendedProps = new { type = "deadline", assignedBy = (string)t.AssignedByName, branch = (string)(t.AssignedByBranch ?? ""), dueDate = dueStr, taskType = (string)(t.TaskType ?? "Individual") }
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
            // Get the task's title and assignedBy to find all sibling group tasks
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
    }



    public class OJTNotification
    {
        public int TaskId { get; set; }
        public string TaskTitle { get; set; } = string.Empty;
        public string AssignedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsDueSoon { get; set; }
        public bool IsResubmit { get; set; }  // true = employee requested resubmission
        public int SubmissionId { get; set; }
        public DateTime? Deadline { get; set; }
    }
}