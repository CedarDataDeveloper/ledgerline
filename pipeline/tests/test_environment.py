"""Checks that this machine can reach SQL Server from Python (LL-001).

The server name comes from LEDGERLINE_SQL_SERVER and defaults to the local
default instance.
"""

import os

import pyodbc

DRIVER = "ODBC Driver 18 for SQL Server"


def test_odbc_driver_18_is_installed():
    assert DRIVER in pyodbc.drivers()


def test_sql_server_accepts_a_windows_login():
    server = os.environ.get("LEDGERLINE_SQL_SERVER", "localhost")

    # Driver 18 encrypts by default. A local instance has a self-signed
    # certificate, so the test trusts it.
    connection_string = (
        f"DRIVER={{{DRIVER}}};SERVER={server};DATABASE=master;"
        "Trusted_Connection=yes;TrustServerCertificate=yes;"
    )

    connection = pyodbc.connect(connection_string, timeout=5)
    try:
        assert connection.execute("SELECT 1;").fetchval() == 1
    finally:
        connection.close()
