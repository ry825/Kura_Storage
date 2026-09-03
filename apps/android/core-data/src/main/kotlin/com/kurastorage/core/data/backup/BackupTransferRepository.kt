@file:Suppress(
    "CyclomaticComplexMethod",
    "LongMethod",
    "LongParameterList",
    "LoopWithTooManyJumpStatements",
    "MagicNumber",
    "MaxLineLength",
    "NestedBlockDepth",
    "ReturnCount",
    "TooManyFunctions",
)

package com.kurastorage.core.data.backup

import com.kurastorage.core.data.AuthenticatedCallResult
import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.SyncLifecycleState
import com.kurastorage.core.network.BackupApi
import com.kurastorage.core.network.BackupCompareCandidateDto
import com.kurastorage.core.network.BackupCompareRequestDto
import com.kurastorage.core.network.BackupUploadContextDto
import com.kurastorage.core.network.CreateUploadSessionRequestDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.UploadSessionApi
import com.kurastorage.core.network.UploadSessionDto
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.InputStream
import java.security.MessageDigest
import java.time.Clock
import java.time.Duration
import java.time.Instant
import java.util.UUID
import kotlin.math.min
import kotlin.random.Random

private const val MAX_BATCH_FILES = 100
private const val MAX_BATCH_BYTES = 2L * 1024 * 1024 * 1024
private const val MAX_RETRIES = 10
private const val HASH_BUFFER_BYTES = 64 * 1024
private const val MAX_CHUNK_BYTES = 16 * 1024 * 1024
private val MAX_BATCH_DURATION: Duration = Duration.ofMinutes(20)
private val LEASE_DURATION: Duration = Duration.ofMinutes(30)
private val OCTET_STREAM = "application/octet-stream".toMediaTypeOrNull()

enum class BackupCompareDecision {
    NEW,
    CHANGED,
    ALREADY_UPLOADED,
    BLOCKED_CURRENT_STATE,
}

data class BackupCompareCandidate(
    val localDocumentKey: String,
    val relativePath: String,
    val size: Long,
    val modifiedAt: Instant,
    val checksum: String?,
)

data class BackupCompareResult(
    val localDocumentKey: String,
    val decision: BackupCompareDecision,
    val remoteFileId: String?,
    val expectedRemoteFileVersion: Long?,
    val errorCode: String?,
)

data class BackupUploadDecision(
    val decision: BackupCompareDecision,
    val remoteFileId: String?,
    val remoteFileVersion: Long?,
)

data class BackupRemoteSession(
    val id: String,
    val status: String,
    val nextOffset: Long,
    val preferredChunkBytes: Int,
    val maximumChunkBytes: Int,
    val remoteFileId: String? = null,
    val remoteFileVersion: Long? = null,
)

enum class BackupRemoteFailureKind {
    TRANSIENT,
    AUTHENTICATION,
    SESSION_EXPIRED,
    REMOTE_CONFLICT,
    PROTOCOL,
}

class BackupRemoteException(
    val kind: BackupRemoteFailureKind,
    cause: Throwable? = null,
) : RuntimeException(cause)

interface BackupRemoteDataSource {
    suspend fun compare(
        destinationFolderId: String,
        candidates: List<BackupCompareCandidate>,
    ): List<BackupCompareResult>

    suspend fun createSession(
        item: LocalSyncItem,
        destinationFolderId: String,
        idempotencyKey: String,
        decision: BackupUploadDecision,
    ): BackupRemoteSession

    suspend fun session(sessionId: String): BackupRemoteSession

    suspend fun uploadChunk(
        sessionId: String,
        offset: Long,
        bytes: ByteArray,
    ): Long

    suspend fun complete(sessionId: String): BackupRemoteSession
}

interface BackupTransferStore {
    suspend fun enabledRules(scope: AccountScopeId): List<LocalBackupRule>

    suspend fun claim(
        scope: AccountScopeId,
        leaseOwner: String,
        now: Instant,
        duration: Duration,
        limit: Int,
    ): List<LocalSyncItem>

    suspend fun save(item: LocalSyncItem)

    suspend fun cleanupHistory(
        scope: AccountScopeId,
        now: Instant,
    ): Int
}

