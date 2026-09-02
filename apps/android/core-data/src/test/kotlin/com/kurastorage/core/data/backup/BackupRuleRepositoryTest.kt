package com.kurastorage.core.data.backup

import com.kurastorage.core.database.backup.BackupRuleDao
import com.kurastorage.core.database.backup.BackupRuleEntity
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupSourceType
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID

class BackupRuleRepositoryTest {
    private val scope = AccountScopeId("a".repeat(64))

    @Test
    fun accountScopeUsesServerUserAndDeviceNamespace() {
        val user = UUID.randomUUID().toString()
        val device = UUID.randomUUID().toString()
        val first = AccountScopeHasher.create("verified-server-key", user, device)
        assertEquals(first, AccountScopeHasher.create("verified-server-key", user, device))
        assertNotEquals(first, AccountScopeHasher.create("other-server-key", user, device))
        assertNotEquals(first, AccountScopeHasher.create("verified-server-key", user, UUID.randomUUID().toString()))
    }

    @Test
    fun creatingSafRuleRetainsPermissionAndRevalidatesRemoteFolder() =
        runTest {
            val dao = FakeRuleDao()
            val permission = FakePermissionController()
            var validatedFolder: String? = null
            val repository = repository(dao, permission) { validatedFolder = it }
            val folderId = UUID.randomUUID().toString()

            val rule = repository.create(scope, command(folderId))

            assertEquals(folderId, validatedFolder)
            assertEquals(rule.sourceLocator, permission.taken)
            assertTrue(permission.hasReadPermission(rule.sourceLocator))
            assertEquals(scope.value, dao.saved?.accountScopeId)
        }

    @Test
    fun enablingRuleFailsWhenSourcePermissionWasLost() =
        runTest {
            val dao = FakeRuleDao()
            val permission = FakePermissionController()
            val repository = repository(dao, permission) {}
            val rule = repository.create(scope, command(UUID.randomUUID().toString()))
            permission.available = false

            assertThrows(IllegalArgumentException::class.java) {
                kotlinx.coroutines.runBlocking { repository.setEnabled(scope, rule.id, true) }
            }
        }

    @Test
    fun deletingRuleOnlyMutatesLocalDatabase() =
        runTest {
            val dao = FakeRuleDao()
            val repository = repository(dao, FakePermissionController()) {}
            val rule = repository.create(scope, command(UUID.randomUUID().toString()))
            repository.delete(scope, rule.id)
            assertEquals(null, dao.saved)
        }

    private fun command(folderId: String) =
        CreateBackupRuleCommand(
            sourceType = BackupSourceType.SAF_TREE,
            sourceLocator = "content://documents/tree/photos",
            displayName = " Photos ",
            remoteFolderId = folderId,
            networkMode = BackupNetworkMode.LOCAL_DIRECT_ONLY,
            requiresChargingForInitialRun = true,
            minimumBatteryPercent = 25,
        )

    private fun repository(
        dao: FakeRuleDao,
        permission: FakePermissionController,
        validator: suspend (String) -> Unit,
    ) = RoomBackupRuleRepository(
        dao,
        permission,
        RemoteBackupFolderValidator(validator),
        Clock.fixed(Instant.EPOCH, ZoneOffset.UTC),
    )
}

private class FakePermissionController : PersistableSourcePermissionController {
    var taken: String? = null
    var available = true

    override fun takeReadPermission(sourceUri: String) {
        taken = sourceUri
    }

    override fun hasReadPermission(sourceUri: String): Boolean = available && taken == sourceUri
}

private class FakeRuleDao : BackupRuleDao {
    var saved: BackupRuleEntity? = null

    override fun observeByScope(scopeId: String): Flow<List<BackupRuleEntity>> = flowOf(listOfNotNull(saved))

    override suspend fun find(
        id: String,
        scopeId: String,
    ): BackupRuleEntity? = saved?.takeIf { it.id == id && it.accountScopeId == scopeId }

    override suspend fun upsert(rule: BackupRuleEntity) {
        saved = rule
    }

    override suspend fun setEnabled(
        id: String,
        scopeId: String,
        enabled: Boolean,
        updatedAt: Long,
    ): Int {
        val current = find(id, scopeId) ?: return 0
        saved = current.copy(enabled = enabled, updatedAt = updatedAt)
        return 1
    }

    override suspend fun delete(rule: BackupRuleEntity) {
        if (saved == rule) saved = null
    }
}
