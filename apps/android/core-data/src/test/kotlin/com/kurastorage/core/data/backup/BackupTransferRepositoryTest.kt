@file:Suppress("MaxLineLength")

package com.kurastorage.core.data.backup

import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.LocalSyncItemId
import com.kurastorage.core.model.backup.SyncLifecycleState
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.time.Clock
import java.time.Duration
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID

class BackupTransferRepositoryTest {
    @Test
    fun compareCompletesAlreadyUploadedAndUploadsNewItemInPersistedChunks() =
        runBlocking {
            val first = item("first", size = 3)
            val second = item("second", size = 5)
            val store = FakeTransferStore(listOf(first, second), rule())
            val remote =
                FakeBackupRemote().apply {
                    decisions[first.localDocumentKey] = result(first, BackupCompareDecision.ALREADY_UPLOADED)
                    decisions[second.localDocumentKey] = result(second, BackupCompareDecision.NEW)
                    preferredChunkBytes = 2
                }
            val metrics = mutableListOf<BackupMetric>()
            val repository = repository(store, remote, telemetry = BackupTelemetry(metrics::add))

            val outcome = repository.transfer(SCOPE)

            assertEquals(2, outcome.completedCount)
            assertEquals(5, outcome.transferredBytes)
            assertEquals(SyncLifecycleState.COMPLETED, store.items.getValue(first.id).lifecycleState)
            val uploaded = store.items.getValue(second.id)
            assertEquals(SyncLifecycleState.COMPLETED, uploaded.lifecycleState)
            assertEquals(5, uploaded.confirmedOffset)
            assertNotNull(uploaded.uploadSessionId)
            assertEquals(listOf(0L, 2L, 4L), remote.uploadedOffsets)
            assertTrue(metrics.all { it.toString().contains("first").not() && it.toString().contains("second").not() })
        }

    @Test
    fun existingSessionIsReconciledAndResumedWithoutCreatingAnotherSession() =
        runBlocking {
            val existing = item("resume", 6).copy(uploadSessionId = UUID.randomUUID().toString(), confirmedOffset = 2)
            val store = FakeTransferStore(listOf(existing), rule())
            val remote =
                FakeBackupRemote().apply {
                    decisions[existing.localDocumentKey] = result(existing, BackupCompareDecision.CHANGED)
                    sessionOffset = 4
                }

            repository(store, remote).transfer(SCOPE)

            assertEquals(0, remote.createdSessions)
            assertEquals(listOf(4L), remote.uploadedOffsets)
            assertEquals(SyncLifecycleState.COMPLETED, store.items.getValue(existing.id).lifecycleState)
        }

    @Test
    fun protocolMismatchFailsClosedWithoutUploading() =
        runBlocking {
            val candidate = item("candidate", 2)
            val store = FakeTransferStore(listOf(candidate), rule())
            val remote = FakeBackupRemote().apply { omitCompareResponse = true }

            repository(store, remote).transfer(SCOPE)

            val failed = store.items.getValue(candidate.id)
            assertEquals(SyncLifecycleState.FAILED, failed.lifecycleState)
            assertEquals(BackupFailureReason.PROTOCOL_ERROR, failed.failureReason)
            assertTrue(remote.uploadedOffsets.isEmpty())
        }

    @Test
    fun policyChangeAtChunkBoundaryPersistsSessionAndReturnsToPending() =
        runBlocking {
            val candidate = item("switch", 5)
            val store = FakeTransferStore(listOf(candidate), rule())
            val remote =
                FakeBackupRemote().apply {
                    decisions[candidate.localDocumentKey] = result(candidate, BackupCompareDecision.NEW)
                    preferredChunkBytes = 2
                }
            var calls = 0
            val policy =
                BackupPolicyProvider {
                    calls++
                    if (calls <= 2) allowedPolicy() else blockedPolicy(BackupWaitReason.NETWORK)
                }

            repository(store, remote, policy).transfer(SCOPE)

            val pending = store.items.getValue(candidate.id)
            assertEquals(SyncLifecycleState.PENDING, pending.lifecycleState)
            assertEquals(BackupWaitReason.NETWORK, pending.waitReason)
            assertEquals(2, pending.confirmedOffset)
            assertNotNull(pending.uploadSessionId)
            assertFalse(remote.completed)
        }

