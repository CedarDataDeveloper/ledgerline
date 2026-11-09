SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- Every migration starts with the SET options above. Indexed views (blueprint
-- 7.4) can only be created and kept up to date by sessions that have them.

-- The warehouse schemas that the migrations own. dbt creates silver and gold
-- itself in later tickets. CREATE SCHEMA must be the only statement in its
-- batch.

CREATE SCHEMA bronze;
GO

CREATE SCHEMA etl;
GO

CREATE SCHEMA recon;
GO
