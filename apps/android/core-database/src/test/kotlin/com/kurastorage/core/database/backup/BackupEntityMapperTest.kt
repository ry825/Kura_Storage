package com.kurastorage.core.database.backup

import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupFailureReason
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.BackupStateCount
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.ExternalWifiPolicyId
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.LocalSyncItemId
import com.kurastorage.core.model.backup.ScanCheckpoint
import com.kurastorage.core.model.backup.SyncLifecycleState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.time.Instant
import java.util.UUID

class BackupEntityMapperTest {
    private val scope = AccountScopeId("a".repeat(64))
    private val ruleId = BackupRuleId(UUID.randomUUID().toString())

    @Test
    fun ruleRoundTripsAllTypedFields() {
        val rule =
            LocalBackupRule(
                ruleId,
                scope,
                BackupSourceType.SAF_TREE,
                "content://documents/tree/photos",
                "Photos",
                UUID.randomUUID().toString(),
                true,
                BackupNetworkMode.LOCAL_DIRECT_OR_ALLOWED_WIFI_ZEROTIER,
                true,
                35,
                Instant.ofEpochMilli(1),
                Instant.ofEpochMilli(2),
                Instant.ofEpochMilli(3),
                Instant.ofEpochMilli(4),
            )
        assertEquals(rule, BackupEntityMapper.toModel(BackupEntityMapper.toEntity(rule)))
    }

    @Test
    fun syncItemRoundTripsRecoveryAndTransferFields() {
        val item =
            LocalSyncItem(
                LocalSyncItemId(UUID.randomUUID().toString()),
                scope,
                ruleId,
                "opaque-key",
                "content://documents/item/photo",
                "album/photo.jpg",
                "photo.jpg",
                100,
                Instant.ofEpochMilli(5),
                "b".repeat(64),
                "fingerprint",
                UUID.randomUUID().toString(),
                7,
                SyncLifecycleState.UPLOADING,
                BackupWaitReason.NETWORK,
                BackupFailureReason.NONE,
                2,
                Instant.ofEpochMilli(6),
                "worker",
                Instant.ofEpochMilli(7),
                UUID.randomUUID().toString(),
                UUID.randomUUID().toString(),
                50,
                Instant.ofEpochMilli(8),
                Instant.ofEpochMilli(9),
                Instant.ofEpochMilli(10),
                null,
            )
        assertEquals(item, BackupEntityMapper.toModel(BackupEntityMapper.toEntity(item)))
    }

    @Test
    fun wifiPolicyUsesStableEmptyKeyForUnrestrictedBssid() {
        val policy =
            ExternalWifiPolicy(
                ExternalWifiPolicyId(UUID.randomUUID().toString()),
                scope,
                "Home",
                "Home Wi-Fi",
                null,
                false,
                true,
                Instant.EPOCH,
                Instant.EPOCH,
            )
        val entity = BackupEntityMapper.toEntity(policy)
        assertEquals("", entity.normalizedBssidKey)
        assertEquals(policy, BackupEntityMapper.toModel(entity))
    }

    @Test
    fun checkpointRoundTripsIncrementalScanFields() {
        val checkpoint =
            ScanCheckpoint(
                ruleId = ruleId,
                mediaStoreVersion = "version-7",
                generation = 42,
                fullScanToken = "opaque-token",
                lastCompletedAt = Instant.ofEpochMilli(11),
                updatedAt = Instant.ofEpochMilli(12),
            )

        assertEquals(checkpoint, BackupEntityMapper.toModel(BackupEntityMapper.toEntity(checkpoint)))
    }

    @Test
    fun stateCountMapsToTypedLifecycleState() {
        val expected = BackupStateCount(SyncLifecycleState.READY_TO_UPLOAD, 3)

        assertEquals(
            expected,
            BackupEntityMapper.toModel(BackupStateCountEntity(SyncLifecycleState.READY_TO_UPLOAD.name, 3)),
        )
    }

    @Test
    fun unknownPersistedEnumFailsClosed() {
        val entity =
            BackupRuleEntity(
                UUID.randomUUID().toString(),
                scope.value,
                "FUTURE_SOURCE",
                "images",
                "Images",
                UUID.randomUUID().toString(),
                true,
                BackupNetworkMode.LOCAL_DIRECT_ONLY.name,
                false,
                0,
                null,
                null,
                0,
                0,
            )
        assertThrows(IllegalArgumentException::class.java) { BackupEntityMapper.toModel(entity) }
    }
}
