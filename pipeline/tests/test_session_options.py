"""Checks that a pyodbc session can write to tables under indexed views (LL-002).

Account balances come from an indexed view (blueprint 7.4). SQL Server only
allows inserts into the tables beneath it when certain SET options are on, so
the pipeline's connections must have them. ARITHABORT is left out on purpose:
the session reports it as off, but ANSI_WARNINGS ON implies it at this
database compatibility level.

The server name comes from LEDGERLINE_SQL_SERVER and defaults to the local
default instance.
"""

import os

import pyodbc

DRIVER = "ODBC Driver 18 for SQL Server"

# Option name and the value (1 = on, 0 = off) indexed views need.
REQUIRED_OPTIONS = {
    "ANSI_NULLS": 1,
    "ANSI_PADDING": 1,
    "ANSI_WARNINGS": 1,
    "CONCAT_NULL_YIELDS_NULL": 1,
    "QUOTED_IDENTIFIER": 1,
    "NUMERIC_ROUNDABORT": 0,
}


def test_pyodbc_session_has_the_options_indexed_views_need():
    server = os.environ.get("LEDGERLINE_SQL_SERVER", "localhost")

    # Driver 18 encrypts by default. A local instance has a self-signed
    # certificate, so the test trusts it.
    connection_string = (
        f"DRIVER={{{DRIVER}}};SERVER={server};DATABASE=master;"
        "Trusted_Connection=yes;TrustServerCertificate=yes;"
    )

    connection = pyodbc.connect(connection_string, timeout=5)
    try:
        actual = {}
        for option in REQUIRED_OPTIONS:
            # SESSIONPROPERTY returns sql_variant, which pyodbc cannot read.
            actual[option] = connection.execute(
                "SELECT CAST(SESSIONPROPERTY(?) AS int);", option
            ).fetchval()

        assert actual == REQUIRED_OPTIONS
    finally:
        connection.close()