    @Test
    fun transientFailuresBackoffAndAuthenticationWaitsWithoutInfiniteRetry() =
        runBlocking {
            val retryable = item("retryable", 2)
            val retryableStore = FakeTransferStore(listOf(retryable), rule())
            val retryableRemote = FakeBackupRemote().apply { compareFailure = BackupRemoteFailureKind.TRANSIENT }
            val retryableResult = repository(retryableStore, retryableRemote).transfer(SCOPE)
            assertTrue(retryableResult.retryRecommended)
            assertEquals(1, retryableStore.items.getValue(retryable.id).retryCount)
            assertEquals(BackupWaitReason.SERVER_RECONCILIATION, retryableStore.items.getValue(retryable.id).waitReason)

            val transient = item("retry", 2).copy(retryCount = 9)
            val transientStore = FakeTransferStore(listOf(transient), rule())
            val transientRemote = FakeBackupRemote().apply { compareFailure = BackupRemoteFailureKind.TRANSIENT }
            val transientResult = repository(transientStore, transientRemote).transfer(SCOPE)
            assertEquals(SyncLifecycleState.FAILED, transientStore.items.getValue(transient.id).lifecycleState)
            assertEquals(BackupFailureReason.RETRY_EXHAUSTED, transientStore.items.getValue(transient.id).failureReason)
            assertFalse(transientResult.retryRecommended)

            val auth = item("auth", 2)
            val authStore = FakeTransferStore(listOf(auth), rule())
            val authRemote = FakeBackupRemote().apply { compareFailure = BackupRemoteFailureKind.AUTHENTICATION }
            repository(authStore, authRemote).transfer(SCOPE)
            assertEquals(BackupWaitReason.AUTHENTICATION, authStore.items.getValue(auth.id).waitReason)
            assertEquals(0, authStore.items.getValue(auth.id).retryCount)
        }

    @Test
    fun changedSourceAndRemoteConflictNeverPublishOrStartReplacementUpload() =
        runBlocking {
            val changed = item("changed", 2)
            val changedStore = FakeTransferStore(listOf(changed), rule())
            val changedRemote =
                FakeBackupRemote().apply {
                    decisions[changed.localDocumentKey] =
                        result(
                            changed,
                            BackupCompareDecision.NEW,
                        )
                }
            repository(changedStore, changedRemote, fingerprint = "different").transfer(SCOPE)
            assertEquals(BackupFailureReason.SOURCE_CHANGED, changedStore.items.getValue(changed.id).failureReason)
            assertEquals(0, changedRemote.createdSessions)

            val blocked = item("blocked", 2)
            val blockedStore = FakeTransferStore(listOf(blocked), rule())
            val blockedRemote =
                FakeBackupRemote().apply {
                    decisions[blocked.localDocumentKey] = result(blocked, BackupCompareDecision.BLOCKED_CURRENT_STATE)
                }
            repository(blockedStore, blockedRemote).transfer(SCOPE)
            assertEquals(BackupFailureReason.REMOTE_CONFLICT, blockedStore.items.getValue(blocked.id).failureReason)
            assertEquals(0, blockedRemote.createdSessions)
        }

    private fun repository(
        store: FakeTransferStore,
        remote: FakeBackupRemote,
        policy: BackupPolicyProvider = BackupPolicyProvider { allowedPolicy() },
        fingerprint: String? = null,
        telemetry: BackupTelemetry = BackupTelemetry {},
    ) = BackupTransferRepository(
        store,
        remote,
        object : BackupContentSource {
            override fun open(sourceLocator: String) = ByteArrayInputStream(store.content)

            override suspend fun fingerprint(item: LocalSyncItem) = fingerprint ?: item.sourceFingerprint
        },
        policy,
        telemetry,
        Clock.fixed(NOW, ZoneOffset.UTC),
        kotlin.random.Random(1),
    )

    private fun result(
        item: LocalSyncItem,
        decision: BackupCompareDecision,
    ) = BackupCompareResult(
        item.localDocumentKey,
        decision,
        if (decision == BackupCompareDecision.NEW) null else UUID.randomUUID().toString(),
        if (decision == BackupCompareDecision.NEW) null else 2,
        if (decision == BackupCompareDecision.BLOCKED_CURRENT_STATE) "BLOCKED" else null,
    )

    private fun allowedPolicy() =
        BackupPolicyDecision(
            BackupExecutionMode.AUTO_BACKUP_ALLOWED,
            BackupWaitReason.NONE,
            ConnectionRoute.LOCAL_DIRECT,
            "network",
            1,
        )

    private fun blockedPolicy(reason: BackupWaitReason) =
        BackupPolicyDecision(BackupExecutionMode.BLOCKED, reason, connectionGeneration = 2)

