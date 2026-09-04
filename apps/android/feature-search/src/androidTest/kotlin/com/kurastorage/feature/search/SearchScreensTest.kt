package com.kurastorage.feature.search

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performImeAction
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ErrorCategory
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.RecentFileItem
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchInput
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.TagItem
import com.kurastorage.core.ui.KuraStorageTheme
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class SearchScreensTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun searchSupportsImeFiltersResultMetadataPagingAndNavigation() {
        val state =
            mutableStateOf(
                SearchUiState(
                    input = SearchInput(query = "report"),
                    items = listOf(item()),
                    hasSearched = true,
                    canLoadMore = true,
                ),
            )
        var searches = 0
        var opened = false
        var managedTags = false
        compose.setContent {
            Box(Modifier.width(320.dp)) {
                SearchScreen(
                    state.value,
                    {},
                    { state.value = state.value.copy(input = it) },
                    { searches++ },
                    {},
                    {},
                    { opened = true },
                    ownerOptions = listOf(SearchFilterOption(OWNER, "Owner")),
                    shareOptions = listOf(SearchFilterOption(TARGET, "Shared folder")),
                    tagOptions = listOf(TagItem(ID_2, "Work")),
                    onManageTags = { managedTags = true },
                )
            }
        }

        compose.onNodeWithTag("search-query").performImeAction()
        compose.onNodeWithText("DOCUMENT").performScrollTo().performClick()
        compose.onNodeWithText("Owner", useUnmergedTree = true).performScrollTo().assertIsDisplayed()
        compose.onNodeWithTag("search-results").performScrollToNode(hasText("Shared from: Shared folder"))
        compose.onNodeWithText("Shared from: Shared folder").assertIsDisplayed()
        compose.onNodeWithText("Work").performScrollTo().performClick()
        compose.onNodeWithText("Manage tags").performScrollTo().performClick()
        compose.onNodeWithTag("search-result-$ID").performScrollTo().performClick()
        compose.onNodeWithTag("search-results").performScrollToNode(hasTestTag("search-load-more"))
        compose.onNodeWithText("Load more").performScrollTo().assertIsDisplayed()
        compose.runOnIdle {
            assertEquals(1, searches)
            assertTrue(opened)
            assertEquals(SearchFileCategory.DOCUMENT, state.value.input.fileCategory)
            assertEquals(listOf(ID_2), state.value.input.tagIds)
            assertTrue(managedTags)
        }
    }

    @Test
    fun missingSearchAndRecentItemsShowStateAndCannotOpen() {
        var searchOpened = false
        var recentOpened = false
        val showRecent = mutableStateOf(false)
        val missing = item().copy(status = FileEntryStatus.MISSING)
        compose.setContent {
            if (showRecent.value) {
                RecentFilesScreen(
                    RecentFilesUiState(items = listOf(RecentFileItem(missing, NOW))),
                    {},
                    {},
                    {},
                    { recentOpened = true },
                )
            } else {
                SearchScreen(
                    SearchUiState(input = SearchInput(query = "x"), items = listOf(missing), hasSearched = true),
                    {},
                    {},
                    {},
                    {},
                    {},
                    { searchOpened = true },
                )
            }
        }
        compose.onNodeWithTag("search-results").performScrollToNode(hasTestTag("search-result-$ID"))
        compose.onNodeWithText("File missing", useUnmergedTree = true).assertIsDisplayed()
        compose.onNodeWithTag("search-result-$ID").performClick()
        compose.runOnIdle { assertFalse(searchOpened) }

        compose.runOnIdle { showRecent.value = true }
        compose.onNodeWithTag("recent-results").performScrollToNode(hasTestTag("recent-result-$ID"))
        compose.onNodeWithText("File missing", useUnmergedTree = true).assertIsDisplayed()
        compose.onNodeWithText("report.pdf").performClick()
        compose.runOnIdle { assertFalse(recentOpened) }
    }

    @Test
    fun narrowLargeFontLayoutScrollsToControlsAndRejectsInvalidRanges() {
        val state = mutableStateOf(SearchUiState(input = SearchInput(query = "report")))
        val landscape = mutableStateOf(false)
        compose.setContent {
            val density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, fontScale = 2f)) {
                KuraStorageTheme(darkTheme = true) {
                    Box(
                        Modifier.requiredSize(
                            width = if (landscape.value) 360.dp else 280.dp,
                            height = if (landscape.value) 280.dp else 360.dp,
                        ),
                    ) {
                        SearchScreen(
                            state.value,
                            {},
                            { state.value = state.value.copy(input = it) },
                            {},
                            {},
                            {},
                            {},
                        )
                    }
                }
            }
        }

        compose.onNodeWithTag("search-results").performScrollToNode(hasTestTag("updated-from"))
        compose.onNodeWithTag("updated-from").performTextInput("not-a-date")
        compose.onNodeWithText("Enter valid ISO-8601 dates and whole-byte sizes.").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Apply ranges").assertIsNotEnabled()
        compose.onNodeWithTag("search-results").performScrollToNode(hasTestTag("search-submit"))
        compose.onNodeWithTag("search-submit").assertIsNotEnabled()
        compose.onNodeWithTag("search-results").performScrollToNode(hasTestTag("search-query"))
        compose.onNodeWithTag("search-query").performClick()
        compose.onNodeWithTag("search-results").performScrollToNode(hasTestTag("search-submit"))
        compose.onNodeWithTag("search-submit").assertIsDisplayed()
        compose.runOnIdle { landscape.value = true }
        compose.onNodeWithTag("search-results").performScrollToNode(hasTestTag("updated-from"))
        compose.onNodeWithTag("updated-from").assertIsDisplayed()
        val capture = compose.onRoot().captureToImage()
        assertTrue(capture.width > 0 && capture.height > 0)
    }

    @Test
    fun searchCommitsPendingRangesBeforeSubmitting() {
        val state = mutableStateOf(SearchUiState(input = SearchInput(query = "report")))
        var submitted: SearchInput? = null
        compose.setContent {
            SearchScreen(
                state.value,
                {},
                { state.value = state.value.copy(input = it) },
                { submitted = state.value.input },
                {},
                {},
                {},
            )
        }

        compose.onNodeWithTag("search-results").performScrollToNode(hasTestTag("minimum-size"))
        compose.onNodeWithTag("minimum-size").performTextInput("20")
        compose.onNodeWithTag("search-submit").performScrollTo().performClick()

        compose.runOnIdle { assertEquals(20L, submitted?.minSize) }
    }

    @Test
    fun searchAndRecentRenderLoadingEmptyErrorAndSpecialInput() {
        val search = mutableStateOf(SearchUiState(loading = true))
        val showRecent = mutableStateOf(false)
        compose.setContent {
            if (showRecent.value) {
                RecentFilesScreen(RecentFilesUiState(), {}, {}, {}, {})
            } else {
                SearchScreen(search.value, {}, { search.value = search.value.copy(input = it) }, {}, {}, {}, {})
            }
        }
        compose.onNodeWithTag("search-results").performScrollToNode(hasText("Searching"))
        compose.onNodeWithText("Searching").assertIsDisplayed()
        compose.runOnIdle { search.value = SearchUiState(hasSearched = true) }
        compose.onNodeWithText("No matching files or folders.").assertIsDisplayed()
        compose.runOnIdle {
            search.value = SearchUiState(hasSearched = true, error = SearchUiError("Network error", ErrorCategory.CONNECTION))
        }
        compose.onNodeWithText("Network error").performScrollTo().assertIsDisplayed()
        compose.runOnIdle { search.value = SearchUiState() }
        compose.onNodeWithTag("search-query").performTextInput("\u65e5本 %_\\")
        compose.runOnIdle { assertEquals("\u65e5本 %_\\", search.value.input.query) }

        compose.runOnIdle { showRecent.value = true }
        compose.onNodeWithText("No recently opened files.").assertIsDisplayed()
    }

    @Test
    fun categoryModeUsesSearchContractAndClearAndFavoritesRemainReachable() {
        val state = mutableStateOf(SearchUiState(input = SearchInput(fileCategory = SearchFileCategory.IMAGE)))
        var cleared = false
        var favorites = false
        compose.setContent {
            SearchScreen(
                state = state.value,
                onBack = {},
                onInput = { state.value = state.value.copy(input = it) },
                onSearch = {},
                onRefresh = {},
                onLoadMore = {},
                onOpen = {},
                categoryMode = SearchFileCategory.IMAGE,
                onClear = { cleared = true },
                onFavorites = { favorites = true },
            )
        }

        compose.onNodeWithText("Photo files").assertIsDisplayed()
        compose.onNodeWithText("VIDEO").performScrollTo().performClick()
        compose.onNodeWithTag("search-results").performScrollToNode(hasText("Clear"))
        compose.onNodeWithText("Clear").performClick()
        compose.onNodeWithTag("search-results").performScrollToNode(hasText("Browse favorites"))
        compose.onNodeWithText("Browse favorites").performClick()
        compose.runOnIdle {
            assertEquals(SearchFileCategory.VIDEO, state.value.input.fileCategory)
            assertTrue(cleared)
            assertTrue(favorites)
        }
    }

    private companion object {
        const val ID = "00000000-0000-4000-8000-000000000001"
        const val ID_2 = "00000000-0000-4000-8000-000000000002"
        const val OWNER = "00000000-0000-4000-8000-000000000002"
        const val TARGET = "00000000-0000-4000-8000-000000000003"
        val NOW: Instant = Instant.parse("2026-08-25T00:00:00Z")

        fun item() =
            SearchResultItem(
                ID,
                FileEntryType.FILE,
                "report.pdf",
                "application/pdf",
                SearchFileCategory.DOCUMENT,
                20,
                FileEntryStatus.ACTIVE,
                NOW,
                OwnerSummary(OWNER, "Owner"),
                SharePermission.VIEWER,
                PermissionSource.INHERITED,
                TARGET,
            )
    }
}
