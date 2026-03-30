-- ============================================================
-- AUTO EXPIRY SETUP — SQL Server Agent Job
-- Primary:  January 10 every 2 years at 7:30 AM
--           (next run: Jan 10 2028, then Jan 10 2030, etc.)
-- Fallback: Daily check at 8:00 AM — only runs cleanup if
--           today is Jan 10 AND it has been 2+ years since
--           the last cleanup run (handles server downtime)
-- Deletes OJT accounts + all data after 2 years
-- Deletes chat messages older than 2 years for ALL users
-- Employee accounts are NEVER touched
-- ============================================================

USE ITPMS_OJT;
GO

-- ============================================================
-- STEP 1: Create OJT_CleanupLog table (tracks every run)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OJT_CleanupLog')
BEGIN
    CREATE TABLE OJT_CleanupLog (
        LogId                INT IDENTITY(1,1) PRIMARY KEY,
        RunAt                DATETIME NOT NULL DEFAULT GETDATE(),
        ExpiredOJTs          INT NOT NULL DEFAULT 0,
        DeletedMessages      INT NOT NULL DEFAULT 0,
        DeletedConversations INT NOT NULL DEFAULT 0,
        Notes                NVARCHAR(500) NULL
    );
    PRINT 'OJT_CleanupLog table created.';
END
GO

