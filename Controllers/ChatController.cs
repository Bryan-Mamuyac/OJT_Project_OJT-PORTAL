using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using ITPMS_OJT.Models;
using ITPMS_OJT.Hubs;
using Microsoft.AspNetCore.SignalR;
using Dapper;
namespace ITPMS_OJT.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ChatHub>? _hub;

        public ChatController(IConfiguration config, IWebHostEnvironment env, IHubContext<ChatHub>? hub = null)
        {
            _config = config;
            _env = env;
            _hub = hub;
        }

        private SqlConnection GetConnection() =>
            new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        private string GetCurrentRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "";

        // ===================== INDEX =====================
        public async Task<IActionResult> Index(int? openWith)
        {
            int userId = GetCurrentUserId();
            string role = GetCurrentRole();
            using var conn = GetConnection();

            await SetChatSidebarCounts(conn, userId, role);

            // Load conversation list with last message + unread count
            var convos = (await conn.QueryAsync<dynamic>(@"
                SELECT * FROM (
                    SELECT
                        c.ConversationId,
                        CASE WHEN c.User1Id = @Me THEN c.User2Id ELSE c.User1Id END AS OtherId,
                        u.FirstName + ' ' + u.LastName AS OtherName,
                        u.Role AS OtherRole,
                        u.Department AS OtherDept,
                        u.Branch AS OtherBranch,
                        u.ProfilePicture AS OtherPic,
                        (SELECT TOP 1 Body FROM ChatMessages
                         WHERE ConversationId = c.ConversationId
                         ORDER BY SentAt DESC) AS LastMessage,
                        (SELECT TOP 1 AttachedFileName FROM ChatMessages
                         WHERE ConversationId = c.ConversationId
                         ORDER BY SentAt DESC) AS LastFile,
                        (SELECT TOP 1 SentAt FROM ChatMessages
                         WHERE ConversationId = c.ConversationId
                         ORDER BY SentAt DESC) AS LastSentAt,
                        (SELECT COUNT(*) FROM ChatMessages
                         WHERE ConversationId = c.ConversationId
                           AND SenderId != @Me AND IsRead = 0) AS UnreadCount
                    FROM ChatConversations c
                    JOIN Users u ON u.UserId = CASE WHEN c.User1Id = @Me THEN c.User2Id ELSE c.User1Id END
                    WHERE c.User1Id = @Me OR c.User2Id = @Me
                ) AS ConvList
                ORDER BY ISNULL(LastSentAt, '1900-01-01') DESC",
                new { Me = userId })).ToList();

            // If openWith specified, auto-create/find conversation
            int openConvId = 0;
            if (openWith.HasValue && openWith.Value > 0)
            {
                openConvId = await GetOrCreateConvoId(conn, userId, openWith.Value);
            }
            else if (convos.Count > 0)
            {
                openConvId = (int)convos[0].ConversationId;
            }

            ViewBag.Conversations = convos;
            ViewBag.OpenConvId = openConvId;
            ViewBag.MyId = userId;
            ViewBag.MyRole = role;
            ViewBag.MyName = User.FindFirst("FullName")?.Value ?? "";
            return View();
        }

        // ===================== GET OR CREATE (JSON) =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GetOrCreateConversation(int targetUserId)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            int convId = await GetOrCreateConvoId(conn, userId, targetUserId);
            return Json(new { conversationId = convId });
        }

        // ===================== GET OR CREATE CONVERSATION (redirect) =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartConversation(int targetUserId)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            int convId = await GetOrCreateConvoId(conn, userId, targetUserId);
            return RedirectToAction("Index", new { openWith = targetUserId });
        }

        private async Task<int> GetOrCreateConvoId(SqlConnection conn, int me, int other)
        {
            int u1 = Math.Min(me, other);
            int u2 = Math.Max(me, other);
            var existing = await conn.ExecuteScalarAsync<int?>(
                "SELECT ConversationId FROM ChatConversations WHERE User1Id=@U1 AND User2Id=@U2",
                new { U1 = u1, U2 = u2 });
            if (existing.HasValue) return existing.Value;
            var newId = await conn.ExecuteScalarAsync<decimal>(
                @"INSERT INTO ChatConversations(User1Id,User2Id,CreatedAt)
                  VALUES(@U1,@U2,GETDATE());
                  SELECT SCOPE_IDENTITY();",
                new { U1 = u1, U2 = u2 });
            return (int)newId;
        }

        // ===================== GET MESSAGES =====================
        [HttpGet]
        [ResponseCache(NoStore = true)]
        public async Task<IActionResult> GetMessages(int conversationId, int afterId = 0)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();

            // Verify user belongs to this conversation
            var conv = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM ChatConversations WHERE ConversationId=@Id AND (User1Id=@Me OR User2Id=@Me)",
                new { Id = conversationId, Me = userId });
            if (conv == null) return Unauthorized();

            var messages = (await conn.QueryAsync<dynamic>(@"
                SELECT m.MessageId, m.ConversationId, m.SenderId,
                       m.Body, m.AttachedFilePath, m.AttachedFileName, m.FileType,
                       m.SentAt, m.IsRead,
                       u.FirstName + ' ' + u.LastName AS SenderName,
                       u.ProfilePicture AS SenderPic
                FROM ChatMessages m
                JOIN Users u ON u.UserId = m.SenderId
                WHERE m.ConversationId = @ConvId
                  AND m.MessageId > @AfterId
                ORDER BY m.MessageId ASC",
                new { ConvId = conversationId, AfterId = afterId })).ToList();

            // Mark as read
            await conn.ExecuteAsync(
                "UPDATE ChatMessages SET IsRead=1 WHERE ConversationId=@ConvId AND SenderId!=@Me AND IsRead=0",
                new { ConvId = conversationId, Me = userId });

            return Json(messages.Select(m => new {
                messageId = (int)m.MessageId,
                senderId = (int)m.SenderId,
                senderName = (string)(m.SenderName ?? ""),
                senderPic = m.SenderPic == null ? null : (string)m.SenderPic,
                body = m.Body == null ? null : (string)m.Body,
                attachedFilePath = m.AttachedFilePath == null ? null : (string)m.AttachedFilePath,
                attachedFileName = m.AttachedFileName == null ? null : (string)m.AttachedFileName,
                fileType = m.FileType == null ? null : (string)m.FileType,
                sentAt = ((DateTime)m.SentAt).ToString("o"),
                sentAtDisplay = FormatTime((DateTime)m.SentAt),
                isMine = (int)m.SenderId == userId
            }));
        }

        // ===================== SEND MESSAGE =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int conversationId, string? body, IFormFile? file)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();

            var convRow = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM ChatConversations WHERE ConversationId=@Id AND (User1Id=@Me OR User2Id=@Me)",
                new { Id = conversationId, Me = userId });
            if (convRow == null) return Unauthorized();
            var conv = convRow; // alias for compatibility

            if (string.IsNullOrWhiteSpace(body) && file == null)
                return BadRequest("Empty message.");
            if (!string.IsNullOrEmpty(body) && body.Trim().Length > 500)
                return BadRequest("Message exceeds 500 characters.");

            string? filePath = null, fileName = null, fileType = null;

            if (file != null && file.Length > 0)
            {
                var allowed = new[] { ".png",".jpg",".jpeg",".pdf",".docx",".doc",
                                      ".xlsx",".xls",".pptx",".ppt",".pbix",".zip" };
                var ext = Path.GetExtension(file.FileName).ToLower();
                if (!allowed.Contains(ext))
                    return BadRequest("File type not allowed.");
                if (file.Length > 20 * 1024 * 1024)
                    return BadRequest("File too large (max 20MB).");

                var dir = Path.Combine(_env.WebRootPath, "uploads", "chat");
                Directory.CreateDirectory(dir);
                var sn = Guid.NewGuid().ToString("N") + ext;
                using var fs = System.IO.File.Create(Path.Combine(dir, sn));
                await file.CopyToAsync(fs);
                filePath = $"/uploads/chat/{sn}";
                fileName = file.FileName;
                fileType = ext switch
                {
                    ".png" or ".jpg" or ".jpeg" => "image",
                    ".pdf" => "pdf",
                    ".docx" or ".doc" => "word",
                    ".xlsx" or ".xls" => "excel",
                    ".pptx" or ".ppt" => "ppt",
                    ".pbix" => "powerbi",
                    ".zip" => "zip",
                    _ => "other"
                };
            }

            var msgIdRaw = await conn.ExecuteScalarAsync<decimal>(@"
                INSERT INTO ChatMessages(ConversationId,SenderId,Body,AttachedFilePath,AttachedFileName,FileType,SentAt,IsRead)
                VALUES(@ConvId,@Sender,@Body,@FilePath,@FileName,@FileType,GETDATE(),0);
                SELECT SCOPE_IDENTITY();",
                new
                {
                    ConvId = conversationId,
                    Sender = userId,
                    Body = body?.Trim(),
                    FilePath = filePath,
                    FileName = fileName,
                    FileType = fileType
                });

            int msgId = (int)msgIdRaw;
            var msg = await conn.QueryFirstAsync<dynamic>(
                @"SELECT m.*, u.FirstName+' '+u.LastName AS SenderName, u.ProfilePicture AS SenderPic
                  FROM ChatMessages m JOIN Users u ON u.UserId=m.SenderId
                  WHERE m.MessageId=@Id", new { Id = msgId });

            // Build payload once, push to other clients via SignalR
            var pushPayload = new
            {
                conversationId = conversationId,
                messageId = (int)msg.MessageId,
                senderId = (int)msg.SenderId,
                senderName = (string)(msg.SenderName ?? ""),
                senderPic = msg.SenderPic == null ? null : (string)msg.SenderPic,
                body = msg.Body == null ? null : (string)msg.Body,
                attachedFilePath = msg.AttachedFilePath == null ? null : (string)msg.AttachedFilePath,
                attachedFileName = msg.AttachedFileName == null ? null : (string)msg.AttachedFileName,
                fileType = msg.FileType == null ? null : (string)msg.FileType,
                sentAt = ((DateTime)msg.SentAt).ToString("o"),
                sentAtDisplay = FormatTime((DateTime)msg.SentAt),
                isMine = false  // from receiver's perspective
            };
            if (_hub != null)
                await _hub.Clients
                           .Group("conv_" + conversationId)
                           .SendAsync("ReceiveMessage", pushPayload);

            // Notify the other user their unread count changed
            int otherUser = ((int)convRow.User1Id == userId) ? (int)convRow.User2Id : (int)convRow.User1Id;
            if (_hub != null)
                await _hub.Clients.User(otherUser.ToString())
                                   .SendAsync("UnreadCountChanged");

            return Json(new
            {
                messageId = (int)msg.MessageId,
                senderId = (int)msg.SenderId,
                senderName = (string)(msg.SenderName ?? ""),
                senderPic = msg.SenderPic == null ? null : (string)msg.SenderPic,
                body = msg.Body == null ? null : (string)msg.Body,
                attachedFilePath = msg.AttachedFilePath == null ? null : (string)msg.AttachedFilePath,
                attachedFileName = msg.AttachedFileName == null ? null : (string)msg.AttachedFileName,
                fileType = msg.FileType == null ? null : (string)msg.FileType,
                sentAt = ((DateTime)msg.SentAt).ToString("o"),
                sentAtDisplay = FormatTime((DateTime)msg.SentAt),
                isMine = true
            });
        }

        // ===================== GET USERS (people picker) =====================
        [HttpGet]
        [ResponseCache(NoStore = true)]
        public async Task<IActionResult> GetUsers(string role = "", string branch = "", string department = "")
        {
            int myId = GetCurrentUserId();
            string myRole = GetCurrentRole();
            using var conn = GetConnection();

            string sql = @"SELECT UserId, FirstName+' '+LastName AS FullName,
                                  Role, Department, Branch, ProfilePicture
                           FROM Users WHERE Status IN ('Active','Approved') AND UserId != @Me";

            // Role filtering rules
            if (myRole == "OJT")
            {
                // OJT can chat with Employees and other OJTs (not Admins)
                sql += " AND Role IN ('Employee','OJT')";
            }
            else if (myRole == "Employee")
            {
                // Employee can chat with other Employees and OJTs (not Admins)
                sql += " AND Role IN ('Employee','OJT')";
            }
            else
            {
                // Admin can chat with everyone
            }

            if (!string.IsNullOrEmpty(role)) sql += " AND Role = @Role";
            if (!string.IsNullOrEmpty(branch)) sql += " AND Branch = @Branch";
            if (!string.IsNullOrEmpty(department)) sql += " AND Department = @Department";

            sql += " ORDER BY Role, FirstName, LastName";

            var users = (await conn.QueryAsync<dynamic>(sql,
                new { Me = myId, Role = role, Branch = branch, Department = department })).ToList();

            return Json(users.Select(u => new {
                userId = (int)u.UserId,
                fullName = (string)(u.FullName ?? ""),
                role = (string)(u.Role ?? ""),
                department = u.Department == null ? null : (string)u.Department,
                branch = u.Branch == null ? null : (string)u.Branch,
                pic = u.ProfilePicture == null ? null : (string)u.ProfilePicture
            }));
        }

        // ===================== GET UNREAD COUNT (for sidebar badge polling) =====================
        [HttpGet]
        [ResponseCache(NoStore = true)]
        public async Task<IActionResult> GetUnreadCount()
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            int count = await GetUnreadChatCount(conn, userId);
            return Json(new { count });
        }

        // ===================== MARK CONVERSATION READ =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int conversationId)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            await conn.ExecuteAsync(
                "UPDATE ChatMessages SET IsRead=1 WHERE ConversationId=@ConvId AND SenderId!=@Me AND IsRead=0",
                new { ConvId = conversationId, Me = userId });
            int remaining = await GetUnreadChatCount(conn, userId);
            return Json(new { remaining });
        }

        // ===================== GET CONVERSATION INFO =====================
        [HttpGet]
        [ResponseCache(NoStore = true)]
        public async Task<IActionResult> GetConversation(int conversationId)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var conv = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT c.ConversationId,
                       CASE WHEN c.User1Id=@Me THEN c.User2Id ELSE c.User1Id END AS OtherId,
                       u.FirstName+' '+u.LastName AS OtherName,
                       u.Role AS OtherRole, u.Department AS OtherDept, u.Branch AS OtherBranch,
                       u.ProfilePicture AS OtherPic
                FROM ChatConversations c
                JOIN Users u ON u.UserId = CASE WHEN c.User1Id=@Me THEN c.User2Id ELSE c.User1Id END
                WHERE c.ConversationId=@Id AND (c.User1Id=@Me OR c.User2Id=@Me)",
                new { Id = conversationId, Me = userId });
            if (conv == null) return NotFound();
            return Json(new
            {
                conversationId = (int)conv.ConversationId,
                otherId = (int)conv.OtherId,
                otherName = (string)conv.OtherName,
                otherRole = (string)conv.OtherRole,
                otherDept = (string?)conv.OtherDept,
                otherBranch = (string?)conv.OtherBranch,
                otherPic = (string?)conv.OtherPic
            });
        }

        // ===================== DELETE MESSAGE =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            int userId = GetCurrentUserId();
            using var conn = GetConnection();
            var msg = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT MessageId, AttachedFilePath FROM ChatMessages WHERE MessageId=@Id AND SenderId=@Me",
                new { Id = messageId, Me = userId });
            if (msg == null) return Json(new { success = false, error = "Not found or not yours." });

            // Delete file from disk
            if (msg.AttachedFilePath != null)
            {
                string? fp = msg.AttachedFilePath as string;
                if (!string.IsNullOrEmpty(fp))
                {
                    var fullPath = Path.Combine(_env.WebRootPath, fp.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }
            }
            // Get conversation ID before deleting
            var convDel = await conn.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT m.ConversationId FROM ChatMessages m WHERE m.MessageId=@Id",
                new { Id = messageId });

            // Hard delete — no wasted space in DB
            await conn.ExecuteAsync("DELETE FROM ChatMessages WHERE MessageId=@Id", new { Id = messageId });

            // Push delete event to ALL clients in this conversation via SignalR
            if (convDel != null && _hub != null)
                await _hub.Clients.Group("conv_" + (int)convDel.ConversationId)
                                   .SendAsync("MessageDeleted", messageId);

            return Json(new { success = true });
        }


        // ===================== HELPERS =====================
        public static async Task<int> GetUnreadChatCount(SqlConnection conn, int userId)
        {
            return await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM ChatMessages m
                JOIN ChatConversations c ON c.ConversationId = m.ConversationId
                WHERE (c.User1Id=@Me OR c.User2Id=@Me)
                  AND m.SenderId != @Me AND m.IsRead = 0",
                new { Me = userId });
        }

        private async Task SetChatSidebarCounts(SqlConnection conn, int userId, string role)
        {
            // Set existing sidebar badge counts depending on role
            var pic = await conn.ExecuteScalarAsync<string?>(
                "SELECT ProfilePicture FROM Users WHERE UserId=@UserId", new { UserId = userId });
            ViewBag.ProfilePicUrl = string.IsNullOrEmpty(pic) ? null : $"/uploads/profiles/{pic}";

            int unread = await GetUnreadChatCount(conn, userId);
            ViewBag.UnreadChatCount = unread;

            if (role == "OJT")
            {
                var tasks = (await conn.QueryAsync<TaskItem>(
                    "SELECT Status FROM Tasks WHERE AssignedToUserId=@UserId", new { UserId = userId })).ToList();
                ViewBag.NewTaskCount = tasks.Count(t => t.Status == "New");
                ViewBag.PendingTaskCount = tasks.Count(t => t.Status == "Pending");
                ViewBag.CompletedTaskCount = tasks.Count(t => t.Status == "Approved");
                var newTasks = (await conn.QueryAsync<OJTNotification>(
                    @"SELECT t.TaskId, t.Title AS TaskTitle, t.CreatedAt, t.Deadline,
                        u.FirstName + ' ' + u.LastName AS AssignedByName, 0 AS IsDueSoon
                      FROM Tasks t JOIN Users u ON t.AssignedByUserId = u.UserId
                      WHERE t.AssignedToUserId = @UserId AND t.Status = 'New'
                        AND t.CreatedAt >= DATEADD(day,-7,GETDATE())
                        AND t.TaskId NOT IN (SELECT TaskId FROM NotificationReads WHERE OJTUserId=@UserId)
                      ORDER BY t.CreatedAt DESC",
                    new { UserId = userId })).ToList();
                ViewBag.Notifications = newTasks;
            }
            else if (role == "Employee")
            {
                var tasks = (await conn.QueryAsync<TaskItem>(
                    "SELECT Status FROM Tasks WHERE AssignedByUserId=@UserId AND Status!='Cancelled'",
                    new { UserId = userId })).ToList();
                ViewBag.EmpPendingCount = tasks.Count(t => t.Status == "Pending");
                ViewBag.EmpCompletedCount = tasks.Count(t => t.Status == "Approved");
                ViewBag.EmpTotalCount = tasks.Count;
                ViewBag.EmpPendingReviewCount = await conn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM TaskSubmissions ts
                      JOIN Tasks t ON t.TaskId=ts.TaskId
                      WHERE t.AssignedByUserId=@UserId AND ts.Status='Pending'",
                    new { UserId = userId });
            }
        }

        private string FormatTime(DateTime dt)
        {
            var now = DateTime.Now;
            if (dt.Date == now.Date) return dt.ToString("h:mm tt");
            if (dt.Date == now.Date.AddDays(-1)) return "Yesterday";
            return dt.ToString("MMM dd");
        }
    }
}