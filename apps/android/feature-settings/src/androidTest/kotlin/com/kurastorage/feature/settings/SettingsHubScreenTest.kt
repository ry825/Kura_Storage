package com.kurastorage.feature.settings

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.platform.LocalContext
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

    @Test
    fun settingsTextAndUiColorsMeetContrastThresholdsAcrossSupportedSchemes() {
        val schemeIndex = mutableIntStateOf(0)
        var ratios = emptyList<Float>()
        compose.setContent {
            val context = LocalContext.current
            when (schemeIndex.intValue) {
                0 -> KuraStorageTheme(darkTheme = false) { contrastProbe { ratios = it } }
                1 -> KuraStorageTheme(darkTheme = true) { contrastProbe { ratios = it } }
                2 -> MaterialTheme(dynamicLightColorScheme(context)) { contrastProbe { ratios = it } }
                else -> MaterialTheme(dynamicDarkColorScheme(context)) { contrastProbe { ratios = it } }
            }
        }

        repeat(4) { index ->
            compose.runOnIdle { schemeIndex.intValue = index }
            compose.waitForIdle()
            compose.runOnIdle {
                assertTrue("Normal and supporting text must have at least 4.5:1 contrast", ratios.take(2).all { it >= 4.5f })
                assertTrue("Actionable UI color must have at least 3:1 contrast", ratios.last() >= 3f)
            }
        }
    }

    private fun androidx.compose.ui.test.junit4.ComposeContentTestRule.scrollSettingsTo(text: String) =
        onNodeWithTag("settings-list").performScrollToNode(hasText(text)).let { onNodeWithText(text) }

    @Composable
    private fun contrastProbe(onMeasured: (List<Float>) -> Unit) {
        val colors = MaterialTheme.colorScheme
        SideEffect {
            onMeasured(
                listOf(
                    contrastRatio(colors.onSurface, colors.surface),
                    contrastRatio(colors.onSurfaceVariant, colors.surface),
                    contrastRatio(colors.primary, colors.surface),
                ),
            )
        }
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

    private fun contrastRatio(
        foreground: Color,
        background: Color,
    ): Float {
        val lighter = maxOf(foreground.luminance(), background.luminance())
        val darker = minOf(foreground.luminance(), background.luminance())
        return (lighter + 0.05f) / (darker + 0.05f)
    }
}
