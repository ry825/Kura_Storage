package com.kurastorage.feature.settings

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import com.kurastorage.core.ui.KuraStorageTheme
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test

class SettingsHubScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun adminSeesSettingsDestinationsAndCanLogOut() {
        var loggedOut = false
        compose.setContent {
            KuraStorageTheme {
                SettingsHubScreen(
                    isAdmin = true,
                    accountStatus = "Administrator",
                    connectionStatus = "Local direct",
                    onConnection = {},
                    onMediaSettings = {},
                    onBackupSettings = {},
                    onWifiSettings = {},
                    onCacheManagement = {},
                    onActivity = {},
                    onTrash = {},
                    onLogout = { loggedOut = true },
                )
            }
        }

        compose.onNodeWithText("Connection status").assertIsDisplayed()
        compose.onNodeWithText("Automatic backup").assertIsDisplayed()
        compose.onNodeWithText("Media quality and data use").assertIsDisplayed()
        compose.scrollSettingsTo("Trash and storage").assertIsDisplayed()
        compose.scrollSettingsTo("Cache management").assertIsDisplayed()
        compose.scrollSettingsTo("Log out").performClick()
        compose.runOnIdle { assertTrue(loggedOut) }
    }

    @Test
    fun memberDoesNotSeeAdministrativeStorageDestinations() {
        compose.setContent {
            KuraStorageTheme {
                SettingsHubScreen(
                    isAdmin = false,
                    accountStatus = "Member",
                    connectionStatus = "External via ZeroTier",
                    onConnection = {},
                    onMediaSettings = {},
                    onBackupSettings = {},
                    onWifiSettings = {},
                    onCacheManagement = {},
                    onActivity = {},
                    onTrash = {},
                    onLogout = {},
                )
            }
        }

        compose.onAllNodesWithText("Trash and storage").assertCountEquals(0)
        compose.onAllNodesWithText("Cache management").assertCountEquals(0)
    }

    @Test
    fun logoutRemainsReachableAtTwoHundredPercentInLandscape() {
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale = 2f)) {
                KuraStorageTheme(darkTheme = true) {
                    Box(Modifier.size(width = 800.dp, height = 360.dp).testTag("settings-landscape")) {
                        SettingsHubScreen(
                            isAdmin = true,
                            accountStatus = "Administrator",
                            connectionStatus = "Local direct",
                            onConnection = {},
                            onMediaSettings = {},
                            onBackupSettings = {},
                            onWifiSettings = {},
                            onCacheManagement = {},
                            onActivity = {},
                            onTrash = {},
                            onLogout = {},
                        )
                    }
                }
            }
        }

        compose.scrollSettingsTo("Log out").assertIsDisplayed()
        compose.onNodeWithTag("settings-landscape").captureToImage()
    }

    private fun androidx.compose.ui.test.junit4.ComposeContentTestRule.scrollSettingsTo(text: String) =
        onNodeWithTag("settings-list").performScrollToNode(hasText(text)).let { onNodeWithText(text) }
}
