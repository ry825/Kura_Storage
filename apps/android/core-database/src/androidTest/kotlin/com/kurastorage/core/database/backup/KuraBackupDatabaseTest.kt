package com.kurastorage.core.database.backup

import android.content.Context
import androidx.room.Room
import androidx.room.testing.MigrationTestHelper
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.SyncLifecycleState
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import java.io.IOException
import java.time.Duration
import java.time.Instant
import java.util.UUID
import kotlin.system.measureTimeMillis

@RunWith(AndroidJUnit4::class)
class KuraBackupDatabaseTest {
    private val context = ApplicationProvider.getApplicationContext<Context>()
    private var database: KuraBackupDatabase? = null

    @get:Rule
    val migrationHelper =
        MigrationTestHelper(
            InstrumentationRegistry.getInstrumentation(),
            KuraBackupDatabase::class.java,
        )

    @After
    fun closeDatabase() {
        database?.close()
        context.deleteDatabase(TEST_DATABASE)
    }

    @Test
    @Throws(IOException::class)
    fun exportedSchemaMigratesWithoutDestructiveFallback() {
        migrationHelper.createDatabase(TEST_DATABASE, 1).close()
        migrationHelper
            .runMigrationsAndValidate(TEST_DATABASE, 2, true, KuraBackupDatabase.MIGRATION_1_2)
            .close()
    }