    private fun rule() =
        LocalBackupRule(
            RULE_ID,
            SCOPE,
            BackupSourceType.MEDIA_IMAGES,
            "external",
            "Rule",
            UUID.randomUUID().toString(),
            true,
            BackupNetworkMode.LOCAL_DIRECT_ONLY,
            false,
            0,
            NOW,
            null,
            NOW,
            NOW,
        )

    private fun item(
        key: String,
        size: Long,
    ) = LocalSyncItem(
        LocalSyncItemId(UUID.randomUUID().toString()),
        SCOPE,
        RULE_ID,
        UUID.nameUUIDFromBytes(key.toByteArray()).toString(),
        "content://fixture/$key",
        "$key.bin",
        "$key.bin",
        size,
        NOW,
        "0".repeat(64),
        "fingerprint",
        null,
        null,
        SyncLifecycleState.PENDING,
        BackupWaitReason.NONE,
        BackupFailureReason.NONE,
        0,
        null,
        null,
        null,
        null,
        null,
        0,
        NOW,
        NOW,
        null,
        null,
    )

    private companion object {
        val NOW: Instant = Instant.parse("2026-09-03T00:00:00Z")
        val SCOPE = AccountScopeId("a".repeat(64))
        val RULE_ID = BackupRuleId(UUID.randomUUID().toString())
    }
}

private class FakeTransferStore(
    initial: List<LocalSyncItem>,
    private val rule: LocalBackupRule,
) : BackupTransferStore {
    val items = initial.associateBy { it.id }.toMutableMap()
    val content = ByteArray((initial.maxOfOrNull { it.size } ?: 0).toInt()) { it.toByte() }

    override suspend fun enabledRules(scope: AccountScopeId) = listOf(rule)

    override suspend fun claim(
        scope: AccountScopeId,
        leaseOwner: String,
        now: Instant,
        duration: Duration,
        limit: Int,
    ) = items.values.filter { it.lifecycleState == SyncLifecycleState.PENDING }.take(limit).map {
        it
            .copy(lifecycleState = SyncLifecycleState.COMPARING, leaseOwner = leaseOwner, leaseExpiresAt = now.plus(duration))
            .also { claimed -> items[claimed.id] = claimed }
    }

    override suspend fun save(item: LocalSyncItem) {
        items[item.id] = item
    }

    override suspend fun cleanupHistory(
        scope: AccountScopeId,
        now: Instant,
    ) = 0
}

private class FakeBackupRemote : BackupRemoteDataSource {
    val decisions = mutableMapOf<String, BackupCompareResult>()
    val uploadedOffsets = mutableListOf<Long>()
    var preferredChunkBytes = 8
    var sessionOffset = 0L
    var createdSessions = 0
    var completed = false
    var omitCompareResponse = false
    var compareFailure: BackupRemoteFailureKind? = null
    private val sessionId = UUID.randomUUID().toString()

    override suspend fun compare(
        destinationFolderId: String,
        candidates: List<BackupCompareCandidate>,
    ): List<BackupCompareResult> {
        compareFailure?.let { throw BackupRemoteException(it) }
        if (omitCompareResponse) return emptyList()
        return candidates.map { candidate -> decisions.getValue(candidate.localDocumentKey) }
    }

    override suspend fun createSession(
        item: LocalSyncItem,
        destinationFolderId: String,
        idempotencyKey: String,
        decision: BackupUploadDecision,
    ): BackupRemoteSession {
        createdSessions++
        return activeSession(0)
    }

    override suspend fun session(sessionId: String) = activeSession(sessionOffset)

    override suspend fun uploadChunk(
        sessionId: String,
        offset: Long,
        bytes: ByteArray,
    ): Long {
        uploadedOffsets += offset
        sessionOffset = offset + bytes.size
        return sessionOffset
    }

    override suspend fun complete(sessionId: String): BackupRemoteSession {
        completed = true
        val changed = decisions.values.singleOrNull { it.decision == BackupCompareDecision.CHANGED }
        return activeSession(sessionOffset).copy(
            status = "COMPLETED",
            remoteFileId = changed?.remoteFileId ?: UUID.randomUUID().toString(),
            remoteFileVersion = changed?.expectedRemoteFileVersion?.plus(1) ?: 1,
        )
    }

    private fun activeSession(offset: Long) = BackupRemoteSession(sessionId, "ACTIVE", offset, preferredChunkBytes, preferredChunkBytes)
}
