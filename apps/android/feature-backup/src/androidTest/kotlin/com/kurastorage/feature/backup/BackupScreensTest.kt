package com.kurastorage.feature.backup

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertHeightIsAtLeast
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import com.kurastorage.core.data.backup.BackupProgressSnapshot
import com.kurastorage.core.data.backup.ConnectedWifi
import com.kurastorage.core.data.backup.CurrentWifiResult
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.SyncLifecycleState
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import java.time.Instant
import java.util.UUID

class BackupScreensTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun settingsExplainsOneWayBackupAndForceStop() {
        compose.setContent { BackupSettingsScreen({}, {}, {}, {}) }

        compose
            .onNodeWithText(
                "One-way backup adds and updates server files. Deleting a source from this device never deletes the server copy.",
            ).assertIsDisplayed()
        compose
            .onNodeWithText(
                "Android cannot run scheduled work after you force-stop KuraStorage. Open the app again to resume scheduling.",
            ).assertIsDisplayed()
        compose.onNodeWithText("Backup rules").assertHasClickAction()
    }

    @Test
    fun overviewUsesTextForCountsAndWaitingReason() {
        compose.setContent {
            BackupOverviewScreen(
                state =
                    BackupOverviewState(
                        loading = false,
                        progress =
                            BackupProgressSnapshot(
                                mapOf(SyncLifecycleState.PENDING to 2, SyncLifecycleState.FAILED to 1),
                                emptyMap(),
                                mapOf(BackupWaitReason.AUTHENTICATION to 2),
                                null,
                            ),
                    ),
                onRunNow = {},
                onPause = {},
                onRetry = {},
                onRetryAll = {},
                onLoadMore = {},
                onBack = {},
            )
        }

        compose.onNodeWithText("Pending").assertIsDisplayed()
        compose.onNodeWithText("Waiting: sign-in").assertIsDisplayed()
        compose.onNodeWithText("Retry failures").assertHasClickAction()
    }

    @Test
    fun wifiPermissionFailureIsExplainedAndFailClosed() {
        compose.setContent {
            BackupWifiScreen(
                state = BackupWifiState(loading = false, currentWifi = CurrentWifiResult.PermissionRequired(setOf("wifi"))),
                onRefresh = {},
                onRequestPermission = {},
                onRegister = { _, _, _ -> },
                onSave = {},
                onDelete = {},
                onOpenAppSettings = {},
                onBack = {},
            )
        }

        compose.onNodeWithText("Automatic backup remains stopped until granted.", substring = true).assertIsDisplayed()
        compose.onNodeWithText("Grant Wi-Fi permission").assertHasClickAction()
    }

    @Test
    fun wifiRegistrationRequiresExplicitConfirmation() {
        var registrations = 0
        compose.setContent {
            BackupWifiScreen(
                state =
                    BackupWifiState(
                        loading = false,
                        currentWifi = CurrentWifiResult.Connected(ConnectedWifi("Private fixture", null, false)),
                    ),
                onRefresh = {},
                onRequestPermission = {},
                onRegister = { _, _, _ -> registrations += 1 },
                onSave = {},
                onDelete = {},
                onOpenAppSettings = {},
                onBack = {},
            )
        }

        compose.onNodeWithText("Display name").performTextInput("Home")
        compose.onNodeWithText("Register current Wi-Fi").performClick()
        compose.onNodeWithText("Allow this Wi-Fi?").assertIsDisplayed()
        assertEquals(0, registrations)
        compose.onNodeWithText("Allow current Wi-Fi").performClick()
        assertEquals(1, registrations)
    }

    @Test
    fun settingsRemainReadableAtLargeFontInDarkMode() {
        compose.setContent {
            val density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, fontScale = 2f)) {
                MaterialTheme(colorScheme = darkColorScheme()) {
                    BackupSettingsScreen({}, {}, {}, {})
                }
            }
        }

        compose.onNodeWithText("Automatic backup").assertIsDisplayed()
        compose.onNodeWithText("Backup status and history").assertHasClickAction()
    }

    @Test
    fun overviewRetainsTextStatusSemanticsAtLargeFontInDarkMode() {
        compose.setContent {
            val density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, fontScale = 2f)) {
                MaterialTheme(colorScheme = darkColorScheme()) {
                    BackupOverviewScreen(
                        state =
                            BackupOverviewState(
                                loading = false,
                                progress =
                                    BackupProgressSnapshot(
                                        mapOf(
                                            SyncLifecycleState.PENDING to 12,
                                            SyncLifecycleState.UPLOADING to 3,
                                            SyncLifecycleState.COMPLETED to 40,
                                            SyncLifecycleState.FAILED to 2,
                                        ),
                                        emptyMap(),
                                        emptyMap(),
                                        null,
                                    ),
                            ),
                        onRunNow = {},
                        onPause = {},
                        onRetry = {},
                        onRetryAll = {},
                        onLoadMore = {},
                        onBack = {},
                    )
                }
            }
        }

        compose.onNodeWithText("Pending").assertIsDisplayed()
        compose.onNodeWithText("Uploading").assertIsDisplayed()
        compose.onNodeWithText("Succeeded").assertIsDisplayed()
        compose.onNodeWithText("Failed").assertIsDisplayed()
        compose.onNodeWithContentDescription("Pending: 12").assertIsDisplayed()
        compose.onNodeWithContentDescription("Failed: 2").assertIsDisplayed()
        compose.onNodeWithText("Back up now").assertHasClickAction().assertHeightIsAtLeast(48.dp)
    }

    @Test
    fun deletingRuleRequiresConfirmationAndExplainsServerRetention() {
        var deletions = 0
        compose.setContent {
            BackupRulesScreen(
                state = BackupRulesState(loading = false, rules = listOf(rule())),
                selectedSource = null,
                selectedDestination = null,
                onPickSafSource = {},
                onPickDestination = {},
                onRequestMediaPermission = {},
                onSave = { _, _, _ -> },
                onToggle = { _, _ -> },
                onDelete = { deletions += 1 },
                onSelectionsConsumed = {},
                onBack = {},
            )
        }

        compose.onNodeWithText("Delete").performClick()
        compose.onNodeWithText("Files already backed up on the server are not deleted.", substring = true).assertIsDisplayed()
        assertEquals(0, deletions)
        compose.onNodeWithText("Delete rule").performClick()
        assertEquals(1, deletions)
    }

    private fun rule() =
        LocalBackupRule(
            id = BackupRuleId(UUID.randomUUID().toString()),
            accountScopeId = AccountScopeId("a".repeat(64)),
            sourceType = BackupSourceType.SAF_TREE,
            sourceLocator = "content://anonymous/tree",
            displayName = "Anonymous rule",
            remoteFolderId = UUID.randomUUID().toString(),
            enabled = true,
            networkMode = BackupNetworkMode.LOCAL_DIRECT_ONLY,
            requiresChargingForInitialRun = false,
            minimumBatteryPercent = 20,
            initialRunCompletedAt = Instant.EPOCH,
            pausedAt = null,
            createdAt = Instant.EPOCH,
            updatedAt = Instant.EPOCH,
        )
}
