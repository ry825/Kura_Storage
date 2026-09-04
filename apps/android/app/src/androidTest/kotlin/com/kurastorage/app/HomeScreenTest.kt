package com.kurastorage.app

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.AdminStorageStatus
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.RecentFileItem
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.StorageAvailability
import com.kurastorage.core.ui.KuraStorageTheme
import com.kurastorage.feature.files.AdminStorageState
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class HomeScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun homeShowsConnectionBackupCategoriesRecentAndSecondaryDestinations() {
        var selectedCategory: SearchFileCategory? = null
        var openedRecent: String? = null
        compose.setContent {
            KuraStorageTheme {
                HomeScreen(
                    connection = connected(),
                    state =
                        HomeUiState(
                            recentLoading = false,
                            recentItems = listOf(recent()),
                            backupLoading = false,
                            backupSummary = HomeBackupSummary(Instant.EPOCH, pendingCount = 3, uploadingCount = 0, failedCount = 1),
                        ),
                    onFiles = {},
                    onCategory = { selectedCategory = it },
                    onOpenRecent = { openedRecent = it.id },
                    onTrash = {},
                )
            }
        }

        compose.onNodeWithText("ZeroTier").assertIsDisplayed()
        compose.onNodeWithText("Storage is available").assertIsDisplayed()
        compose.onNodeWithText("Needs attention").assertIsDisplayed()
        compose.onNodeWithText("Pending: 3").assertIsDisplayed()
        compose.onNodeWithText("Current status").assert(SemanticsMatcher.keyIsDefined(SemanticsProperties.Heading))
        compose.onNodeWithText("My files").assertIsDisplayed()
        compose.onNodeWithText("Family shared").assertIsDisplayed()
        compose.scrollHomeTo("Photos").performClick()
        compose.scrollHomeTo("report.pdf").performClick()
        compose.scrollHomeTo("Favorites").assertIsDisplayed()
        compose.scrollHomeTo("Tags").assertIsDisplayed()
        compose.scrollHomeTo("Activity").assertIsDisplayed()
        compose.scrollHomeTo("Trash").assertIsDisplayed()
        compose.runOnIdle {
            assertEquals(SearchFileCategory.IMAGE, selectedCategory)
            assertEquals(ENTRY_ID, openedRecent)
        }
    }

    @Test
    fun homeShowsCapacityWarningOnlyWhenAdminStatusIsPresent() {
        val warning = AdminStorageStatus("AVAILABLE", 100, 10, 20, true, 5, 1, 30, 0, null)
        compose.setContent {
            KuraStorageTheme {
                HomeScreen(
                    connection = connected(),
                    adminStorageState = AdminStorageState(loading = false, status = warning),
                    onFiles = {},
                    onTrash = {},
                )
            }
        }
        compose.onNodeWithText("Storage capacity warning").performScrollTo().assertIsDisplayed()
    }

    @Test
    fun homeHidesCapacityDetailsForMemberStateAndKeepsPartialErrorsLocal() {
        compose.setContent {
            KuraStorageTheme {
                HomeScreen(
                    connection = connected(),
                    state = HomeUiState(recentLoading = false, recentError = true, backupLoading = false, backupError = true),
                    adminStorageState = AdminStorageState(loading = false),
                    onFiles = {},
                    onTrash = {},
                )
            }
        }
        compose.onAllNodesWithText("Storage capacity warning").assertCountEquals(0)
        compose.onNodeWithText("Status unavailable").assertIsDisplayed()
        compose.scrollHomeTo("Recent files unavailable").assertIsDisplayed()
        compose.scrollHomeTo("My files").assertIsDisplayed()
    }

    @Test
    fun compactTwoHundredPercentDarkFixtureRemainsScrollableAndCapturable() {
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale = 2f)) {
                KuraStorageTheme(darkTheme = true) {
                    Box(Modifier.size(width = 360.dp, height = 800.dp).testTag("home-fixture")) {
                        HomeScreen(
                            connection = connected(),
                            onFiles = {},
                            onTrash = {},
                        )
                    }
                }
            }
        }

        compose.scrollHomeTo("Search all files").assertIsDisplayed()
        compose.onNodeWithTag("home-fixture").captureToImage()
    }

    @Test
    fun landscapeFixtureKeepsPrimaryAndSecondaryDestinationsReachable() {
        compose.setContent {
            KuraStorageTheme(darkTheme = false) {
                Box(Modifier.size(width = 800.dp, height = 360.dp).testTag("home-landscape")) {
                    HomeScreen(connection = connected(), onFiles = {}, onTrash = {})
                }
            }
        }

        compose.scrollHomeTo("My files").assertIsDisplayed()
        compose.scrollHomeTo("Search all files").assertIsDisplayed()
        compose.onNodeWithTag("home-landscape").captureToImage()
    }

    private fun androidx.compose.ui.test.junit4.ComposeContentTestRule.scrollHomeTo(text: String) =
        onNodeWithTag("home-list").performScrollToNode(hasText(text)).let { onNodeWithText(text) }

    private fun connected() =
        ConnectionStatus.Connected(
            ConnectionRoute.REMOTE_SECURE,
            StorageAvailability.AVAILABLE,
        )

    private fun recent() =
        RecentFileItem(
            metadata =
                SearchResultItem(
                    id = ENTRY_ID,
                    entryType = FileEntryType.FILE,
                    name = "report.pdf",
                    mimeType = "application/pdf",
                    fileCategory = SearchFileCategory.DOCUMENT,
                    size = 2048,
                    status = FileEntryStatus.ACTIVE,
                    updatedAt = Instant.EPOCH,
                    owner = OwnerSummary(OWNER_ID, "Owner"),
                    permission = SharePermission.VIEWER,
                    permissionSource = PermissionSource.DIRECT,
                    shareTargetId = null,
                ),
            openedAt = Instant.EPOCH,
        )

    private companion object {
        const val ENTRY_ID = "00000000-0000-4000-8000-000000000001"
        const val OWNER_ID = "00000000-0000-4000-8000-000000000002"
    }
}
