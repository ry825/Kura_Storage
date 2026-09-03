package com.kurastorage.core.data.backup

import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.ScanCheckpoint
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.InputStream
import java.security.MessageDigest
import java.time.Clock
import java.time.Instant
import java.util.UUID

private const val SCAN_BATCH_SIZE = 500
private const val SHA_BUFFER_SIZE = 64 * 1024
private const val UNSIGNED_BYTE_MASK = 0xff
private const val MAX_RELATIVE_PATH_LENGTH = 1_024
private const val MAX_DISPLAY_NAME_LENGTH = 255
private const val MAX_MIME_TYPE_LENGTH = 255
const val SAF_PERIODIC_SCAN_INTERVAL_HOURS = 6L

enum class ScanTrigger {
    APP_START,
    CONTENT_CHANGED,
    PENDING_ADDED,
    ALLOWED_CONNECTION,
    PERIODIC,
    MANUAL,
}

data class SourceSnapshot(
    val version: String?,
    val generation: Long?,
)

data class SourceScanOutcome(
    val completed: Boolean,
    val snapshot: SourceSnapshot,
)

data class ScannedDocumentMetadata(
    val providerKey: String,
    val identityDiscriminator: String,
    val sourceLocator: String,
    val relativePath: String,
    val displayName: String,
    val mimeType: String,
    val size: Long,
    val modifiedAtMillis: Long,
) {
    init {
        require(providerKey.isNotBlank() && identityDiscriminator.isNotBlank())
        require(sourceLocator.startsWith("content://"))
        require(displayName.isNotBlank() && displayName.length <= MAX_DISPLAY_NAME_LENGTH)
        require(mimeType.isNotBlank() && mimeType.length <= MAX_MIME_TYPE_LENGTH)
        require(size >= 0 && modifiedAtMillis >= 0)
        require(relativePath == ScannerPathPolicy.normalize(relativePath.split('/')))
    }
}

data class StoredDocumentMetadata(
    val localDocumentKey: String,
    val identityDiscriminator: String,
    val relativePath: String,
    val displayName: String,
    val size: Long,
    val modifiedAtMillis: Long,
    val checksum: String?,
) {
    fun matches(document: ScannedDocumentMetadata): Boolean =
        identityDiscriminator == document.identityDiscriminator &&
            relativePath == document.relativePath &&
            displayName == document.displayName &&
            size == document.size &&
            modifiedAtMillis == document.modifiedAtMillis
}

data class PreparedScannedDocument(
    val metadata: ScannedDocumentMetadata,
    val localDocumentKey: String,
    val checksum: String,
)

data class ScanResult(
    val completed: Boolean,
    val observedCount: Int,
    val hashedCount: Int,
    val trigger: ScanTrigger,
)

interface BackupDocumentSource {
    suspend fun snapshot(rule: LocalBackupRule): SourceSnapshot

    suspend fun scan(
        rule: LocalBackupRule,
        afterGeneration: Long?,
        emit: suspend (ScannedDocumentMetadata) -> Unit,
    ): SourceScanOutcome
}

fun interface DocumentChecksumSource {
    fun open(sourceLocator: String): InputStream
}

interface BackupScanStore {
    suspend fun checkpoint(ruleId: BackupRuleId): ScanCheckpoint?

    suspend fun existing(
        rule: LocalBackupRule,
        document: ScannedDocumentMetadata,
    ): StoredDocumentMetadata?

    suspend fun applyBatch(
        rule: LocalBackupRule,
        documents: List<PreparedScannedDocument>,
        observedAt: Instant,
    )

    suspend fun complete(
        rule: LocalBackupRule,
        snapshot: SourceSnapshot,
        observedAt: Instant,
        wasFullScan: Boolean,
    )
}

fun interface BackupScanRequestHandler {
    suspend fun scan(
        rule: LocalBackupRule,
        trigger: ScanTrigger,
    ): ScanResult
}

class BackupScanTriggerDispatcher(
    private val handler: BackupScanRequestHandler,
) {
    suspend fun appStarted(rule: LocalBackupRule) = handler.scan(rule, ScanTrigger.APP_START)

    suspend fun mediaStoreChanged(rule: LocalBackupRule) = handler.scan(rule, ScanTrigger.CONTENT_CHANGED)

    suspend fun pendingAdded(rule: LocalBackupRule) = handler.scan(rule, ScanTrigger.PENDING_ADDED)

    suspend fun allowedConnectionReached(rule: LocalBackupRule) = handler.scan(rule, ScanTrigger.ALLOWED_CONNECTION)

    suspend fun periodicCheck(rule: LocalBackupRule) = handler.scan(rule, ScanTrigger.PERIODIC)

    suspend fun manualRequested(rule: LocalBackupRule) = handler.scan(rule, ScanTrigger.MANUAL)
}

