package com.kurastorage.feature.connection

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
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
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.StorageAvailability
import com.kurastorage.core.ui.KuraStorageTheme
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

class ConnectionScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun checkingExplainsTheOrderedConnectionChecks() {
        compose.setContent { KuraStorageTheme { ConnectionScreen(ConnectionStatus.Checking, {}, {}) } }

        compose.onNodeWithText("KuraStorage").assertIsDisplayed()
        compose.onNodeWithTag("connection-progress").assertIsDisplayed()
        compose.onNodeWithText("Checking connection").assertIsDisplayed()
        compose.onNodeWithText("Local direct").assertIsDisplayed()
        compose.onNodeWithText("ZeroTier").assertIsDisplayed()
        compose.onNodeWithText("Server and storage").assertIsDisplayed()
    }

    @Test
    fun connectionStatesHaveDistinctGuidance() {
        val states =
            listOf(
                ConnectionStatus.Connected(ConnectionRoute.LOCAL_DIRECT, StorageAvailability.AVAILABLE) to
                    "Connected locally",
                ConnectionStatus.Connected(ConnectionRoute.REMOTE_SECURE, StorageAvailability.AVAILABLE) to
                    "Connected through ZeroTier",
                ConnectionStatus.Disconnected to "KuraStorage is unreachable",
                ConnectionStatus.TlsFailure to "Secure connection failed",
                ConnectionStatus.IncompatibleProtocol to "App update required",
                ConnectionStatus.Connected(ConnectionRoute.LOCAL_DIRECT, StorageAvailability.UNAVAILABLE) to
                    "Storage unavailable",
            )
        val currentState = mutableStateOf(states.first().first)
        compose.setContent { KuraStorageTheme { ConnectionScreen(currentState.value, {}, {}) } }

        states.forEach { (state, title) ->
            compose.runOnIdle { currentState.value = state }
            compose.onNodeWithText(title).assertIsDisplayed()
        }
    }

    @Test
    fun disconnectedGuidanceUsesTheExternalZeroTierAppAndRechecks() {
        var rechecks = 0
        compose.setContent {
            KuraStorageTheme { ConnectionScreen(ConnectionStatus.Disconnected, { rechecks++ }, {}) }
        }

        compose.scrollConnectionTo("Check connection and membership in the separate ZeroTier app").assertIsDisplayed()
        compose.onAllNodesWithText("VPN", substring = true).assertCountEquals(0)
        compose.scrollConnectionTo("Check again").performClick()
        compose.runOnIdle { assertEquals(1, rechecks) }
    }

    @Test
    fun onlyAvailableStorageAdvances() {
        val state = mutableStateOf<ConnectionStatus>(ConnectionStatus.Checking)
        var advances = 0
        compose.setContent { KuraStorageTheme { ConnectionScreen(state.value, {}, { advances++ }) } }

        compose.runOnIdle {
            state.value = ConnectionStatus.Connected(ConnectionRoute.LOCAL_DIRECT, StorageAvailability.UNAVAILABLE)
        }
        compose.runOnIdle { assertEquals(0, advances) }
        compose.runOnIdle {
            state.value = ConnectionStatus.Connected(ConnectionRoute.REMOTE_SECURE, StorageAvailability.AVAILABLE)
        }
        compose.waitForIdle()
        compose.runOnIdle { assertEquals(1, advances) }
    }

    @Test
    fun compactTwoHundredPercentDarkStateRemainsScrollableAndCapturable() {
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale = 2f)) {
                KuraStorageTheme(darkTheme = true) {
                    Box(Modifier.size(width = 360.dp, height = 800.dp)) {
                        ConnectionScreen(ConnectionStatus.Disconnected, {}, {})
                    }
                }
            }
        }

        compose.scrollConnectionTo("Check again").assertIsDisplayed()
        compose.onNodeWithTag("connection-screen").captureToImage()
    }

    private fun androidx.compose.ui.test.junit4.ComposeContentTestRule.scrollConnectionTo(text: String) =
        onNodeWithTag("connection-screen").performScrollToNode(hasText(text)).let { onNodeWithText(text) }
}
