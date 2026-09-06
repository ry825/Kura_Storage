package com.kurastorage.core.data

import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.FilePage
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class FolderUploadServerPlannerTest {
    @Test
    fun `folders are created parent first and files receive resolved parent ids`() =
        runTest {
            val files = FolderFiles()
            val plan =
                FolderUploadPlan(
                    entries =
                        listOf(
                            folderEntry("root", "Selected"),
                            folderEntry("empty", "Selected", "Empty"),
                            folderEntry("nested", "Selected", "Nested"),
                            fileEntry("child", "Selected", "Nested", "child.txt"),
                        ),
                    rejections = emptyList(),
                )

            val result = FolderUploadServerPlanner(files).prepare("destination", plan)

            assertEquals(
                listOf("destination:Selected", "id-Selected:Empty", "id-Selected:Nested"),
                files.createCalls,
            )
            assertEquals("id-Nested", result.readyFiles.single().parentFolderId)
            assertTrue(result.failures.isEmpty())
        }

    @Test
    fun `retry resolves created folder conflicts without creating duplicates`() =
        runTest {
            val files = FolderFiles()
            val planner = FolderUploadServerPlanner(files)
            val plan = FolderUploadPlan(listOf(folderEntry("root", "Selected")), emptyList())

            planner.prepare("destination", plan)
            val retried = planner.prepare("destination", plan)

            assertEquals(1, files.entries.count { it.parentId == "destination" && it.name == "Selected" })
            assertEquals("id-Selected", retried.folderIds[listOf("Selected")])
        }

    @Test
    fun `failed parent blocks descendants while independent sibling continues`() =
        runTest {
            val files = FolderFiles(failNames = setOf("Broken"))
            val plan =
                FolderUploadPlan(
                    listOf(
                        folderEntry("root", "Selected"),
                        folderEntry("broken", "Selected", "Broken"),
                        folderEntry("child", "Selected", "Broken", "Child"),
                        folderEntry("ok", "Selected", "Ok"),
                        fileEntry("blocked-file", "Selected", "Broken", "blocked.txt"),
                        fileEntry("ok-file", "Selected", "Ok", "ok.txt"),
                    ),
                    emptyList(),
                )

            val result = FolderUploadServerPlanner(files).prepare("destination", plan)

            assertEquals(listOf("ok.txt"), result.readyFiles.map { it.entry.relativeSegments.last() })
            assertEquals(
                listOf("Selected/Broken", "Selected/Broken/Child", "Selected/Broken/blocked.txt"),
                result.failures.map {
                    it.path.joinToString("/")
                },
            )
        }

    private class FolderFiles(
        private val failNames: Set<String> = emptySet(),
    ) : FileRepository {
        val entries = mutableListOf<FileEntry>()
        val createCalls = mutableListOf<String>()

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ): FilePage {
            val matches = entries.filter { it.parentId == parentId }
            return FilePage(parentId, matches, page, pageSize, matches.size.toLong())
        }

        override suspend fun createFolder(
            parentId: String?,
            name: String,
        ): FileEntry {
            createCalls += "$parentId:$name"
            if (name in failNames) throw KuraStorageException.Api(ApiError(ErrorCode.STORAGE_UNAVAILABLE, null, 503))
            if (entries.any { it.parentId == parentId && it.name == name }) {
                throw KuraStorageException.Api(ApiError(ErrorCode.FILE_NAME_CONFLICT, null, 409))
            }
            return serverFolder("id-$name", parentId, name).also(entries::add)
        }

        override suspend fun detail(fileId: String) = error("unused")

        override suspend fun rename(
            fileId: String,
            name: String,
        ) = error("unused")

        override suspend fun move(
            fileId: String,
            targetParentId: String,
        ) = error("unused")

        override suspend fun trash(fileId: String) = error("unused")

        override suspend fun listTrash(
            page: Int,
            pageSize: Int,
        ) = error("unused")

        override suspend fun restore(fileId: String) = error("unused")
    }

    private fun folderEntry(
        id: String,
        vararg path: String,
    ) = FolderUploadEntry.Folder(id, path.toList())

    private fun fileEntry(
        id: String,
        vararg path: String,
    ) = FolderUploadEntry.File(id, path.toList(), "content://$id", 3, "text/plain")

    private companion object {
        fun serverFolder(
            id: String,
            parentId: String?,
            name: String,
        ) = FileEntry(
            id,
            parentId,
            name,
            FileEntryType.FOLDER,
            null,
            0,
            FileEntryStatus.ACTIVE,
            1,
            null,
            Instant.EPOCH,
            Instant.EPOCH,
        )
    }
}
