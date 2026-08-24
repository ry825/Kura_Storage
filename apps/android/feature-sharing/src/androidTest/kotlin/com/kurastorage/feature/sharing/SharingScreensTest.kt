package com.kurastorage.feature.sharing

import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.ShareCandidate
import com.kurastorage.core.model.ShareItem
import com.kurastorage.core.model.ShareMember
import com.kurastorage.core.model.SharePermission
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
        compose.onNodeWithText("FOLDER: Photos").assertIsDisplayed()
        compose.onNodeWithText("Owner: Owner").assertIsDisplayed()
        compose.onNodeWithText("Permission: MANAGER").assertIsDisplayed()
        compose.onNodeWithText("Applies to this folder and descendants").assertIsDisplayed()
        compose.onNodeWithText("Sharing settings").assertIsDisplayed()
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
        compose.onNodeWithText("MANAGER").performClick()
        compose.runOnIdle { assertTrue(permission == SharePermission.MANAGER) }
        compose.onNodeWithTag("submit-share-member").performClick()
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
        compose.onNodeWithText("Permissions are inherited by descendants.").assertIsDisplayed()
        compose.onAllNodesWithText("CONTRIBUTOR").assertCountEquals(2)
        compose.onNodeWithTag("submit-share-member").performScrollTo().assertIsDisplayed()

        compose.runOnIdle { state.value = state.value.copy(confirmation = Confirmation.REMOVE_MEMBER) }
        compose.onNodeWithText("Remove this family member from the share?").assertIsDisplayed()

        compose.runOnIdle { state.value = state.value.copy(confirmation = Confirmation.DELETE_SHARE) }
        compose.onNodeWithText("Remove this share for every member?").assertIsDisplayed()

        compose.runOnIdle { state.value = state.value.copy(confirmation = null, accessLost = true) }
        compose.onNodeWithText("This share is no longer available. Return to the latest shared list.").assertIsDisplayed()
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
