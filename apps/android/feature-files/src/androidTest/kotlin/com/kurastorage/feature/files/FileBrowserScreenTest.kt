package com.kurastorage.feature.files

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
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

    private fun folder() =
        FileEntry(
            "folder",
            "root",
            "Photos",
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
