SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- The double-entry ledger (blueprint 7.1 to 7.4): the bank's chart of
-- accounts, one postable ledger account per customer account or GL account,
-- journals and their entries, the balance view, and the triggers that keep
-- the ledger right even when someone goes around the posting procedure.
--
-- Money is DECIMAL(19,4). Positive amount = debit, negative = credit.
-- Customer accounts are liabilities of the bank, so a customer's balance is
-- the negative of the sum of their entries.
--
-- Naming as in 0002_core.sql, plus ix_ for a non-unique index.

-- ledger.gl_account: the bank's own chart of accounts (blueprint 7.2). The
-- blueprint lists only the codes; this table gives each a name and a type.
CREATE TABLE ledger.gl_account (
    gl_code      CHAR(4)     NOT NULL,
    name         VARCHAR(60) NOT NULL,
    account_type VARCHAR(10) NOT NULL,
    CONSTRAINT pk_gl_account PRIMARY KEY (gl_code),
    CONSTRAINT ck_gl_account_account_type CHECK (account_type IN ('ASSET', 'CLEARING', 'EQUITY', 'INCOME', 'EXPENSE'))
);
GO

-- The v1 chart of accounts. 1300 (card settlement clearing) arrives with
-- cards in a later ticket.
INSERT INTO ledger.gl_account (gl_code, name, account_type)
VALUES ('1010', 'Cash at central bank',       'ASSET'),
       ('1250', 'Outbound payments clearing', 'CLEARING'),
       ('3000', 'Share capital',              'EQUITY'),
       ('4100', 'Fee income',                 'INCOME'),
       ('5100', 'Interest expense',           'EXPENSE');
GO

-- ledger.ledger_account: every account an entry can be posted to. A CUSTOMER
-- ledger account stands for one core.account; a GL ledger account stands for
-- one GL code. Entries point here, so one foreign key covers both kinds.
CREATE TABLE ledger.ledger_account (
    ledger_account_id BIGINT IDENTITY(1,1) NOT NULL,
    kind              VARCHAR(10) NOT NULL,
    account_id        BIGINT      NULL,
    gl_code           CHAR(4)     NULL,
    CONSTRAINT pk_ledger_account PRIMARY KEY (ledger_account_id),
    CONSTRAINT fk_ledger_account_account    FOREIGN KEY (account_id) REFERENCES core.account (account_id),
    CONSTRAINT fk_ledger_account_gl_account FOREIGN KEY (gl_code)    REFERENCES ledger.gl_account (gl_code),
    CONSTRAINT ck_ledger_account_kind CHECK (kind IN ('CUSTOMER', 'GL')),
    -- A CUSTOMER row has an account and no GL code; a GL row the reverse.
    -- Written as "if this kind, then that link", so an unknown kind is
    -- refused by ck_ledger_account_kind alone.
    CONSTRAINT ck_ledger_account_link CHECK (
        (kind <> 'CUSTOMER' OR (account_id IS NOT NULL AND gl_code IS NULL))
        AND (kind <> 'GL' OR (gl_code IS NOT NULL AND account_id IS NULL))
    )
);
GO

-- Each account and each GL code has at most one ledger account. A plain
-- UNIQUE constraint would allow only one NULL, and every GL row has a NULL
-- account_id (and every customer row a NULL gl_code), so the indexes skip
-- the NULLs.
CREATE UNIQUE INDEX ux_ledger_account_account_id
    ON ledger.ledger_account (account_id)
    WHERE account_id IS NOT NULL;
GO

CREATE UNIQUE INDEX ux_ledger_account_gl_code
    ON ledger.ledger_account (gl_code)
    WHERE gl_code IS NOT NULL;
GO

-- One GL ledger account per GL code seeded above.
INSERT INTO ledger.ledger_account (kind, gl_code)
SELECT 'GL', gl_code
FROM ledger.gl_account
ORDER BY gl_code;
GO

-- One CUSTOMER ledger account for every account that already exists. The
-- trigger below covers every account opened from now on.
INSERT INTO ledger.ledger_account (kind, account_id)
SELECT 'CUSTOMER', account_id
FROM core.account
ORDER BY account_id;
GO

