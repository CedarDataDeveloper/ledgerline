SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- The posting engine (blueprint 7.3): exactly one door into the ledger. The
-- application calls ledger.usp_post_journal with the journal's lines as one
-- table-valued parameter; the procedure checks every rule, then writes the
-- journal and all of its entries in one transaction.
--
-- Not yet here: the audit event (blueprint step 5) and reversals arrive in
-- LL-009, and so do the permissions that make this procedure the only way
-- in. Until then the triggers in 0005_ledger.sql are the backstop.
--
-- Errors the ledger raises (THROW numbers):
--   50001  INV-01  the lines do not net to zero in each currency
--                  (also raised by the trigger ledger.tr_entry_balanced)
--   50002  INV-02  an UPDATE or DELETE on ledger.journal, ledger.entry or
--                  ledger.ledger_account (raised by the immutability
--                  triggers in 0005_ledger.sql)
--   50003          fewer than two lines
--   50004          a zero amount, or an amount finer than one fils
--   50005          a ledger account that does not exist
--   50006  INV-04  a customer account would go below zero
--   50007  INV-05  a net debit to a FROZEN account
--   50008  INV-05  any posting to a CLOSED account
--   50009  INV-08  no business date is OPEN
--   50010  INV-03  the idempotency key is missing, blank, padded with
--                  spaces, or longer than 100 characters
--   50011  INV-03  the lock on the idempotency key could not be taken
-- Other rules are table constraints and raise SQL Server's own errors, for
-- example 547 for an unknown journal type (ck_journal_journal_type).

-- One line of a journal. Positive amount = debit, negative = credit.
CREATE TYPE ledger.journal_line AS TABLE (
    ledger_account_id BIGINT        NOT NULL,
    amount            DECIMAL(19,4) NOT NULL,
    currency_code     CHAR(3)       NOT NULL
);
GO

