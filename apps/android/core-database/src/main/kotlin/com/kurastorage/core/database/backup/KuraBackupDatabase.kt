package com.kurastorage.core.database.backup

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.room.withTransaction
import androidx.sqlite.db.SupportSQLiteDatabase

@Database(
    entities = [
        BackupRuleEntity::class,
        LocalSyncItemEntity::class,
        ExternalWifiPolicyEntity::class,
        ScanCheckpointEntity::class,
        SourceIdentityMappingEntity::class,
    ],
    version = 2,
    exportSchema = true,
)
abstract class KuraBackupDatabase :
    RoomDatabase(),
    BackupDatabaseAccess {
    abstract override fun backupRuleDao(): BackupRuleDao

    abstract override fun localSyncItemDao(): LocalSyncItemDao

    abstract override fun externalWifiPolicyDao(): ExternalWifiPolicyDao

    abstract override fun scanCheckpointDao(): ScanCheckpointDao

    abstract override fun sourceIdentityMappingDao(): SourceIdentityMappingDao

    override suspend fun <R> inTransaction(block: suspend () -> R): R = withTransaction(block)

    companion object {
        const val DATABASE_NAME = "kurastorage_backup.db"

        val MIGRATION_1_2 =
            object : Migration(1, 2) {
                override fun migrate(db: SupportSQLiteDatabase) {
                    db.execSQL(
                        """
                        CREATE TABLE IF NOT EXISTS `source_identity_mappings` (
                            `rule_id` TEXT NOT NULL,
                            `provider_key` TEXT NOT NULL,
                            `identity_discriminator` TEXT NOT NULL,
                            `local_document_key` TEXT NOT NULL,
                            `first_seen_at` INTEGER NOT NULL,
                            `last_seen_at` INTEGER NOT NULL,
                            PRIMARY KEY(`rule_id`, `provider_key`),
                            FOREIGN KEY(`rule_id`) REFERENCES `backup_rules`(`id`) ON UPDATE NO ACTION ON DELETE CASCADE
                        )
                        """.trimIndent(),
                    )
                    db.execSQL(
                        "CREATE INDEX IF NOT EXISTS `index_source_identity_mappings_rule_id` " +
                            "ON `source_identity_mappings` (`rule_id`)",
                    )
                    db.execSQL(
                        "CREATE UNIQUE INDEX IF NOT EXISTS " +
                            "`index_source_identity_mappings_rule_id_local_document_key` " +
                            "ON `source_identity_mappings` (`rule_id`, `local_document_key`)",
                    )
                }
            }
    }
}

fun createBackupDatabase(context: Context): BackupDatabaseAccess =
    Room
        .databaseBuilder(
            context.applicationContext,
            KuraBackupDatabase::class.java,
            KuraBackupDatabase.DATABASE_NAME,
        ).addMigrations(KuraBackupDatabase.MIGRATION_1_2)
        .build()

interface BackupDatabaseAccess {
    fun backupRuleDao(): BackupRuleDao

    fun localSyncItemDao(): LocalSyncItemDao

    fun externalWifiPolicyDao(): ExternalWifiPolicyDao

    fun scanCheckpointDao(): ScanCheckpointDao

    fun sourceIdentityMappingDao(): SourceIdentityMappingDao

    suspend fun <R> inTransaction(block: suspend () -> R): R
}
