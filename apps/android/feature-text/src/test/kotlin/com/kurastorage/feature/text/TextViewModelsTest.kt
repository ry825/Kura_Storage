@file:Suppress("MaxLineLength")

package com.kurastorage.feature.text

import androidx.lifecycle.SavedStateHandle
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.TextFileRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.FilePage
import com.kurastorage.core.model.FileVersionChangeKind
import com.kurastorage.core.model.FileVersionItem
import com.kurastorage.core.model.FileVersionPage
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.TextDocument
import com.kurastorage.core.model.TextMutationResult
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.withContext
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class TextViewModelsTest {
    private val dispatcher = UnconfinedTestDispatcher()

    @Before fun setUp() = Dispatchers.setMain(dispatcher)

    @After fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `viewer is read only while editor becomes dirty and saves expected version`() =
        runTest(dispatcher) {
            val repository = FakeTextRepository()
            val viewer = TextEditorViewModel("file", FakeFiles(file(SharePermission.VIEWER)), repository, SavedStateHandle())
            assertEquals(TextEditorPhase.VIEWING, viewer.state.value.phase)
            assertFalse(viewer.state.value.canEdit)

            val editor = TextEditorViewModel("file", FakeFiles(file(SharePermission.EDITOR)), repository, SavedStateHandle())
            editor.beginEditing()
            editor.updateDraft("updated")
            assertTrue(editor.state.value.dirty)
            assertTrue(editor.requestExit())
            editor.save()

            assertEquals(TextEditorPhase.SAVED, editor.state.value.phase)
            assertEquals(
                2L,
                editor.state.value.document
                    ?.fileVersion,
            )
            assertEquals(Triple("updated", 1L, repository.lastOperationId), repository.lastSave)
        }

    @Test
    fun `version conflict loads current text and only offers reload diff or save as copy`() =
        runTest(dispatcher) {
            val repository = FakeTextRepository().apply { saveConflict = true }
            val viewModel = TextEditorViewModel("file", FakeFiles(file(SharePermission.EDITOR)), repository, SavedStateHandle())
            viewModel.beginEditing()
            viewModel.updateDraft("hello\nlocal")
            repository.document = document("hello\nserver", 2)
            viewModel.save()

            assertEquals(TextEditorPhase.CONFLICT, viewModel.state.value.phase)
            assertEquals(
                2L,
                viewModel.state.value.conflict
                    ?.current
                    ?.fileVersion,
            )
            assertTrue(
                viewModel.state.value.diff
                    .isNotEmpty(),
            )
            assertFalse(viewModel.state.value.forceOverwriteAvailable)
            viewModel.reloadAfterConflict()
            assertEquals("hello\nserver", viewModel.state.value.draft)
            assertFalse(viewModel.state.value.dirty)
        }

    @Test
    fun `saved state is bounded and restored only for the matching base version`() =
        runTest(dispatcher) {
            val handle = SavedStateHandle()
            val repository = FakeTextRepository()
            val viewModel = TextEditorViewModel("file", FakeFiles(file(SharePermission.EDITOR)), repository, handle)
            viewModel.beginEditing()
            viewModel.updateDraft("x".repeat(TextEditorViewModel.MAX_SAVED_DRAFT_BYTES + 1))
            assertFalse(viewModel.state.value.draftPersisted)

            val restoredHandle = SavedStateHandle(mapOf("text.fileId" to "file", "text.baseVersion" to 1L, "text.draft" to "restored"))
            val restored = TextEditorViewModel("file", FakeFiles(file(SharePermission.EDITOR)), repository, restoredHandle)
            assertEquals("restored", restored.state.value.draft)
            assertTrue(restored.state.value.dirty)
        }

    @Test
    fun `oversized save is rejected and discard confirmation can be dismissed or accepted`() =
        runTest(dispatcher) {
            val viewModel =
                TextEditorViewModel(
                    "file",
                    FakeFiles(file(SharePermission.EDITOR)),
                    FakeTextRepository(),
                    SavedStateHandle(),
                )
            viewModel.beginEditing()
            viewModel.updateDraft("😀".repeat(262_145))
            viewModel.save()
            assertEquals(ErrorCode.TEXT_SIZE_LIMIT_EXCEEDED, viewModel.state.value.errorCode)
            assertTrue(viewModel.requestExit())
            viewModel.dismissDiscardConfirmation()
            assertFalse(viewModel.state.value.showDiscardConfirmation)
            viewModel.requestExit()
            viewModel.discardChanges()
            assertFalse(viewModel.state.value.dirty)
            assertFalse(viewModel.state.value.showDiscardConfirmation)
        }

    @Test
    fun `history pages previews compares and restores after confirmation`() =
        runTest(dispatcher) {
            val repository = FakeTextRepository()
            val viewModel = VersionHistoryViewModel("file", FakeFiles(file(SharePermission.EDITOR)), repository)
            assertEquals(
                listOf(2L),
                viewModel.state.value.items
                    .map(FileVersionItem::version),
            )
            viewModel.loadMore()
            assertEquals(
                listOf(2L, 1L),
                viewModel.state.value.items
                    .map(FileVersionItem::version),
            )
            viewModel.preview(1)
            assertEquals(
                1L,
                viewModel.state.value.preview
                    ?.fileVersion,
            )
            assertNotNull(viewModel.state.value.previewDiff)
            viewModel.requestRestore(1)
            assertEquals(null, viewModel.state.value.preview)
            assertEquals(1L, viewModel.state.value.restoreConfirmationVersion)
            viewModel.confirmRestore()
            assertEquals(
                2L,
                viewModel.state.value.current
                    ?.fileVersion,
            )
            assertEquals(1L, repository.lastRestoredVersion)
        }

    @Test
    fun `restore version conflict remains explicit and never reports success`() =
        runTest(dispatcher) {
            val repository = FakeTextRepository().apply { restoreConflict = true }
            val viewModel = VersionHistoryViewModel("file", FakeFiles(file(SharePermission.EDITOR)), repository)
            viewModel.requestRestore(1)
            viewModel.confirmRestore()

            assertTrue(viewModel.state.value.restoreConflict)
            assertEquals(ErrorCode.FILE_VERSION_CONFLICT, viewModel.state.value.errorCode)
            assertEquals(1L, repository.document.fileVersion)
        }

    @Test
    fun `history dismisses dialogs and revalidates restore permission and connectivity`() =
        runTest(dispatcher) {
            val files = FakeFiles(file(SharePermission.EDITOR))
            val repository = FakeTextRepository()
            val viewModel = VersionHistoryViewModel("file", files, repository)
            viewModel.preview(1)
            viewModel.dismissPreview()
            assertEquals(null, viewModel.state.value.preview)
            viewModel.requestRestore(1)
            viewModel.dismissRestore()
            assertEquals(null, viewModel.state.value.restoreConfirmationVersion)

            viewModel.requestRestore(1)
            files.entry = file(SharePermission.VIEWER)
            viewModel.confirmRestore()
            assertEquals(ErrorCode.FILE_NOT_FOUND, viewModel.state.value.errorCode)

            files.entry = file(SharePermission.EDITOR)
            viewModel.refresh()
            repository.currentFailure = KuraStorageException.Network(java.io.IOException("offline"))
            viewModel.requestRestore(1)
            viewModel.confirmRestore()
            assertEquals(ErrorCode.UNKNOWN, viewModel.state.value.errorCode)
            assertFalse(viewModel.state.value.restoring)
        }

    @Test
    fun `permission is revalidated on save and viewer cannot request restore`() =
        runTest(dispatcher) {
            val files = FakeFiles(file(SharePermission.EDITOR))
            val repository = FakeTextRepository()
            val editor = TextEditorViewModel("file", files, repository, SavedStateHandle())
            editor.beginEditing()
            editor.updateDraft("updated")
            files.entry = file(SharePermission.VIEWER)
            editor.save()
            assertEquals(TextEditorPhase.ERROR, editor.state.value.phase)
            assertEquals(ErrorCode.FILE_NOT_FOUND, editor.state.value.errorCode)
            assertEquals(null, repository.lastSave)

            files.entry = file(SharePermission.EDITOR)
            editor.updateDraft("retry")
            editor.save()
            assertEquals(TextEditorPhase.SAVED, editor.state.value.phase)
            assertEquals(
                "retry",
                editor.state.value.document
                    ?.content,
            )

            val history = VersionHistoryViewModel("file", files, repository)
            files.entry = file(SharePermission.VIEWER)
            history.refresh()
            assertFalse(history.state.value.canRestore)
            history.requestRestore(1)
            assertEquals(null, history.state.value.restoreConfirmationVersion)
        }

    @Test
    fun `offline and forbidden reads expose fail closed typed errors`() =
        runTest(dispatcher) {
            val network = FakeTextRepository().apply { currentFailure = KuraStorageException.Network(java.io.IOException("offline")) }
            val offline = TextEditorViewModel("file", FakeFiles(file(SharePermission.VIEWER)), network, SavedStateHandle())
            assertEquals(TextEditorPhase.ERROR, offline.state.value.phase)
            assertEquals(ErrorCode.UNKNOWN, offline.state.value.errorCode)

            val forbidden =
                FakeTextRepository().apply {
                    currentFailure = KuraStorageException.Api(ApiError(ErrorCode.FILE_NOT_FOUND, "hidden", 404))
                }
            val hidden = TextEditorViewModel("file", FakeFiles(file(SharePermission.VIEWER)), forbidden, SavedStateHandle())
            assertEquals(ErrorCode.FILE_NOT_FOUND, hidden.state.value.errorCode)
            assertEquals("hidden", hidden.state.value.requestId)
        }

    @Test
    fun `cancelled older load cannot overwrite the newest generation`() =
        runTest(dispatcher) {
            val repository = StaleTextRepository()
            val viewModel = TextEditorViewModel("file", FakeFiles(file(SharePermission.VIEWER)), repository, SavedStateHandle())
            viewModel.load()
            assertEquals(
                "new",
                viewModel.state.value.document
                    ?.content,
            )
            repository.first.complete(document("old", 1))
            assertEquals(
                "new",
                viewModel.state.value.document
                    ?.content,
            )
        }

    private class FakeTextRepository : TextFileRepository {
        var document = document("hello", 1)
        var saveConflict = false
        var lastSave: Triple<String, Long, String>? = null
        var lastOperationId = ""
        var lastRestoredVersion: Long? = null
        var currentFailure: Throwable? = null
        var restoreConflict = false

        override suspend fun current(fileId: String) = currentFailure?.let { throw it } ?: document

        override suspend fun save(
            fileId: String,
            content: String,
            expectedVersion: Long,
            operationId: String,
        ): TextMutationResult {
            lastOperationId = operationId
            lastSave = Triple(content, expectedVersion, operationId)
            if (saveConflict) throw KuraStorageException.Api(ApiError(ErrorCode.FILE_VERSION_CONFLICT, "conflict", 409))
            document = document(content, expectedVersion + 1)
            return mutation(document.fileVersion, FileVersionChangeKind.TEXT_EDIT)
        }

        override suspend fun versions(
            fileId: String,
            page: Int,
            pageSize: Int,
        ) = FileVersionPage(listOf(version(if (page == 1) 2 else 1)), page, 1, 2)

        override suspend fun version(
            fileId: String,
            version: Long,
        ) = document("old-$version", version)

        override suspend fun restore(
            fileId: String,
            version: Long,
            expectedVersion: Long,
            operationId: String,
        ): TextMutationResult {
            if (restoreConflict) {
                throw KuraStorageException.Api(ApiError(ErrorCode.FILE_VERSION_CONFLICT, "restore-conflict", 409))
            }
            lastRestoredVersion = version
            document = document("old-$version", expectedVersion + 1)
            return mutation(document.fileVersion, FileVersionChangeKind.RESTORE)
        }
    }

    private class FakeFiles(
        var entry: FileEntry,
    ) : FileRepository {
        override suspend fun detail(fileId: String) = entry

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ) = FilePage(null, listOf(entry), 1, 1, 1)

        override suspend fun createFolder(
            parentId: String?,
            name: String,
        ): FileEntry = error("unused")

        override suspend fun rename(
            fileId: String,
            name: String,
        ): FileEntry = error("unused")

        override suspend fun move(
            fileId: String,
            targetParentId: String,
        ): FileEntry = error("unused")

        override suspend fun trash(fileId: String): FileEntry = error("unused")

        override suspend fun listTrash(
            page: Int,
            pageSize: Int,
        ): FilePage = error("unused")

        override suspend fun restore(fileId: String): FileEntry = error("unused")
    }

    private class StaleTextRepository : TextFileRepository {
        val first = CompletableDeferred<TextDocument>()
        private var calls = 0

        override suspend fun current(fileId: String): TextDocument {
            calls += 1
            return if (calls == 1) withContext(NonCancellable) { first.await() } else document("new", 2)
        }

        override suspend fun save(
            fileId: String,
            content: String,
            expectedVersion: Long,
            operationId: String,
        ): TextMutationResult = error("unused")

        override suspend fun versions(
            fileId: String,
            page: Int,
            pageSize: Int,
        ): FileVersionPage = error("unused")

        override suspend fun version(
            fileId: String,
            version: Long,
        ): TextDocument = error("unused")

        override suspend fun restore(
            fileId: String,
            version: Long,
            expectedVersion: Long,
            operationId: String,
        ): TextMutationResult = error("unused")
    }

    private companion object {
        fun document(
            content: String,
            version: Long,
        ) = TextDocument(content, "UTF-8", version, content.length.toLong(), "a".repeat(64))

        fun mutation(
            version: Long,
            kind: FileVersionChangeKind,
        ) = TextMutationResult(version, 5, "a".repeat(64), kind, Instant.EPOCH)

        fun version(value: Long) = FileVersionItem(value, 5, "a".repeat(64), FileVersionChangeKind.TEXT_EDIT, "Ryo", Instant.EPOCH)

        fun file(permission: SharePermission) =
            FileEntry(
                "file",
                null,
                "notes.txt",
                FileEntryType.FILE,
                "text/plain",
                5,
                FileEntryStatus.ACTIVE,
                1,
                null,
                Instant.EPOCH,
                Instant.EPOCH,
                owner = OwnerSummary("owner", "Owner"),
                permission = permission,
                permissionSource = PermissionSource.DIRECT,
            )
    }
}
