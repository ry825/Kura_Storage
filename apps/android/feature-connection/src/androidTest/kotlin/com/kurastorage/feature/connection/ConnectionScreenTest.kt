package com.kurastorage.feature.connection

import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.StorageAvailability
import org.junit.Rule
import org.junit.Test

class ConnectionScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun connectionStatesHaveDistinctGuidance() {
        val states =
            listOf(
                ConnectionStatus.Connected(
                    ConnectionRoute.LOCAL_DIRECT,
                    StorageAvailability.AVAILABLE,
                ) to "Connected directly on the local network",
                ConnectionStatus.Connected(
                    ConnectionRoute.REMOTE_SECURE,
                    StorageAvailability.AVAILABLE,
                ) to "Connected through ZeroTier",
                ConnectionStatus.Disconnected to "KuraStorage is unreachable",
                ConnectionStatus.TlsFailure to "Secure connection failed",
                ConnectionStatus.IncompatibleProtocol to "アプリの更新が必要です",
                ConnectionStatus.Connected(
                    ConnectionRoute.LOCAL_DIRECT,
                    StorageAvailability.UNAVAILABLE,
                ) to "Storage unavailable",
            )

        val currentState = mutableStateOf(states.first().first)
        compose.setContent { ConnectionScreen(currentState.value, {}, {}) }

        states.forEach { (state, text) ->
            compose.runOnIdle { currentState.value = state }
            compose.onNodeWithText(text).assertIsDisplayed()
        }
    }
}
