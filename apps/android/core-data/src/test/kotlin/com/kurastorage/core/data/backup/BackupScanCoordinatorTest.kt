package com.kurastorage.core.data.backup

import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.ScanCheckpoint
import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.FileNotFoundException
import java.time.Instant
import java.util.UUID

class BackupScanCoordinatorTest {
    @Test
    fun unchangedMetadataSkipsChecksumAndChangedDocumentsAreBatched() =
        runTest {
            val documents = (0 until 1_001).map(::document)
            val store = FakeScanStore(documents.associate { it.providerKey to it.toStored() }.toMutableMap())
            store.stored[documents.last().providerKey] = documents.last().toStored().copy(size = 1)
            val checksums = CountingChecksumSource()
            val coordinator = BackupScanCoordinator(store, FakeDocumentSource(documents), checksums)

            val result = coordinator.scan(rule(BackupSourceType.SAF_TREE), ScanTrigger.MANUAL)

            assertTrue(result.completed)
            assertEquals(1, checksums.openCount)
            assertEquals(listOf(500, 500, 1), store.batchSizes)
            assertEquals(1, store.completedScans)
        }

    @Test
    fun generationRollbackForcesFullScanAndOnlyCompletedFullScanMarksMissing() =
        runTest {
            val store = FakeScanStore()
            store.checkpoint = checkpoint(version = "v1", generation = 20)
            val source = FakeDocumentSource(listOf(document(1)), SourceSnapshot("v1", 10))
            val coordinator = BackupScanCoordinator(store, source, CountingChecksumSource())

            coordinator.scan(rule(BackupSourceType.MEDIA_IMAGES), ScanTrigger.APP_START)

            assertEquals(null, source.requestedAfterGeneration)
            assertTrue(store.lastCompletionWasFull)
            assertEquals(10L, store.checkpoint?.generation)
        }

    @Test
    fun interruptedScanDoesNotAdvanceCheckpointOrMarkMissing() =
        runTest {
            val initial = checkpoint(version = "v1", generation = 5)
            val store = FakeScanStore().also { it.checkpoint = initial }
            val source = FakeDocumentSource(listOf(document(1)), SourceSnapshot("v1", 6), completed = false)

            val result =
                BackupScanCoordinator(store, source, CountingChecksumSource())
                    .scan(rule(BackupSourceType.MEDIA_IMAGES), ScanTrigger.CONTENT_CHANGED)

            assertFalse(result.completed)
            assertEquals(initial, store.checkpoint)
            assertEquals(0, store.completedScans)
        }

    @Test
    fun duplicateProviderRowsConvergeBeforeHashAndQueueWrite() =
        runTest {
            val duplicate = document(1)
            val store = FakeScanStore()
            val checksums = CountingChecksumSource()

            BackupScanCoordinator(store, FakeDocumentSource(listOf(duplicate, duplicate)), checksums)
                .scan(rule(BackupSourceType.SAF_TREE), ScanTrigger.CONTENT_CHANGED)

            assertEquals(1, checksums.openCount)
            assertEquals(listOf(1), store.batchSizes)
            assertEquals(1, store.stored.size)
        }

    @Test
    fun periodicMediaScanIsFullSoMissingItemsCanBeRecovered() =
        runTest {
            val store = FakeScanStore().also { it.checkpoint = checkpoint("v1", 5) }
            val source = FakeDocumentSource(emptyList(), SourceSnapshot("v1", 6))

            BackupScanCoordinator(store, source, CountingChecksumSource())
                .scan(rule(BackupSourceType.MEDIA_IMAGES), ScanTrigger.PERIODIC)

            assertEquals(null, source.requestedAfterGeneration)
            assertTrue(store.lastCompletionWasFull)
        }

