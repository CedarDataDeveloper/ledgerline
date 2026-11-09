SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- Every migration starts with the SET options above. Indexed views, such as
-- the account balance view (blueprint 7.4), can only be created and kept up
-- to date by sessions that have them.

-- The BankCore schemas (blueprint 7.1). CREATE SCHEMA must be the only
-- statement in its batch.

CREATE SCHEMA core;
GO

CREATE SCHEMA ledger;
GO

CREATE SCHEMA ops;
GO

CREATE SCHEMA pii;
GO
