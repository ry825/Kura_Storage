package com.kurastorage.feature.auth

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import org.junit.Rule
import org.junit.Test

class AuthScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun registrationAndLoginAreDistinct() {
        compose.setContent {
            AuthScreen(AuthUiState.Form(registration = true), { _, _ -> }, {}, {})
        }
        compose.onNodeWithText("Register this device").assertIsDisplayed()

        compose.setContent {
            AuthScreen(AuthUiState.Form(registration = false), { _, _ -> }, {}, {})
        }
        compose.onNodeWithText("Sign in").assertIsDisplayed()
    }

    @Test
    fun remoteUnregisteredDeviceRequiresLocalDirect() {
        compose.setContent {
            AuthScreen(AuthUiState.RequiresLocalDirect, { _, _ -> }, {}, {})
        }
        compose.onNodeWithText("Local connection required").assertIsDisplayed()
    }
}
