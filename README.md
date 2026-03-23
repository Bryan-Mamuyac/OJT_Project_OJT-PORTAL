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

The login page is the entry point of the system. Users authenticate using their registered email and password. Access is restricted based on role — inactive accounts are blocked from logging in regardless of credentials.

---

### Admin

The Admin is the super user of the system, managing all users and monitoring the entire system across all branches and departments.

---

**Admin Dashboard**

![Admin Dashboard](docs/screenshots/Admin_Dashboard.png)

The main Admin dashboard provides a system-wide overview, displaying the total count of OJT trainees and Employees per branch. Each branch card — Agoo, Reina Mercedes, Candon, and Pasig — shows active user counts at a glance.

---

**Admin Dashboard — Branch Summary**

![Admin Dashboard Overview](docs/screenshots/Admin_Dashboard_2.png)

A closer view of the branch breakdown cards. Admins can quickly assess the distribution of active trainees and employees across all four branches without navigating away from the dashboard.

---

**User Management — All Users**

![Admin All Users](docs/screenshots/Admin_Dashboard_Allusers.png)

The All Users page lists every registered user in the system. Admins can filter by role, branch, department, and status. Each user entry displays a status badge — Active, Approved, or Inactive — and provides quick access to edit or deactivate any account.

---

**Register New User — Step 1**

![Register User](docs/screenshots/Admin_Dashboard_RegisterUser.png)

The user registration form allows the Admin to create new Employee or OJT accounts. Required fields include name, email, role, department, branch assignment, and initial password. Only the Admin has access to this form.

---

**Register New User — Step 2**

![Register User Step 2](docs/screenshots/Admin_Dashboard_RegisterUser_2.png)

The second step of the registration process confirms the user details before submission. Once registered, the new user can immediately log in with the credentials set by the Admin.

---

### Employee

Employees supervise OJT trainees. They create and assign tasks, review submissions, and monitor OJT progress within their department and branch.

---

**Employee Dashboard**

![Employee Dashboard](docs/screenshots/Employee_Dashboard.png)

The Employee dashboard presents a summary of all task activity through stat cards — Total Tasks, Pending Tasks, Completed Tasks, Cancelled Tasks, and the count of Active OJT Trainees within the same department and branch. An analytics chart visualizes task assignment versus completion over a selected date range.

---

**Employee Dashboard — Recent Tasks**

![Employee Dashboard Overview](docs/screenshots/Employee_Dashboard_2.png)

The lower section of the Employee dashboard displays a recent tasks table with status badges and a My OJT Trainees card listing all active trainees under the Employee's supervision, filterable by branch.

---

**Task Management — Create Task**

![Create Task](docs/screenshots/Employee_Dashboard_CreateTask.png)

The task creation form allows Employees to assign either an Individual or Group task. Fields include task title, description, deadline, and file attachment. Supported file types are PDF, DOCX, XLSX, PPTX, PNG, JPG, PBIX, and ZIP.

---

**Task Management — Create Task (Group)**

![Create Task Step 2](docs/screenshots/Employee_Dashboard_CreateTask2.png)

When creating a Group task, the Employee selects multiple OJT trainees as members. The task is then distributed to all selected members simultaneously, with each member able to submit their own response independently.

---

**Tasks Given**

![Tasks Given](docs/screenshots/Employee_Dashboard_TaskGiven.png)

The Tasks Given page lists all tasks created by the Employee. Each entry shows the task title, assigned trainee or group, deadline, and current status. Employees can filter by status or search by trainee name from this view.

---

**Pending Submissions Review**

![Pending Review](docs/screenshots/Employee_Dashboard_PendingReview.png)

The Pending Submissions page displays all OJT submissions awaiting review. Employees can view the submission details — title, description, attached files, and link URL — then either Approve the submission or Request Resubmission. Requesting resubmission deletes the old files immediately and notifies the OJT trainee.

---

**Completed Tasks**

![Completed Tasks](docs/screenshots/Employee_Dashboard_Completed.png)

The Completed Tasks page shows all approved submissions. Upon approval, both the submission file and the original task file are permanently deleted from disk to keep the system clean and storage efficient.

---

**History**

![History](docs/screenshots/Employee_Dashboard_History.png)

The History page serves as an archive of all approved and completed tasks. Employees can review past task records and remove individual entries from the history log if needed.

---

**Calendar**

![Employee Calendar](docs/screenshots/Employee_Dashboard_Calendar.png)

The Employee calendar is powered by FullCalendar v6 and supports both Month and List views. Events are color-coded — blue for assigned tasks, green for completed tasks, and purple for upcoming due dates. Employees can filter by branch and OJT trainee, and export the calendar as a PDF.

---

**Chat**

![Employee Chat](docs/screenshots/Employee_Dashboard_Chats.png)

The chat interface follows a messenger-style layout with a conversation list on the left and the active message thread on the right. Employees can start new conversations using the people picker, send file attachments up to 20MB, and receive messages in real time via SignalR WebSockets.

---

### OJT Trainee

OJT trainees receive tasks from their assigned Employee supervisor. They submit work, track their progress, and communicate via chat.

---

**OJT Dashboard**

![OJT Dashboard](docs/screenshots/OJT_Dashboard.png)

The OJT dashboard provides a personal task summary through stat cards — Total Tasks, Pending (submitted and awaiting review), Completed, and New Tasks. An analytics chart tracks assignment and completion trends over time.

---

**OJT Dashboard — Recent Tasks**

![OJT Dashboard Overview](docs/screenshots/OJT_Dashboard_2.png)

The lower section of the OJT dashboard displays recent task entries with status badges and deadline indicators. Overdue tasks are highlighted in red and near-deadline tasks in amber to help trainees prioritize their workload.

---

**New Tasks**

![OJT New Task](docs/screenshots/OJT_Dashboard_NewTask.png)

The New Tasks page lists all tasks recently assigned by the Employee supervisor that have not yet been started or submitted. Trainees can view task details including title, description, deadline, and any attached reference files.

---

**Pending Tasks**

![OJT Pending Tasks](docs/screenshots/OJT_Dashboard_PendingTask.png)

The Pending Tasks page shows all tasks that have been submitted and are currently awaiting review by the Employee. Submitted files remain visible under the Attached Files section until the Employee takes action.

---

**Submit Task**

![OJT Submit Task](docs/screenshots/OJT_Dashboard_SubmitTask.png)

The task submission form allows OJT trainees to upload their completed work. Trainees can provide a submission title, description, one or more file attachments, and an optional link URL. In the case of a resubmission, the old files are already cleared and fresh uploads are required.

---

**Completed Tasks**

![OJT Completed](docs/screenshots/OJT_Dashboard_Completed.png)

The Completed Tasks page lists all tasks that have been reviewed and approved by the Employee supervisor. Trainees can refer to this page to track their overall progress throughout their OJT period.

---

**History**

![OJT History](docs/screenshots/OJT_Dashboard_History.png)

The History page provides OJT trainees with a full archive of their approved tasks, serving as a personal record of completed work over the duration of their training.

---

**Calendar**

![OJT Calendar](docs/screenshots/OJT_Dashboard_Calendar.png)

The OJT calendar mirrors the Employee calendar in functionality, filtered to show only the trainee's own tasks. Color-coded events mark assigned tasks in blue, completed tasks in green, and due dates in purple. Filters are available for branch and supervisor.

---

**Chat**

![OJT Chat](docs/screenshots/OJT_Dashboard_Chats.png)

OJT trainees access the same real-time chat system as Employees. They can initiate conversations, exchange files, and receive instant message notifications. Unread message counts are reflected on the sidebar badge, updated live via SignalR.

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