class BackupScanCoordinator(
    private val store: BackupScanStore,
    private val source: BackupDocumentSource,
    private val checksums: DocumentChecksumSource,
    private val clock: Clock = Clock.systemUTC(),
    private val keyFactory: () -> String = { UUID.randomUUID().toString() },
) : BackupScanRequestHandler {
    private val activeScansLock = Mutex()
    private val activeScans = mutableMapOf<String, CompletableDeferred<ScanResult>>()

    override suspend fun scan(
        rule: LocalBackupRule,
        trigger: ScanTrigger,
    ): ScanResult {
        val result = CompletableDeferred<ScanResult>()
        val active =
            activeScansLock.withLock {
                activeScans[rule.id.value] ?: result.also { activeScans[rule.id.value] = it }
            }
        if (active !== result) return active.await()
        @Suppress("TooGenericExceptionCaught")
        try {
            return performScan(rule, trigger).also(result::complete)
        } catch (error: Throwable) {
            result.completeExceptionally(error)
            throw error
        } finally {
            activeScansLock.withLock { activeScans.remove(rule.id.value, result) }
        }
    }

    private suspend fun performScan(
        rule: LocalBackupRule,
        trigger: ScanTrigger,
    ): ScanResult {
        require(rule.enabled && rule.pausedAt == null) { "Backup rule is not scannable" }
        val observedAt = Instant.now(clock)
        val checkpoint = store.checkpoint(rule.id)
        val snapshot = source.snapshot(rule)
        val afterGeneration = incrementalGeneration(rule.sourceType, checkpoint, snapshot, trigger)
        val batch = LinkedHashMap<String, ScannedDocumentMetadata>(SCAN_BATCH_SIZE)
        var observedCount = 0
        var hashedCount = 0

        suspend fun flush() {
            if (batch.isEmpty()) return
            val prepared =
                batch.values.map { document ->
                    val existing = store.existing(rule, document)
                    val stableIdentity =
                        LocalDocumentIdentityResolver.resolve(
                            existing?.let { LocalDocumentIdentity(it.localDocumentKey, it.identityDiscriminator) },
                            document.identityDiscriminator,
                            keyFactory,
                        )
                    val checksum =
                        if (existing?.matches(document) == true && existing.checksum != null) {
                            existing.checksum
                        } else {
                            hashedCount++
                            checksums.open(document.sourceLocator).use(::sha256)
                        }
                    PreparedScannedDocument(document, stableIdentity.localDocumentKey, checksum)
                }
            store.applyBatch(rule, prepared, observedAt)
            batch.clear()
        }

        val outcome =
            source.scan(rule, afterGeneration) { document ->
                batch[document.providerKey] = document
                observedCount++
                if (batch.size == SCAN_BATCH_SIZE) flush()
            }
        flush()
        if (outcome.completed) {
            store.complete(rule, outcome.snapshot, observedAt, afterGeneration == null)
        }
        return ScanResult(outcome.completed, observedCount, hashedCount, trigger)
    }

    @Suppress("ReturnCount")
    private fun incrementalGeneration(
        sourceType: BackupSourceType,
        checkpoint: ScanCheckpoint?,
        snapshot: SourceSnapshot,
        trigger: ScanTrigger,
    ): Long? {
        val previousGeneration = checkpoint?.generation
        val currentGeneration = snapshot.generation
        if (sourceType == BackupSourceType.SAF_TREE || trigger == ScanTrigger.PERIODIC) return null
        if (previousGeneration == null || currentGeneration == null) return null
        return previousGeneration.takeIf {
            checkpoint.mediaStoreVersion == snapshot.version && currentGeneration >= previousGeneration
        }
    }
}

object ScannerPathPolicy {
    fun normalize(segments: List<String>): String {
        require(segments.isNotEmpty())
        val normalized =
            segments
                .map { raw ->
                    require(raw.isNotEmpty() && raw.none(Char::isISOControl))
                    require(raw != "." && raw != ".." && '/' !in raw && '\\' !in raw)
                    raw.trim().also { require(it.isNotEmpty()) }
                }.joinToString("/")
        require(normalized.length <= MAX_RELATIVE_PATH_LENGTH)
        return normalized
    }
}

data class LocalDocumentIdentity(
    val localDocumentKey: String,
    val identityDiscriminator: String,
)

object LocalDocumentIdentityResolver {
    fun resolve(
        existing: LocalDocumentIdentity?,
        identityDiscriminator: String,
        keyFactory: () -> String,
    ): LocalDocumentIdentity =
        LocalDocumentIdentity(
            localDocumentKey =
                existing
                    ?.takeIf { it.identityDiscriminator == identityDiscriminator }
                    ?.localDocumentKey
                    ?: keyFactory(),
            identityDiscriminator = identityDiscriminator,
        )
}

private fun sha256(stream: InputStream): String {
    val digest = MessageDigest.getInstance("SHA-256")
    val buffer = ByteArray(SHA_BUFFER_SIZE)
    while (true) {
        val count = stream.read(buffer)
        if (count < 0) break
        if (count > 0) digest.update(buffer, 0, count)
    }
    return digest.digest().joinToString("") { byte -> "%02x".format(byte.toInt() and UNSIGNED_BYTE_MASK) }
}
