using System.ComponentModel.DataAnnotations;

namespace ITPMS_OJT.Models
{
    public class TaskItem
    {
        public int TaskId { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        public int AssignedToUserId { get; set; }
        public int AssignedByUserId { get; set; }

        public string Status { get; set; } = "New"; // New, Pending, Approved, Cancelled

        public string TaskType { get; set; } = "Individual"; // Individual, Group
        public string? WorkStatus { get; set; } // null=Not Started, In Progress, On Hold, On Break, Nature Call
        public string WorkStatusDisplay => WorkStatus switch
        {
            "In Progress" => "In Progress",
            "On Hold" => "On Hold",
            "On Break" => "On Break",
            "Nature Call" => "Nature Call",
            _ => "Not Started"
        };
        public bool HasStarted => WorkStatus != null;

        public DateTime? Deadline { get; set; }

        public string? AttachedFilePath { get; set; }
        public string? AttachedFileName { get; set; }

        // Helper: parse AttachedFilePath (may be JSON array or single path)
        public List<(string Path, string Name)> GetAttachedFiles()
        {
            if (string.IsNullOrEmpty(AttachedFilePath)) return new();
            if (AttachedFilePath.TrimStart().StartsWith("["))
            {
                try
                {
                    var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(AttachedFilePath) ?? new();
                    var names = System.Text.Json.JsonSerializer.Deserialize<List<string>>(AttachedFileName ?? "[]") ?? new();
                    var result = new List<(string, string)>();
                    for (int i = 0; i < paths.Count; i++)
                        result.Add((paths[i], i < names.Count ? names[i] : System.IO.Path.GetFileName(paths[i])));
                    return result;
                }
                catch { return new(); }
            }
            return new List<(string, string)> { (AttachedFilePath, AttachedFileName ?? System.IO.Path.GetFileName(AttachedFilePath)) };
        }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Loaded via JOIN
        public string? AssignedToName { get; set; }
        public string? AssignedByName { get; set; }
        public string? AssignedByBranch { get; set; }
        public string? AssignedToUsername { get; set; }
        public string? AssignedToBranch { get; set; }

        // ── PH timezone ──
        private static readonly TimeZoneInfo PhZone =
            TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); // UTC+8, same as PH

        /// <summary>Convert stored UTC deadline → PH local time for display.</summary>
        public string DeadlinePhDisplay
        {
            get
            {
                if (!Deadline.HasValue) return "No deadline";
                // Deadline is stored as UTC in the DB
                var utc = DateTime.SpecifyKind(Deadline.Value, DateTimeKind.Utc);
                var phTime = TimeZoneInfo.ConvertTimeFromUtc(utc, PhZone);
                // e.g. "February 24, 2026 04:30 PM"
                return phTime.ToString("MMMM dd, yyyy hh:mm tt");
            }
        }

        /// <summary>Is the deadline past right now (PH time)?</summary>
        public bool IsOverdue
        {
            get
            {
                if (!Deadline.HasValue || Status == "Approved") return false;
                var nowUtc = DateTime.UtcNow;
                return Deadline.Value < nowUtc;
            }
        }

        /// <summary>Is the deadline within the next 24 hours?</summary>
        public bool IsNearDeadline
        {
            get
            {
                if (!Deadline.HasValue || IsOverdue || Status == "Approved") return false;
                var nowUtc = DateTime.UtcNow;
                return Deadline.Value <= nowUtc.AddHours(24);
            }
        }
    }

