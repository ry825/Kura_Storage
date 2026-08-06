package com.kurastorage.feature.auth

import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import org.junit.Rule
import org.junit.Test

class AuthScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun registrationAndLoginAreDistinct() {
        val state = mutableStateOf<AuthUiState>(AuthUiState.Form(registration = true))
        compose.setContent {
            AuthScreen(state.value, { _, _ -> }, {}, {})
        }
        compose.onNodeWithText("Register this device").assertIsDisplayed()

        compose.runOnIdle {
            state.value = AuthUiState.Form(registration = false)
        }
        compose.onAllNodesWithText("Sign in").assertCountEquals(2)
    }

    @Test
    fun remoteUnregisteredDeviceRequiresLocalDirect() {
        compose.setContent {
            AuthScreen(AuthUiState.RequiresLocalDirect, { _, _ -> }, {}, {})
        }
        compose.onNodeWithText("Local connection required").assertIsDisplayed()
    }
}
