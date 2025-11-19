-- Minimal initial schema + SystemType seeds (idempotent)
CREATE TABLE IF NOT EXISTS SystemType (
  Id BIGINT PRIMARY KEY,
  Name NVARCHAR(200) NOT NULL
);

-- Insert stable system types 1..16 if not exists
MERGE INTO SystemType AS target
USING (VALUES
  (1, 'project:standard'),
  (2, 'workspace:standard'),
  (3, 'repository'),
  (4, 'chatroom'),
  (5, 'kanban:board'),
  (6, 'user'),
  (7, 'group'),
  (8, 'filerecord'),
  (9, 'directconversation'),
  (10,'kanban:card'),
  (11,'attestation'),
  (12,'contribution-record'),
  (13,'audit-event'),
  (14,'permission-policy'),
  (15,'github-board-link'),
  (16,'resource-record')
) AS src (Id, Name)
ON target.Id = src.Id
WHEN NOT MATCHED THEN
  INSERT (Id, Name) VALUES (src.Id, src.Name);

-- Minimal Users table (for seed)
CREATE TABLE IF NOT EXISTS [User] (
  Id BIGINT PRIMARY KEY IDENTITY(1000,1),
  Email NVARCHAR(256) NOT NULL UNIQUE,
  DisplayName NVARCHAR(256) NOT NULL,
  AttributesJson NVARCHAR(MAX) NULL,
  Version INT NOT NULL DEFAULT 1,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
  UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
  IsDeleted BIT NOT NULL DEFAULT 0
);

-- Seed sample admin/faculty/student users if missing by email
INSERT INTO [User] (Id, Email, DisplayName, Version)
SELECT 1000, 'admin@example.edu', 'System Admin', 1
WHERE NOT EXISTS (SELECT 1 FROM [User] WHERE Email = 'admin@example.edu');

INSERT INTO [User] (Id, Email, DisplayName, Version)
SELECT 1001, 'faculty@example.edu', 'Faculty User', 1
WHERE NOT EXISTS (SELECT 1 FROM [User] WHERE Email = 'faculty@example.edu');

INSERT INTO [User] (Id, Email, DisplayName, Version)
SELECT 1002, 'student@example.edu', 'Student User', 1
WHERE NOT EXISTS (SELECT 1 FROM [User] WHERE Email = 'student@example.edu');