-- No account can exist without a ledger account, so any account can be
-- posted to from the moment it is opened. Like the token triggers, it reads
-- every row of the inserted pseudo-table, so a multi-row insert is covered.
CREATE TRIGGER core.tr_account_ledger_account
ON core.account
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO ledger.ledger_account (kind, account_id)
    SELECT 'CUSTOMER', account_id
    FROM inserted;
END;
GO

-- Ledger accounts are insert-only. A ledger account's link is its identity:
-- repointing it to another account or GL code would move an account's whole
-- history to that account. Like the ledger itself, nothing is deleted. The bank's
-- chart of accounts (ledger.gl_account) is not locked this way, so a GL
-- account's name can still be corrected.
CREATE TRIGGER ledger.tr_ledger_account_immutable
ON ledger.ledger_account
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    THROW 50002, N'INV-02: ledger.ledger_account is insert-only. A ledger account is never repointed or deleted.', 1;
END;
GO

-- ledger.journal: the header of one balanced posting. Its entries are in
-- ledger.entry.
CREATE TABLE ledger.journal (
    journal_id          BIGINT IDENTITY(1,1) NOT NULL,
    journal_type        VARCHAR(30)   NOT NULL,
    business_date       DATE          NOT NULL, -- the open business date at posting time (INV-08)
    posted_at_utc       DATETIME2(3)  NOT NULL,
    -- A key is an identifier, so two keys are the same only if they are the
    -- same characters. Under the server's collation abc and ABC would be one
    -- key, while the posting procedure's application lock on the key is
    -- case-sensitive, so the two would disagree. A binary collation compares
    -- the exact characters (ADR-009).
    idempotency_key     NVARCHAR(100) COLLATE Latin1_General_BIN2 NOT NULL,
    reverses_journal_id BIGINT        NULL,     -- set only on a REVERSAL journal (LL-009)
    CONSTRAINT pk_journal PRIMARY KEY (journal_id),
    CONSTRAINT fk_journal_business_calendar FOREIGN KEY (business_date)       REFERENCES ops.business_calendar (business_date),
    CONSTRAINT fk_journal_reverses_journal  FOREIGN KEY (reverses_journal_id) REFERENCES ledger.journal (journal_id),
    -- INV-03: a key can be used once. The posting procedure checks first and
    -- returns the existing journal; this constraint is the backstop.
    CONSTRAINT uq_journal_idempotency_key UNIQUE (idempotency_key),
    -- The key is not empty and has no leading or trailing whitespace (space,
    -- tab, line feed, carriage return). SQL Server's = ignores trailing
    -- spaces even under a binary collation, so 'abc' and 'abc ' would still
    -- collide; comparing the byte length (DATALENGTH) with that of the
    -- trimmed key sees them. The column is NOT NULL, so DATALENGTH is never
    -- NULL and the check can never come out unknown and let a row through.
    CONSTRAINT ck_journal_idempotency_key CHECK (
        DATALENGTH(idempotency_key) > 0
        AND DATALENGTH(idempotency_key) = DATALENGTH(TRIM(NCHAR(32) + NCHAR(9) + NCHAR(10) + NCHAR(13) FROM idempotency_key))
    ),
    -- journal_id alone is already unique. This pair exists so that entries
    -- can reference both columns (see fk_entry_journal).
    CONSTRAINT uq_journal_id_business_date UNIQUE (journal_id, business_date),
    CONSTRAINT ck_journal_journal_type CHECK (journal_type IN (
        'TRANSFER', 'PAYMENT_OUT', 'SALARY_CREDIT', 'INTEREST_CAPITALISATION',
        'CLEARING_SETTLEMENT', 'FEE', 'REVERSAL', 'CAPITAL_INJECTION'))
);
GO

-- A journal can be reversed at most once. Filtered, because most journals
-- reverse nothing and have a NULL here.
CREATE UNIQUE INDEX ux_journal_reverses_journal_id
    ON ledger.journal (reverses_journal_id)
    WHERE reverses_journal_id IS NOT NULL;
GO