interface BackupContentSource {
    fun open(sourceLocator: String): InputStream

    suspend fun fingerprint(item: LocalSyncItem): String
}

fun interface BackupPolicyProvider {
    suspend fun evaluate(rule: LocalBackupRule): BackupPolicyDecision
}

enum class BackupMetricKind {
    BATCH,
    COMPLETED,
    WAITING,
    FAILED,
    RETRY,
}

data class BackupMetric(
    val kind: BackupMetricKind,
    val itemCount: Int = 0,
    val byteCount: Long = 0,
    val elapsedMillis: Long = 0,
    val waitReason: BackupWaitReason = BackupWaitReason.NONE,
    val failureReason: BackupFailureReason = BackupFailureReason.NONE,
    val retryCount: Int = 0,
)

fun interface BackupTelemetry {
    fun record(metric: BackupMetric)
}

data class BackupTransferBatchResult(
    val claimedCount: Int,
    val completedCount: Int,
    val transferredBytes: Long,
    val hasRemaining: Boolean,
    val retryRecommended: Boolean = false,
)

class BackupTransferRepository(
    private val store: BackupTransferStore,
    private val remote: BackupRemoteDataSource,
    private val source: BackupContentSource,
    private val policy: BackupPolicyProvider,
    private val telemetry: BackupTelemetry = BackupTelemetry {},
    private val clock: Clock = Clock.systemUTC(),
    private val random: Random = Random.Default,
) {
    @Suppress("LongMethod", "CyclomaticComplexMethod", "TooGenericExceptionCaught")
    suspend fun transfer(scope: AccountScopeId): BackupTransferBatchResult {
        val startedAt = Instant.now(clock)
        val batchDeadline = startedAt.plus(MAX_BATCH_DURATION)
        val leaseOwner = UUID.randomUUID().toString()
        val rules = store.enabledRules(scope).associateBy { it.id }
        val claimed = store.claim(scope, leaseOwner, startedAt, LEASE_DURATION, MAX_BATCH_FILES)
        var completed = 0
        var transferred = 0L
        var capped = false
        var retryRecommended = false
        val eligible = mutableListOf<Pair<LocalSyncItem, LocalBackupRule>>()
        for (item in claimed) {
            val rule = rules[item.ruleId]
            if (rule == null) {
                store.save(item.waiting(BackupWaitReason.SOURCE_PERMISSION))
                continue
            }
            val decision = policy.evaluate(rule)
            if (!decision.allowed) {
                store.save(item.waiting(decision.waitReason))
                telemetry.record(BackupMetric(BackupMetricKind.WAITING, 1, waitReason = decision.waitReason))
                continue
            }
            eligible += item to rule
        }
        eligible.groupBy { it.second.remoteFolderId }.forEach { (folderId, entries) ->
            val compare =
                try {
                    remote.compare(folderId, entries.map { it.first.toCandidate() })
                } catch (error: BackupRemoteException) {
                    entries.forEach { (item, _) -> retryRecommended = handleRemoteFailure(item, error) || retryRecommended }
                    return@forEach
                }
            val byKey =
                try {
                    validateCompare(entries.map { it.first }, compare)
                } catch (_: IllegalArgumentException) {
                    entries.forEach { (item, _) ->
                        store.save(item.failed(BackupFailureReason.PROTOCOL_ERROR))
                    }
                    return@forEach
                }
            for ((claimedItem, rule) in entries) {
                currentCoroutineContext().ensureActive()
                if (
                    completed >= MAX_BATCH_FILES ||
                    !Instant.now(clock).isBefore(batchDeadline)
                ) {
                    store.save(claimedItem.waiting(BackupWaitReason.NONE))
                    capped = true
                    continue
                }
                val comparison = byKey.getValue(claimedItem.localDocumentKey)
                when (comparison.decision) {
                    BackupCompareDecision.ALREADY_UPLOADED -> {
                        val finished = claimedItem.completed(comparison, Instant.now(clock))
                        store.save(finished)
                        completed++
                        telemetry.record(BackupMetric(BackupMetricKind.COMPLETED, 1))
                    }
                    BackupCompareDecision.BLOCKED_CURRENT_STATE -> {
                        store.save(claimedItem.failed(BackupFailureReason.REMOTE_CONFLICT))
                        telemetry.record(
                            BackupMetric(BackupMetricKind.FAILED, 1, failureReason = BackupFailureReason.REMOTE_CONFLICT),
                        )
                    }
                    BackupCompareDecision.NEW,
                    BackupCompareDecision.CHANGED,
                    -> {
                        val remainingByteBudget = MAX_BATCH_BYTES - transferred
                        if (transferred > 0 && claimedItem.size > remainingByteBudget) {
                            store.save(claimedItem.waiting(BackupWaitReason.NONE))
                            capped = true
                            continue
                        }
                        val result = upload(claimedItem, rule, comparison, remainingByteBudget, batchDeadline)
                        completed += if (result.completed) 1 else 0
                        transferred += result.bytes
                        capped = capped || result.stoppedEarly
                        retryRecommended = retryRecommended || result.retryRecommended
                    }
                }
            }
        }
        store.cleanupHistory(scope, Instant.now(clock))
        telemetry.record(
            BackupMetric(
                BackupMetricKind.BATCH,
                claimed.size,
                transferred,
                Duration.between(startedAt, Instant.now(clock)).toMillis().coerceAtLeast(0),
            ),
        )
        return BackupTransferBatchResult(
            claimed.size,
            completed,
            transferred,
            capped || claimed.size == MAX_BATCH_FILES,
            retryRecommended,
        )
    }

    private suspend fun upload(
        original: LocalSyncItem,
        rule: LocalBackupRule,
        comparison: BackupCompareResult,
        byteBudget: Long,
        batchDeadline: Instant,
    ): UploadResult {
        var item = original.copy(lifecycleState = SyncLifecycleState.READY_TO_UPLOAD)
        store.save(item)
        return try {
            if (source.fingerprint(item) != item.sourceFingerprint) {
                store.save(item.failed(BackupFailureReason.SOURCE_CHANGED))
                return UploadResult()
            }
            val idempotencyKey = item.idempotencyKey ?: UUID.randomUUID().toString()
            var session =
                if (item.uploadSessionId == null) {
                    remote.createSession(
                        item,
                        rule.remoteFolderId,
                        idempotencyKey,
                        BackupUploadDecision(comparison.decision, comparison.remoteFileId, comparison.expectedRemoteFileVersion),
                    )
                } else {
                    remote.session(checkNotNull(item.uploadSessionId))
                }
            item = item.uploading(session, idempotencyKey)
            store.save(item)
            if (session.status == "COMPLETED") {
                validateCompletedSession(session, comparison)
                store.save(item.completed(session, Instant.now(clock)))
                return UploadResult(completed = true)
            }
            var sent = 0L
            while (item.confirmedOffset < item.size) {
                currentCoroutineContext().ensureActive()
                if (sent >= byteBudget || !Instant.now(clock).isBefore(batchDeadline)) {
                    store.save(item.waiting(BackupWaitReason.NONE))
                    return UploadResult(sent, stoppedEarly = true)
                }
                val policyDecision = policy.evaluate(rule)
                if (!policyDecision.allowed) {
                    store.save(item.waiting(policyDecision.waitReason))
                    return UploadResult(sent, stoppedEarly = true)
                }
                if (source.fingerprint(item) != item.sourceFingerprint) {
                    store.save(item.failed(BackupFailureReason.SOURCE_CHANGED))
                    return UploadResult(sent)
                }
                val remainingBudget = (byteBudget - sent).coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
                val chunkSize = min(min(min(session.preferredChunkBytes, session.maximumChunkBytes), MAX_CHUNK_BYTES), remainingBudget)
                require(chunkSize > 0) { "Server returned an invalid chunk size" }
                val bytes =
                    source.open(item.sourceLocator).use {
                        it.skipFully(item.confirmedOffset)
                        it.readChunk(item.size, item.confirmedOffset, chunkSize)
                    }
                val nextOffset = remote.uploadChunk(session.id, item.confirmedOffset, bytes)
                require(nextOffset == item.confirmedOffset + bytes.size) { "Server returned a non-contiguous offset" }
                item = item.copy(confirmedOffset = nextOffset)
                store.save(item)
                sent += bytes.size
                if (source.fingerprint(item) != item.sourceFingerprint) {
                    store.save(item.failed(BackupFailureReason.SOURCE_CHANGED))
                    return UploadResult(sent)
                }
            }
            val finalPolicy = policy.evaluate(rule)
            if (!finalPolicy.allowed) {
                store.save(item.waiting(finalPolicy.waitReason))
                return UploadResult(sent, stoppedEarly = true)
            }
            session = remote.complete(session.id)
            validateCompletedSession(session, comparison)
            store.save(item.completed(session, Instant.now(clock)))
            telemetry.record(BackupMetric(BackupMetricKind.COMPLETED, 1, sent))
            UploadResult(sent, completed = true)
        } catch (cancelled: CancellationException) {
            store.save(item.waiting(BackupWaitReason.SERVER_RECONCILIATION))
            throw cancelled
        } catch (error: BackupRemoteException) {
            UploadResult(retryRecommended = handleRemoteFailure(item, error))
        } catch (_: SecurityException) {
            store.save(item.failed(BackupFailureReason.PERMISSION_REVOKED))
            UploadResult()
        } catch (_: IllegalArgumentException) {
            store.save(item.failed(BackupFailureReason.PROTOCOL_ERROR))
            UploadResult()
        } catch (_: Exception) {
            store.save(item.failed(BackupFailureReason.SOURCE_UNAVAILABLE))
            UploadResult()
        }
    }

    private suspend fun handleRemoteFailure(
        item: LocalSyncItem,
        error: BackupRemoteException,
    ): Boolean =
        when (error.kind) {
            BackupRemoteFailureKind.AUTHENTICATION -> {
                store.save(item.waiting(BackupWaitReason.AUTHENTICATION))
                false
            }
            BackupRemoteFailureKind.TRANSIENT -> {
                val retry = item.retryCount + 1
                if (retry >= MAX_RETRIES) {
                    store.save(item.failed(BackupFailureReason.RETRY_EXHAUSTED))
                    false
                } else {
                    val seconds = (1L shl retry.coerceAtMost(8)) + random.nextLong(0, 4)
                    store.save(
                        item.waiting(BackupWaitReason.SERVER_RECONCILIATION).copy(
                            retryCount = retry,
                            nextAttemptAt = Instant.now(clock).plusSeconds(seconds),
                        ),
                    )
                    telemetry.record(BackupMetric(BackupMetricKind.RETRY, 1, retryCount = retry))
                    true
                }
            }
            BackupRemoteFailureKind.SESSION_EXPIRED,
            BackupRemoteFailureKind.REMOTE_CONFLICT,
            -> {
                store.save(item.failed(BackupFailureReason.REMOTE_CONFLICT))
                false
            }
            BackupRemoteFailureKind.PROTOCOL -> {
                store.save(item.failed(BackupFailureReason.PROTOCOL_ERROR))
                false
            }
        }

    private fun validateCompare(
        requested: List<LocalSyncItem>,
        response: List<BackupCompareResult>,
    ): Map<String, BackupCompareResult> {
        val requestedByKey = requested.associateBy { it.localDocumentKey }
        val expected = requestedByKey.keys
        require(response.size == expected.size) { "Backup compare response count differed" }
        val indexed = response.associateBy { it.localDocumentKey }
        require(indexed.size == response.size && indexed.keys == expected) { "Backup compare response keys differed" }
        response.forEach { result ->
            val item = requestedByKey.getValue(result.localDocumentKey)
            when (result.decision) {
                BackupCompareDecision.NEW ->
                    require(
                        result.remoteFileId == null &&
                            result.expectedRemoteFileVersion == null &&
                            item.remoteFileId == null,
                    )
                BackupCompareDecision.CHANGED,
                BackupCompareDecision.ALREADY_UPLOADED,
                -> {
                    require(result.remoteFileId != null && result.expectedRemoteFileVersion != null)
                    require(item.remoteFileId == null || item.remoteFileId == result.remoteFileId)
                    require(item.remoteFileVersion == null || item.remoteFileVersion == result.expectedRemoteFileVersion)
                }
                BackupCompareDecision.BLOCKED_CURRENT_STATE -> require(result.errorCode != null)
            }
        }
        return indexed
    }

    private fun validateCompletedSession(
        session: BackupRemoteSession,
        comparison: BackupCompareResult,
    ) {
        require(session.status == "COMPLETED")
        require(session.remoteFileId != null && session.remoteFileVersion != null)
        if (comparison.decision == BackupCompareDecision.CHANGED) {
            require(session.remoteFileId == comparison.remoteFileId)
            require(session.remoteFileVersion > requireNotNull(comparison.expectedRemoteFileVersion))
        }
    }

    private data class UploadResult(
        val bytes: Long = 0,
        val completed: Boolean = false,
        val stoppedEarly: Boolean = false,
        val retryRecommended: Boolean = false,
    )
}

