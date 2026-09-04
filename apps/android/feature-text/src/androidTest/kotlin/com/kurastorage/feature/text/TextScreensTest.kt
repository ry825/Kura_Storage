package com.kurastorage.feature.text

import androidx.activity.ComponentActivity
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.unit.Density
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileVersionChangeKind
import com.kurastorage.core.model.FileVersionItem
import com.kurastorage.core.model.TextConflict
import com.kurastorage.core.model.TextDocument
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class TextScreensTest {
    @get:Rule val compose = createAndroidComposeRule<ComponentActivity>()

    @Test
    fun viewer_exposes_read_only_content_and_history_action() {
        compose.setContent {
            TextEditorScreen(
                state = TextEditorUiState(document = document(), draft = "hello", phase = TextEditorPhase.VIEWING),
                onBack = {},
                onRequestExit = { false },
                onDismissDiscard = {},
                onDiscard = {},
                onBeginEdit = {},
                onDraftChange = {},
                onSave = {},
                onReloadConflict = {},
                onSaveAsCopy = {},
                onHistory = {},
            )
        }
        compose.onNodeWithContentDescription("Text file content").assertIsDisplayed()
        compose.onNodeWithText("Read only").assertIsDisplayed()
        compose.onNodeWithText("History").assertHasClickAction()
    }

    @Test
    fun conflict_exposes_accessible_editor_reload_and_save_as_copy_without_force_overwrite() {
        compose.setContent {
            TextEditorScreen(
                state =
                    TextEditorUiState(
                        document = document(),
                        draft = "local",
                        phase = TextEditorPhase.CONFLICT,
                        dirty = true,
                        canEdit = true,
                        conflict = TextConflict("local", 1, document("server", 2)),
                        diff = BoundedLineDiff.compare("server", "local"),
                        diffTruncated = true,
                    ),
                onBack = {},
                onRequestExit = { true },
                onDismissDiscard = {},
                onDiscard = {},
                onBeginEdit = {},
                onDraftChange = {},
                onSave = {},
                onReloadConflict = {},
                onSaveAsCopy = {},
                onHistory = {},
            )
        }
        compose.onNodeWithContentDescription("Text file content editor").assertIsDisplayed()
        compose.onNodeWithTag("reload-conflict").assertHasClickAction()
        compose.onNodeWithTag("save-as-copy").assertHasClickAction()
        compose.onNodeWithText("− server / + local", substring = true).performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Comparison limit reached").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Force overwrite").assertDoesNotExist()
    }

    @Test
    fun failed_save_keeps_the_draft_editable_and_retryable() {
        compose.setContent {
            TextEditorScreen(
                state =
                    TextEditorUiState(
                        document = document(),
                        draft = "unsaved",
                        phase = TextEditorPhase.ERROR,
                        dirty = true,
                        canEdit = true,
                        errorCode = ErrorCode.RATE_LIMIT_EXCEEDED,
                    ),
                onBack = {},
                onRequestExit = { true },
                onDismissDiscard = {},
                onDiscard = {},
                onBeginEdit = {},
                onDraftChange = {},
                onSave = {},
                onReloadConflict = {},
                onSaveAsCopy = {},
                onHistory = {},
            )
        }
        compose.onNodeWithContentDescription("Text file content editor").assertIsDisplayed()
        compose.onNodeWithTag("save-text").assertIsEnabled()
        compose.onNodeWithText("RATE_LIMIT_EXCEEDED", substring = true).performScrollTo().assertIsDisplayed()
    }

    @Test
    fun dirty_exit_offers_save_discard_and_cancel() {
        var saveAndExit = false
        var discarded = false
        compose.setContent {
            TextEditorScreen(
                state =
                    TextEditorUiState(
                        document = document(),
                        draft = "unsaved",
                        phase = TextEditorPhase.EDITING,
                        dirty = true,
                        canEdit = true,
                        showDiscardConfirmation = true,
                    ),
                onBack = {},
                onRequestExit = { true },
                onDismissDiscard = {},
                onDiscard = { discarded = true },
                onBeginEdit = {},
                onDraftChange = {},
                onSave = {},
                onReloadConflict = {},
                onSaveAsCopy = {},
                onHistory = {},
                onSaveAndExit = { saveAndExit = true },
            )
        }

        compose.onNodeWithText("Save and leave").performClick()
        compose.runOnIdle { assertTrue(saveAndExit) }
        compose.onNodeWithText("Discard").performClick()
        compose.runOnIdle { assertTrue(discarded) }
        compose.onNodeWithText("Cancel").assertHasClickAction()
    }

    @Test
    fun compact_two_hundred_percent_text_keeps_editor_status_and_save_reachable() {
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale = 2f)) {
                TextEditorScreen(
                    state =
                        TextEditorUiState(
                            document = document("line\n".repeat(80)),
                            draft = "changed\n".repeat(80),
                            phase = TextEditorPhase.EDITING,
                            dirty = true,
                            canEdit = true,
                        ),
                    onBack = {},
                    onRequestExit = { true },
                    onDismissDiscard = {},
                    onDiscard = {},
                    onBeginEdit = {},
                    onDraftChange = {},
                    onSave = {},
                    onReloadConflict = {},
                    onSaveAsCopy = {},
                    onHistory = {},
                )
            }
        }

        compose.onNodeWithTag("text-dirty").performScrollTo().assertIsDisplayed()
        compose.onNodeWithTag("save-text").performScrollTo().assertIsDisplayed()
    }

    @Test
    fun history_exposes_restore_conflict_metadata_and_bounded_loading_state() {
        compose.setContent {
            VersionHistoryScreen(
                state =
                    VersionHistoryUiState(
                        loading = false,
                        loadingMore = true,
                        items =
                            listOf(
                                FileVersionItem(
                                    2,
                                    5,
                                    "a".repeat(64),
                                    FileVersionChangeKind.EXTERNAL_CHANGE,
                                    "External change",
                                    Instant.EPOCH,
                                ),
                            ),
                        page = 1,
                        hasNextPage = true,
                        restoreConflict = true,
                        errorCode = ErrorCode.FILE_VERSION_CONFLICT,
                    ),
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onPreview = {},
                onDismissPreview = {},
                onRequestRestore = {},
                onDismissRestore = {},
                onConfirmRestore = {},
            )
        }
        compose.onNodeWithTag("history-list").assertIsDisplayed()
        compose.onNodeWithText("The current version changed", substring = true).assertIsDisplayed()
        compose.onNodeWithText("External change").assertIsDisplayed()
        compose.onNodeWithTag("history-load-more").performScrollTo().assertIsNotEnabled()
    }

    @Test
    fun conflict_screen_can_be_captured_for_screenshot_regression() {
        compose.setContent {
            TextEditorScreen(
                state =
                    TextEditorUiState(
                        document = document(),
                        draft = "local",
                        phase = TextEditorPhase.CONFLICT,
                        dirty = true,
                        canEdit = true,
                        conflict = TextConflict("local", 1, document("server", 2)),
                        diff = BoundedLineDiff.compare("server", "local"),
                    ),
                onBack = {},
                onRequestExit = { true },
                onDismissDiscard = {},
                onDiscard = {},
                onBeginEdit = {},
                onDraftChange = {},
                onSave = {},
                onReloadConflict = {},
                onSaveAsCopy = {},
                onHistory = {},
            )
        }

        val screenshot = compose.onRoot().captureToImage()

        assertTrue(screenshot.width > 0 && screenshot.height > 0)
    }

    @Test
    fun history_screen_can_be_captured_for_screenshot_regression() {
        compose.setContent {
            VersionHistoryScreen(
                state =
                    VersionHistoryUiState(
                        loading = false,
                        items =
                            listOf(
                                FileVersionItem(
                                    2,
                                    5,
                                    "a".repeat(64),
                                    FileVersionChangeKind.TEXT_EDIT,
                                    "Editor",
                                    Instant.EPOCH,
                                ),
                            ),
                    ),
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onPreview = {},
                onDismissPreview = {},
                onRequestRestore = {},
                onDismissRestore = {},
                onConfirmRestore = {},
            )
        }

        val screenshot = compose.onRoot().captureToImage()

        assertTrue(screenshot.width > 0 && screenshot.height > 0)
    }

    private fun document(
        content: String = "hello",
        version: Long = 1,
    ) = TextDocument(content, "UTF-8", version, content.length.toLong(), "a".repeat(64))
}
