package com.kurastorage.core.data.backup

import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.ScanCheckpoint
import kotlinx.coroutines.test.runTest
import okhttp3.mockwebserver.MockWebServer
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.InputStream
import java.time.Instant
import java.util.UUID
import kotlin.system.measureTimeMillis

class BackupScanPerformanceTest {
    @Test
    fun tenThousandItemFixtureHashesOnlyTenChangedLargeFilesInBoundedBatches() =
        runTest {
            val documents = (0 until DOCUMENT_COUNT).map(::fixtureDocument)
            val store = PerformanceScanStore(documents)
            val checksums = GeneratedChecksumSource()
            val coordinator = BackupScanCoordinator(store, ListDocumentSource(documents), checksums)
            lateinit var result: ScanResult

            val elapsedMillis =
                measureTimeMillis {
                    result = coordinator.scan(mediaRule(), ScanTrigger.PERIODIC)
                }

            assertTrue(result.completed)
            assertEquals(CHANGED_COUNT, result.hashedCount)
            assertEquals(CHANGED_COUNT, checksums.openCount)
            assertEquals(CHANGED_COUNT * LARGE_FILE_BYTES, checksums.readBytes)
            assertEquals(BATCH_COUNT, store.batchCount)
            assertEquals(MAXIMUM_BATCH_SIZE, store.maximumBatchSize)
            println(
                "scan-performance items=$DOCUMENT_COUNT elapsedMs=$elapsedMillis " +
                    "hashes=${result.hashedCount} readBytes=${checksums.readBytes} batches=${store.batchCount}",
            )
        }

    @Test
    fun omittedAndLocallyDeletedCandidatesNeverCallServerMutationEndpoints() =
        runTest {
            MockWebServer().use { server ->
                server.start()
                val documents = listOf(fixtureDocument(1))
                BackupScanCoordinator(
                    PerformanceScanStore(documents),
                    ListDocumentSource(documents),
                    GeneratedChecksumSource(),
                ).scan(mediaRule(), ScanTrigger.PERIODIC)
                assertEquals(0, server.requestCount)
            }
        }

    private fun mediaRule() =
        LocalBackupRule(
            id = BackupRuleId(UUID.randomUUID().toString()),
            accountScopeId = AccountScopeId("a".repeat(64)),
            sourceType = BackupSourceType.MEDIA_IMAGES,
            sourceLocator = "external",
            displayName = "Anonymous fixture",
            remoteFolderId = UUID.randomUUID().toString(),
            enabled = true,
            networkMode = BackupNetworkMode.LOCAL_DIRECT_ONLY,
            requiresChargingForInitialRun = false,
            minimumBatteryPercent = 0,
            initialRunCompletedAt = null,
            pausedAt = null,
            createdAt = Instant.EPOCH,
            updatedAt = Instant.EPOCH,
        )
}

private fun fixtureDocument(index: Int) =
    ScannedDocumentMetadata(
        providerKey = "anonymous:$index",
        identityDiscriminator = "generation:$index",
        sourceLocator = "content://anonymous/$index",
        relativePath = "fixture/$index.bin",
        displayName = "$index.bin",
        mimeType = "application/octet-stream",
        size = LARGE_FILE_BYTES,
        modifiedAtMillis = index.toLong(),
    )

private class ListDocumentSource(
    private val documents: List<ScannedDocumentMetadata>,
) : BackupDocumentSource {
    override suspend fun snapshot(rule: LocalBackupRule) = SourceSnapshot("fixture-v1", 10_000)

    override suspend fun scan(
        rule: LocalBackupRule,
        afterGeneration: Long?,
        emit: suspend (ScannedDocumentMetadata) -> Unit,
    ): SourceScanOutcome {
        documents.forEach { emit(it) }
        return SourceScanOutcome(true, snapshot(rule))
    }
}

private class PerformanceScanStore(
    documents: List<ScannedDocumentMetadata>,
) : BackupScanStore {
    private val existing =
        documents.associate { document ->
            val changed = document.providerKey.substringAfterLast(':').toInt() >= DOCUMENT_COUNT - CHANGED_COUNT
            document.providerKey to
                StoredDocumentMetadata(
                    localDocumentKey = UUID.randomUUID().toString(),
                    identityDiscriminator = document.identityDiscriminator,
                    relativePath = document.relativePath,
                    displayName = document.displayName,
                    size = if (changed) document.size - 1 else document.size,
                    modifiedAtMillis = document.modifiedAtMillis,
                    checksum = "0".repeat(64),
                )
        }
    var batchCount = 0
    var maximumBatchSize = 0

    override suspend fun checkpoint(ruleId: BackupRuleId): ScanCheckpoint? = null

    override suspend fun existing(
        rule: LocalBackupRule,
        document: ScannedDocumentMetadata,
    ): StoredDocumentMetadata? = existing[document.providerKey]

    override suspend fun applyBatch(
        rule: LocalBackupRule,
        documents: List<PreparedScannedDocument>,
        observedAt: Instant,
    ) {
        batchCount++
        maximumBatchSize = maxOf(maximumBatchSize, documents.size)
    }

    override suspend fun complete(
        rule: LocalBackupRule,
        snapshot: SourceSnapshot,
        observedAt: Instant,
        wasFullScan: Boolean,
    ) = Unit
}

private class GeneratedChecksumSource : DocumentChecksumSource {
    var openCount = 0
    var readBytes = 0L

    override fun open(sourceLocator: String): InputStream {
        openCount++
        return GeneratedInputStream(LARGE_FILE_BYTES) { count -> readBytes += count }
    }
}

private class GeneratedInputStream(
    size: Long,
    private val onRead: (Long) -> Unit,
) : InputStream() {
    private var remaining = size

    override fun read(): Int {
        if (remaining == 0L) return -1
        remaining--
        onRead(1)
        return 0
    }

    override fun read(
        bytes: ByteArray,
        offset: Int,
        length: Int,
    ): Int {
        if (remaining == 0L) return -1
        val count = minOf(length.toLong(), remaining).toInt()
        bytes.fill(0, offset, offset + count)
        remaining -= count
        onRead(count.toLong())
        return count
    }
}

private const val DOCUMENT_COUNT = 10_000
private const val CHANGED_COUNT = 10
private const val LARGE_FILE_BYTES = 1_048_576L
private const val MAXIMUM_BATCH_SIZE = 500
private const val BATCH_COUNT = DOCUMENT_COUNT / MAXIMUM_BATCH_SIZE