-- @idempotency_key is wider than the column on purpose. SQL Server silently
-- cuts an argument that is longer than its parameter, without an error. With
-- NVARCHAR(100), a 101-character key would arrive as its first 100
-- characters, match a different request's journal, and get that journal's
-- id back: the second request would never post, and nobody would be told.
-- At 400, an over-long key arrives whole and step 1 refuses it.
CREATE PROCEDURE ledger.usp_post_journal
    @idempotency_key NVARCHAR(400),
    @journal_type    VARCHAR(30),
    @lines           ledger.journal_line READONLY,
    @journal_id      BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    -- Any error rolls back the whole transaction, including a transaction the
    -- caller opened, so a half-done unit of work can never be committed.
    SET XACT_ABORT ON;

    DECLARE @message NVARCHAR(2048);
    DECLARE @refused_id BIGINT;

    -- @journal_id is an OUTPUT parameter, so a caller can pass a value in.
    -- Clear it, or the "key already used" check below could be fooled.
    SET @journal_id = NULL;

    -- 1. INV-03: a repeated key has no new effect. A retry gets the original
    --    journal_id back and nothing else happens.
    --    The key must be exactly what the column will store: not missing,
    --    not empty, no leading or trailing whitespace, at most 100
    --    characters. Lengths are measured in bytes with DATALENGTH (two
    --    bytes per character), because LEN ignores trailing spaces. These
    --    are the same rules as ck_journal_idempotency_key, checked here first
    --    so the caller gets 50010 and the key check below sees a clean key.
    IF @idempotency_key IS NULL
       OR DATALENGTH(@idempotency_key) = 0
       OR DATALENGTH(@idempotency_key) <> DATALENGTH(TRIM(NCHAR(32) + NCHAR(9) + NCHAR(10) + NCHAR(13) FROM @idempotency_key))
       OR DATALENGTH(@idempotency_key) > 200
        THROW 50010, N'INV-03: the idempotency key must be 1 to 100 characters with no leading or trailing whitespace.', 1;

    SELECT @journal_id = journal_id
    FROM ledger.journal
    WHERE idempotency_key = @idempotency_key;

    IF @journal_id IS NOT NULL
        RETURN;

    -- 2. Validate the lines, each rule with its own error.
    IF (SELECT COUNT(*) FROM @lines) < 2
        THROW 50003, N'A journal needs at least two lines.', 1;

    -- AED has 100 fils to the dirham; postings are in whole fils.
    IF EXISTS (SELECT 1 FROM @lines WHERE amount = 0 OR amount <> ROUND(amount, 2))
        THROW 50004, N'Every amount must be non-zero and in whole fils (at most two decimal places).', 1;

    IF EXISTS (
        SELECT 1
        FROM @lines AS l
        WHERE NOT EXISTS (SELECT 1 FROM ledger.ledger_account AS la WHERE la.ledger_account_id = l.ledger_account_id)
    )
        THROW 50005, N'Every line must name an existing ledger account.', 1;

    -- INV-01: every journal balances, in each currency separately.
    IF EXISTS (SELECT 1 FROM @lines GROUP BY currency_code HAVING SUM(amount) <> 0)
        THROW 50001, N'INV-01: the lines of a journal must net to zero in each currency.', 1;

    -- The journal's net effect on each account it touches: one row per
    -- account, whatever the number of lines. A positive net_amount is a net
    -- debit. (v1 is AED only, so currencies are not kept apart here.)
    DECLARE @accounts TABLE (
        ledger_account_id BIGINT        NOT NULL PRIMARY KEY,
        net_amount        DECIMAL(19,4) NOT NULL,
        kind              VARCHAR(10)   NULL,  -- filled in step 6
        account_status    VARCHAR(10)   NULL   -- filled in step 6; NULL for GL accounts
    );

    INSERT INTO @accounts (ledger_account_id, net_amount)
    SELECT ledger_account_id, SUM(amount)
    FROM @lines
    GROUP BY ledger_account_id;

    -- 3. Everything from here on is one transaction. If the caller already
    --    has a transaction open, this one is nested inside it: our COMMIT
    --    only counts down, and the caller's COMMIT makes the posting final.
    BEGIN TRANSACTION;

    -- INV-03 under concurrency: two calls with the same key at the same
    -- moment could both pass the check in step 1. An exclusive application
    -- lock on the key, owned by the transaction, lets only one through at a
    -- time; the second waits until the first commits, then finds its journal
    -- below. uq_journal_idempotency_key stays as the backstop.
    DECLARE @lock_resource NVARCHAR(255) = N'ledger.usp_post_journal:' + @idempotency_key;
    DECLARE @lock_result INT;

    EXEC @lock_result = sys.sp_getapplock
        @Resource  = @lock_resource,
        @LockMode  = 'Exclusive',
        @LockOwner = 'Transaction';

    -- 0 or 1 means granted; a negative value means timed out, cancelled or
    -- chosen as a deadlock victim.
    IF @lock_result < 0
        THROW 50011, N'INV-03: the lock on the idempotency key could not be taken.', 1;

    SELECT @journal_id = journal_id
    FROM ledger.journal
    WHERE idempotency_key = @idempotency_key;

    IF @journal_id IS NOT NULL
    BEGIN
        COMMIT TRANSACTION; -- nothing was written; this releases the lock
        RETURN;
    END;

    -- 4. INV-08: postings use the open business date only. REPEATABLEREAD
    --    keeps a shared lock on the OPEN row until the transaction ends, so
    --    end of day cannot close the date while this posting is in flight.
    DECLARE @business_date DATE;

    SELECT @business_date = business_date
    FROM ops.business_calendar WITH (REPEATABLEREAD)
    WHERE status = 'OPEN';

    IF @business_date IS NULL
        THROW 50009, N'INV-08: no business date is open.', 1;

    -- 5. Lock every ledger account in the journal, one at a time, in
    --    ascending ledger_account_id order (ADR-021). Two journals that share
    --    an account then queue one behind the other, so each sees the
    --    other's balance; and because every posting takes its locks in the
    --    same order, two postings can never each hold a lock the other needs
    --    (no deadlock). UPDLOCK is held until the transaction ends.
    --    The lock is on the ledger_account row, not the balance view: an
    --    account with no entries has no row in the view to lock.
    DECLARE @lock_id BIGINT = 0; -- ledger_account_id starts at 1
    DECLARE @locked_id BIGINT;

    WHILE 1 = 1
    BEGIN
        -- The next account above the last one locked.
        SET @lock_id = (SELECT MIN(ledger_account_id) FROM @accounts WHERE ledger_account_id > @lock_id);
        IF @lock_id IS NULL
            BREAK;

        SELECT @locked_id = ledger_account_id -- the value is not used; the lock is the point
        FROM ledger.ledger_account WITH (UPDLOCK, ROWLOCK)
        WHERE ledger_account_id = @lock_id;
    END;

    -- 6. INV-05 (BR-07): read each customer account's status. REPEATABLEREAD
    --    keeps a shared lock on the core.account row until the transaction
    --    ends, so a freeze cannot slip in between this check and the insert.
    UPDATE a
    SET a.kind = la.kind,
        a.account_status = ca.status
    FROM @accounts AS a
    JOIN ledger.ledger_account AS la ON la.ledger_account_id = a.ledger_account_id
    LEFT JOIN core.account AS ca WITH (REPEATABLEREAD) ON ca.account_id = la.account_id;

    -- A CLOSED account accepts nothing, debit or credit.
    SELECT TOP (1) @refused_id = ledger_account_id
    FROM @accounts
    WHERE account_status = 'CLOSED'
    ORDER BY ledger_account_id;

    IF @refused_id IS NOT NULL
    BEGIN
        SET @message = CONCAT(N'INV-05: ledger account ', @refused_id, N' belongs to a closed account.');
        THROW 50008, @message, 1;
    END;

    -- A FROZEN account refuses a net debit but still accepts a net credit,
    -- such as a salary.
    SELECT TOP (1) @refused_id = ledger_account_id
    FROM @accounts
    WHERE account_status = 'FROZEN' AND net_amount > 0
    ORDER BY ledger_account_id;

    IF @refused_id IS NOT NULL
    BEGIN
        SET @message = CONCAT(N'INV-05: ledger account ', @refused_id, N' belongs to a frozen account and cannot be debited.');
        THROW 50007, @message, 1;
    END;

    -- 7. INV-04: no customer account goes below zero. For each customer
    --    account this journal debits on balance, its current net_amount plus
    --    this journal's net_amount must not be above zero (customer balance =
    --    -net_amount). An account with no entries has no row in the view, so
    --    ISNULL makes its net_amount zero; without it the sum would be NULL
    --    and the check would let the debit through. GL accounts have no limit.
    SELECT TOP (1) @refused_id = a.ledger_account_id
    FROM @accounts AS a
    LEFT JOIN ledger.v_account_balance AS v WITH (NOEXPAND) ON v.ledger_account_id = a.ledger_account_id
    WHERE a.kind = 'CUSTOMER'
      AND a.net_amount > 0
      AND ISNULL(v.net_amount, 0) + a.net_amount > 0
    ORDER BY a.ledger_account_id;

    IF @refused_id IS NOT NULL
    BEGIN
        SET @message = CONCAT(N'INV-04: ledger account ', @refused_id, N' does not hold enough to cover this debit.');
        THROW 50006, @message, 1;
    END;

    -- 8. Write the journal, then all of its entries in ONE insert, each
    --    stamped with the open business date. One statement matters: the
    --    balancing trigger checks the journal after each insert, so the
    --    entries must arrive together.
    INSERT INTO ledger.journal (journal_type, business_date, posted_at_utc, idempotency_key)
    VALUES (@journal_type, @business_date, SYSUTCDATETIME(), @idempotency_key);

    SET @journal_id = CAST(SCOPE_IDENTITY() AS BIGINT);

    INSERT INTO ledger.entry (journal_id, business_date, ledger_account_id, amount, currency_code)
    SELECT @journal_id, @business_date, ledger_account_id, amount, currency_code
    FROM @lines;

    COMMIT TRANSACTION;
END;
GO
