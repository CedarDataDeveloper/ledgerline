SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- The bank's working day (blueprint 7.1). It arrives now, before the rest of
-- the ops schema, because the posting procedure (0006) must stamp every entry
-- with the one open business date (INV-08), and a journal's date must be a
-- real calendar day.
--
-- Table only: no rows and no procedures. Opening a date, end of day and
-- rolling to the next date come in LL-006. Until then, tests insert their own
-- open date.

-- ops.business_calendar: one row per business date. A date is OPEN while the
-- bank posts to it, EOD_RUNNING while end of day runs, then CLOSED.
CREATE TABLE ops.business_calendar (
    business_date DATE        NOT NULL,
    status        VARCHAR(12) NOT NULL,
    CONSTRAINT pk_business_calendar PRIMARY KEY (business_date),
    CONSTRAINT ck_business_calendar_status CHECK (status IN ('OPEN', 'EOD_RUNNING', 'CLOSED'))
);
GO

-- At most one date is OPEN at a time. The index holds only the OPEN row, so a
-- second OPEN row is a duplicate key, while any number of CLOSED rows is fine.
CREATE UNIQUE INDEX ux_business_calendar_one_open
    ON ops.business_calendar (status)
    WHERE status = 'OPEN';
GO
