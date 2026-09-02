package com.kurastorage.core.model.backup

import org.junit.Assert.assertThrows
import org.junit.Test
import java.time.Instant
import java.util.UUID

class BackupModelsTest {
    @Test
    fun accountScopeRejectsNonSha256Value() {
        assertThrows(IllegalArgumentException::class.java) { AccountScopeId("server-user-device") }
    }

    @Test
    fun ruleRejectsPhysicalSafPathAndInvalidBattery() {
        assertThrows(IllegalArgumentException::class.java) {
            rule(sourceLocator = "/storage/photos", minimumBattery = 101)
        }
    }

    @Test
    fun syncItemRejectsTraversalAndInvalidChecksum() {
        assertThrows(IllegalArgumentException::class.java) {
            item(relativePath = "photos/../secret.jpg", checksum = "not-a-checksum")
        }
    }

    private fun rule(
        sourceLocator: String,
        minimumBattery: Int,
    ) = LocalBackupRule(
        id = BackupRuleId(UUID.randomUUID().toString()),
        accountScopeId = AccountScopeId("a".repeat(64)),
        sourceType = BackupSourceType.SAF_TREE,
        sourceLocator = sourceLocator,
        displayName = "Photos",
        remoteFolderId = UUID.randomUUID().toString(),
        enabled = true,
        networkMode = BackupNetworkMode.LOCAL_DIRECT_ONLY,
        requiresChargingForInitialRun = true,
        minimumBatteryPercent = minimumBattery,
        initialRunCompletedAt = null,
        pausedAt = null,
        createdAt = Instant.EPOCH,
        updatedAt = Instant.EPOCH,
    )

    private fun item(
        relativePath: String,
        checksum: String,
    ) = LocalSyncItem(
        id = LocalSyncItemId(UUID.randomUUID().toString()),
        accountScopeId = AccountScopeId("a".repeat(64)),
        ruleId = BackupRuleId(UUID.randomUUID().toString()),
        localDocumentKey = "opaque",
        sourceLocator = "content://provider/item",
        relativePath = relativePath,
        displayName = "item",
        size = 1,
        modifiedAt = Instant.EPOCH,
        checksum = checksum,
        sourceFingerprint = "fingerprint",
        remoteFileId = null,
        remoteFileVersion = null,
        lifecycleState = SyncLifecycleState.PENDING,
        waitReason = BackupWaitReason.NONE,
        failureReason = BackupFailureReason.NONE,
        retryCount = 0,
        nextAttemptAt = null,
        leaseOwner = null,
        leaseExpiresAt = null,
        uploadSessionId = null,
        idempotencyKey = null,
        confirmedOffset = 0,
        firstSeenAt = Instant.EPOCH,
        lastSeenAt = Instant.EPOCH,
        lastAttemptAt = null,
        completedAt = null,
    )
}
