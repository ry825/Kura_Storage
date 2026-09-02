package com.kurastorage.core.data.backup

import android.content.ContentResolver
import android.content.Intent
import android.net.Uri
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.database.backup.BackupEntityMapper
import com.kurastorage.core.database.backup.BackupRuleDao
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.filePermissionCapabilities
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.security.MessageDigest
import java.time.Clock
import java.time.Instant
import java.util.UUID

private const val UNSIGNED_BYTE_MASK = 0xff

fun interface RemoteBackupFolderValidator {
    suspend fun requireWritableFolder(remoteFolderId: String)
}

class FileRepositoryRemoteBackupFolderValidator(
    private val files: FileRepository,
) : RemoteBackupFolderValidator {
    override suspend fun requireWritableFolder(remoteFolderId: String) {
        UUID.fromString(remoteFolderId)
        val folder = files.detail(remoteFolderId)
        require(
            folder.entryType == FileEntryType.FOLDER &&
                folder.status == FileEntryStatus.ACTIVE &&
                filePermissionCapabilities(folder.permission, folder.permissionSource).canCreate,
        ) { "Backup destination is not an active writable folder" }
    }
}

interface PersistableSourcePermissionController {
    fun takeReadPermission(sourceUri: String)

    fun hasReadPermission(sourceUri: String): Boolean
}

class AndroidPersistableSourcePermissionController(
    private val contentResolver: ContentResolver,
) : PersistableSourcePermissionController {
    override fun takeReadPermission(sourceUri: String) {
        contentResolver.takePersistableUriPermission(
            Uri.parse(sourceUri),
            Intent.FLAG_GRANT_READ_URI_PERMISSION,
        )
    }

    override fun hasReadPermission(sourceUri: String): Boolean =
        contentResolver.persistedUriPermissions.any { permission ->
            permission.isReadPermission && permission.uri == Uri.parse(sourceUri)
        }
}

object AccountScopeHasher {
    fun create(
        serverIdentityKey: String,
        userId: String,
        deviceId: String,
    ): AccountScopeId {
        require(serverIdentityKey.isNotBlank())
        UUID.fromString(userId)
        UUID.fromString(deviceId)
        val input = listOf(serverIdentityKey, userId, deviceId).joinToString("\u0000")
        val digest = MessageDigest.getInstance("SHA-256").digest(input.toByteArray(Charsets.UTF_8))
        return AccountScopeId(
            digest.joinToString("") { byte -> "%02x".format(byte.toInt() and UNSIGNED_BYTE_MASK) },
        )
    }
}

data class CreateBackupRuleCommand(
    val sourceType: BackupSourceType,
    val sourceLocator: String,
    val displayName: String,
    val remoteFolderId: String,
    val networkMode: BackupNetworkMode,
    val requiresChargingForInitialRun: Boolean,
    val minimumBatteryPercent: Int,
)

enum class BackupSourceAccess {
    AVAILABLE,
    PERMISSION_REQUIRED,
}

interface BackupRuleRepository {
    fun observe(accountScopeId: AccountScopeId): Flow<List<LocalBackupRule>>

    suspend fun create(
        accountScopeId: AccountScopeId,
        command: CreateBackupRuleCommand,
    ): LocalBackupRule

    suspend fun setEnabled(
        accountScopeId: AccountScopeId,
        ruleId: BackupRuleId,
        enabled: Boolean,
    )

    suspend fun save(
        accountScopeId: AccountScopeId,
        rule: LocalBackupRule,
    )

    suspend fun delete(
        accountScopeId: AccountScopeId,
        ruleId: BackupRuleId,
    )

    suspend fun sourceAccess(rule: LocalBackupRule): BackupSourceAccess
}