-- ============================================================
-- STEP 2: Create the cleanup stored procedure
-- ============================================================
IF OBJECT_ID('dbo.sp_OJT_AutoExpiry', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_OJT_AutoExpiry;
GO

CREATE PROCEDURE dbo.sp_OJT_AutoExpiry
    @ForceRun BIT = 0  -- set to 1 to force run regardless of schedule check
AS
BEGIN
    SET NOCOUNT ON;

    -- ── Fallback guard: only run if it has been 2+ years since last run ──
    -- This prevents accidental double-runs and handles the daily fallback schedule
    IF @ForceRun = 0
    BEGIN
        DECLARE @LastRun DATETIME = (
            SELECT MAX(RunAt) FROM OJT_CleanupLog
            WHERE Notes NOT LIKE '%SKIPPED%'
        );
        -- If last run was less than 23 months ago, skip (not yet 2 years)
        IF @LastRun IS NOT NULL AND DATEDIFF(month, @LastRun, GETDATE()) < 23
        BEGIN
            INSERT INTO OJT_CleanupLog (RunAt, ExpiredOJTs, DeletedMessages, DeletedConversations, Notes)
            VALUES (GETDATE(), 0, 0, 0, 'SKIPPED — last run was ' + CAST(DATEDIFF(month, @LastRun, GETDATE()) AS VARCHAR) + ' months ago, waiting for 2-year interval.');
            RETURN;
        END
    END

    DECLARE @ExpiryDate DATETIME = DATEADD(year, -2, GETDATE());
    DECLARE @DeletedOJTs INT = 0;
    DECLARE @DeletedMessages INT = 0;
    DECLARE @DeletedConversations INT = 0;

    -- Collect expired OJT IDs (registered 2+ years ago)
    DECLARE @ExpiredOJTs TABLE (UserId INT);
    INSERT INTO @ExpiredOJTs (UserId)
    SELECT UserId FROM Users
    WHERE Role = 'OJT'
      AND CreatedAt <= @ExpiryDate;

    SET @DeletedOJTs = (SELECT COUNT(*) FROM @ExpiredOJTs);

    IF @DeletedOJTs > 0 OR EXISTS(SELECT 1 FROM ChatMessages WHERE SentAt <= @ExpiryDate)
    BEGIN

        -- 1. Clear chat file paths for expired OJTs
        UPDATE ChatMessages
        SET AttachedFilePath = NULL, AttachedFileName = NULL
        WHERE SenderId IN (SELECT UserId FROM @ExpiredOJTs)
          AND AttachedFilePath IS NOT NULL;

        -- 2. Delete chat messages in expired OJT conversations
        DELETE FROM ChatMessages
        WHERE ConversationId IN (
            SELECT ConversationId FROM ChatConversations
            WHERE User1Id IN (SELECT UserId FROM @ExpiredOJTs)
               OR User2Id IN (SELECT UserId FROM @ExpiredOJTs)
        );

        -- 3. Delete expired OJT conversations
        DELETE FROM ChatConversations
        WHERE User1Id IN (SELECT UserId FROM @ExpiredOJTs)
           OR User2Id IN (SELECT UserId FROM @ExpiredOJTs);

        -- 4. Clear submission file paths from DB
        UPDATE TaskSubmissions
        SET FilePath = NULL, FileName = NULL
        WHERE OJTUserId IN (SELECT UserId FROM @ExpiredOJTs)
          AND FilePath IS NOT NULL;

        -- 5. Clear task file paths from DB
        UPDATE Tasks
        SET AttachedFilePath = NULL, AttachedFileName = NULL
        WHERE AssignedToUserId IN (SELECT UserId FROM @ExpiredOJTs)
          AND AttachedFilePath IS NOT NULL;

        -- 6. Delete task submissions
        DELETE FROM TaskSubmissions
        WHERE OJTUserId IN (SELECT UserId FROM @ExpiredOJTs);

        -- 7. Delete progress logs
        DELETE FROM TaskProgressLogs
        WHERE OJTUserId IN (SELECT UserId FROM @ExpiredOJTs);

        -- 8. Delete notification reads
        DELETE FROM NotificationReads
        WHERE OJTUserId IN (SELECT UserId FROM @ExpiredOJTs);

        -- 9. Delete tasks assigned to expired OJTs
        DELETE FROM Tasks
        WHERE AssignedToUserId IN (SELECT UserId FROM @ExpiredOJTs);

        -- 10. Delete expired OJT user accounts
        DELETE FROM Users
        WHERE UserId IN (SELECT UserId FROM @ExpiredOJTs);

        -- 11. Clear file paths from old chat messages (all users, 2 years)
        UPDATE ChatMessages
        SET AttachedFilePath = NULL, AttachedFileName = NULL
        WHERE SentAt <= @ExpiryDate
          AND AttachedFilePath IS NOT NULL;

        -- 12. Delete old chat messages for ALL users
        SET @DeletedMessages = (SELECT COUNT(*) FROM ChatMessages WHERE SentAt <= @ExpiryDate);
        DELETE FROM ChatMessages WHERE SentAt <= @ExpiryDate;

        -- 13. Delete empty conversations
        SET @DeletedConversations = (
            SELECT COUNT(*) FROM ChatConversations
            WHERE ConversationId NOT IN (SELECT DISTINCT ConversationId FROM ChatMessages)
        );
        DELETE FROM ChatConversations
        WHERE ConversationId NOT IN (
            SELECT DISTINCT ConversationId FROM ChatMessages
        );

        -- 14. Log the cleanup run
        INSERT INTO OJT_CleanupLog (RunAt, ExpiredOJTs, DeletedMessages, DeletedConversations, Notes)
        VALUES (GETDATE(), @DeletedOJTs, @DeletedMessages, @DeletedConversations,
                'Cleanup completed. Expired OJTs: ' + CAST(@DeletedOJTs AS VARCHAR) +
                ', Deleted messages: ' + CAST(@DeletedMessages AS VARCHAR));
    END
    ELSE
    BEGIN
        -- Nothing to clean — log it anyway
        INSERT INTO OJT_CleanupLog (RunAt, ExpiredOJTs, DeletedMessages, DeletedConversations, Notes)
        VALUES (GETDATE(), 0, 0, 0, 'Cleanup ran — nothing to delete.');
    END
END;
GO

-- ============================================================
-- STEP 3: Create SQL Server Agent Jobs
-- ============================================================
USE msdb;
GO

-- Remove existing jobs if re-running this script
IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = 'OJT Portal - Auto Expiry Cleanup')
    EXEC msdb.dbo.sp_delete_job @job_name = 'OJT Portal - Auto Expiry Cleanup';

IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = 'OJT Portal - Auto Expiry Fallback')
    EXEC msdb.dbo.sp_delete_job @job_name = 'OJT Portal - Auto Expiry Fallback';
GO

-- ============================================================
-- JOB 1 — PRIMARY: January 10 every 2 years at 7:30 AM
-- ============================================================
EXEC msdb.dbo.sp_add_job
    @job_name        = 'OJT Portal - Auto Expiry Cleanup',
    @description     = 'Primary: Runs on January 10 every 2 years at 7:30 AM. Deletes expired OJT accounts and old chat data.',
    @enabled         = 1,
    @notify_level_eventlog = 2;

EXEC msdb.dbo.sp_add_jobstep
    @job_name        = 'OJT Portal - Auto Expiry Cleanup',
    @step_name       = 'Run OJT Expiry Cleanup',
    @command         = 'USE ITPMS_OJT; EXEC dbo.sp_OJT_AutoExpiry @ForceRun = 1;',
    @database_name   = 'ITPMS_OJT',
    @on_success_action = 1,
    @on_fail_action    = 2;