    @Test
    fun concurrentTriggersForSameRuleShareOneScan() =
        runTest {
            val store = FakeScanStore()
            val source = FakeDocumentSource(listOf(document(1)), delayMillis = 100)
            val coordinator = BackupScanCoordinator(store, source, CountingChecksumSource())
            val rule = rule(BackupSourceType.SAF_TREE)

            val first = async { coordinator.scan(rule, ScanTrigger.APP_START) }
            val second = async { coordinator.scan(rule, ScanTrigger.MANUAL) }

            first.await()
            second.await()
            assertEquals(1, source.scanCount)
            assertEquals(1, source.maximumConcurrentScans)
        }

    @Test
    fun allScannerEntryPointsConvergeOnTypedTriggers() =
        runTest {
            val observed = mutableListOf<ScanTrigger>()
            val dispatcher =
                BackupScanTriggerDispatcher { _, trigger ->
                    observed += trigger
                    ScanResult(true, 0, 0, trigger)
                }
            val rule = rule(BackupSourceType.SAF_TREE)

            dispatcher.appStarted(rule)
            dispatcher.mediaStoreChanged(rule)
            dispatcher.pendingAdded(rule)
            dispatcher.allowedConnectionReached(rule)
            dispatcher.periodicCheck(rule)
            dispatcher.manualRequested(rule)

            assertEquals(6L, SAF_PERIODIC_SCAN_INTERVAL_HOURS)
            assertEquals(ScanTrigger.entries, observed)
        }

    @Test
    fun invalidRelativePathIsRejectedBeforePersistence() =
        runTest {
            val store = FakeScanStore()
            assertThrows(IllegalArgumentException::class.java) {
                document(1).copy(relativePath = "../secret.jpg")
            }
            assertTrue(store.batchSizes.isEmpty())
        }

    @Test
    fun disabledRuleIsRejectedBeforeReadingSource() =
        runTest {
            val source = FakeDocumentSource(listOf(document(1)))

            assertSuspendThrows<IllegalArgumentException> {
                BackupScanCoordinator(FakeScanStore(), source, CountingChecksumSource())
                    .scan(rule(BackupSourceType.SAF_TREE).copy(enabled = false), ScanTrigger.APP_START)
            }

            assertEquals(0, source.scanCount)
        }

    @Test
    fun documentDisappearingDuringChecksumDoesNotAdvanceCheckpoint() =
        runTest {
            val initial = checkpoint(version = "v1", generation = 5)
            val store = FakeScanStore().also { it.checkpoint = initial }
            val checksumSource = DocumentChecksumSource { throw FileNotFoundException("document disappeared") }

            assertSuspendThrows<FileNotFoundException> {
                BackupScanCoordinator(
                    store,
                    FakeDocumentSource(listOf(document(1)), SourceSnapshot("v1", 6)),
                    checksumSource,
                ).scan(rule(BackupSourceType.MEDIA_IMAGES), ScanTrigger.CONTENT_CHANGED)
            }

            assertEquals(initial, store.checkpoint)
            assertEquals(0, store.completedScans)
        }

    private fun rule(sourceType: BackupSourceType) =
        LocalBackupRule(
            id = BackupRuleId(UUID.randomUUID().toString()),
            accountScopeId = AccountScopeId("a".repeat(64)),
            sourceType = sourceType,
            sourceLocator = if (sourceType == BackupSourceType.SAF_TREE) "content://provider/tree/root" else "external",
            displayName = "Camera",
            remoteFolderId = UUID.randomUUID().toString(),
            enabled = true,
            networkMode = BackupNetworkMode.LOCAL_DIRECT_ONLY,
            requiresChargingForInitialRun = false,
            minimumBatteryPercent = 20,
            initialRunCompletedAt = null,
            pausedAt = null,
            createdAt = Instant.EPOCH,
            updatedAt = Instant.EPOCH,
        )

    private fun checkpoint(
        version: String,
        generation: Long,
    ) = ScanCheckpoint(
        ruleId = BackupRuleId(UUID.randomUUID().toString()),
        mediaStoreVersion = version,
        generation = generation,
        fullScanToken = null,
        lastCompletedAt = Instant.EPOCH,
        updatedAt = Instant.EPOCH,
    )
}

