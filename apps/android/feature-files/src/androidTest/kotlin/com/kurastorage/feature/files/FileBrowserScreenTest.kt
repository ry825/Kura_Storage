package com.kurastorage.feature.files

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.text.AnnotatedString
import com.kurastorage.core.model.ErrorCategory
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class FileBrowserScreenTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun fileListShowsActionsAndEntry() {
        compose.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = false, entries = listOf(file())),
                trashMode = false,
                onOpen = {},
                onShowDetails = {},
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onCreateFolder = {},
                onChooseUpload = {},
                onChooseDownload = {},
                onTrash = {},
                onRestore = {},
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onNodeWithText("My files").assertIsDisplayed()
        compose.onNodeWithText("Upload").assertIsDisplayed()
        compose.onNodeWithText("File: document.txt").assertIsDisplayed()
    }

    @Test
    fun folderCreationSubmitsEnteredName() {
        var createdName: String? = null
        compose.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = false),
                trashMode = false,
                onOpen = {},
                onShowDetails = {},
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onCreateFolder = { createdName = it },
                onChooseUpload = {},
                onChooseDownload = {},
                onTrash = {},
                onRestore = {},
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onNodeWithText("New folder").performClick()
        compose.onNodeWithText("Name").performTextInput("Photos")
        compose.onNodeWithText("Create").performClick()
        compose.runOnIdle { assertEquals("Photos", createdName) }
    }

    @Test
    fun trashShowsRestoreConfirmationAndTransferProgress() {
        val selected = file()
        compose.setContent {
            FileBrowserScreen(
                state =
                    FileBrowserState(
                        loading = false,
                        entries = listOf(selected),
                        selected = selected,
                        transfer =
                            com.kurastorage.core.model.TransferEvent
                                .Progress(2, 5),
                    ),
                trashMode = true,
                onOpen = {},
                onShowDetails = {},
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onCreateFolder = {},
                onChooseUpload = {},
                onChooseDownload = {},
                onTrash = {},
                onRestore = {},
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onNodeWithText("Trash").assertIsDisplayed()
        compose.onNodeWithText("2 / 5 bytes").assertIsDisplayed()
        compose.onNodeWithText("Restore").performClick()
        compose.onNodeWithText("Restore this item?").assertIsDisplayed()
    }

    @Test
    fun activeFolderExposesDetailsAction() {
        val folder = folder()
        var selected: FileEntry? = null
        compose.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = false, entries = listOf(folder)),
                trashMode = false,
                onOpen = {},
                onShowDetails = { selected = it },
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onCreateFolder = {},
                onChooseUpload = {},
                onChooseDownload = {},
                onTrash = {},
                onRestore = {},
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onNodeWithText("Folder: Photos").assertIsDisplayed()
        compose.onNodeWithText("Actions").performClick()
        compose.runOnIdle { assertEquals(folder, selected) }
    }

    @Test
    fun renameDialogShowsCurrentNameLoadingAndConflictError() {
        var submitted = false
        val selected = file()
        compose.setContent {
            FileBrowserScreen(
                state =
                    FileBrowserState(
                        loading = false,
                        entries = listOf(selected),
                        rename =
                            RenameState(
                                target = selected,
                                input = selected.name,
                                submitting = true,
                                error =
                                    BrowserError(
                                        "An item with the same name already exists. Choose another name or folder.",
                                        ErrorCategory.CONFLICT,
                                        ErrorCode.FILE_NAME_CONFLICT,
                                    ),
                            ),
                    ),
                trashMode = false,
                onOpen = {},
                onShowDetails = {},
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onCreateFolder = {},
                onChooseUpload = {},
                onChooseDownload = {},
                onTrash = {},
                onRestore = {},
                onSubmitRename = { submitted = true },
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onNodeWithText("Rename document.txt").assertIsDisplayed()
        compose
            .onNodeWithTag("rename-input")
            .assert(SemanticsMatcher.expectValue(SemanticsProperties.EditableText, AnnotatedString("document.txt")))
            .assertIsNotEnabled()
        compose.onNodeWithText("Renaming…").assertIsNotEnabled()
        compose.onNodeWithText("An item with the same name already exists. Choose another name or folder.").assertIsDisplayed()
        compose.runOnIdle { assertEquals(false, submitted) }
    }

    @Test
    fun fakeServerFlowRenamesAndMovesFileFromActions() {
        compose.setContent {
            var state by remember {
                mutableStateOf(FileBrowserState(loading = false, entries = listOf(file()), selected = file()))
            }
            FileBrowserScreen(
                state = state,
                trashMode = false,
                onOpen = {},
                onShowDetails = { state = state.copy(selected = it) },
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onCreateFolder = {},
                onChooseUpload = {},
                onChooseDownload = {},
                onTrash = {},
                onRestore = {},
                onRename = {
                    state = state.copy(selected = null, rename = RenameState(it, it.name))
                },
                onRenameInput = { input -> state = state.copy(rename = state.rename?.copy(input = input)) },
                onSubmitRename = {
                    val renamed = state.entries.single().copy(name = checkNotNull(state.rename).input)
                    state =
                        state.copy(
                            entries = listOf(renamed),
                            rename = null,
                            placementResult = "Renamed to ${renamed.name}.",
                        )
                },
                onMove = { target ->
                    state =
                        state.copy(
                            selected = null,
                            movePicker =
                                MovePickerState(
                                    target = target,
                                    currentFolderId = "destination",
                                    currentFolderName = "Destination",
                                    loading = false,
                                ),
                        )
                },
                onConfirmMove = {
                    state = state.copy(movePicker = null, placementResult = "Moved renamed.txt.")
                },
                onDismissDetail = { state = state.copy(selected = null) },
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onNodeWithText("Rename").performClick()
        compose.onNodeWithTag("rename-input").performTextReplacement("renamed.txt")
        compose.onNodeWithText("Rename").performClick()
        compose.onNodeWithText("File: renamed.txt").assertIsDisplayed()
        compose.onNodeWithText("Renamed to renamed.txt.").assertIsDisplayed()

        compose.onNodeWithText("Actions").performClick()
        compose.onNodeWithText("Move").performClick()
        compose.onNodeWithText("Destination: Destination").assertIsDisplayed()
        compose.onNodeWithTag("move-confirm").assertIsEnabled().performClick()
        compose.onNodeWithText("Moved renamed.txt.").assertIsDisplayed()
    }

    @Test
    fun movePickerBlocksCurrentParentAndTargetButAllowsNavigationAndConfirmation() {
        val target = folder()
        val destination = folder("destination", "root", "Destination")
        var opened: FileEntry? = null
        var confirmed = false
        compose.setContent {
            FileBrowserScreen(
                state =
                    FileBrowserState(
                        loading = false,
                        entries = listOf(target),
                        movePicker =
                            MovePickerState(
                                target = target,
                                currentFolderId = "root",
                                currentFolderName = "My files",
                                folders = listOf(target, destination),
                                loading = false,
                            ),
                    ),
                trashMode = false,
                onOpen = {},
                onShowDetails = {},
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onCreateFolder = {},
                onChooseUpload = {},
                onChooseDownload = {},
                onTrash = {},
                onRestore = {},
                onOpenMoveFolder = { opened = it },
                onConfirmMove = { confirmed = true },
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onNodeWithText("This is the current folder.").assertIsDisplayed()
        compose.onNodeWithTag("move-folder-folder").assertIsNotEnabled()
        compose.onNodeWithTag("move-folder-destination").assertIsEnabled().performClick()
        compose.runOnIdle { assertEquals(destination, opened) }
        compose.onNodeWithTag("move-confirm").assertIsNotEnabled()
        compose.runOnIdle { assertEquals(false, confirmed) }
    }

    @Test
    fun movePickerShowsCycleStorageRecoveryAndUnknownResultErrors() {
        val target = folder()
        val messages =
            listOf(
                "This folder cannot be moved there. Choose another folder." to ErrorCode.FILE_MOVE_CYCLE,
                "Storage is unavailable." to ErrorCode.STORAGE_UNAVAILABLE,
                "Recovery is required before this item can be changed." to ErrorCode.RECOVERY_REQUIRED,
                "The result is unknown because the connection was interrupted. Refresh to confirm." to null,
            )
        val errorState = mutableStateOf(error(messages.first().first, messages.first().second))
        compose.setContent {
            FileBrowserScreen(
                state =
                    FileBrowserState(
                        loading = false,
                        movePicker =
                            MovePickerState(
                                target = target,
                                currentFolderId = "destination",
                                currentFolderName = "Destination",
                                loading = false,
                                error = errorState.value,
                            ),
                    ),
                trashMode = false,
                onOpen = {},
                onShowDetails = {},
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onCreateFolder = {},
                onChooseUpload = {},
                onChooseDownload = {},
                onTrash = {},
                onRestore = {},
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }
        messages.forEach { (message, code) ->
            compose.runOnIdle { errorState.value = error(message, code) }
            compose.onNodeWithText(message).assertIsDisplayed()
        }
    }

    @Test
    fun trashDoesNotExposeRenameOrMoveActions() {
        compose.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = false, entries = listOf(file(), folder())),
                trashMode = true,
                onOpen = {},
                onShowDetails = {},
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onCreateFolder = {},
                onChooseUpload = {},
                onChooseDownload = {},
                onTrash = {},
                onRestore = {},
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onAllNodesWithText("Actions").assertCountEquals(0)
        compose.onAllNodesWithText("Rename").assertCountEquals(0)
        compose.onAllNodesWithText("Move").assertCountEquals(0)
    }

    private fun file() =
        FileEntry(
            "file",
            "root",
            "document.txt",
            FileEntryType.FILE,
            "text/plain",
            5,
            FileEntryStatus.ACTIVE,
            1,
            null,
            Instant.EPOCH,
            Instant.EPOCH,
        )

    private fun folder(
        id: String = "folder",
        parentId: String? = "root",
        name: String = "Photos",
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

    private fun error(
        message: String,
        code: ErrorCode?,
    ) = BrowserError(
        message,
        if (code == ErrorCode.FILE_MOVE_CYCLE) ErrorCategory.CONFLICT else ErrorCategory.STORAGE,
        code,
        resultUnknown = code == null,
    )
}