    public class CreateTaskViewModel
    {
        [Required(ErrorMessage = "Title is required")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select a task type")]
        [Display(Name = "Task Type")]
        public string TaskType { get; set; } = "Individual";

        [Display(Name = "Assign To")]
        public int? AssignedToUserId { get; set; }

        /// <summary>Date part: yyyy-MM-dd  (from date input)</summary>
        [Required(ErrorMessage = "Deadline date is required")]
        public string? DeadlineDate { get; set; }

        /// <summary>
        /// Time part submitted by the dropdown.
        /// Values are always two-digit 24h hour + minutes, e.g.:
        ///   AM slots: "08:00" … "12:00"
        ///   PM slots: "01:00" … "05:00"  ← these mean 13:00–17:00
        /// </summary>
        [Required(ErrorMessage = "Deadline time is required")]
        public string? DeadlineTime { get; set; }

        /// <summary>"AM" or "PM"</summary>
        public string? DeadlineAmPm { get; set; }

        public List<IFormFile>? AttachedFiles { get; set; }

        /// <summary>
        /// Converts DeadlineDate + DeadlineTime + DeadlineAmPm → UTC DateTime.
        ///
        /// Logic:
        ///   The dropdown sends values like "01:00" for 1 PM and "08:00" for 8 AM.
        ///   We parse the hour numerically and, when DeadlineAmPm == "PM" and hour < 12,
        ///   we add 12 to get the correct 24-hour value before building the DateTime.
        /// </summary>
        public DateTime? GetDeadlineUtc()
        {
            if (string.IsNullOrWhiteSpace(DeadlineDate) || string.IsNullOrWhiteSpace(DeadlineTime))
                return null;

            try
            {
                // Parse date
                if (!DateOnly.TryParseExact(DeadlineDate, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var date))
                    return null;

                // Parse time  e.g. "04:30"  →  hour=4, minute=30
                var timeParts = DeadlineTime.Split(':');
                if (timeParts.Length != 2) return null;

                int hour = int.Parse(timeParts[0]);
                int minute = int.Parse(timeParts[1]);

                // Convert to 24-hour using the selected AM/PM period
                if (DeadlineAmPm == "PM" && hour < 12)
                    hour += 12;   // 1 PM → 13, 4:30 PM → 16, etc.
                else if (DeadlineAmPm == "AM" && hour == 12)
                    hour = 0;     // 12 AM (midnight) → 0  (not used in our slots but safe)

                // Build local PH DateTime
                var phLocal = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0,
                                           DateTimeKind.Unspecified);

                // Convert PH (UTC+8) → UTC for storage
                var phZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                return TimeZoneInfo.ConvertTimeToUtc(phLocal, phZone);
            }
            catch
            {
                return null;
            }
        }
    }

    public class TaskSubmission
    {
        public int SubmissionId { get; set; }
        public int TaskId { get; set; }
        public int OJTUserId { get; set; }

        [Required]
        public string SubmissionTitle { get; set; } = string.Empty;

        [Required]
        public string SubmissionDescription { get; set; } = string.Empty;

        public string? FilePath { get; set; }
        public string? FileName { get; set; }
        public string? LinkUrl { get; set; }

        public List<(string Path, string Name)> GetSubmittedFiles()
        {
            if (string.IsNullOrEmpty(FilePath)) return new();
            if (FilePath.TrimStart().StartsWith("["))
            {
                try
                {
                    var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(FilePath) ?? new();
                    var names = System.Text.Json.JsonSerializer.Deserialize<List<string>>(FileName ?? "[]") ?? new();
                    var result = new List<(string, string)>();
                    for (int i = 0; i < paths.Count; i++)
                        result.Add((paths[i], i < names.Count ? names[i] : System.IO.Path.GetFileName(paths[i])));
                    return result;
                }
                catch { return new(); }
            }
            return new List<(string, string)> { (FilePath, FileName ?? System.IO.Path.GetFileName(FilePath)) };
        }

        public string Status { get; set; } = "Submitted";

        public DateTime SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }

