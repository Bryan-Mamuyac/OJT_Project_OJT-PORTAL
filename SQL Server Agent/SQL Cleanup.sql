-- Fix 1: Add Notes column to OJT_CleanupLog (if it doesn't exist)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('OJT_CleanupLog') AND name = 'Notes'
)
BEGIN
    ALTER TABLE OJT_CleanupLog ADD Notes NVARCHAR(500) NULL;
    PRINT 'Notes column added to OJT_CleanupLog.';
END
ELSE
    PRINT 'Notes column already exists.';
GO