class AuthenticatedBackupRemoteDataSource(
    private val backupApi: BackupApi,
    private val uploadApi: UploadSessionApi,
    private val executor: AuthenticatedRequestExecutor,
) : BackupRemoteDataSource {
    override suspend fun compare(
        destinationFolderId: String,
        candidates: List<BackupCompareCandidate>,
    ): List<BackupCompareResult> =
        authenticated { token ->
            backupApi.compareBackup(
                token,
                BackupCompareRequestDto(
                    destinationFolderId,
                    candidates.map {
                        BackupCompareCandidateDto(
                            it.localDocumentKey,
                            it.relativePath,
                            it.size,
                            it.modifiedAt.toString(),
                            it.checksum,
                        )
                    },
                ),
            )
        }.items.map { result ->
            BackupCompareResult(
                result.localDocumentKey,
                runCatching { enumValueOf<BackupCompareDecision>(result.decision) }
                    .getOrElse { throw BackupRemoteException(BackupRemoteFailureKind.PROTOCOL, it) },
                result.remoteFileId,
                result.expectedRemoteFileVersion,
                result.errorCode,
            )
        }

    override suspend fun createSession(
        item: LocalSyncItem,
        destinationFolderId: String,
        idempotencyKey: String,
        decision: BackupUploadDecision,
    ): BackupRemoteSession =
        authenticated { token ->
            uploadApi.createUploadSession(
                token,
                idempotencyKey,
                CreateUploadSessionRequestDto(
                    destinationFolderId,
                    item.displayName,
                    null,
                    item.size,
                    item.checksum,
                    BackupUploadContextDto(
                        item.localDocumentKey,
                        item.relativePath,
                        item.modifiedAt.toString(),
                        decision.decision.name,
                        decision.remoteFileId,
                        decision.remoteFileVersion,
                    ),
                ),
            )
        }.toBackupSession()

    override suspend fun session(sessionId: String): BackupRemoteSession =
        authenticated { uploadApi.getUploadSession(it, sessionId) }.toBackupSession()

    override suspend fun uploadChunk(
        sessionId: String,
        offset: Long,
        bytes: ByteArray,
    ): Long =
        authenticated { token ->
            uploadApi.uploadChunk(token, sessionId, offset, bytes.sha256(), bytes.toRequestBody(OCTET_STREAM))
        }.nextOffset

    override suspend fun complete(sessionId: String): BackupRemoteSession {
        val file = authenticated { uploadApi.completeUploadSession(it, sessionId) }
        return BackupRemoteSession(sessionId, "COMPLETED", file.size, 1, 1, file.id, file.fileVersion)
    }

    private suspend fun <T> authenticated(call: suspend (String) -> NetworkCallResult<T>): T =
        try {
            executor.execute { token ->
                when (val result = call(token)) {
                    is NetworkCallResult.Success -> AuthenticatedCallResult.Success(result.value)
                    NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
                }
            }
        } catch (error: KuraStorageException.Api) {
            throw BackupRemoteException(error.toBackupFailureKind(), error)
        } catch (error: KuraStorageException.Network) {
            throw BackupRemoteException(BackupRemoteFailureKind.TRANSIENT, error)
        }
}

