# Installing pgvector on Windows (PostgreSQL)

This project can optionally use **pgvector** for vector similarity search in PostgreSQL (for NPC memory embeddings). JitRealm will attempt `CREATE EXTENSION vector;` at startup when `Memory.UsePgvector` is enabled, so your Postgres instance must have the pgvector extension installed.

These steps are based on the DEV Community guide: [Install pgvector on Windows](https://dev.to/mehmetakar/install-pgvector-on-windows-6gl).

## Prerequisites

- PostgreSQL installed (example below assumes `C:\Program Files\PostgreSQL\17`)
- Git for Windows
- Visual Studio build tools (C++ build tools). (For the official Windows PostgreSQL distribution you typically need **MSVC**: `cl.exe` + `nmake`.)

## 1) Clone pgvector

```powershell
git clone https://github.com/pgvector/pgvector.git
cd pgvector
```

## 2) Build + install the extension

The pgvector repo includes a Windows build file: `Makefile.win`.

### Option A (recommended for official PostgreSQL on Windows): MSVC + `nmake` (`Makefile.win`)

1) Install **Visual Studio 2022 Build Tools** with the **Desktop development with C++** workload.

2) Open **“x64 Native Tools Command Prompt for VS 2022”** (or “Developer PowerShell for VS 2022”) and verify:

```powershell
where cl
where nmake
```

3) Build + install pgvector using `Makefile.win` (set `PGROOT` to your Postgres install root):

```powershell
set PGROOT=C:\Program Files\PostgreSQL\17
cd C:\Users\matsl\pgvector
nmake /F Makefile.win
nmake /F Makefile.win install
```

### Option B (only if your PostgreSQL was built with MinGW/MSYS2): `make` (`Makefile`)

Ensure `pg_config.exe` is on your PATH (it lives in your PostgreSQL `bin` directory, e.g. `C:\Program Files\PostgreSQL\17\bin\pg_config.exe`), then run:

```powershell
make
make install
```

## 3) (If required) enable extension loading + restart Postgres

The referenced guide suggests adding pgvector to `shared_preload_libraries` and restarting Postgres. In many setups, **pgvector does not require** `shared_preload_libraries`; try `CREATE EXTENSION vector;` first and only do this if you know you need it or are troubleshooting.

- Edit `postgresql.conf` (example path): `C:\Program Files\PostgreSQL\17\data\postgresql.conf`
- Add/uncomment:

```conf
shared_preload_libraries = 'pgvector'
```

Restart the Postgres service (example service name from the guide; adjust for your installed version):

```powershell
net stop postgresql-x64-15
net start postgresql-x64-15
```

## 4) Create the extension in your database

Open `psql` and run:

```sql
CREATE EXTENSION vector;
```

## 5) Verify the extension is available

```sql
SELECT * FROM pg_available_extensions WHERE name = 'vector';
```

## 6) Quick smoke test

```sql
CREATE TABLE items (id SERIAL PRIMARY KEY, embedding VECTOR(3));
INSERT INTO items (embedding) VALUES ('[1, 2, 3]');
SELECT * FROM items;
```

## Troubleshooting

- **`pg_config not found`**: add your PostgreSQL `bin` folder to PATH.
- **Build errors**: confirm Visual Studio C++ build tools are installed (and your `make` toolchain works).
- **Permissions**: run your terminal as Administrator if needed.


