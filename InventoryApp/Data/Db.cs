using System.IO;
using Microsoft.Data.Sqlite;
using Dapper;

namespace InventoryApp.Data;

/// <summary>Hands out open SQLite connections and handles auto-initialization for portability.</summary>
public static class Db
{
    private static readonly object _initLock = new();
    private static volatile bool _isInitialized = false;
    internal static void ResetAfterRestore() { lock(_initLock) { _isInitialized=false; } }

    public static SqliteConnection Open()
    {
        EnsureInitialized();
        var conn = new SqliteConnection(AppConfig.ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        cmd.ExecuteNonQuery();

        return conn;
    }

    /// <summary>Used by the login window or startup to ensure connection and schema readiness.</summary>
    public static void TestConnection()
    {
        using var conn = Open();
    }

    public static void EnsureInitialized()
    {
        if (_isInitialized) return;
        lock (_initLock)
        {
            if (_isInitialized) return;

            using (var conn = new SqliteConnection(AppConfig.ConnectionString))
            {
                conn.Open();

                var tableCount = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='users'");

                if (tableCount == 0)
                {
                    InitializeSchemaAndSeed(conn);
                }
                ApplyMigrations(conn);
                ApplyV04Migration(conn);
            }

            _isInitialized = true;
        }
    }

    private static void ApplyMigrations(SqliteConnection conn)
    {
        var version = conn.ExecuteScalar<int>("PRAGMA user_version;");
        if (version > 3) throw new InvalidOperationException("Database dibuat oleh versi aplikasi yang lebih baru.");
        if (version >= 2) return;
        using var tx = conn.BeginTransaction();
        var hasVersion = conn.Query("PRAGMA table_info(assets)", transaction: tx)
            .Any(row => (string)row.name == "row_version");
        if (!hasVersion) conn.Execute("ALTER TABLE assets ADD COLUMN row_version INTEGER NOT NULL DEFAULT 0", transaction: tx);
        if (!conn.Query("PRAGMA table_info(assets)", transaction: tx).Any(row => (string)row.name == "building_id"))
            conn.Execute("ALTER TABLE assets ADD COLUMN building_id INTEGER NULL REFERENCES buildings(id); UPDATE assets SET building_id=(SELECT building_id FROM rooms WHERE id=assets.room_id)", transaction: tx);
        // A separate table allows Admin/Viewer roles without rewriting legacy foreign-key targets.
        conn.Execute(@"CREATE TABLE IF NOT EXISTS user_access(user_id INTEGER PRIMARY KEY REFERENCES users(id), role TEXT NOT NULL CHECK(role IN ('ADMIN','VIEWER','REVIEW')));
            INSERT OR IGNORE INTO user_access SELECT id,CASE WHEN role='ADMIN' THEN 'ADMIN' ELSE 'REVIEW' END FROM users;
            CREATE TABLE IF NOT EXISTS import_batches(hash TEXT PRIMARY KEY,source TEXT NOT NULL,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE IF NOT EXISTS ui_preferences(user_id INTEGER PRIMARY KEY REFERENCES users(id),hide_guide INTEGER NOT NULL DEFAULT 0);
            CREATE TRIGGER IF NOT EXISTS asset_building_insert BEFORE INSERT ON assets
            WHEN NEW.building_id IS NULL OR (NEW.room_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM rooms WHERE id=NEW.room_id AND building_id=NEW.building_id))
            BEGIN SELECT RAISE(ABORT,'Gedung wajib dan ruangan harus sesuai gedung.'); END;
            CREATE TRIGGER IF NOT EXISTS asset_building_update BEFORE UPDATE OF building_id,room_id ON assets
            WHEN NEW.building_id IS NULL OR (NEW.room_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM rooms WHERE id=NEW.room_id AND building_id=NEW.building_id))
            BEGIN SELECT RAISE(ABORT,'Gedung wajib dan ruangan harus sesuai gedung.'); END;", transaction: tx);
        conn.Execute(@"
            DROP VIEW IF EXISTS v_asset_list;
            CREATE VIEW v_asset_list AS
            SELECT a.id AS Id, a.asset_code AS AssetCode, it.name AS ItemTypeName,
                it.type_description AS TypeDescription, c.name AS CategoryName,
                b.name AS BuildingName, r.name AS RoomName, a.room_id AS RoomId,
                b.id AS BuildingId, cond.code AS ConditionCode, cond.name AS ConditionName,
                a.condition_id AS ConditionId, a.status AS Status,
                a.acquisition_year AS AcquisitionYear, a.notes AS Notes,
                a.legacy_code AS LegacyCode, a.updated_at AS UpdatedAt, a.row_version AS RowVersion
            FROM assets a JOIN item_types it ON it.id=a.item_type_id
            JOIN categories c ON c.id=it.category_id
            LEFT JOIN rooms r ON r.id=a.room_id
            LEFT JOIN buildings b ON b.id=a.building_id
            LEFT JOIN conditions cond ON cond.id=a.condition_id;
            PRAGMA user_version=2;
        ", transaction: tx);
        tx.Commit();
    }
    private static void ApplyV04Migration(SqliteConnection conn)
    {
        if(conn.ExecuteScalar<int>("PRAGMA user_version")==3)return;
        if(conn.ExecuteScalar<int>("SELECT COUNT(*) FROM users")>0)
            Services.AutomaticBackup.SnapshotBeforeMigration(conn);
        using var tx=conn.BeginTransaction();
        conn.Execute(@"CREATE TABLE user_access_v04(user_id INTEGER PRIMARY KEY REFERENCES users(id),role TEXT NOT NULL CHECK(role IN ('ADMIN','EDITOR','VIEWER','REVIEW')));
            INSERT INTO user_access_v04 SELECT user_id,role FROM user_access;
            DROP TABLE user_access;
            ALTER TABLE user_access_v04 RENAME TO user_access;
            PRAGMA user_version=3;",transaction:tx);
        tx.Commit();
    }
    private static void InitializeSchemaAndSeed(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();

        conn.Execute(@"
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS users (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                username        TEXT NOT NULL UNIQUE,
                full_name       TEXT NOT NULL,
                password_hash   TEXT NOT NULL,
                role            TEXT NOT NULL DEFAULT 'USER' CHECK(role IN ('ADMIN','USER')),
                is_active       INTEGER NOT NULL DEFAULT 1,
                last_login_at   TEXT NULL,
                created_at      TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                updated_at      TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            );

            CREATE TABLE IF NOT EXISTS categories (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT NOT NULL UNIQUE,
                description TEXT NULL,
                is_active   INTEGER NOT NULL DEFAULT 1,
                created_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                updated_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            );

            CREATE TABLE IF NOT EXISTS conditions (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                code        TEXT NOT NULL UNIQUE,
                name        TEXT NOT NULL,
                description TEXT NULL,
                sort_order  INTEGER NOT NULL DEFAULT 0,
                is_active   INTEGER NOT NULL DEFAULT 1,
                created_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                updated_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            );

            CREATE TABLE IF NOT EXISTS buildings (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT NOT NULL UNIQUE,
                code        TEXT NULL UNIQUE,
                address     TEXT NULL,
                is_active   INTEGER NOT NULL DEFAULT 1,
                created_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                updated_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            );

            CREATE TABLE IF NOT EXISTS rooms (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                building_id INTEGER NOT NULL REFERENCES buildings(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                name        TEXT NOT NULL,
                code        TEXT NULL,
                floor       TEXT NULL,
                is_active   INTEGER NOT NULL DEFAULT 1,
                created_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                updated_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                UNIQUE(building_id, name)
            );

            CREATE TABLE IF NOT EXISTS item_types (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                category_id      INTEGER NOT NULL REFERENCES categories(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                name             TEXT NOT NULL,
                type_description TEXT NULL,
                color            TEXT NULL,
                code_prefix      TEXT NOT NULL,
                photo_path       TEXT NULL,
                notes            TEXT NULL,
                status           TEXT NOT NULL DEFAULT 'ACTIVE' CHECK(status IN ('PENDING_REVIEW','ACTIVE','INACTIVE')),
                created_by       INTEGER NULL REFERENCES users(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                reviewed_by      INTEGER NULL REFERENCES users(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                reviewed_at      TEXT NULL,
                created_at       TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                updated_at       TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                UNIQUE(category_id, name)
            );

            CREATE TABLE IF NOT EXISTS assets (
                id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_code          TEXT NOT NULL UNIQUE,
                item_type_id        INTEGER NOT NULL REFERENCES item_types(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                room_id             INTEGER NULL REFERENCES rooms(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                condition_id        INTEGER NULL REFERENCES conditions(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                status              TEXT NOT NULL DEFAULT 'ACTIVE' CHECK(status IN ('ACTIVE','INACTIVE','DISPOSED')),
                acquisition_year    INTEGER NULL,
                notes               TEXT NULL,
                photo_path          TEXT NULL,
                legacy_code         TEXT NULL,
                legacy_source_file  TEXT NULL,
                legacy_row_number   INTEGER NULL,
                created_by          INTEGER NULL REFERENCES users(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                updated_by          INTEGER NULL REFERENCES users(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                created_at          TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                updated_at          TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            );

            CREATE INDEX IF NOT EXISTS ix_assets_item_type ON assets(item_type_id);
            CREATE INDEX IF NOT EXISTS ix_assets_room ON assets(room_id);
            CREATE INDEX IF NOT EXISTS ix_assets_status ON assets(status);
            CREATE INDEX IF NOT EXISTS ix_assets_legacy_code ON assets(legacy_code);

            CREATE TABLE IF NOT EXISTS identifiers (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id        INTEGER NOT NULL REFERENCES assets(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                identifier_type TEXT NOT NULL CHECK(identifier_type IN ('QR','BARCODE','RFID')),
                value           TEXT NOT NULL,
                is_active       INTEGER NOT NULL DEFAULT 1,
                active_value    TEXT GENERATED ALWAYS AS (CASE WHEN is_active = 1 THEN value ELSE NULL END) STORED,
                issued_at       TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                deactivated_at  TEXT NULL,
                created_at      TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                updated_at      TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                UNIQUE(identifier_type, active_value)
            );

            CREATE INDEX IF NOT EXISTS ix_identifiers_asset ON identifiers(asset_id);
            CREATE INDEX IF NOT EXISTS ix_identifiers_value ON identifiers(value);

            CREATE TABLE IF NOT EXISTS audit_entries (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                entity_type TEXT NOT NULL,
                entity_id   INTEGER NOT NULL,
                entity_code TEXT NULL,
                action      TEXT NOT NULL,
                field_name  TEXT NULL,
                old_value   TEXT NULL,
                new_value   TEXT NULL,
                note        TEXT NULL,
                user_id     INTEGER NOT NULL REFERENCES users(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                changed_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            );

            CREATE INDEX IF NOT EXISTS ix_audit_entity ON audit_entries(entity_type, entity_id, changed_at);
            CREATE INDEX IF NOT EXISTS ix_audit_changed_at ON audit_entries(changed_at);

            CREATE TABLE IF NOT EXISTS asset_code_sequences (
                prefix      TEXT PRIMARY KEY,
                next_number INTEGER NOT NULL DEFAULT 1,
                updated_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                setting_key   TEXT PRIMARY KEY,
                setting_value TEXT NOT NULL,
                description   TEXT NULL,
                updated_by    INTEGER NULL REFERENCES users(id) ON UPDATE CASCADE ON DELETE RESTRICT,
                updated_at    TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            );

            CREATE TRIGGER IF NOT EXISTS trg_audit_no_update
            BEFORE UPDATE ON audit_entries
            BEGIN
                SELECT RAISE(ABORT, 'Riwayat audit tidak boleh diubah.');
            END;

            CREATE TRIGGER IF NOT EXISTS trg_audit_no_delete
            BEFORE DELETE ON audit_entries
            BEGIN
                SELECT RAISE(ABORT, 'Riwayat audit tidak boleh dihapus.');
            END;

            CREATE TRIGGER IF NOT EXISTS trg_assets_code_immutable
            BEFORE UPDATE OF asset_code ON assets
            BEGIN
                SELECT RAISE(ABORT, 'Kode Aset bersifat permanen dan tidak boleh diubah.');
            END;

            CREATE VIEW IF NOT EXISTS v_asset_list AS
            SELECT
                a.id                AS Id,
                a.asset_code        AS AssetCode,
                it.name             AS ItemTypeName,
                it.type_description AS TypeDescription,
                c.name              AS CategoryName,
                b.name              AS BuildingName,
                r.name              AS RoomName,
                a.room_id           AS RoomId,
                b.id                AS BuildingId,
                cond.code           AS ConditionCode,
                cond.name           AS ConditionName,
                a.condition_id      AS ConditionId,
                a.status            AS Status,
                a.acquisition_year  AS AcquisitionYear,
                a.notes             AS Notes,
                a.legacy_code       AS LegacyCode,
                a.updated_at        AS UpdatedAt
            FROM assets a
            JOIN item_types it    ON it.id = a.item_type_id
            JOIN categories c     ON c.id  = it.category_id
            LEFT JOIN rooms r     ON r.id  = a.room_id
            LEFT JOIN buildings b ON b.id  = r.building_id
            LEFT JOIN conditions cond ON cond.id = a.condition_id;
        ", transaction: tx);

        conn.Execute(@"
            INSERT OR IGNORE INTO conditions (code, name, description, sort_order) VALUES
            ('A', 'Baik', 'Layak pakai, tidak ada kerusakan berarti', 1);

            INSERT OR IGNORE INTO app_settings (setting_key, setting_value, description) VALUES
            ('new_item_requires_approval', '0', 'Persetujuan Admin untuk Item Baru (1 = aktif, 0 = nonaktif)'),
            ('asset_code_format',          '{PREFIX}-{SEQ}', 'Format Kode Aset. Belum final.'),
            ('asset_code_seq_padding',     '4',              'Jumlah digit nomor urut Kode Aset');

        ", transaction: tx);

        tx.Commit();
    }
}