private fun KuraStorageException.Api.toBackupFailureKind() =
    when (error.code) {
        ErrorCode.AUTHENTICATION_REQUIRED,
        ErrorCode.DEVICE_REVOKED,
        ErrorCode.REFRESH_TOKEN_REUSED,
        -> BackupRemoteFailureKind.AUTHENTICATION
        ErrorCode.UPLOAD_SESSION_EXPIRED,
        ErrorCode.UPLOAD_SESSION_CANCELLED,
        -> BackupRemoteFailureKind.SESSION_EXPIRED
        ErrorCode.UPLOAD_OFFSET_MISMATCH -> BackupRemoteFailureKind.TRANSIENT
        else -> if (error.canRetry) BackupRemoteFailureKind.TRANSIENT else BackupRemoteFailureKind.REMOTE_CONFLICT
    }

private fun UploadSessionDto.toBackupSession() =
    BackupRemoteSession(
        id,
        status,
        nextOffset,
        preferredChunkBytes,
        maximumChunkBytes,
        file?.id,
        file?.fileVersion,
    )

private fun LocalSyncItem.toCandidate() = BackupCompareCandidate(localDocumentKey, relativePath, size, modifiedAt, checksum)

private fun LocalSyncItem.waiting(reason: BackupWaitReason) =
    copy(
        lifecycleState = SyncLifecycleState.PENDING,
        waitReason = reason,
        leaseOwner = null,
        leaseExpiresAt = null,
    )

