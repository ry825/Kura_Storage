package com.kurastorage.core.database.backup

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase

@Database(
    entities = [
        BackupRuleEntity::class,
        LocalSyncItemEntity::class,
        ExternalWifiPolicyEntity::class,
        ScanCheckpointEntity::class,
    ],
    version = 1,
    exportSchema = true,
)
abstract class KuraBackupDatabase : RoomDatabase() {
    abstract fun backupRuleDao(): BackupRuleDao

    abstract fun localSyncItemDao(): LocalSyncItemDao

    abstract fun externalWifiPolicyDao(): ExternalWifiPolicyDao

    abstract fun scanCheckpointDao(): ScanCheckpointDao

    companion object {
        const val DATABASE_NAME = "kurastorage_backup.db"

        fun create(context: Context): KuraBackupDatabase =
            Room
                .databaseBuilder(context.applicationContext, KuraBackupDatabase::class.java, DATABASE_NAME)
                .build()
    }
}
