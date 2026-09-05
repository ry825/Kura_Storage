package com.kurastorage.feature.files

import android.graphics.Bitmap
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.SemanticsNodeInteraction
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.StateRestorationTester
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import com.kurastorage.core.model.AdminStorageStatus
import com.kurastorage.core.model.ErrorCategory
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadOperation
import com.kurastorage.core.model.UploadState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import java.io.File
import java.time.Instant
import java.util.concurrent.atomic.AtomicInteger

class FileBrowserScreenTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun fileGridShowsOverflowAndEntry() {
        var backed = false
        var refreshed = false
        var searched = false
        compose.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = false, entries = listOf(file())),
                trashMode = false,
                onOpen = {},
                onShowDetails = {},
                onBack = { backed = true },
                onRefresh = { refreshed = true },
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
                onSearch = { searched = true },
            )
        }

        compose.onNodeWithText("My files").assertIsDisplayed()
        compose.onNodeWithContentDescription("Upload file").assertIsDisplayed()
        compose.onNodeWithText("document.txt").assertIsDisplayed()
        compose.onNodeWithContentDescription("Back").performClick()
        compose.onNodeWithContentDescription("Refresh files").performClick()
        compose.onNodeWithContentDescription("Search files").performClick()
        compose.runOnIdle {
            assertTrue(backed)
            assertTrue(refreshed)
            assertTrue(searched)
        }
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
        compose.onNodeWithText("Delete permanently").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Restore").performScrollTo().performClick()
        compose.onNodeWithText("Restore this item?").assertIsDisplayed()
    }

    @Test
    fun pausedUploadShowsConfirmedProgressResumeAndCancelConfirmation() {
        var retried = false
        var cancelled = false
        val operation =
            UploadOperation(
                "content://video",
                "root",
                "a-very-long-video-file-name.mp4",
                10,
                "video/mp4",
                "a".repeat(64),
                "key",
                "session",
                4,
                Instant.parse("2026-08-23T00:00:00Z"),
                UploadState.PAUSED,
            )
        compose.setContent {
            FileBrowserScreen(
                state =
                    FileBrowserState(
                        loading = false,
                        transfer = TransferEvent.UploadStatus(operation, "Connection interrupted", canRetry = true),
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
                onCancelTransfer = { cancelled = true },
                onRetryTransfer = { retried = true },
                onOpenDownload = {},
            )
        }

        compose.onNodeWithText("4 / 10 bytes").assertIsDisplayed()
        compose.onNodeWithText("a-very-long-video-file-name.mp4").assertIsDisplayed()
        compose
            .onNodeWithTag("transfer-status")
            .assert(
                SemanticsMatcher.expectValue(
                    SemanticsProperties.StateDescription,
                    "Upload paused; received bytes are safe on the server",
                ),
            )
        compose.onNodeWithTag("resume-upload").assertIsDisplayed().performClick()
        compose.runOnIdle { assertEquals(true, retried) }
        compose.onNodeWithTag("cancel-upload").performClick()
        compose.onNodeWithText("Cancel upload?").assertIsDisplayed()
        compose.onNodeWithTag("confirm-cancel-upload").performClick()
        compose.runOnIdle { assertEquals(true, cancelled) }
    }

    @Test
    fun uploadProgressHandlesZeroAndCompleteBoundariesAndDismissesCancellation() {
        var operation by
            mutableStateOf(
                UploadOperation(
                    "content://empty",
                    "root",
                    "empty-file-with-a-name-that-must-remain-accessible.bin",
                    0,
                    "application/octet-stream",
                    "a".repeat(64),
                    "key",
                    "session",
                    0,
                    Instant.parse("2026-08-23T00:00:00Z"),
                    UploadState.PAUSED,
                ),
            )
        compose.setContent {
            FileBrowserScreen(
                state =
                    FileBrowserState(
                        loading = false,
                        transfer = TransferEvent.UploadStatus(operation, canRetry = true),
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

        compose.onNodeWithText("0 / 0 bytes").assertIsDisplayed()
        compose.onNodeWithText(operation.fileName).assertIsDisplayed()
        compose.onNodeWithTag("cancel-upload").performClick()
        compose.onNodeWithText("Cancel upload?").assertIsDisplayed()
        compose.onNodeWithText("Keep uploading").performClick()
        compose.onAllNodesWithText("Cancel upload?").assertCountEquals(0)

        compose.runOnIdle {
            operation = operation.copy(size = 10, confirmedOffset = 10, state = UploadState.COMPLETED)
        }
        compose.onNodeWithText("10 / 10 bytes").assertIsDisplayed()
        compose.onNodeWithText("Upload completed").assertIsDisplayed()
        compose.onAllNodesWithTag("cancel-upload").assertCountEquals(0)
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

        compose.onNodeWithText("Photos").assertIsDisplayed()
        compose.onNodeWithContentDescription("More actions for Photos").performClick()
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
                                    destinationWritable = true,
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
        compose.onNodeWithText("renamed.txt").assertIsDisplayed()
        compose.onNodeWithText("Renamed to renamed.txt.").assertIsDisplayed()

        compose.onNodeWithContentDescription("More actions for renamed.txt").performClick()
        compose.onNodeWithText("Move").performScrollTo().performClick()
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

    @Test
    fun permanentDeleteSeparatesRestoreShowsIrreversibleFolderWarningAndDisablesWhileSubmitting() {
        val target = folder().copy(status = FileEntryStatus.TRASHED)
        var purgeRequests = 0
        val screenState =
            mutableStateOf(
                FileBrowserState(
                    loading = false,
                    entries = listOf(target),
                    selected = target,
                    retention = RetentionDisplayState(RetentionStage.BEFORE_DEADLINE, "Scheduled for automatic deletion: tomorrow"),
                ),
            )
        compose.setContent {
            FileBrowserScreen(
                state = screenState.value,
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
                onBeginPermanentDelete = {
                    screenState.value =
                        screenState.value.copy(
                            selected = null,
                            permanentDelete = PermanentDeleteState(target, "key"),
                        )
                },
                onConfirmPermanentDelete = {
                    purgeRequests++
                    screenState.value =
                        screenState.value.copy(
                            permanentDelete = screenState.value.permanentDelete?.copy(submitting = true),
                        )
                },
                onCancelPermanentDelete = {
                    screenState.value = screenState.value.copy(permanentDelete = null)
                },
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }
        compose.onNodeWithText("Restore").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Scheduled for automatic deletion: tomorrow").performScrollTo().assertIsDisplayed()
        compose
            .onNodeWithText("Delete permanently")
            .performScrollTo()
            .assertIsDisplayed()
            .performClick()
        compose.onNodeWithText("This operation cannot be undone.").assertIsDisplayed()
        compose.onNodeWithText("The folder and everything inside it will be permanently deleted.").assertIsDisplayed()
        compose.runOnIdle { assertEquals(0, purgeRequests) }
        compose.onNodeWithText("Cancel").performClick()
        compose.runOnIdle {
            assertEquals(0, purgeRequests)
            screenState.value = screenState.value.copy(selected = target)
        }
        compose.onNodeWithText("Delete permanently").performScrollTo().performClick()
        compose.onNodeWithTag("confirm-permanent-delete").performClick()
        compose.runOnIdle { assertEquals(1, purgeRequests) }
        compose.onNodeWithText("Deleting…").assertIsNotEnabled()
        compose.runOnIdle {
            screenState.value =
                screenState.value.copy(
                    entries = emptyList(),
                    permanentDelete = null,
                    placementResult = "Deleted permanently.",
                )
        }
        compose.onNodeWithText("Trash is empty.").assertIsDisplayed()
        compose.onNodeWithText("Deleted permanently.").assertIsDisplayed()
    }

    @Test
    fun unknownPermanentDeleteKeepsSameRetryAndRequiresRefreshOrRetry() {
        val target = file().copy(status = FileEntryStatus.TRASHED)
        var retries = 0
        val deletionState =
            mutableStateOf(
                PermanentDeleteState(
                    target = target,
                    idempotencyKey = "stable-key",
                    resultUnknown = true,
                    error =
                        BrowserError(
                            "The result is unknown because the connection was interrupted. Refresh to confirm.",
                            ErrorCategory.CONNECTION,
                            resultUnknown = true,
                        ),
                ),
            )
        compose.setContent {
            FileBrowserScreen(
                state =
                    FileBrowserState(
                        loading = false,
                        entries = listOf(target),
                        permanentDelete = deletionState.value,
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
                onConfirmPermanentDelete = { retries++ },
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onNodeWithText("Refresh to confirm").assertIsDisplayed()
        compose.onNodeWithText("Cancel").assertIsNotEnabled()
        compose.onNodeWithText("Retry same request").performClick()
        compose.runOnIdle { assertEquals(1, retries) }

        compose.runOnIdle {
            deletionState.value =
                deletionState.value.copy(
                    resultUnknown = false,
                    error =
                        BrowserError(
                            "This deletion key conflicts with another request. Refresh the list.",
                            ErrorCategory.CONFLICT,
                            ErrorCode.IDEMPOTENCY_CONFLICT,
                        ),
                )
        }
        compose.onNodeWithText("Refresh to confirm").assertIsDisplayed()
        compose.onNodeWithText("Refresh list first").assertIsNotEnabled()
    }

    @Test
    fun capacityWarningShowsAdminDetailsAndTrashActionWhileNormalStateIsHidden() {
        val warning = AdminStorageStatus("AVAILABLE", 100, 10, 20, true, 5, 1, 30, 0, null)
        val panelState = mutableStateOf(AdminStorageState(loading = false, status = warning))
        compose.setContent {
            AdminStoragePanel(panelState.value, {}, {})
        }
        compose.onNodeWithTag("capacity-warning").assertIsDisplayed()
        compose.onNodeWithText("Open Trash").assertIsDisplayed()
        compose
            .onNodeWithText(
                "The 30-day retention period is not shortened. Delete unneeded trash manually or expand storage.",
            ).assertIsDisplayed()

        compose.runOnIdle { panelState.value = AdminStorageState(loading = false, status = warning.copy(capacityWarning = false)) }
        compose.onAllNodesWithText("Storage capacity warning").assertCountEquals(0)
    }

    @Test
    fun missingEntryShowsAccessibleStateAndIndexOnlyConfirmation() {
        val missing =
            file().copy(
                status = FileEntryStatus.MISSING,
                missingDetectedAt = Instant.parse("2026-08-22T00:00:00Z"),
                missingLastCheckedAt = Instant.parse("2026-08-22T00:05:00Z"),
            )
        var rechecks = 0
        val screenState = mutableStateOf(FileBrowserState(loading = false, entries = listOf(missing), selected = missing))
        compose.setContent {
            FileBrowserScreen(
                state = screenState.value,
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
                onRecheckMissing = { rechecks++ },
                onBeginMissingIndexDelete = {
                    screenState.value = screenState.value.copy(selected = null, missingIndexDelete = MissingIndexDeleteState(it))
                },
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }

        compose.onAllNodesWithText("ファイルが見つかりません").assertCountEquals(2)
        compose.onNodeWithText("2026-08-22T00:05:00Z").performScrollTo().assertIsDisplayed()
        compose.onNodeWithTag("recheck-missing").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(1, rechecks) }
        compose.onNodeWithTag("delete-missing-index").performClick()
        compose.onNodeWithText("KuraStorageの索引だけを削除します。HDD上のファイルは削除しません。").assertIsDisplayed()
        compose.onNodeWithText("索引だけ削除").assertIsDisplayed()
        compose.onAllNodesWithText("Download").assertCountEquals(0)
        compose.onAllNodesWithText("Move to trash").assertCountEquals(0)
    }

    @Test
    fun candidateAndUnknownStatusesDoNotExposeDestructiveActions() {
        val candidate = file().copy(status = FileEntryStatus.MISSING_CANDIDATE)
        val unknown = file().copy(id = "future", name = "future.txt", status = FileEntryStatus.UNKNOWN)
        compose.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = false, entries = listOf(candidate, unknown)),
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

        compose.onNodeWithText("ファイルを確認中").assertIsDisplayed()
        compose.onNodeWithText("アプリの更新が必要です").assertIsDisplayed()
        compose.onAllNodesWithTag("delete-missing-index").assertCountEquals(0)
    }

    @Test
    fun sharedPermissionControlsAreFailClosedAcrossViewerContributorEditorManagerAndUnknown() {
        val permission = mutableStateOf(SharePermission.VIEWER)
        val source = mutableStateOf(PermissionSource.DIRECT)
        compose.setContent {
            FileBrowserScreen(
                state = sharedControlState(permission.value, source.value),
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
        compose.onNodeWithText("Download original").performScrollTo().assertIsDisplayed()
        compose.onAllNodesWithText("Photos").assertCountEquals(2)
        compose.onAllNodesWithText("Owner").assertCountEquals(2)
        compose.onAllNodesWithText("Read only").assertCountEquals(3)
        compose.onNodeWithText("Shared from").assertIsDisplayed()
        assertSharedControlCounts(create = 0, rename = 0, move = 0, trash = 0, sharing = 0)

        compose.runOnIdle {
            permission.value = SharePermission.CONTRIBUTOR
            source.value = PermissionSource.INHERITED
        }
        compose.onNodeWithText("Download original").performScrollTo().assertIsDisplayed()
        assertSharedControlCounts(create = 1, rename = 0, move = 0, trash = 0, sharing = 0)

        compose.runOnIdle {
            permission.value = SharePermission.EDITOR
            source.value = PermissionSource.DIRECT
        }
        assertSharedControlCounts(create = 1, rename = 1, move = 1, trash = 1, sharing = 0)

        compose.runOnIdle {
            permission.value = SharePermission.MANAGER
            source.value = PermissionSource.INHERITED
        }
        assertSharedControlCounts(create = 1, rename = 1, move = 1, trash = 1, sharing = 1)
        compose.onNodeWithText("Share").performScrollTo().assertIsDisplayed()

        compose.runOnIdle {
            source.value = PermissionSource.UNKNOWN
        }
        compose.onNodeWithText("Download original").performScrollTo().assertIsDisplayed()
        assertSharedControlCounts(create = 0, rename = 0, move = 0, trash = 0, sharing = 0)

        compose.runOnIdle {
            permission.value = SharePermission.UNKNOWN
            source.value = PermissionSource.OWNER
        }
        compose.onNodeWithText("Download original").performScrollTo().assertIsDisplayed()
        assertSharedControlCounts(create = 0, rename = 0, move = 0, trash = 0, sharing = 0)
    }

    @Test
    fun activeFileDetailNotifiesDisplayOnceUntilItIsClosedAndReopened() {
        val state = mutableStateOf(FileBrowserState(loading = false, selected = file()))
        var displayed = 0
        compose.setContent {
            FileBrowserScreen(
                state = state.value,
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
                onDetailDisplayed = { displayed++ },
                onDismissDetail = {},
                onCancelTransfer = {},
                onRetryTransfer = {},
                onOpenDownload = {},
            )
        }
        compose.runOnIdle {
            assertEquals(1, displayed)
            state.value = state.value.copy(historySyncError = "retry")
        }
        compose.runOnIdle { assertEquals(1, displayed) }
        compose.runOnIdle { state.value = state.value.copy(selected = null) }
        compose.runOnIdle { state.value = state.value.copy(selected = file()) }
        compose.runOnIdle { assertEquals(2, displayed) }
    }

    @Test
    fun unsupportedFileDetailsShowTypeReasonAndOnlySafeFallback() {
        val unsupported = file().copy(name = "archive.bin", mimeType = "application/x-private-archive")
        compose.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = false, entries = listOf(unsupported), selected = unsupported),
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

        compose.onNodeWithText("Unsupported file").assertIsDisplayed()
        compose.onNodeWithText("application/x-private-archive").assertIsDisplayed()
        compose.onNodeWithText("Download original").performScrollTo().assertIsDisplayed()
        compose.onAllNodesWithText("Open").assertCountEquals(0)
        compose.onAllNodesWithText("Open").assertCountEquals(0)
    }

    @Test
    fun listAndGridShareTypeMetadataFallbackAndLongName() {
        val longName = "a-very-long-file-name-that-must-remain-readable-and-ellipsize-safely.txt"
        compose.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = false, entries = listOf(folder(), file().copy(name = longName))),
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

        compose.onNodeWithContentDescription("Folder").assertIsDisplayed()
        compose.onNodeWithContentDescription("Text file").assertIsDisplayed()
        compose.onNodeWithContentDescription("Show as list").performClick()
        compose.onNodeWithText("Folders").assertIsDisplayed()
        compose.onNodeWithText("Files").assertIsDisplayed()
        compose.onNodeWithText(longName).assertIsDisplayed()
        compose.onNodeWithTag("file-list").captureEvidence("pr1-files-list.png")
    }

    @Test
    fun displayModeSurvivesStateRestorationAndRefreshIsDisabledWhileLoading() {
        val restoration = StateRestorationTester(compose)
        restoration.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = true, entries = listOf(file())),
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

        compose.onNodeWithContentDescription("Refreshing files").assertIsNotEnabled()
        compose.onNodeWithContentDescription("Show as list").performClick()
        compose.onNodeWithTag("file-list").assertIsDisplayed()
        restoration.emulateSavedInstanceStateRestore()
        compose.onNodeWithTag("file-list").assertIsDisplayed()
    }

    @Test
    fun compactLargeTextReadOnlyDetailsUseScrollableSheetAndFriendlyLabels() {
        val selected =
            file().copy(
                permission = SharePermission.VIEWER,
                permissionSource = PermissionSource.DIRECT,
                name = "a-very-long-read-only-document-name-that-needs-wrapping.txt",
            )
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale = 2f)) {
                Box(Modifier.size(width = 360.dp, height = 800.dp)) {
                    FileBrowserScreen(
                        state = FileBrowserState(loading = false, entries = listOf(selected), selected = selected, personalRoot = false),
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
            }
        }

        compose.onNodeWithTag("file-details-sheet").assertIsDisplayed().captureEvidence("pr1-file-details-200pct.png")
        compose.onAllNodesWithText("Read only").assertCountEquals(2)
        compose.onAllNodesWithText("Rename").assertCountEquals(0)
        compose.onAllNodesWithText("Move to Trash").assertCountEquals(0)
    }

    @Test
    fun compactTwoHundredPercentDarkFixtureRemainsReachable() {
        val entry = file().copy(name = "long-document-name-for-compact-layout.txt")
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale = 2f)) {
                com.kurastorage.core.ui.KuraStorageTheme(darkTheme = true) {
                    Box(Modifier.size(width = 360.dp, height = 800.dp).testTag("files-compact")) {
                        FileBrowserScreen(
                            state = FileBrowserState(loading = false, entries = listOf(entry)),
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
                }
            }
        }

        compose.onNodeWithTag("file-grid").assertIsDisplayed()
        compose.onNodeWithTag("upload-fab").assertIsDisplayed()
        compose.onNodeWithTag("files-compact").captureEvidence("pr1-files-grid-360dp-200pct-dark.png")
    }

    @Test
    fun landscapeFixtureKeepsFileActionsReachable() {
        compose.setContent {
            Box(Modifier.size(width = 800.dp, height = 360.dp).testTag("files-landscape")) {
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
        }

        compose.onNodeWithTag("file-grid").assertIsDisplayed()
        compose.onNodeWithContentDescription("More actions for document.txt").assertIsDisplayed()
        compose.onNodeWithTag("files-landscape").captureToImage()
    }

    @Test
    fun thousandEntryListAndGridComposeOnlyVisibleThumbnailWindow() {
        val thumbnails = AtomicInteger()
        val entries =
            (0 until 1_000).map { index ->
                file().copy(id = "file-$index", name = "photo-$index.jpg", mimeType = "image/jpeg")
            }
        compose.setContent {
            FileBrowserScreen(
                state = FileBrowserState(loading = false, entries = entries),
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
                thumbnail = { _, _ ->
                    SideEffect { thumbnails.incrementAndGet() }
                    androidx.compose.material3.Text("thumbnail")
                },
            )
        }

        compose.runOnIdle { assertTrue(thumbnails.get() < 100) }
        thumbnails.set(0)
        compose.onNodeWithContentDescription("Show as list").performClick()
        compose.runOnIdle { assertTrue(thumbnails.get() < 250) }
    }

    private fun sharedControlState(
        permission: SharePermission,
        source: PermissionSource,
    ): FileBrowserState {
        val entry = file().copy(permission = permission, permissionSource = source, shareTargetId = "shared-root")
        val currentFolder = folder().copy(permission = permission, permissionSource = source, shareTargetId = "shared-root")
        return FileBrowserState(
            loading = false,
            entries = listOf(entry),
            selected = entry,
            currentFolder = currentFolder,
            personalRoot = false,
        )
    }

    private fun SemanticsNodeInteraction.captureEvidence(fileName: String) {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val directory = File(requireNotNull(context.getExternalFilesDir(null)), "ui-evidence").apply { mkdirs() }
        File(directory, fileName).outputStream().use { stream ->
            captureToImage().asAndroidBitmap().compress(Bitmap.CompressFormat.PNG, 100, stream)
        }
    }

    private fun assertSharedControlCounts(
        create: Int,
        rename: Int,
        move: Int,
        trash: Int,
        sharing: Int,
    ) {
        compose.onAllNodesWithText("New folder").assertCountEquals(create)
        compose.onAllNodesWithTag("upload-fab").assertCountEquals(create)
        compose.onAllNodesWithText("Rename").assertCountEquals(rename)
        compose.onAllNodesWithText("Move").assertCountEquals(move)
        compose.onAllNodesWithText("Move to Trash").assertCountEquals(trash)
        compose.onAllNodesWithText("Share").assertCountEquals(sharing)
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
            owner = OwnerSummary("owner", "Owner"),
            permission = SharePermission.MANAGER,
            permissionSource = PermissionSource.OWNER,
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
        owner = OwnerSummary("owner", "Owner"),
        permission = SharePermission.MANAGER,
        permissionSource = PermissionSource.OWNER,
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
