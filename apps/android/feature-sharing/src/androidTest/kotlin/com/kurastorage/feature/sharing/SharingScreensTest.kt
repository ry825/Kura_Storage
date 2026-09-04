package com.kurastorage.feature.sharing

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.ShareCandidate
import com.kurastorage.core.model.ShareItem
import com.kurastorage.core.model.ShareMember
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.ui.KuraStorageTheme
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class SharingScreensTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun listShowsRootOwnerPermissionScopeAndManagement() {
        compose.setContent {
            SharingScreen(
                SharingListState(loading = false, items = listOf(item())),
                {},
                {},
                {},
                {},
                {},
                {},
                {},
            )
        }
        compose.onNodeWithText("Photos").assertIsDisplayed()
        compose.onNodeWithText("Owner: Owner • MANAGER (DIRECT)").assertIsDisplayed()
        compose.onNodeWithText("Applies to this folder and descendants").assertIsDisplayed()
        compose.onNodeWithText("Manage").assertIsDisplayed()
    }

    @Test
    fun fileSettingsExcludeContributorAndManagerShowsConfirmation() {
        var permission: SharePermission? = null
        var submitted = false
        compose.setContent {
            SharingSettingsScreen(
                SharingSettingsState(
                    loading = false,
                    targetEntryId = "target",
                    targetType = FileEntryType.FILE,
                    targetName = "photo.jpg",
                    candidates = listOf(ShareCandidate("user", "Alex")),
                    selectedUserId = "user",
                ),
                {},
                {},
                {},
                { permission = it },
                { submitted = true },
                { _, _ -> },
                {},
                {},
                {},
                {},
            )
        }
        compose.onAllNodesWithText("CONTRIBUTOR").assertCountEquals(0)
        compose.onNodeWithText("MANAGER").performScrollTo().performClick()
        compose.runOnIdle { assertTrue(permission == SharePermission.MANAGER) }
        compose.onNodeWithTag("submit-share-member").performScrollTo().performClick()
        compose.runOnIdle { assertTrue(submitted) }
    }

    @Test
    fun managerConfirmationCannotBeSilentlySkipped() {
        compose.setContent {
            SharingSettingsScreen(
                SharingSettingsState(
                    loading = false,
                    targetEntryId = "target",
                    targetType = FileEntryType.FOLDER,
                    targetName = "Photos",
                    confirmation = Confirmation.GRANT_MANAGER,
                ),
                {},
                {},
                {},
                {},
                {},
                { _, _ -> },
                {},
                {},
                {},
                {},
            )
        }
        compose.onNodeWithText("Grant Manager permission? This person can manage sharing.").assertIsDisplayed()
        compose.onNodeWithTag("confirm-sharing-change").assertIsDisplayed()
    }

    @Test
    fun listRendersLoadingEmptyErrorAndPagingStates() {
        val state = mutableStateOf(SharingListState())
        compose.setContent {
            SharingScreen(state.value, {}, {}, {}, {}, {}, {}, {})
        }
        compose.onNodeWithTag("sharing-loading").assertIsDisplayed()

        compose.runOnIdle { state.value = SharingListState(loading = false) }
        compose.onNodeWithTag("sharing-empty").assertIsDisplayed()

        compose.runOnIdle {
            state.value = SharingListState(loading = false, items = listOf(item()), canLoadMore = true, error = "Network error")
        }
        compose.onNodeWithText("Network error").assertIsDisplayed()
        compose.onNodeWithText("Load more").assertIsDisplayed()
    }

    @Test
    fun folderSettingsShowInheritanceRemovalDeletionAndLostAccessStates() {
        val state =
            mutableStateOf(
                SharingSettingsState(
                    loading = false,
                    targetEntryId = "target",
                    targetType = FileEntryType.FOLDER,
                    targetName = "Photos",
                    share = item(),
                    candidates = listOf(ShareCandidate("other", "Taylor")),
                ),
            )
        compose.setContent {
            SharingSettingsScreen(state.value, {}, {}, {}, {}, {}, { _, _ -> }, {}, {}, {}, {})
        }
        compose.onNodeWithText("These permissions apply to this folder and its descendants.").assertIsDisplayed()
        compose.onAllNodesWithText("CONTRIBUTOR").assertCountEquals(2)
        compose.onNodeWithTag("submit-share-member").performScrollTo().assertIsDisplayed()

        compose.runOnIdle { state.value = state.value.copy(confirmation = Confirmation.REMOVE_MEMBER) }
        compose.onNodeWithText("Remove this family member from this folder share and its descendants?").assertIsDisplayed()

        compose.runOnIdle { state.value = state.value.copy(confirmation = Confirmation.DELETE_SHARE) }
        compose.onNodeWithText("Remove this share for every member, including inherited access to descendants?").assertIsDisplayed()

        compose.runOnIdle { state.value = state.value.copy(confirmation = null, accessLost = true) }
        compose.onNodeWithText("This share is no longer available. Return to the latest shared list.").assertIsDisplayed()
    }

    @Test
    fun compactLargeTextDarkSharingSettingsKeepsMemberSearchReachable() {
        compose.setContent {
            val density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, fontScale = 2f)) {
                KuraStorageTheme(darkTheme = true) {
                    Box(Modifier.requiredSize(320.dp, 640.dp)) {
                        SharingSettingsScreen(
                            SharingSettingsState(
                                loading = false,
                                targetEntryId = "target",
                                targetType = FileEntryType.FOLDER,
                                targetName = "A very long shared family folder name",
                                share = item(),
                                candidates = listOf(ShareCandidate("other", "Taylor")),
                            ),
                            {},
                            {},
                            {},
                            {},
                            {},
                            { _, _ -> },
                            {},
                            {},
                            {},
                            {},
                        )
                    }
                }
            }
        }

        compose.onNodeWithTag("share-candidate-search").performScrollTo().assertIsDisplayed()
        val capture = compose.onRoot().captureToImage()
        assertTrue(capture.width > 0 && capture.height > 0)
    }

    private fun item() =
        ShareItem(
            "share",
            "target",
            FileEntryType.FOLDER,
            "Photos",
            OwnerSummary("owner", "Owner"),
            SharePermission.MANAGER,
            listOf(ShareMember("user", "Alex", SharePermission.VIEWER)),
            Instant.EPOCH,
            Instant.EPOCH,
        )
}
