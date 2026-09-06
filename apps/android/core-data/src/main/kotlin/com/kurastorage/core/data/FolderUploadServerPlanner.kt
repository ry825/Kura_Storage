package com.kurastorage.core.data

import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive

data class FolderUploadReadyFile(
    val entry: FolderUploadEntry.File,
    val parentFolderId: String,
)

enum class FolderUploadServerFailureReason {
    PARENT_FAILED,
    CREATE_FAILED,
    NAME_CONFLICT_WITH_FILE,
    INVALID_SERVER_RESPONSE,
}

data class FolderUploadServerFailure(
    val path: List<String>,
    val reason: FolderUploadServerFailureReason,
)

data class FolderUploadServerPlan(
    val folderIds: Map<List<String>, String>,
    val readyFiles: List<FolderUploadReadyFile>,
    val failures: List<FolderUploadServerFailure>,
)

class FolderUploadServerPlanner(
    private val files: FileRepository,
) {
    suspend fun prepare(
        destinationFolderId: String,
        plan: FolderUploadPlan,
    ): FolderUploadServerPlan {
        require(destinationFolderId.isNotBlank())
        val folderIds = linkedMapOf<List<String>, String>()
        val failures = mutableListOf<FolderUploadServerFailure>()
        plan.entries
            .filterIsInstance<FolderUploadEntry.Folder>()
            .sortedBy { it.relativeSegments.size }
            .forEach { folder ->
                currentCoroutineContext().ensureActive()
                val parentPath = folder.relativeSegments.dropLast(1)
                val parentId = if (parentPath.isEmpty()) destinationFolderId else folderIds[parentPath]
                if (parentId == null) {
                    failures +=
                        FolderUploadServerFailure(
                            folder.relativeSegments,
                            FolderUploadServerFailureReason.PARENT_FAILED,
                        )
                    return@forEach
                }
                when (val resolved = createOrResolve(parentId, folder.relativeSegments.last())) {
                    is FolderResolution.Ready -> folderIds[folder.relativeSegments] = resolved.entry.id
                    is FolderResolution.Failed ->
                        failures += FolderUploadServerFailure(folder.relativeSegments, resolved.reason)
                }
            }

        val readyFiles = mutableListOf<FolderUploadReadyFile>()
        plan.entries.filterIsInstance<FolderUploadEntry.File>().forEach { file ->
            currentCoroutineContext().ensureActive()
            val parentPath = file.relativeSegments.dropLast(1)
            val parentId = folderIds[parentPath]
            if (parentId == null) {
                failures +=
                    FolderUploadServerFailure(
                        file.relativeSegments,
                        FolderUploadServerFailureReason.PARENT_FAILED,
                    )
            } else {
                readyFiles += FolderUploadReadyFile(file, parentId)
            }
        }
        return FolderUploadServerPlan(folderIds, readyFiles, failures)
    }

    private suspend fun createOrResolve(
        parentId: String,
        name: String,
    ): FolderResolution =
        try {
            files.createFolder(parentId, name).toFolderResolution()
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (failure: KuraStorageException.Api) {
            if (failure.error.code != ErrorCode.FILE_NAME_CONFLICT) {
                FolderResolution.Failed(FolderUploadServerFailureReason.CREATE_FAILED)
            } else {
                resolveConflict(parentId, name)
            }
        } catch (_: Exception) {
            FolderResolution.Failed(FolderUploadServerFailureReason.CREATE_FAILED)
        }

    @Suppress("ReturnCount")
    private suspend fun resolveConflict(
        parentId: String,
        name: String,
    ): FolderResolution {
        var pageNumber = 1
        do {
            val page =
                try {
                    files.list(parentId, pageNumber, FileRepository.DEFAULT_PAGE_SIZE)
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (_: Exception) {
                    return FolderResolution.Failed(FolderUploadServerFailureReason.CREATE_FAILED)
                }
            page.items.firstOrNull { it.name == name && it.status == FileEntryStatus.ACTIVE }?.let { existing ->
                return if (existing.entryType == FileEntryType.FOLDER) {
                    FolderResolution.Ready(existing)
                } else {
                    FolderResolution.Failed(FolderUploadServerFailureReason.NAME_CONFLICT_WITH_FILE)
                }
            }
            pageNumber++
        } while (page.hasNextPage)
        return FolderResolution.Failed(FolderUploadServerFailureReason.CREATE_FAILED)
    }

    private fun FileEntry.toFolderResolution(): FolderResolution =
        if (entryType == FileEntryType.FOLDER && status == FileEntryStatus.ACTIVE && id.isNotBlank()) {
            FolderResolution.Ready(this)
        } else {
            FolderResolution.Failed(FolderUploadServerFailureReason.INVALID_SERVER_RESPONSE)
        }

    private sealed interface FolderResolution {
        data class Ready(
            val entry: FileEntry,
        ) : FolderResolution

        data class Failed(
            val reason: FolderUploadServerFailureReason,
        ) : FolderResolution
    }
}
