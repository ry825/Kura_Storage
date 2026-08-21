package com.kurastorage.core.data

import com.kurastorage.core.model.AdminStorageStatus
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.FilePage
import com.kurastorage.core.model.TrashPurgeRunSummary
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.network.AdminStorageApi
import com.kurastorage.core.network.AdminStorageStatusDto
import com.kurastorage.core.network.CreateFolderRequestDto
import com.kurastorage.core.network.FileApi
import com.kurastorage.core.network.FileEntryDto
import com.kurastorage.core.network.FileEntryPageDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.UpdateFileRequestDto
import java.time.Instant

interface FileRepository {
    suspend fun list(
        parentId: String?,
        page: Int = 1,
        pageSize: Int = DEFAULT_PAGE_SIZE,
    ): FilePage

    suspend fun detail(fileId: String): FileEntry

    suspend fun createFolder(
        parentId: String?,
        name: String,
    ): FileEntry

    suspend fun rename(
        fileId: String,
        name: String,
    ): FileEntry

    suspend fun move(
        fileId: String,
        targetParentId: String,
    ): FileEntry

    suspend fun trash(fileId: String): FileEntry

    suspend fun listTrash(
        page: Int = 1,
        pageSize: Int = DEFAULT_PAGE_SIZE,
    ): FilePage

    suspend fun restore(fileId: String): FileEntry

    suspend fun purge(
        fileId: String,
        idempotencyKey: String,
    ): Unit = error("Purge is not implemented by this test double")

    companion object {
        const val DEFAULT_PAGE_SIZE = 100
    }
}

class DefaultFileRepository(
    private val api: FileApi,
    private val executor: AuthenticatedRequestExecutor,
) : FileRepository {
    override suspend fun list(
        parentId: String?,
        page: Int,
        pageSize: Int,
    ) = authenticated { api.listFiles(it, parentId, page, pageSize) }.toModel()

    override suspend fun detail(fileId: String) = authenticated { api.getFile(it, fileId) }.toModel()

    override suspend fun createFolder(
        parentId: String?,
        name: String,
    ) = authenticated { api.createFolder(it, CreateFolderRequestDto(parentId, name)) }.toModel()

    override suspend fun rename(
        fileId: String,
        name: String,
    ) = authenticated { api.updateFile(it, fileId, UpdateFileRequestDto(name = name)) }.toModel()

    override suspend fun move(
        fileId: String,
        targetParentId: String,
    ) = authenticated { api.updateFile(it, fileId, UpdateFileRequestDto(parentId = targetParentId)) }.toModel()

    override suspend fun trash(fileId: String) = authenticated { api.trash(it, fileId) }.toModel()

    override suspend fun listTrash(
        page: Int,
        pageSize: Int,
    ) = authenticated { api.listTrash(it, page, pageSize) }.toModel()

    override suspend fun restore(fileId: String) = authenticated { api.restore(it, fileId) }.toModel()

    override suspend fun purge(
        fileId: String,
        idempotencyKey: String,
    ) {
        authenticated { api.purge(it, fileId, idempotencyKey) }
    }

    private suspend fun <T> authenticated(call: suspend (String) -> NetworkCallResult<T>): T =
        executor.execute { token ->
            when (val result = call(token)) {
                is NetworkCallResult.Success -> AuthenticatedCallResult.Success(result.value)
                NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
            }
        }
}

interface AdminStorageRepository {
    suspend fun get(): AdminStorageStatus?
}

class DefaultAdminStorageRepository(
    private val api: AdminStorageApi,
    private val executor: AuthenticatedRequestExecutor,
    private val authentication: AuthenticationRepository,
) : AdminStorageRepository {
    override suspend fun get(): AdminStorageStatus? {
        if (authentication.role() != UserRole.ADMIN) return null
        return executor
            .execute { token ->
                when (val result = api.getAdminStorage(token)) {
                    is NetworkCallResult.Success -> AuthenticatedCallResult.Success(result.value)
                    NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
                }
            }.toModel()
    }
}

class FilePager(
    private val loadPage: suspend (page: Int) -> FilePage,
) {
    private var current: FilePage? = null

    suspend fun refresh(): FilePage = loadPage(1).also { current = it }

    @Suppress("ReturnCount")
    suspend fun loadNext(): FilePage {
        val existing = current ?: return refresh()
        if (!existing.hasNextPage) return existing
        val next = loadPage(existing.page + 1)
        return existing
            .copy(
                items = existing.items + next.items,
                page = next.page,
                totalCount = next.totalCount,
            ).also { current = it }
    }
}

internal fun FileEntryDto.toModel() =
    FileEntry(
        id = id,
        parentId = parentId,
        name = name,
        entryType = FileEntryType.valueOf(entryType),
        mimeType = mimeType,
        size = size,
        status = FileEntryStatus.valueOf(status),
        fileVersion = fileVersion,
        trashedAt = trashedAt?.let(Instant::parse),
        createdAt = Instant.parse(createdAt),
        updatedAt = Instant.parse(updatedAt),
        purgeEligibleAt = purgeEligibleAt?.let(Instant::parse),
    )

@Suppress("MaxLineLength")
internal fun FileEntryPageDto.toModel() = FilePage(parentId, items.map(FileEntryDto::toModel), page, pageSize, totalCount)

internal fun AdminStorageStatusDto.toModel() =
    AdminStorageStatus(
        storage = storage,
        totalBytes = totalBytes,
        availableBytes = availableBytes,
        capacityWarningThresholdBytes = capacityWarningThresholdBytes,
        capacityWarning = capacityWarning,
        trashBytes = trashBytes,
        expiredTrashRootCount = expiredTrashRootCount,
        retentionDays = retentionDays,
        recoveryRequiredPurgeCount = recoveryRequiredPurgeCount,
        lastPurgeRun =
            lastPurgeRun?.let {
                TrashPurgeRunSummary(
                    startedAt = Instant.parse(it.startedAt),
                    completedAt = it.completedAt?.let(Instant::parse),
                    status = it.status,
                    examinedRootCount = it.examinedRootCount,
                    deletedRootCount = it.deletedRootCount,
                    releasedBytes = it.releasedBytes,
                    errorCount = it.errorCount,
                )
            },
    )
