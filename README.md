# INTERNIFY

> *"Your Portal to Real-Life Work Experience"*

> A web-based On-the-Job Training management system built with **ASP.NET Core MVC 8**, designed to streamline trainee supervision, task management, real-time communication, and performance tracking across multiple branches and departments.

---

## Table of Contents

- [Overview](#overview)
- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Branches](#branches)
- [Roles](#roles)
- [Screenshots](#screenshots)
- [Features](#features)
- [Database Tables](#database-tables)
- [File Management](#file-management)
- [Data Retention & Auto Expiry](#data-retention--auto-expiry)
- [Security](#security)
- [Getting Started](#getting-started)

---

## Overview

INTERNIFY is a full-stack role-based web application that manages On-the-Job Training programs across four branches. It covers the complete OJT lifecycle — from user registration and task assignment to submission review, real-time messaging, and calendar tracking — all within a clean, self-managing system that automatically handles notification hygiene and scheduled account expiry.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core MVC 8 |
| ORM | Dapper |
| Database | SQL Server |
| Authentication | Cookie Authentication + BCrypt |
| Real-time | SignalR WebSockets |
| Calendar | FullCalendar v6.1.10 |
| Charts | Chart.js |
| Icons | Font Awesome |
| CSS | Custom (Inter font, CSS variables) |
| File Exports | html2canvas + jsPDF |
| Job Scheduling | SQL Server Agent |

---

## Architecture

INTERNIFY is a **Server-Side MVC Web Application**. The server renders full HTML pages using Razor views and sends them to the browser. It also exposes internal **AJAX JSON endpoints** consumed by the frontend JavaScript for dynamic data, and uses **SignalR WebSockets** for real-time features.

### Request Types

| Type | Description | Examples |
|---|---|---|
| **MVC (Full Page)** | Server renders complete HTML and returns it to the browser | Dashboard, Task pages, History |
| **AJAX JSON Endpoints** | Controller actions returning JSON, called by frontend JS without page reload | `GetAnalytics`, `GetCalendarTasks`, `GetMessages`, `GetUnreadCount` |
| **SignalR WebSocket** | Persistent two-way connection — server pushes data to clients instantly | Real-time chat, delete events, unread badge updates |

### How a typical page request works

```
Browser → GET /OJT/Index
        ↓
OJTController.Index() runs
        ↓
Queries SQL Server via Dapper
        ↓
Passes model to Razor view (.cshtml)
        ↓
Server renders full HTML
        ↓
Complete HTML page sent to browser
```

### How real-time chat works

```
User sends message → POST /Chat/SendMessage
        ↓
Saved to SQL Server
        ↓
SignalR pushes ReceiveMessage to all clients in conversation group
        ↓
Recipient's browser receives message instantly — no page reload
```

---

## Branches

- Agoo
- Reina Mercedes
- Candon
- Pasig

---

## Roles

The system has three roles: **Admin**, **Employee**, and **OJT Trainee**.

---

## Screenshots

### Login

![Login](docs/screenshots/Login_Dashboard.png)

The login page is the entry point of the system. Users authenticate with their registered credentials. Inactive accounts are blocked from logging in regardless of the password entered.

---

### Admin

#### Dashboard

![Admin Dashboard](docs/screenshots/Admin_Dashboard.png)

The Admin dashboard provides a system-wide overview showing the total count of OJT trainees and Employees per branch. Each branch card displays active user counts at a glance.

---

#### Dashboard — Branch Summary

![Admin Dashboard Overview](docs/screenshots/Admin_Dashboard_2.png)

A closer view of the branch breakdown cards across all four branches.

---

#### User Management — All Users

![Admin All Users](docs/screenshots/Admin_Dashboard_Allusers.png)

Lists every registered user with filters by role, branch, department, and status. Each entry shows a status badge — Active, Approved, or Inactive.

---

#### Register New User

![Register User](docs/screenshots/Admin_Dashboard_RegisterUser.png)

![Register User Step 2](docs/screenshots/Admin_Dashboard_RegisterUser_2.png)

Admins create new Employee or OJT accounts with role, department, branch, and an initial password.

---

### Employee

#### Dashboard

![Employee Dashboard](docs/screenshots/Employee_Dashboard.png)

![Employee Dashboard Overview](docs/screenshots/Employee_Dashboard_2.png)

Stat cards for Total Tasks, Pending Tasks (submitted and awaiting review), Completed Tasks, Cancelled Tasks, and Active OJT Trainees across all branches in the same department. Includes an analytics chart and recent tasks table.

---

#### Create Task

![Create Task](docs/screenshots/Employee_Dashboard_CreateTask.png)

![Create Task Group](docs/screenshots/Employee_Dashboard_CreateTask2.png)

Employees assign Individual or Group tasks to OJTs across all branches within their department.

---

#### Tasks Given

![Tasks Given](docs/screenshots/Employee_Dashboard_TaskGiven.png)

All tasks created by the Employee with status, assignee, and deadline.

---

#### Pending Submissions

![Pending Review](docs/screenshots/Employee_Dashboard_PendingReview.png)

All OJT submissions awaiting review. Employees can Approve or Request Resubmission.

---

#### Completed Tasks

![Completed Tasks](docs/screenshots/Employee_Dashboard_Completed.png)

All approved submissions with attached files accessible for reference.

---

#### History

![History](docs/screenshots/Employee_Dashboard_History.png)

Archive of all approved and completed tasks with individual entry deletion.

---

#### Calendar

![Employee Calendar](docs/screenshots/Employee_Dashboard_Calendar.png)

FullCalendar v6 with Month and List views. Color-coded — blue for assigned, green for completed, purple for due dates. Branch and OJT filters with PDF export.

---

#### Chat

![Employee Chat](docs/screenshots/Employee_Dashboard_Chats.png)

Real-time messenger-style chat powered by SignalR WebSockets.

---

### OJT Trainee

#### Dashboard

![OJT Dashboard](docs/screenshots/OJT_Dashboard.png)

![OJT Dashboard Overview](docs/screenshots/OJT_Dashboard_2.png)

Personal task summary with stat cards and analytics chart. Overdue tasks shown in red, near-deadline in amber.

---

#### New Tasks

![OJT New Task](docs/screenshots/OJT_Dashboard_NewTask.png)

All newly assigned tasks with details and attached reference files.

---

#### Pending Tasks

![OJT Pending Tasks](docs/screenshots/OJT_Dashboard_PendingTask.png)

Tasks submitted and awaiting Employee review. Attached files remain visible.

---

#### Submit Task

![OJT Submit Task](docs/screenshots/OJT_Dashboard_SubmitTask.png)

Submit work with title, description, file attachments, and optional link URL.

---

#### Completed Tasks

![OJT Completed](docs/screenshots/OJT_Dashboard_Completed.png)

All tasks reviewed and approved by the Employee supervisor. Submitted files remain accessible for reference.

---

#### History

![OJT History](docs/screenshots/OJT_Dashboard_History.png)

Full archive of approved tasks as a personal training record.

---

#### Calendar

![OJT Calendar](docs/screenshots/OJT_Dashboard_Calendar.png)

Personal calendar filtered to own tasks with branch and supervisor filters.

---

#### Chat

![OJT Chat](docs/screenshots/OJT_Dashboard_Chats.png)

Same real-time chat system as Employee with live unread badge updates.

---

## Features

### Admin

- Register new users (Employee or OJT) with role, department, and branch assignment
- Edit any user's information and activate or deactivate accounts
- Deactivating a user immediately removes them from task assignments, group member lists, and OJT trainee counts
- View all users with filters by role, branch, department, and status
- Dashboard showing total OJTs and Employees per branch
- Admin password automatically seeded and re-synced on every application startup via `SeedAdminUser()`

---

### Employee

- Dashboard stat cards: Total Tasks, Pending Tasks (submitted awaiting review), Completed Tasks, Cancelled Tasks, Active OJT Trainees across all branches in the same department
- Analytics chart — tasks assigned vs. completed with date range and branch/OJT filters
- Create **Individual** or **Group** tasks — assignable to OJTs across all branches within the same department
- Supported file types: PDF, DOCX, DOC, XLSX, XLS, PPTX, PPT, PNG, JPG, JPEG, PBIX, ZIP
- Edit and cancel tasks
- Review OJT submissions — **Approve** or **Request Resubmission**
- Submitted files and task reference files are preserved and remain downloadable after approval
- Inactive OJT users automatically excluded from group task member lists
- Task details page shows submitted files as downloadable chips with file type icons
- History view with individual entry deletion
- Completed tasks searchable by task title
- FullCalendar v6 with Month and List views — Today, Month, List buttons
- PDF calendar export

---

### OJT Trainee

- Dashboard stat cards: Total Tasks, Pending, Completed, New Tasks
- Analytics chart with date range filter
- View and manage assigned tasks by status (New, Pending, Completed, Cancelled)
- Submit work with title, description, file attachments, and optional link URL
- Submitted files remain visible and downloadable after approval
- Work status tracking: In Progress, On Hold, On Break, Nature Call
- Deadline badges — overdue in red, near-deadline in amber
- Profile picture upload and deletion — old photo deleted from disk and database simultaneously
- **Notification Bell** with three types:
  - 🔵 **New Task** — when a new task is assigned (last 7 days, unread)
  - 🟡 **Due Soon** — when deadline is within 3 days
- Notifications are self-cleaning — dismissed records older than 7 days are automatically purged
- Completed tasks searchable by task title
- FullCalendar v6 with same features as Employee, filtered to own tasks

---

### Chat — Both Employee and OJT

- Real-time messaging via **SignalR WebSockets** with automatic HTTP polling fallback
- Messenger-style layout — conversation list left, message thread right
- All conversation groups joined on page load — previews update in real time for all conversations including unopened ones
- Conversation preview shows **"You: [message]"** from the sender's own perspective
- File attachments up to 20MB — inline image preview with lightbox, other files as download chips
- Delete own messages — hard deletes from database and disk, pushed instantly via SignalR
- **5-second send cooldown** — applies to both text and file sends; countdown shown on button with toast warning
- **500-character limit** — live counter, turns amber at 50 remaining, shows "Max length reached" at 0
- Unread badge on sidebar updated live via SignalR push

---

## Database Tables

| Table | Purpose |
|---|---|
| `Users` | All users — Admin, Employee, OJT |
| `Tasks` | All tasks created by Employees |
| `TaskSubmissions` | OJT submissions per task |
| `TaskProgressLogs` | OJT work status logs per task |
| `NotificationReads` | Tracks dismissed notifications; self-cleaning, auto-purges rows older than 7 days |
| `ChatConversations` | One row per unique conversation pair |
| `ChatMessages` | All chat messages; hard deleted on user delete action |
| `OJT_CleanupLog` | Audit log of every auto-expiry job run |

---

## File Management

Uploaded files are stored in the `wwwroot/uploads/` directory and remain accessible for reference throughout the task lifecycle. Files are only removed during the automatic OJT account expiry cycle.

| Folder | Contents |
|---|---|
| `/uploads/profiles/` | User profile pictures |
| `/uploads/tasks/` | Files attached to tasks by Employees |
| `/uploads/submissions/` | Files uploaded by OJTs when submitting tasks |
| `/uploads/chat/` | Files sent in chat conversations |

| Event | Action |
|---|---|
| User uploads new profile photo | Old photo deleted from `/uploads/profiles/`, new file saved, database updated |
| User deletes profile photo | File deleted from `/uploads/profiles/`, database column set to `NULL` |
| Chat message with attachment deleted | File deleted from `/uploads/chat/`, database row hard deleted |
| OJT account expires (2 years) | All associated files cleared from database; physical files removed from upload folders |

---

## Data Retention & Auto Expiry

The system uses a **SQL Server Agent Job** to automatically manage data retention on a two-year cycle. No manual intervention is required after setup.

### Rules

| Data | Retention Period | Action |
|---|---|---|
| OJT accounts | 2 years from registration date | Account and all associated data deleted |
| OJT tasks and submissions | 2 years (tied to OJT expiry) | Deleted with the account |
| OJT progress logs | 2 years (tied to OJT expiry) | Deleted with the account |
| OJT chat conversations | 2 years (tied to OJT expiry) | Deleted with the account |
| Chat messages (all users) | 2 years from sent date | Messages older than 2 years deleted |
| Empty conversations | On each cleanup run | Conversations with no remaining messages deleted |
| Employee accounts | Never expire | Always retained |

### Schedule

| Job | Schedule | Purpose |
|---|---|---|
| **Primary** | January 10 every year at 7:30 AM | Main cleanup — internal 2-year guard ensures it only runs every 2 years (next: Jan 10, 2028) |
| **Fallback** | Daily at 8:00 AM | Catches missed runs — only acts during the Jan 10–40 window if the primary was missed due to server downtime |

### How the fallback works

If the server is down on January 10, 2028 at 7:30 AM, the fallback job running daily at 8:00 AM will detect that cleanup is due and execute it automatically — within a 30-day recovery window after January 10.

### Viewing the cleanup log

```sql
SELECT * FROM ITPMS_OJT.dbo.OJT_CleanupLog ORDER BY RunAt DESC;
```

### Running cleanup manually

```sql
USE ITPMS_OJT;
EXEC dbo.sp_OJT_AutoExpiry @ForceRun = 1;
```

### Requirements

- SQL Server Agent must be running (green icon in SSMS Object Explorer)
- Run `Fix_CleanupLog_Notes.sql` once to add the Notes column if missing
- Run `OJT_AutoExpiry_Setup.sql` once to create the stored procedure, log table, and Agent jobs

---

## Security

- Cookie-based authentication with **8-hour session** and sliding expiration
- **BCrypt** password hashing — passwords never stored in plain text
- Anti-forgery tokens on all `POST` actions
- Role-based authorization using `[Authorize]` attributes and role checks
- Inactive users cannot log in or appear in any pickers or lists
- Admin password automatically seeded and re-synced on every application startup

---

## Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server
- Visual Studio 2022 or VS Code
- SQL Server Agent enabled and running

### Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/Bryan-Mamuyac/OJT_Project_OJT-PORTAL.git
   cd OJT_Project_OJT-PORTAL
   ```

2. **Configure the database connection** in `appsettings.json`
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=YOUR_SERVER;Database=ITPMS_OJT;Trusted_Connection=True;"
   }
   ```

3. **Run the SQL schema scripts** in order using SSMS

4. **Run the auto-expiry setup scripts** in SSMS
   ```
   Fix_CleanupLog_Notes.sql       ← adds Notes column to log table
   OJT_AutoExpiry_Setup.sql       ← creates procedure, log table, and Agent jobs
   ```

5. **Create the upload folders** inside `wwwroot/`
   ```
   wwwroot/uploads/profiles/
   wwwroot/uploads/tasks/
   wwwroot/uploads/submissions/
   wwwroot/uploads/chat/
   ```

6. **Build and run the application**
   ```bash
   dotnet run
   ```

7. **Log in as Admin** — the Admin account is automatically seeded on first startup
   - Username: `Admin`
   - Password: `admin987654321`

---

## Folder Structure

```
ITPMS_OJT/
├── Controllers/
│   ├── AccountController.cs
│   ├── AdminController.cs
│   ├── EmployeeController.cs
│   ├── OJTController.cs
│   └── ChatController.cs
├── Hubs/
│   └── ChatHub.cs
├── Models/
│   ├── UserModel.cs
│   ├── TaskModel.cs
│   └── ...
├── Views/
│   ├── Admin/
│   ├── Employee/
│   ├── OJT/
│   ├── Chat/
│   └── Shared/
├── wwwroot/
│   ├── css/
│   └── uploads/
│       ├── profiles/
│       ├── tasks/
│       ├── submissions/
│       └── chat/
├── SQL/
│   ├── OJT_AutoExpiry_Setup.sql
│   ├── Fix_CleanupLog_Notes.sql
│   └── ...
└── appsettings.json
```

---

*INTERNIFY — "Your Portal to the Real Life Work Experience" · Built with ASP.NET Core MVC 8 · Powered by SignalR · Managed with Dapper · Automated with SQL Server Agent*