-- ledger.entry: the posting lines. The entries of one journal sum to zero.
CREATE TABLE ledger.entry (
    entry_id          BIGINT IDENTITY(1,1) NOT NULL,
    journal_id        BIGINT        NOT NULL,
    business_date     DATE          NOT NULL,
    ledger_account_id BIGINT        NOT NULL,
    amount            DECIMAL(19,4) NOT NULL, -- positive = debit, negative = credit
    currency_code     CHAR(3)       NOT NULL,
    CONSTRAINT pk_entry PRIMARY KEY (entry_id),
    -- Pointing at the journal's (journal_id, business_date) pair, not just
    -- its id, means an entry's date can never differ from its journal's.
    CONSTRAINT fk_entry_journal        FOREIGN KEY (journal_id, business_date) REFERENCES ledger.journal (journal_id, business_date),
    CONSTRAINT fk_entry_ledger_account FOREIGN KEY (ledger_account_id)         REFERENCES ledger.ledger_account (ledger_account_id),
    CONSTRAINT ck_entry_amount_not_zero CHECK (amount <> 0),
    -- AED has 100 fils to the dirham, and postings are in whole fils
    -- (blueprint 7.5). DECIMAL(19,4) is the project's money type, so the
    -- column has four decimal places and this check keeps the last two zero.
    CONSTRAINT ck_entry_amount_whole_fils CHECK (amount = ROUND(amount, 2)),
    -- v1 is AED only; a later migration widens this list.
    CONSTRAINT ck_entry_currency_code CHECK (currency_code IN ('AED'))
);
GO

-- Point-in-time balances for interest and reconciliation (blueprint 7.4):
-- the entries of one account up to a date, read from the index alone.
CREATE INDEX ix_entry_account_date
    ON ledger.entry (ledger_account_id, business_date)
    INCLUDE (amount);
GO

-- Finds the entries of a journal, as the balancing trigger does.
CREATE INDEX ix_entry_journal_id
    ON ledger.entry (journal_id);
GO

-- ledger.v_account_balance: balances derived from the ledger, never stored
-- (blueprint 7.4, ADR-007). The unique clustered index makes this an indexed
-- view: SQL Server stores the totals and updates them in the same
-- transaction as every insert into ledger.entry, so they cannot drift.
--
-- Read it WITH (NOEXPAND) so the stored totals are used.
-- Customer balance = -net_amount (customer accounts are liabilities).
-- An account with no entries has no row here, and its balance is zero.
CREATE VIEW ledger.v_account_balance
WITH SCHEMABINDING
AS
SELECT e.ledger_account_id,
       SUM(e.amount) AS net_amount,
       COUNT_BIG(*)  AS entry_count -- an indexed view with GROUP BY must count its rows
FROM ledger.entry AS e
GROUP BY e.ledger_account_id;
GO

CREATE UNIQUE CLUSTERED INDEX ux_v_account_balance
    ON ledger.v_account_balance (ledger_account_id);
GO

-- INV-01 backstop: every journal balances. The posting procedure already
-- refuses an unbalanced journal; this trigger also protects the ledger from
-- an insert that goes around the procedure. After any insert into
-- ledger.entry, every journal it touched must net to zero in each currency.
-- So a journal's entries must all arrive in one INSERT statement, as the
-- procedure does. A THROW inside a trigger rolls back the whole
-- transaction, so the refused rows are never stored.
CREATE TRIGGER ledger.tr_entry_balanced
ON ledger.entry
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM ledger.entry AS e
        WHERE e.journal_id IN (SELECT journal_id FROM inserted)
        GROUP BY e.journal_id, e.currency_code
        HAVING SUM(e.amount) <> 0
    )
        THROW 50001, N'INV-01: the entries of a journal must net to zero in each currency.', 1;
END;
GO

-- INV-02: ledger rows are never updated or deleted. An INSTEAD OF trigger
-- runs in place of the UPDATE or DELETE, so the statement never touches a
-- row. A mistake is corrected by posting a reversal journal (LL-009).
-- TRUNCATE skips triggers, but it is already impossible: SQL Server refuses
-- to truncate ledger.entry because the indexed view is built on it, and
-- ledger.journal because a foreign key points at it.
CREATE TRIGGER ledger.tr_entry_immutable
ON ledger.entry
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    THROW 50002, N'INV-02: ledger.entry is append-only. Correct a mistake with a reversal journal.', 1;
END;
GO

CREATE TRIGGER ledger.tr_journal_immutable
ON ledger.journal
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    THROW 50002, N'INV-02: ledger.journal is append-only. Correct a mistake with a reversal journal.', 1;
END;
GO
