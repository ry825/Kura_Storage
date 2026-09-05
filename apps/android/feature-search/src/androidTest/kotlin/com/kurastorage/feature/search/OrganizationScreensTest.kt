package com.kurastorage.feature.search

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.EntryOrganizationState
import com.kurastorage.core.model.FavoriteItem
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.TagItem
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class OrganizationScreensTest {
    @get:Rule val compose = createComposeRule()

    @Test fun favoritesShowsMetadataPagingAndOpensOnlyActiveItems() {
        var opened = false
        compose.setContent {
            Box(Modifier.width(320.dp)) {
                FavoritesScreen(
                    FavoritesUiState(
                        listOf(FavoriteItem(metadata().copy(shareTargetId = TAG.id), NOW)),
                        loading = false,
                        canLoadMore = true,
                    ),
                    {},
                    {},
                    {},
                    { opened = true },
                    listOf(SearchFilterOption(TAG.id, "Shared folder")),
                )
            }
        }
        compose.onNodeWithTag("favorite-$ENTRY").performClick()
        compose.onNodeWithText("Shared from: Shared folder").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Load more").performScrollTo().assertIsDisplayed()
        compose.runOnIdle { assertTrue(opened) }
    }

    @Test fun favoriteThumbnailEntryTapAndDetailsOverflowRemainIndependent() {
        var opened = 0
        var details = 0
        val favorite = FavoriteItem(metadata().copy(name = "Holiday photo.jpg", mimeType = "image/jpeg"), NOW)
        compose.setContent {
            Box(Modifier.width(320.dp)) {
                FavoritesScreen(
                    state = FavoritesUiState(listOf(favorite), loading = false),
                    onBack = {},
                    onRefresh = {},
                    onLoadMore = {},
                    onOpen = { opened++ },
                    thumbnail = { androidx.compose.material3.Text("Photo preview") },
                    onDetails = { details++ },
                )
            }
        }

        compose.onNodeWithText("Photo preview").assertIsDisplayed()
        compose.onNodeWithTag("favorite-$ENTRY").performClick()
        compose.onNodeWithContentDescription("Favorite details: Holiday photo.jpg").performClick()
        compose.runOnIdle {
            assertTrue(opened == 1)
            assertTrue(details == 1)
        }
    }

    @Test fun favoriteFallbackUsesATypeIconWithoutBlockingTheRow() {
        val favorite = FavoriteItem(metadata().copy(name = "notes.txt", mimeType = "text/plain"), NOW)
        compose.setContent {
            FavoritesScreen(FavoritesUiState(listOf(favorite), loading = false), {}, {}, {}, {})
        }

        compose.onNodeWithTag("favorite-$ENTRY").assertIsDisplayed()
        compose.onNodeWithContentDescription("File").assertIsDisplayed()
    }

    @Test fun missingFavoriteKeepsDetailsAvailableWhileEntryTapIsDisabled() {
        var opened = 0
        var details = 0
        val favorite = FavoriteItem(metadata().copy(name = "missing.jpg", status = FileEntryStatus.MISSING), NOW)
        compose.setContent {
            FavoritesScreen(
                state = FavoritesUiState(listOf(favorite), loading = false),
                onBack = {},
                onRefresh = {},
                onLoadMore = {},
                onOpen = { opened++ },
                onDetails = { details++ },
            )
        }

        compose.onNodeWithTag("favorite-$ENTRY").assertIsNotEnabled().performClick()
        compose.onNodeWithContentDescription("Favorite details: missing.jpg").performClick()
        compose.runOnIdle {
            assertTrue(opened == 0)
            assertTrue(details == 1)
        }
    }

    @Test fun tagDialogSupportsKeyboardValidationAndDeleteConfirmation() {
        val state = mutableStateOf(TagsUiState(tags = listOf(TAG), loading = false))
        var opened: TagItem? = null
        compose.setContent {
            TagsScreen(
                state.value,
                {},
                {},
                { state.value = state.value.copy(dialog = TagDialog.CREATE) },
                { state.value = state.value.copy(dialog = TagDialog.RENAME, selected = it, input = it.name) },
                { state.value = state.value.copy(dialog = TagDialog.DELETE, selected = it) },
                { state.value = state.value.copy(input = it) },
                {},
                { state.value = state.value.copy(dialog = null) },
                onOpenTag = { opened = it },
            )
        }
        compose.onNodeWithText(TAG.name).performClick()
        compose.runOnIdle { assertEquals(TAG, opened) }
        compose.onNodeWithTag("tag-create").performClick()
        compose.onNodeWithTag("tag-name").performTextInput("Work")
        compose.onNodeWithText("Cancel").assertIsDisplayed()
        compose.onNodeWithText("Cancel").performClick()
        compose.onNodeWithText("Rename").performClick()
        compose.onNodeWithText("Rename tag").assertIsDisplayed()
        compose.onNodeWithText("Cancel").performClick()
        compose.onNodeWithText("Delete").performClick()
        compose.onNodeWithText("Delete this tag from every file and folder?").assertIsDisplayed()
    }

    @Test fun favoritesRenderLoadingEmptyAndErrorStates() {
        val state = mutableStateOf(FavoritesUiState(loading = true))
        compose.setContent { FavoritesScreen(state.value, {}, {}, {}, {}) }
        compose.onNodeWithText("Loading favorites").assertIsDisplayed()
        compose.runOnIdle { state.value = FavoritesUiState(loading = false) }
        compose.onNodeWithText("No favorite files or folders.").assertIsDisplayed()
        compose.runOnIdle {
            state.value = FavoritesUiState(loading = false, error = OrganizationUiError("Refresh failed", "request"))
        }
        compose.onNodeWithText("Refresh failed").assertIsDisplayed()
        compose.onNodeWithText("Request ID: request").assertIsDisplayed()
    }

    @Test fun activeEntryExposesFavoriteTagAndManagementActions() {
        var favoriteToggles = 0
        var tagToggles = 0
        var managed = false
        compose.setContent {
            EntryOrganizationScreen(
                EntryOrganizationUiState(
                    file(FileEntryStatus.ACTIVE),
                    EntryOrganizationState(false, emptyList()),
                    listOf(TAG),
                    loading = false,
                ),
                {},
                {},
                { favoriteToggles++ },
                { tagToggles++ },
                { managed = true },
            )
        }
        compose.onNodeWithText("Add favorite").performClick()
        compose.onNodeWithText("Work").performClick()
        compose.onNodeWithText("Manage tags").performClick()
        compose.runOnIdle {
            assertTrue(favoriteToggles == 1)
            assertTrue(tagToggles == 1)
            assertTrue(managed)
        }
    }

    @Test fun missingEntryFailsClosedButAllowsRemovingExistingTag() {
        val missing = file(FileEntryStatus.MISSING)
        compose.setContent {
            EntryOrganizationScreen(
                EntryOrganizationUiState(missing, EntryOrganizationState(true, listOf(TAG)), listOf(TAG, TAG_2), loading = false),
                {},
                {},
                {},
                {},
                {},
            )
        }
        compose.onNodeWithText("Unavailable items only allow removing existing organization data.").assertIsDisplayed()
        compose.onNodeWithText("Other").assertIsNotEnabled()
    }

    private companion object {
        const val ENTRY = "00000000-0000-4000-8000-000000000001"
        const val OWNER = "00000000-0000-4000-8000-000000000002"
        val NOW: Instant = Instant.parse("2026-08-28T00:00:00Z")
        val TAG = TagItem("00000000-0000-4000-8000-000000000003", "Work")
        val TAG_2 = TagItem("00000000-0000-4000-8000-000000000004", "Other")

        fun metadata() =
            SearchResultItem(
                ENTRY,
                FileEntryType.FILE,
                "a.pdf",
                "application/pdf",
                SearchFileCategory.DOCUMENT,
                1,
                FileEntryStatus.ACTIVE,
                NOW,
                OwnerSummary(OWNER, "Owner"),
                SharePermission.MANAGER,
                PermissionSource.OWNER,
                null,
            )

        fun file(status: FileEntryStatus) =
            FileEntry(
                ENTRY,
                null,
                "a.pdf",
                FileEntryType.FILE,
                "application/pdf",
                1,
                status,
                1,
                null,
                NOW,
                NOW,
                owner = OwnerSummary(OWNER, "Owner"),
                permission = SharePermission.MANAGER,
                permissionSource = PermissionSource.OWNER,
            )
    }
}