class RoomBackupRuleRepository(
    private val dao: BackupRuleDao,
    private val sourcePermissionController: PersistableSourcePermissionController,
    private val remoteFolderValidator: RemoteBackupFolderValidator,
    private val clock: Clock = Clock.systemUTC(),
) : BackupRuleRepository {
    override fun observe(accountScopeId: AccountScopeId): Flow<List<LocalBackupRule>> =
        dao.observeByScope(accountScopeId.value).map { rules -> rules.map(BackupEntityMapper::toModel) }

    override suspend fun create(
        accountScopeId: AccountScopeId,
        command: CreateBackupRuleCommand,
    ): LocalBackupRule {
        remoteFolderValidator.requireWritableFolder(command.remoteFolderId)
        if (command.sourceType == BackupSourceType.SAF_TREE) {
            sourcePermissionController.takeReadPermission(command.sourceLocator)
            require(sourcePermissionController.hasReadPermission(command.sourceLocator)) {
                "Persistable source permission was not retained"
            }
        }
        val now = Instant.now(clock)
        val rule =
            LocalBackupRule(
                id = BackupRuleId(UUID.randomUUID().toString()),
                accountScopeId = accountScopeId,
                sourceType = command.sourceType,
                sourceLocator = command.sourceLocator,
                displayName = command.displayName.trim(),
                remoteFolderId = command.remoteFolderId,
                enabled = true,
                networkMode = command.networkMode,
                requiresChargingForInitialRun = command.requiresChargingForInitialRun,
                minimumBatteryPercent = command.minimumBatteryPercent,
                initialRunCompletedAt = null,
                pausedAt = null,
                createdAt = now,
                updatedAt = now,
            )
        dao.upsert(BackupEntityMapper.toEntity(rule))
        return rule
    }

    override suspend fun setEnabled(
        accountScopeId: AccountScopeId,
        ruleId: BackupRuleId,
        enabled: Boolean,
    ) {
        val current = requireNotNull(dao.find(ruleId.value, accountScopeId.value))
        if (enabled) {
            remoteFolderValidator.requireWritableFolder(current.remoteFolderId)
            require(
                current.sourceType != BackupSourceType.SAF_TREE.name ||
                    sourcePermissionController.hasReadPermission(current.sourceLocator),
            ) { "Backup source permission is unavailable" }
        }
        require(dao.setEnabled(ruleId.value, accountScopeId.value, enabled, Instant.now(clock).toEpochMilli()) == 1) {
            "Backup rule was not found in the active account scope"
        }
    }

    override suspend fun save(
        accountScopeId: AccountScopeId,
        rule: LocalBackupRule,
    ) {
        require(rule.accountScopeId == accountScopeId)
        requireNotNull(dao.find(rule.id.value, accountScopeId.value))
        remoteFolderValidator.requireWritableFolder(rule.remoteFolderId)
        if (
            rule.sourceType == BackupSourceType.SAF_TREE &&
            !sourcePermissionController.hasReadPermission(rule.sourceLocator)
        ) {
            sourcePermissionController.takeReadPermission(rule.sourceLocator)
        }
        require(
            rule.sourceType != BackupSourceType.SAF_TREE ||
                sourcePermissionController.hasReadPermission(rule.sourceLocator),
        ) { "Backup source permission is unavailable" }
        dao.upsert(BackupEntityMapper.toEntity(rule.copy(updatedAt = Instant.now(clock))))
    }

    override suspend fun delete(
        accountScopeId: AccountScopeId,
        ruleId: BackupRuleId,
    ) {
        val rule = requireNotNull(dao.find(ruleId.value, accountScopeId.value))
        dao.delete(rule)
    }

    override suspend fun sourceAccess(rule: LocalBackupRule): BackupSourceAccess =
        if (
            rule.sourceType != BackupSourceType.SAF_TREE ||
            sourcePermissionController.hasReadPermission(rule.sourceLocator)
        ) {
            BackupSourceAccess.AVAILABLE
        } else {
            BackupSourceAccess.PERMISSION_REQUIRED
        }
}
