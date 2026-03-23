# OJT Portal

> A web-based On-the-Job Training management system built with **ASP.NET Core MVC 8**, designed to streamline trainee supervision, task management, real-time communication, and performance tracking across multiple branches and departments.

---

## Table of Contents

- [Overview](#overview)
- [Tech Stack](#tech-stack)
- [Branches](#branches)
- [Roles](#roles)
- [Screenshots](#screenshots)
- [Features](#features)
- [Database Tables](#database-tables)
- [File Management](#file-management)
- [Security](#security)
- [Getting Started](#getting-started)

---

## Overview

The OJT Portal is a full-stack role-based web application that manages On-the-Job Training programs across four branches. It covers the complete OJT lifecycle — from user registration and task assignment to submission review, real-time messaging, and calendar tracking — all within a clean, self-managing system that automatically handles file cleanup and notification hygiene.

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

The Admin dashboard provides a system-wide overview showing the total count of OJT trainees and Employees per branch. Each branch card — Agoo, Reina Mercedes, Candon, and Pasig — displays active user counts at a glance.

---

#### Dashboard — Branch Summary

![Admin Dashboard Overview](docs/screenshots/Admin_Dashboard_2.png)

A closer view of the branch breakdown cards. Admins can quickly assess the distribution of active trainees and employees across all four branches without navigating away from the dashboard.

---

#### User Management — All Users

![Admin All Users](docs/screenshots/Admin_Dashboard_Allusers.png)

The All Users page lists every registered user in the system. Admins can filter by role, branch, department, and status. Each entry displays a status badge — Active, Approved, or Inactive — with quick access to edit or deactivate any account.

---

#### Register New User

![Register User](docs/screenshots/Admin_Dashboard_RegisterUser.png)

![Register User Step 2](docs/screenshots/Admin_Dashboard_RegisterUser_2.png)

The registration form allows Admins to create new Employee or OJT accounts. Required fields include name, email, role, department, branch, and an initial password. Once registered, the user can log in immediately.

---

### Employee

#### Dashboard

![Employee Dashboard](docs/screenshots/Employee_Dashboard.png)

![Employee Dashboard Overview](docs/screenshots/Employee_Dashboard_2.png)

The Employee dashboard presents stat cards for Total Tasks, Pending Tasks, Completed Tasks, Cancelled Tasks, and Active OJT Trainees within the same department and branch. An analytics chart visualizes task assignment versus completion over a selected date range. The lower section shows a recent tasks table and the My OJT Trainees card.

---

#### Create Task

![Create Task](docs/screenshots/Employee_Dashboard_CreateTask.png)

![Create Task Group](docs/screenshots/Employee_Dashboard_CreateTask2.png)

Employees can assign Individual or Group tasks with a title, description, deadline, and file attachments. Group tasks distribute the assignment to all selected OJT members simultaneously, with each member submitting independently.

---

#### Tasks Given

![Tasks Given](docs/screenshots/Employee_Dashboard_TaskGiven.png)

Lists all tasks created by the Employee with status, assignee, and deadline. Filterable by status and searchable by trainee name.

---

#### Pending Submissions

![Pending Review](docs/screenshots/Employee_Dashboard_PendingReview.png)

Displays all OJT submissions awaiting review. Employees can inspect the submission details — title, description, attached files, link URL — then either **Approve** or **Request Resubmission**. Requesting resubmission immediately deletes the old files from disk and notifies the OJT trainee via the notification bell.

---

#### Completed Tasks

![Completed Tasks](docs/screenshots/Employee_Dashboard_Completed.png)

Shows all approved submissions. Upon approval, both the submission file and the original task file are permanently deleted from disk and cleared from the database to keep storage efficient.

---

#### History

![History](docs/screenshots/Employee_Dashboard_History.png)

An archive of all approved and completed tasks. Employees can review past records and remove individual entries from the history log if needed.

---

#### Calendar

![Employee Calendar](docs/screenshots/Employee_Dashboard_Calendar.png)

Powered by FullCalendar v6 with **Month** and **List** views. Events are color-coded — 🔵 blue for assigned tasks, 🟢 green for completed tasks, and 🟣 purple for upcoming due dates. Filterable by branch and OJT trainee, with PDF export support.

---

#### Chat

![Employee Chat](docs/screenshots/Employee_Dashboard_Chats.png)

A messenger-style real-time chat interface with SignalR WebSockets. Employees can start new conversations via the people picker, send file attachments up to 20MB, and receive messages instantly. Conversation previews update in real time showing "You: [message]" from the sender's perspective.

---

### OJT Trainee

#### Dashboard

![OJT Dashboard](docs/screenshots/OJT_Dashboard.png)

![OJT Dashboard Overview](docs/screenshots/OJT_Dashboard_2.png)

The OJT dashboard shows stat cards for Total Tasks, Pending, Completed, and New Tasks, along with an analytics chart tracking personal progress. Recent task entries display status badges and deadline indicators — overdue tasks are highlighted in red, near-deadline tasks in amber.

---

#### New Tasks

![OJT New Task](docs/screenshots/OJT_Dashboard_NewTask.png)

Lists all tasks recently assigned by the Employee supervisor that have not yet been submitted. Trainees can view task details, deadline, and any attached reference files.

---

#### Pending Tasks

![OJT Pending Tasks](docs/screenshots/OJT_Dashboard_PendingTask.png)

Shows all tasks submitted and awaiting review. Attached files remain visible under the **Attached Files** section until the Employee takes action.

---

#### Submit Task

![OJT Submit Task](docs/screenshots/OJT_Dashboard_SubmitTask.png)

The submission form allows trainees to upload completed work with a title, description, one or more file attachments, and an optional link URL. On resubmission, old files are already cleared by the Employee action and fresh uploads are required.

---

#### Completed Tasks

![OJT Completed](docs/screenshots/OJT_Dashboard_Completed.png)

Lists all tasks that have been reviewed and approved by the Employee supervisor. Trainees can track their overall progress throughout their OJT period from this page.

---

#### History

![OJT History](docs/screenshots/OJT_Dashboard_History.png)

A full archive of all approved tasks, serving as a personal record of completed work over the duration of the training.

---

#### Calendar

![OJT Calendar](docs/screenshots/OJT_Dashboard_Calendar.png)

Mirrors the Employee calendar in functionality, filtered to show only the trainee's own tasks. Color-coded events mark assigned tasks in blue, completed tasks in green, and due dates in purple. Filters are available for branch and supervisor.

---

#### Chat

![OJT Chat](docs/screenshots/OJT_Dashboard_Chats.png)

OJT trainees access the same real-time chat system as Employees. They can initiate conversations, exchange files, and receive instant message notifications. Unread counts are reflected on the sidebar badge, updated live via SignalR.

---

## Features

### Admin

- Register new users (Employee or OJT) with role, department, and branch assignment
- Edit any user's information — name, email, role, department, branch, and status
- Activate or deactivate accounts — deactivating a user immediately removes them from task assignments, group member lists, and OJT trainee counts
- View all users with filters by role, branch, department, and status
- Dashboard showing total OJTs and Employees per branch with system-wide active user counts
- Admin password automatically seeded and re-synced on every application startup via `SeedAdminUser()`

---

### Employee

- Dashboard stat cards: Total Tasks, Pending Tasks, Completed Tasks, Cancelled Tasks, Active OJT Trainees
- Analytics chart — tasks assigned vs. completed over time with date range (Overall, Last 7 Days, Last 30 Days) and branch/OJT filters
- Create **Individual** or **Group** tasks with title, description, deadline, and file attachments
- Supported file types: PDF, DOCX, DOC, XLSX, XLS, PPTX, PPT, PNG, JPG, JPEG, PBIX, ZIP
- Edit and cancel tasks
- Review OJT submissions — **Approve** or **Request Resubmission**
  - On **Approve**: submission files and task files are deleted from disk, all file paths cleared in the database
  - On **Request Resubmission**: old submission files deleted from disk immediately, task reset to New status, OJT notified via purple notification bell entry
- Inactive OJT users automatically excluded from group task member lists
- History view for all approved tasks with individual entry deletion
- FullCalendar v6 with Month and List views — color-coded assigned, completed, and due date events
- PDF calendar export via html2canvas + jsPDF

---

### OJT Trainee

- Dashboard stat cards: Total Tasks, Pending, Completed, New Tasks
- Analytics chart with date range filter
- View and manage all assigned tasks by status (New, Pending, Completed, Cancelled)
- Submit work with title, description, file attachments, and optional link URL
- Work status tracking: In Progress, On Hold, On Break, Nature Call
- Deadline badges — overdue shown in red, near-deadline shown in amber
- Profile picture upload and deletion — old photo deleted from disk and database simultaneously
- **Notification Bell** with three types:
  - 🔵 **New Task** — appears when a new task is assigned (within last 7 days)
  - 🟡 **Due Soon** — appears when deadline is within 3 days
  - 🟣 **Resubmission Required** — appears when Employee requests resubmission
- Notifications are self-cleaning — dismissed records older than 7 days are automatically purged from the database
- FullCalendar v6 with same features as Employee, filtered to own tasks

---

### Chat — Both Employee and OJT

- Real-time messaging via **SignalR WebSockets** with automatic HTTP polling fallback
- Messenger-style layout — conversation list on the left, message thread on the right
- People picker with Search (full-width), Role, Branch, and Department filters — debounced with AbortController
- All conversation groups joined on page load so previews update in real time even for unopened conversations
- Conversation preview shows **"You: [message]"** from the sender's own perspective, updates instantly on send and delete
- File attachments up to 20MB — inline image preview with lightbox, other files shown as download chips
- Delete own messages — hard deletes row from database and file from disk, pushed instantly via SignalR to all clients
- **5-second send cooldown** — applies to both text and file sends to prevent spam; countdown shown on button, toast notification warns on early attempt
- **500-character limit** — live counter, turns amber at 50 remaining, shows "Max length reached" at 0
- Unread badge on sidebar updated live via SignalR `UnreadCountChanged` push

---

## Database Tables

| Table | Purpose |
|---|---|
| `Users` | All users — Admin, Employee, OJT |
| `Tasks` | All tasks created by Employees |
| `TaskSubmissions` | OJT submissions per task |
| `NotificationReads` | Tracks dismissed notifications; self-cleaning, auto-purges rows older than 7 days |
| `ChatConversations` | One row per unique conversation pair |
| `ChatMessages` | All chat messages; hard deleted on user delete action |

---

## File Management

The system is fully self-cleaning. Files are removed from disk automatically whenever they are no longer needed — no manual maintenance required.

| Event | Action |
|---|---|
| User uploads new profile photo | Old photo deleted from `/uploads/profiles/`, new file saved, database updated |
| User deletes profile photo | File deleted from `/uploads/profiles/`, database column set to `NULL` |
| OJT submits task with file | File saved to `/uploads/submissions/`, path stored in database |
| Employee requests resubmission | Old submission file deleted from `/uploads/submissions/`, database path cleared immediately |
| Employee approves submission | Submission file **and** task file both deleted from disk, both database paths set to `NULL` |
| Chat message with attachment deleted | File deleted from `/uploads/chat/`, database row hard deleted |

---

## Security

- Cookie-based authentication with **8-hour session** and sliding expiration
- **BCrypt** password hashing — passwords are never stored in plain text
- Anti-forgery tokens on all `POST` actions
- Role-based authorization using `[Authorize]` attributes and role checks
- Inactive users cannot log in or appear in any user pickers or lists
- Admin password automatically seeded and re-synced on every application startup

---

## Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server
- Visual Studio 2022 or VS Code

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

3. **Run the SQL schema scripts** in order using SSMS or `sqlcmd`

4. **Create the upload folders** inside `wwwroot/`
   ```
   wwwroot/uploads/profiles/
   wwwroot/uploads/tasks/
   wwwroot/uploads/submissions/
   wwwroot/uploads/chat/
   ```

5. **Build and run the application**
   ```bash
   dotnet run
   ```

6. **Log in as Admin** — the Admin account is automatically seeded on first startup
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
│   ├── uploads/
│   │   ├── profiles/
│   │   ├── tasks/
│   │   ├── submissions/
│   │   └── chat/
└── appsettings.json
```

---

*Built with ASP.NET Core MVC 8 · Powered by SignalR · Managed with Dapper*