package com.kurastorage.core.data

import android.content.ContentResolver
import android.content.Intent
import android.net.Uri
import kotlinx.coroutines.CancellationException

sealed interface FolderUploadSelectionResult {
    data class Ready(
        val plan: FolderUploadPlan,
    ) : FolderUploadSelectionResult

    data object Cancelled : FolderUploadSelectionResult

    data class Rejected(
        val message: String,
    ) : FolderUploadSelectionResult
}

fun interface FolderTreePermissionSource {
    fun persistReadPermission(treeUri: String)
}

class FolderUploadSelectionReader(
    private val permissionSource: FolderTreePermissionSource,
    private val plannerFactory: (String) -> FolderUploadPlanner,
) {
    suspend fun read(treeUri: String?): FolderUploadSelectionResult {
        if (treeUri == null) return FolderUploadSelectionResult.Cancelled
        return try {
            permissionSource.persistReadPermission(treeUri)
            FolderUploadSelectionResult.Ready(plannerFactory(treeUri).plan(treeUri))
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (_: SecurityException) {
            FolderUploadSelectionResult.Rejected("The selected folder permission could not be retained.")
        } catch (_: IllegalArgumentException) {
            FolderUploadSelectionResult.Rejected("The selected folder is invalid or exceeds the safe upload limits.")
        } catch (_: Exception) {
            FolderUploadSelectionResult.Rejected("The selected folder could not be read.")
        }
    }
}

class AndroidFolderTreePermissionSource(
    private val resolver: ContentResolver,
) : FolderTreePermissionSource {
    override fun persistReadPermission(treeUri: String) {
        resolver.takePersistableUriPermission(
            Uri.parse(treeUri),
            Intent.FLAG_GRANT_READ_URI_PERMISSION,
        )
    }
}