    @Test
    fun migrationPreservesRulesAndAddsSourceIdentityMapping() {
        val ruleId = UUID.randomUUID().toString()
        migrationHelper.createDatabase(TEST_DATABASE, 1).apply {
            execSQL(
                "INSERT INTO backup_rules VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                arrayOf<Any?>(
                    ruleId,
                    "a".repeat(64),
                    "SAF_TREE",
                    "content://provider/tree/root",
                    "Photos",
                    UUID.randomUUID().toString(),
                    1,
                    "LOCAL_DIRECT_ONLY",
                    0,
                    20,
                    null,
                    null,
                    1,
                    1,
                ),
            )
            close()
        }
        migrationHelper
            .runMigrationsAndValidate(TEST_DATABASE, 2, true, KuraBackupDatabase.MIGRATION_1_2)
            .apply {
                execSQL(
                    "INSERT INTO source_identity_mappings VALUES (?, ?, ?, ?, ?, ?)",
                    arrayOf<Any?>(ruleId, "provider:key", "document:key", "opaque-key", 1, 1),
                )
                query("SELECT COUNT(*) FROM backup_rules").use { cursor ->
                    assertTrue(cursor.moveToFirst())
                    assertEquals(1, cursor.getInt(0))
                }
                close()
            }
    }

    @Test
    fun uniqueDocumentAndRemoteFileConstraintsPreventDuplicateQueueRows() =
        runBlocking {
            val db = openMemoryDatabase()
            val rule = rule()
            db.backupRuleDao().upsert(rule)
            val first = item(rule, documentKey = "opaque", remoteFileId = UUID.randomUUID().toString())
            db.localSyncItemDao().upsertAll(listOf(first))

            db.localSyncItemDao().upsertAll(listOf(first.copy(id = UUID.randomUUID().toString())))
            db.localSyncItemDao().upsertAll(
                listOf(
                    first.copy(
                        id = UUID.randomUUID().toString(),
                        localDocumentKey = "other",
                    ),
                ),
            )

            val stored = db.localSyncItemDao().observeByScope(rule.accountScopeId).first()
            assertEquals(1, stored.size)
            assertEquals(first.id, stored.single().id)
        }

    @Test
    fun claimIsBoundedAtomicAndExpiredUploadRequiresServerReconciliation() =
        runBlocking {
            val db = openMemoryDatabase()
            val rule = rule()
            db.backupRuleDao().upsert(rule)
            val plain = item(rule, documentKey = "plain", remoteFileId = null)
            val uploading =
                item(rule, documentKey = "uploading", remoteFileId = null).copy(
                    lifecycleState = "UPLOADING",
                    leaseOwner = "dead-worker",
                    leaseExpiresAt = 1,
                    uploadSessionId = UUID.randomUUID().toString(),
                )
            db.localSyncItemDao().upsertAll(listOf(plain, uploading))

            val claimed = db.localSyncItemDao().claim(rule.accountScopeId, "worker", 10, 20, 1)
            assertEquals(1, claimed.size)
            assertEquals("COMPARING", claimed.single().lifecycleState)
            assertTrue(db.localSyncItemDao().claim(rule.accountScopeId, "second", 10, 20, 1).isEmpty())

            assertEquals(1, db.localSyncItemDao().recoverExpiredLeases(10))
            val recovered = requireNotNull(db.localSyncItemDao().find(uploading.id, rule.accountScopeId))
            assertEquals("PENDING", recovered.lifecycleState)
            assertEquals("SERVER_RECONCILIATION", recovered.waitReason)
            assertNotNull(recovered.uploadSessionId)
        }

    @Test
    fun accountScopeIsolationAndRuleCascadeSurviveDatabaseReopen() =
        runBlocking {
            val firstScope = "a".repeat(64)
            val secondScope = "b".repeat(64)
            val firstRule = rule(firstScope)
            val secondRule = rule(secondScope)
            val db = openDiskDatabase()
            db.backupRuleDao().upsert(firstRule)
            db.backupRuleDao().upsert(secondRule)
            db.localSyncItemDao().upsertAll(listOf(item(firstRule, "first", null), item(secondRule, "second", null)))
            db.close()
            database = null

            val reopened = openDiskDatabase()
            assertEquals(
                1,
                reopened
                    .localSyncItemDao()
                    .observeByScope(firstScope)
                    .first()
                    .size,
            )
            assertEquals(
                1,
                reopened
                    .localSyncItemDao()
                    .observeByScope(secondScope)
                    .first()
                    .size,
            )
            reopened.backupRuleDao().delete(requireNotNull(reopened.backupRuleDao().find(firstRule.id, firstScope)))
            assertTrue(
                reopened
                    .localSyncItemDao()
                    .observeByScope(firstScope)
                    .first()
                    .isEmpty(),
            )
            assertEquals(
                1,
                reopened
                    .localSyncItemDao()
                    .observeByScope(secondScope)
                    .first()
                    .size,
            )
        }

    @Test
    fun transferSessionAndConfirmedOffsetSurviveDatabaseReopenAndExpiredLeaseRecovery() =
        runBlocking {
            val rule = rule()
            val db = openDiskDatabase()
            db.backupRuleDao().upsert(rule)
            db.localSyncItemDao().upsertAll(listOf(item(rule, "resume", null)))
            val persistence = BackupTransferPersistence(db)
            val claimed =
                persistence
                    .claim(
                        AccountScopeId(rule.accountScopeId),
                        "worker",
                        Instant.ofEpochMilli(10),
                        Duration.ofMinutes(30),
                        100,
                    ).single()
            persistence.store(
                claimed.copy(
                    lifecycleState = SyncLifecycleState.UPLOADING,
                    uploadSessionId = "session-1",
                    idempotencyKey = "operation-1",
                    confirmedOffset = 64,
                ),
            )
            db.close()
            database = null

            val reopened = openDiskDatabase()
            val resumed =
                requireNotNull(
                    BackupTransferPersistence(reopened).find(
                        AccountScopeId(rule.accountScopeId),
                        claimed.id,
                    ),
                )
            assertEquals("session-1", resumed.uploadSessionId)
            assertEquals("operation-1", resumed.idempotencyKey)
            assertEquals(64L, resumed.confirmedOffset)
            assertEquals(1, reopened.localSyncItemDao().recoverExpiredLeases(Duration.ofHours(1).toMillis() + 11))
            val recovered = requireNotNull(reopened.localSyncItemDao().find(claimed.id.value, rule.accountScopeId))
            assertEquals("PENDING", recovered.lifecycleState)
            assertEquals("SERVER_RECONCILIATION", recovered.waitReason)
            assertEquals("session-1", recovered.uploadSessionId)
            assertEquals(64L, recovered.confirmedOffset)
        }

    @Test
    fun cleanupAndProgressQueriesRetainActionableQueueRows() =
        runBlocking {
            val db = openMemoryDatabase()
            val rule = rule()
            db.backupRuleDao().upsert(rule)
            val oldCompleted =
                item(rule, "completed", UUID.randomUUID().toString()).copy(
                    lifecycleState = "COMPLETED",
                    completedAt = 1,
                )
            val oldFailed =
                item(rule, "failed", null).copy(
                    lifecycleState = "FAILED",
                    failureReason = "RETRY_EXHAUSTED",
                    lastAttemptAt = 1,
                )
            val pending = item(rule, "pending", null).copy(waitReason = "NO_ALLOWED_CONNECTION")
            val missing = item(rule, "missing", null).copy(lifecycleState = "LOCAL_MISSING")
            db.localSyncItemDao().upsertAll(listOf(oldCompleted, oldFailed, pending, missing))

            val stateCounts = db.localSyncItemDao().observeRuleStateCounts(rule.accountScopeId).first()
            assertEquals(4, stateCounts.sumOf { it.count })
            assertEquals(
                1,
                db
                    .localSyncItemDao()
                    .observeWaitReasonCounts(rule.accountScopeId)
                    .first()
                    .single()
                    .count,
            )
            assertEquals(1L, db.localSyncItemDao().observeLastCompletedAt(rule.accountScopeId).first())

            assertEquals(
                2,
                BackupTransferPersistence(db).cleanupHistory(
                    AccountScopeId(rule.accountScopeId),
                    Instant.parse("2026-09-03T00:00:00Z"),
                ),
            )
            val retained = db.localSyncItemDao().observeByScope(rule.accountScopeId).first()
            assertEquals(setOf("pending", "missing"), retained.map { it.localDocumentKey }.toSet())
        }

    @Test
    fun pauseAndManualRetryAreScopeBoundAndResetRetryState() =
        runBlocking {
            val db = openMemoryDatabase()
            val rule = rule()
            db.backupRuleDao().upsert(rule)
            val failed =
                item(rule, "failed", null).copy(
                    lifecycleState = "FAILED",
                    waitReason = "NETWORK",
                    failureReason = "RETRY_EXHAUSTED",
                    retryCount = 10,
                    nextAttemptAt = 99,
                )
            db.localSyncItemDao().upsertAll(listOf(failed))

            assertEquals(1, db.backupRuleDao().setPaused(rule.id, rule.accountScopeId, 10, 10))
            assertEquals(10L, db.backupRuleDao().find(rule.id, rule.accountScopeId)?.pausedAt)
            assertEquals(0, db.backupRuleDao().setPaused(rule.id, "b".repeat(64), null, 20))

            assertEquals(1, db.localSyncItemDao().retryFailed(failed.id, rule.accountScopeId))
            val retried = requireNotNull(db.localSyncItemDao().find(failed.id, rule.accountScopeId))
            assertEquals("PENDING", retried.lifecycleState)
            assertEquals("NONE", retried.waitReason)
            assertEquals("NONE", retried.failureReason)
            assertEquals(0, retried.retryCount)
            assertEquals(null, retried.nextAttemptAt)
            assertEquals(0, db.localSyncItemDao().retryFailed(failed.id, rule.accountScopeId))

            val secondFailed =
                item(rule, "second-failed", null).copy(
                    lifecycleState = "FAILED",
                    failureReason = "RETRY_EXHAUSTED",
                    retryCount = 10,
                    lastAttemptAt = 20,
                )
            val otherRule = rule("b".repeat(64))
            val otherFailed =
                item(otherRule, "other-failed", null).copy(
                    lifecycleState = "FAILED",
                    failureReason = "RETRY_EXHAUSTED",
                    retryCount = 10,
                    lastAttemptAt = 30,
                )
            db.backupRuleDao().upsert(otherRule)
            db.localSyncItemDao().upsertAll(listOf(secondFailed, otherFailed))

            assertEquals(
                "second-failed",
                db
                    .localSyncItemDao()
                    .observeHistory(rule.accountScopeId, 1)
                    .first()
                    .single()
                    .localDocumentKey,
            )
            assertEquals(1, db.localSyncItemDao().retryAllFailed(rule.accountScopeId))
            assertEquals("PENDING", db.localSyncItemDao().find(secondFailed.id, rule.accountScopeId)?.lifecycleState)
            assertEquals("FAILED", db.localSyncItemDao().find(otherFailed.id, otherRule.accountScopeId)?.lifecycleState)
        }

    @Test
    fun completedFullScanMarksOnlyUnobservedItemsMissingWithoutDeletingRemoteReferences() =
        runBlocking {
            val db = openMemoryDatabase()
            val ruleEntity = rule()
            db.backupRuleDao().upsert(ruleEntity)
            val model = ruleEntity.toModel()
            val persistence = BackupScanPersistence(db)
            val first = candidate("provider:first", "opaque-first")
            val second = candidate("provider:second", "opaque-second")
            persistence.applyBatch(model, listOf(first, second), Instant.ofEpochMilli(10))
            persistence.complete(model, null, null, Instant.ofEpochMilli(10), true)
            val secondEntity =
                requireNotNull(db.localSyncItemDao().findByDocumentKey(model.id.value, model.accountScopeId.value, "opaque-second"))
            db.localSyncItemDao().upsertAll(
                listOf(secondEntity.copy(remoteFileId = UUID.randomUUID().toString(), remoteFileVersion = 2)),
            )

            persistence.applyBatch(model, listOf(first), Instant.ofEpochMilli(20))
            persistence.complete(model, null, null, Instant.ofEpochMilli(20), true)

            val missing =
                requireNotNull(db.localSyncItemDao().findByDocumentKey(model.id.value, model.accountScopeId.value, "opaque-second"))
            assertEquals("LOCAL_MISSING", missing.lifecycleState)
            assertNotNull(missing.remoteFileId)
            assertEquals(2L, missing.remoteFileVersion)

            persistence.applyBatch(model, listOf(second), Instant.ofEpochMilli(30))

            val reappeared =
                requireNotNull(db.localSyncItemDao().findByDocumentKey(model.id.value, model.accountScopeId.value, "opaque-second"))
            assertEquals(missing.id, reappeared.id)
            assertEquals("PENDING", reappeared.lifecycleState)
            assertEquals(missing.remoteFileId, reappeared.remoteFileId)
            assertEquals(2L, reappeared.remoteFileVersion)
        }

    @Test
    fun sourceIdentityMappingsAreNamespacedByRule() =
        runBlocking {
            val db = openMemoryDatabase()
            val firstRule = rule()
            val secondRule = rule()
            db.backupRuleDao().upsert(firstRule)
            db.backupRuleDao().upsert(secondRule)
            val persistence = BackupScanPersistence(db)

            persistence.applyBatch(firstRule.toModel(), listOf(candidate("provider:same", "opaque-same")), Instant.ofEpochMilli(10))
            persistence.applyBatch(secondRule.toModel(), listOf(candidate("provider:same", "opaque-same")), Instant.ofEpochMilli(10))

            assertNotNull(db.sourceIdentityMappingDao().find(firstRule.id, "provider:same"))
            assertNotNull(db.sourceIdentityMappingDao().find(secondRule.id, "provider:same"))
            assertEquals(
                1,
                db
                    .localSyncItemDao()
                    .observeByScope(firstRule.accountScopeId)
                    .first()
                    .count { it.ruleId == firstRule.id },
            )
            assertEquals(
                1,
                db
                    .localSyncItemDao()
                    .observeByScope(secondRule.accountScopeId)
                    .first()
                    .count { it.ruleId == secondRule.id },
            )
        }

    @Test
    fun tenThousandItemQueueUsesBoundedWritesAndMeasuredDiskSpace() =
        runBlocking {
            val db = openDiskDatabase()
            val rule = rule()
            db.backupRuleDao().upsert(rule)
            val elapsedMillis =
                measureTimeMillis {
                    (0 until PERFORMANCE_ITEM_COUNT)
                        .asSequence()
                        .map { index -> item(rule, "anonymous-$index", null) }
                        .chunked(PERFORMANCE_BATCH_SIZE)
                        .forEach { batch -> db.localSyncItemDao().upsertAll(batch) }
                }
            assertEquals(
                PERFORMANCE_ITEM_COUNT,
                db
                    .localSyncItemDao()
                    .observeByScope(rule.accountScopeId)
                    .first()
                    .size,
            )
            db.close()
            database = null
            val databaseBytes = context.getDatabasePath(TEST_DATABASE).length()
            assertTrue(databaseBytes in 1..MAXIMUM_PERFORMANCE_DATABASE_BYTES)
            println(
                "room-performance items=$PERFORMANCE_ITEM_COUNT batches=" +
                    "${PERFORMANCE_ITEM_COUNT / PERFORMANCE_BATCH_SIZE} elapsedMs=$elapsedMillis bytes=$databaseBytes",
            )
        }

    private fun openMemoryDatabase(): KuraBackupDatabase =
        Room
            .inMemoryDatabaseBuilder(context, KuraBackupDatabase::class.java)
            .allowMainThreadQueries()
            .build()
            .also { database = it }

    private fun openDiskDatabase(): KuraBackupDatabase =
        Room
            .databaseBuilder(context, KuraBackupDatabase::class.java, TEST_DATABASE)
            .allowMainThreadQueries()
            .build()
            .also { database = it }

    private fun rule(scope: String = "a".repeat(64)) =
        BackupRuleEntity(
            id = UUID.randomUUID().toString(),
            accountScopeId = scope,
            sourceType = "SAF_TREE",
            sourceLocator = "content://documents/tree/photos",
            displayName = "Photos",
            remoteFolderId = UUID.randomUUID().toString(),
            enabled = true,
            networkMode = "LOCAL_DIRECT_ONLY",
            requiresChargingForInitialRun = true,
            minimumBatteryPercent = 20,
            initialRunCompletedAt = null,
            pausedAt = null,
            createdAt = 1,
            updatedAt = 1,
        )

    private fun BackupRuleEntity.toModel() =
        LocalBackupRule(
            id = BackupRuleId(id),
            accountScopeId = AccountScopeId(accountScopeId),
            sourceType = BackupSourceType.valueOf(sourceType),
            sourceLocator = sourceLocator,
            displayName = displayName,
            remoteFolderId = remoteFolderId,
            enabled = enabled,
            networkMode = BackupNetworkMode.valueOf(networkMode),
            requiresChargingForInitialRun = requiresChargingForInitialRun,
            minimumBatteryPercent = minimumBatteryPercent,
            initialRunCompletedAt = null,
            pausedAt = null,
            createdAt = Instant.ofEpochMilli(createdAt),
            updatedAt = Instant.ofEpochMilli(updatedAt),
        )

    private fun candidate(
        providerKey: String,
        localDocumentKey: String,
    ) = ScanPersistenceCandidate(
        providerKey = providerKey,
        identityDiscriminator = providerKey,
        localDocumentKey = localDocumentKey,
        sourceLocator = "content://provider/$providerKey",
        relativePath = "$providerKey.jpg",
        displayName = "$providerKey.jpg",
        size = 100,
        modifiedAtMillis = 1,
        checksum = "a".repeat(64),
        sourceFingerprint = "b".repeat(64),
    )

    private fun item(
        rule: BackupRuleEntity,
        documentKey: String,
        remoteFileId: String?,
    ) = LocalSyncItemEntity(
        id = UUID.randomUUID().toString(),
        accountScopeId = rule.accountScopeId,
        ruleId = rule.id,
        localDocumentKey = documentKey,
        sourceLocator = "content://documents/item/$documentKey",
        relativePath = "$documentKey.jpg",
        displayName = "$documentKey.jpg",
        size = 100,
        modifiedAt = 1,
        checksum = null,
        sourceFingerprint = "fingerprint",
        remoteFileId = remoteFileId,
        remoteFileVersion = remoteFileId?.let { 1 },
        lifecycleState = "PENDING",
        waitReason = "NONE",
        failureReason = "NONE",
        retryCount = 0,
        nextAttemptAt = null,
        leaseOwner = null,
        leaseExpiresAt = null,
        uploadSessionId = null,
        idempotencyKey = null,
        confirmedOffset = 0,
        firstSeenAt = 1,
        lastSeenAt = 1,
        lastAttemptAt = null,
        completedAt = null,
    )

    private companion object {
        const val TEST_DATABASE = "backup-migration-test.db"
        const val PERFORMANCE_ITEM_COUNT = 10_000
        const val PERFORMANCE_BATCH_SIZE = 500
        const val MAXIMUM_PERFORMANCE_DATABASE_BYTES = 16L * 1_024 * 1_024
    }
}