        // Loaded via JOIN
        public string? TaskTitle { get; set; }
        public string? OJTName { get; set; }
        public string? OJTBranch { get; set; }
        public string? EmployeeName { get; set; }
        public string? EmployeeBranch { get; set; }
    }

    public class SubmitTaskViewModel
    {
        public int TaskId { get; set; }
        public string TaskTitle { get; set; } = string.Empty;
        public string AssignedByName { get; set; } = string.Empty;
        public string DeadlineDisplay { get; set; } = string.Empty;

        // Previous work status (for display)
        public string? CurrentWorkStatus { get; set; }
        public List<TaskProgressLog> RecentLogs { get; set; } = new();

        [Required(ErrorMessage = "Submission title is required")]
        [Display(Name = "Title")]
        public string SubmissionTitle { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required")]
        public string SubmissionDescription { get; set; } = string.Empty;

        public List<IFormFile>? Files { get; set; }

        [Url(ErrorMessage = "Please enter a valid URL")]
        public string? LinkUrl { get; set; }

        // Work status — set via Save Progress, not required on submit
        public string? WorkStatus { get; set; }

        // Optional note for the selected work status
        public string? WorkStatusDescription { get; set; }
    }

    // ========== Dashboard ViewModels ==========

    public class OJTDashboardViewModel
    {
        public User CurrentUser { get; set; } = new();
        public int TotalTasks { get; set; }
        public int NewTasks { get; set; }
        public int PendingTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int OverdueTasks { get; set; }
        public int CancelledTasks { get; set; }
        public List<TaskItem> Tasks { get; set; } = new();
        public List<TaskSubmission> SubmissionHistory { get; set; } = new();
    }

    public class EmployeeDashboardViewModel
    {
        public User CurrentUser { get; set; } = new();
        public int TotalTasks { get; set; }
        public int PendingTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int CancelledTasks { get; set; }
        public List<TaskItem> Tasks { get; set; } = new();
        public List<User> MyOJTs { get; set; } = new();
        public List<TaskItem> CancelledTaskList { get; set; } = new();
    }

    public class AdminDashboardViewModel
    {
        public int TotalOJTs { get; set; }
        public int TotalEmployees { get; set; }
        public int TotalTasks { get; set; }
        public int PendingApprovals { get; set; }
        public int ApprovedUsers { get; set; }
        public int RejectedUsers { get; set; }
        public List<DepartmentStat> DepartmentStats { get; set; } = new();
        public List<User> PendingUsers { get; set; } = new();
        public List<User> RecentUsers { get; set; } = new();
        public string? SelectedDepartment { get; set; }
        public string? SelectedBranch { get; set; }
    }

    public class DepartmentStat
    {
        public string Department { get; set; } = string.Empty;
        public int OJTCount { get; set; }
        public int EmployeeCount { get; set; }
    }

    // ── Task Progress Log (OJT work status updates) ──
    public class TaskProgressLog
    {
        public int LogId { get; set; }
        public int TaskId { get; set; }
        public int OJTUserId { get; set; }
        public string WorkStatus { get; set; } = string.Empty; // In Progress, On Hold, On Break, Nature Call
        public string? Description { get; set; }
        public DateTime LoggedAt { get; set; }
        public string? OJTName { get; set; }   // joined
        public string? TaskTitle { get; set; } // joined

        public string WorkStatusIcon => WorkStatus switch
        {
            "In Progress" => "fas fa-play-circle",
            "On Hold" => "fas fa-pause-circle",
            "On Break" => "fas fa-coffee",
            "Nature Call" => "fas fa-leaf",
            _ => "fas fa-circle"
        };
        public string WorkStatusColor => WorkStatus switch
        {
            "In Progress" => "#16a34a",
            "On Hold" => "#d97706",
            "On Break" => "#2563ab",
            "Nature Call" => "#059669",
            _ => "#64748b"
        };
    }

    // ── Start/Update Task Progress ViewModel ──
    public class TaskProgressViewModel
    {
        public int TaskId { get; set; }
        public string TaskTitle { get; set; } = string.Empty;
        public string AssignedByName { get; set; } = string.Empty;
        public string DeadlineDisplay { get; set; } = string.Empty;
        public string? CurrentWorkStatus { get; set; } // null = never started
        public string? CurrentDescription { get; set; }

        [Required(ErrorMessage = "Please select a status")]
        public string WorkStatus { get; set; } = string.Empty;

        public string? Description { get; set; } // optional

        public List<TaskProgressLog> RecentLogs { get; set; } = new();
    }
}