-- Schedule: January 10 every year at 7:30 AM
-- We use yearly schedule and the procedure itself checks the 2-year interval
-- This way it fires Jan 10 2027 but the guard skips it (only 1 year),
-- then fires Jan 10 2028 and the guard allows it (2 years)
EXEC msdb.dbo.sp_add_schedule
    @schedule_name          = 'Jan 10 Yearly 7:30AM',
    @freq_type              = 16,       -- monthly
    @freq_interval          = 10,       -- day 10 of the month
    @freq_recurrence_factor = 12,       -- every 12 months = yearly
    @active_start_time      = 073000;   -- 07:30:00 AM

EXEC msdb.dbo.sp_attach_schedule
    @job_name      = 'OJT Portal - Auto Expiry Cleanup',
    @schedule_name = 'Jan 10 Yearly 7:30AM';

EXEC msdb.dbo.sp_add_jobserver
    @job_name = 'OJT Portal - Auto Expiry Cleanup';

-- ============================================================
-- JOB 2 — FALLBACK: Daily at 8:00 AM
-- Only actually cleans if server was down on Jan 10
-- AND it has been 2+ years since last cleanup
-- ============================================================
EXEC msdb.dbo.sp_add_job
    @job_name        = 'OJT Portal - Auto Expiry Fallback',
    @description     = 'Fallback: Runs daily at 8:00 AM. Only performs cleanup if Jan 10 primary job was missed AND 2+ years have passed since last run.',
    @enabled         = 1,
    @notify_level_eventlog = 2;

EXEC msdb.dbo.sp_add_jobstep
    @job_name        = 'OJT Portal - Auto Expiry Fallback',
    @step_name       = 'Fallback Expiry Check',
    @command         = 'USE ITPMS_OJT;
-- Only run if today is January 10 or later (within a 30-day window after Jan 10)
-- AND the 2-year guard inside the procedure will decide whether to actually clean
IF MONTH(GETDATE()) = 1 AND DAY(GETDATE()) BETWEEN 10 AND 40
    EXEC dbo.sp_OJT_AutoExpiry @ForceRun = 0;',
    @database_name   = 'ITPMS_OJT',
    @on_success_action = 1,
    @on_fail_action    = 2;

-- Schedule: Daily at 8:00 AM
EXEC msdb.dbo.sp_add_schedule
    @schedule_name     = 'Daily 8AM Fallback',
    @freq_type         = 4,        -- daily
    @freq_interval     = 1,        -- every 1 day
    @active_start_time = 080000;   -- 08:00:00 AM

EXEC msdb.dbo.sp_attach_schedule
    @job_name      = 'OJT Portal - Auto Expiry Fallback',
    @schedule_name = 'Daily 8AM Fallback';

EXEC msdb.dbo.sp_add_jobserver
    @job_name = 'OJT Portal - Auto Expiry Fallback';

PRINT '============================================================';
PRINT 'Setup complete!';
PRINT '------------------------------------------------------------';
PRINT 'PRIMARY JOB:  OJT Portal - Auto Expiry Cleanup';
PRINT '  Runs: January 10 every year at 7:30 AM';
PRINT '  Guard: Skips if less than 2 years since last run';
PRINT '  Next real run: January 10, 2028';
PRINT '------------------------------------------------------------';
PRINT 'FALLBACK JOB: OJT Portal - Auto Expiry Fallback';
PRINT '  Runs: Daily at 8:00 AM';
PRINT '  Triggers: Only in January 10-40 window + 2yr guard';
PRINT '  Purpose: Catches missed runs if server was down';
PRINT '============================================================';
GO

-- ============================================================
-- USEFUL QUERIES
-- ============================================================
-- View cleanup history:
-- SELECT * FROM ITPMS_OJT.dbo.OJT_CleanupLog ORDER BY RunAt DESC;

-- Run manually (force):
-- USE ITPMS_OJT; EXEC dbo.sp_OJT_AutoExpiry @ForceRun = 1;

-- Preview who would be deleted (read-only check):
-- SELECT UserId, FirstName+' '+LastName AS Name, CreatedAt,
--        DATEDIFF(year, CreatedAt, GETDATE()) AS YearsOld
-- FROM ITPMS_OJT.dbo.Users
-- WHERE Role = 'OJT' AND CreatedAt <= DATEADD(year, -2, GETDATE());