private fun document(index: Int) =
    ScannedDocumentMetadata(
        providerKey = "provider:$index",
        identityDiscriminator = "created:$index",
        sourceLocator = "content://provider/document/$index",
        relativePath = "folder/$index.jpg",
        displayName = "$index.jpg",
        mimeType = "image/jpeg",
        size = 100L + index,
        modifiedAtMillis = index.toLong(),
    )

private fun ScannedDocumentMetadata.toStored() =
    StoredDocumentMetadata(
        localDocumentKey = UUID.randomUUID().toString(),
        identityDiscriminator = identityDiscriminator,
        relativePath = relativePath,
        displayName = displayName,
        size = size,
        modifiedAtMillis = modifiedAtMillis,
        checksum = "0".repeat(64),
    )

private class FakeDocumentSource(
    private val documents: List<ScannedDocumentMetadata>,
    private val currentSnapshot: SourceSnapshot = SourceSnapshot(null, null),
    private val completed: Boolean = true,
    private val delayMillis: Long = 0,
) : BackupDocumentSource {
    var requestedAfterGeneration: Long? = null
    var scanCount = 0
    var concurrentScans = 0
    var maximumConcurrentScans = 0

    override suspend fun snapshot(rule: LocalBackupRule): SourceSnapshot = currentSnapshot

    override suspend fun scan(
        rule: LocalBackupRule,
        afterGeneration: Long?,
        emit: suspend (ScannedDocumentMetadata) -> Unit,
    ): SourceScanOutcome {
        requestedAfterGeneration = afterGeneration
        scanCount++
        concurrentScans++
        maximumConcurrentScans = maxOf(maximumConcurrentScans, concurrentScans)
        if (delayMillis > 0) delay(delayMillis)
        documents.forEach { emit(it) }
        concurrentScans--
        return SourceScanOutcome(completed, currentSnapshot)
    }
}

private class CountingChecksumSource : DocumentChecksumSource {
    var openCount = 0

    override fun open(sourceLocator: String): ByteArrayInputStream {
        openCount++
        return ByteArrayInputStream(sourceLocator.encodeToByteArray())
    }
}

private class FakeScanStore(
    val stored: MutableMap<String, StoredDocumentMetadata> = mutableMapOf(),
) : BackupScanStore {
    var checkpoint: ScanCheckpoint? = null
    val batchSizes = mutableListOf<Int>()
    var completedScans = 0
    var lastCompletionWasFull = false

    override suspend fun checkpoint(ruleId: BackupRuleId): ScanCheckpoint? = checkpoint

    override suspend fun existing(
        rule: LocalBackupRule,
        document: ScannedDocumentMetadata,
    ): StoredDocumentMetadata? = stored[document.providerKey]

    override suspend fun applyBatch(
        rule: LocalBackupRule,
        documents: List<PreparedScannedDocument>,
        observedAt: Instant,
    ) {
        batchSizes += documents.size
        documents.forEach { prepared ->
            stored[prepared.metadata.providerKey] =
                prepared.metadata.toStored().copy(
                    localDocumentKey = prepared.localDocumentKey,
                    checksum = prepared.checksum,
                )
        }
    }

    override suspend fun complete(
        rule: LocalBackupRule,
        snapshot: SourceSnapshot,
        observedAt: Instant,
        wasFullScan: Boolean,
    ) {
        completedScans++
        lastCompletionWasFull = wasFullScan
        checkpoint =
            ScanCheckpoint(rule.id, snapshot.version, snapshot.generation, null, observedAt, observedAt)
    }
}

private suspend inline fun <reified T : Throwable> assertSuspendThrows(noinline block: suspend () -> Unit) {
    try {
        block()
    } catch (error: Throwable) {
        if (error is T) return
        throw AssertionError("Expected ${T::class.java.name}, but caught ${error::class.java.name}", error)
    }
    throw AssertionError("Expected ${T::class.java.name} to be thrown")
}
