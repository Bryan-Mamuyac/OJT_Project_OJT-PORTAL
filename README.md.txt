# OJT Portal

A web-based On-the-Job Training management system built with ASP.NET Core MVC 8. The portal supports three user roles — Admin, Employee, and OJT — and provides task management, real-time chat, calendar tracking, and submission review across four branches.

---

## Table of Contents

- [Overview](#overview)
- [Tech Stack](#tech-stack)
- [Branches](#branches)
- [Roles](#roles)
- [Screenshots](#screenshots)
  - [Login](#login)
  - [Admin](#admin)
  - [Employee](#employee)
  - [OJT Trainee](#ojt-trainee)
- [Features](#features)
- [Database Tables](#database-tables)
- [File Management](#file-management)
- [Security](#security)
- [Getting Started](#getting-started)

---

## Overview

The OJT Portal is designed to streamline OJT trainee management across multiple branches and departments. It handles user registration, task assignment and submission, real-time messaging, calendar scheduling, and performance tracking — all within a role-based access system.

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

---

## Branches

- Agoo
- Reina Mercedes
- Candon
- Pasig

---

## Roles

The system has three roles: **Admin**, **Employee**, and **OJT**.

---

## Screenshots

### Login

![Login Dashboard](docs/screenshots/Login_Dashboard.png)

---

### Admin

The Admin is the super user of the system, managing all users and monitoring the entire system across all branches and departments.

![Admin Dashboard](docs/screenshots/Admin_Dashboard.png)

![Admin Dashboard Overview](docs/screenshots/Admin_Dashboard_2.png)

**User Management — All Users**

![Admin All Users](docs/screenshots/Admin_Dashboard_Allusers.png)

**Register New User**

![Register User](docs/screenshots/Admin_Dashboard_RegisterUser.png)

![Register User Step 2](docs/screenshots/Admin_Dashboard_RegisterUser_2.png)

---

### Employee

Employees supervise OJT trainees. They create and assign tasks, review submissions, and monitor OJT progress within their department and branch.

![Employee Dashboard](docs/screenshots/Employee_Dashboard.png)

![Employee Dashboard Overview](docs/screenshots/Employee_Dashboard_2.png)

**Task Management — Create Task**

![Create Task](docs/screenshots/Employee_Dashboard_CreateTask.png)

![Create Task Step 2](docs/screenshots/Employee_Dashboard_CreateTask2.png)

**Tasks Given**

![Tasks Given](docs/screenshots/Employee_Dashboard_TaskGiven.png)

**Pending Submissions Review**

![Pending Review](docs/screenshots/Employee_Dashboard_PendingReview.png)

**Completed Tasks**

![Completed Tasks](docs/screenshots/Employee_Dashboard_Completed.png)

**History**

![History](docs/screenshots/Employee_Dashboard_History.png)

**Calendar**

![Employee Calendar](docs/screenshots/Employee_Dashboard_Calendar.png)

**Chat**

![Employee Chat](docs/screenshots/Employee_Dashboard_Chats.png)

---

### OJT Trainee

OJT trainees receive tasks from their assigned Employee supervisor. They submit work, track their progress, and communicate via chat.

![OJT Dashboard](docs/screenshots/OJT_Dashboard.png)

![OJT Dashboard Overview](docs/screenshots/OJT_Dashboard_2.png)

**New Tasks**

![OJT New Task](docs/screenshots/OJT_Dashboard_NewTask.png)

**Pending Tasks**

![OJT Pending Tasks](docs/screenshots/OJT_Dashboard_PendingTask.png)

**Submit Task**

![OJT Submit Task](docs/screenshots/OJT_Dashboard_SubmitTask.png)

**Completed Tasks**

![OJT Completed](docs/screenshots/OJT_Dashboard_Completed.png)

**History**

![OJT History](docs/screenshots/OJT_Dashboard_History.png)

**Calendar**

![OJT Calendar](docs/screenshots/OJT_Dashboard_Calendar.png)

**Chat**

![OJT Chat](docs/screenshots/OJT_Dashboard_Chats.png)

---

## Features

### Admin
- Register new users (Employee or OJT) with role, department, and branch assignment
- Edit any user's information and activate or deactivate accounts
- Deactivating a user immediately removes them from task assignments, group member lists, and OJT trainee counts
- View all users with filters by role, branch, department, and status
- Dashboard showing total OJTs and Employees per branch with system-wide active user counts
- Admin password auto-seeded and re-synced on every application startup via `SeedAdminUser()`

### Employee
- Dashboard stat cards: Total Tasks, Pending Tasks, Completed Tasks, Cancelled Tasks, Active OJT Trainees
- Analytics chart showing tasks assigned vs. completed over time with date range and branch filters
- Create Individual or Group tasks with title, description, deadline, and file attachments
- Supported file types: PDF, DOCX, XLSX, PPTX, PNG, JPG, PBIX, ZIP
- Edit and cancel tasks
- Review OJT submissions — Approve or Request Resubmission
- On approval: submission files and task files are deleted from disk and paths cleared in the database
- On resubmission request: old submission files are deleted immediately, task resets to New status, OJT is notified
- History view for all approved tasks
- FullCalendar v6 with Month and List views showing assigned, completed, and due date events
- PDF export of calendar

### OJT Trainee
- Dashboard stat cards: Total Tasks, Pending, Completed, New Tasks
- Analytics chart with date range filter
- View and manage all assigned tasks by status
- Submit work with title, description, file attachments, and optional link URL
- Work status tracking: In Progress, On Hold, On Break, Nature Call
- Deadline badges — overdue shown in red, near-deadline shown in amber
- Notification bell for New Task, Due Soon (within 3 days), and Resubmission Required alerts
- Notifications self-clean — dismissed records older than 7 days are automatically purged
- FullCalendar with same features as Employee, filtered to own tasks

### Chat (Both Employee and OJT)
- Real-time messaging via SignalR WebSockets with automatic HTTP polling fallback
- Messenger-style layout with conversation list and message panel
- People picker with search, role, branch, and department filters
- File attachments up to 20MB (PNG, JPG, JPEG, PDF, DOCX, XLSX, PPTX, PBIX, ZIP)
- Inline image previews with lightbox; other files shown as download chips
- Delete own messages — hard deletes from database and disk, pushed instantly via SignalR
- 5-second send cooldown to prevent spam
- 500 character limit with live counter
- Unread badge on sidebar updated via SignalR push

---

## Database Tables

| Table | Purpose |
|---|---|
| Users | All users — Admin, Employee, OJT |
| Tasks | All tasks created by Employees |
| TaskSubmissions | OJT submissions per task |
| NotificationReads | Tracks dismissed notifications; auto-purges rows older than 7 days |
| ChatConversations | One row per unique conversation pair |
| ChatMessages | All chat messages; hard deleted on user delete action |

---

## File Management

The system is self-cleaning. Files are deleted from disk whenever they are no longer needed.

| Event | Action |
|---|---|
| Employee uploads new profile photo | Old photo deleted from `/uploads/profiles/`, new one saved, database updated |
| Employee deletes profile photo | File deleted from `/uploads/profiles/`, database set to NULL |
| OJT submits task with file | File saved to `/uploads/submissions/`, path stored in database |
| Employee requests resubmission | Old submission file deleted from `/uploads/submissions/`, database path cleared |
| Employee approves submission | Submission file and task file both deleted from disk, both database paths cleared |
| Chat message with file deleted | File deleted from `/uploads/chat/`, database row hard deleted |

---

## Security

- Cookie-based authentication with 8-hour session and sliding expiration
- BCrypt password hashing
- Anti-forgery tokens on all POST actions
- Role-based authorization using `[Authorize]` attributes and role checks
- Inactive users cannot log in or appear in any pickers or lists
- Admin password automatically seeded and re-synced on every application startup

---

## Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server
- Visual Studio 2022 or VS Code

### Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/Bryan-Mamuyac/OJT_Project_OJT-PORTAL.git
   cd OJT_Project_OJT-PORTAL
   ```

2. Configure your database connection string in `appsettings.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=YOUR_SERVER;Database=OJTPortal;Trusted_Connection=True;"
   }
   ```

3. Run the database migrations or execute the SQL schema script.

4. Build and run the application:
   ```bash
   dotnet run
   ```

5. The Admin account is automatically seeded on first startup.

---

## Adding Screenshots to This README

Run the following in PowerShell to create the folder and copy your screenshots automatically:

```powershell
mkdir "C:\Users\OJT\source\repos\ITPMS_OJT\ITPMS_OJT\docs\screenshots"
Copy-Item "C:\Users\OJT\Desktop\Documentation-OJT_PORTAL\*" -Destination "C:\Users\OJT\source\repos\ITPMS_OJT\ITPMS_OJT\docs\screenshots\"
```

Then push to GitHub:

```bash
git add .
git commit -m "Add README and documentation screenshots"
git push
```