private fun LocalSyncItem.failed(reason: BackupFailureReason) =
    copy(
        lifecycleState = SyncLifecycleState.FAILED,
        waitReason = BackupWaitReason.NONE,
        failureReason = reason,
        leaseOwner = null,
        leaseExpiresAt = null,
        nextAttemptAt = null,
    )

private fun LocalSyncItem.completed(
    result: BackupCompareResult,
    completedAt: Instant,
) = copy(
    lifecycleState = SyncLifecycleState.COMPLETED,
    waitReason = BackupWaitReason.NONE,
    failureReason = BackupFailureReason.NONE,
    remoteFileId = result.remoteFileId,
    remoteFileVersion = result.expectedRemoteFileVersion,
    leaseOwner = null,
    leaseExpiresAt = null,
    completedAt = completedAt,
)

private fun LocalSyncItem.uploading(
    session: BackupRemoteSession,
    idempotencyKey: String,
) = copy(
    lifecycleState = SyncLifecycleState.UPLOADING,
    uploadSessionId = session.id,
    idempotencyKey = idempotencyKey,
    confirmedOffset = session.nextOffset,
)

private fun LocalSyncItem.completed(
    session: BackupRemoteSession,
    completedAt: Instant,
) = copy(
    lifecycleState = SyncLifecycleState.COMPLETED,
    waitReason = BackupWaitReason.NONE,
    failureReason = BackupFailureReason.NONE,
    remoteFileId = requireNotNull(session.remoteFileId),
    remoteFileVersion = requireNotNull(session.remoteFileVersion),
    confirmedOffset = size,
    leaseOwner = null,
    leaseExpiresAt = null,
    completedAt = completedAt,
)

private fun InputStream.skipFully(count: Long) {
    var remaining = count
    val scratch = ByteArray(HASH_BUFFER_BYTES)
    while (remaining > 0) {
        val skipped = skip(remaining)
        if (skipped > 0) {
            remaining -= skipped
        } else {
            val read = read(scratch, 0, min(scratch.size.toLong(), remaining).toInt())
            if (read < 0) error("Backup source ended before confirmed offset")
            remaining -= read
        }
    }
}

private fun InputStream.readChunk(
    size: Long,
    offset: Long,
    maximum: Int,
): ByteArray {
    val target = min(size - offset, maximum.toLong()).toInt()
    val result = ByteArray(target)
    var read = 0
    while (read < target) {
        val count = read(result, read, target - read)
        if (count < 0) error("Backup source changed while reading")
        read += count
    }
    return result
}

private fun ByteArray.sha256() = MessageDigest.getInstance("SHA-256").digest(this).joinToString("") { "%02x".format(it) }
