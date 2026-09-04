package com.kurastorage.feature.auth

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performImeAction
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.ui.KuraStorageTheme
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

class AuthScreenTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun registrationAndLoginAreDistinct() {
        val state = mutableStateOf<AuthUiState>(AuthUiState.Form(registration = true, deviceName = "Pixel 9"))
        compose.setContent { KuraStorageTheme { AuthScreen(state.value, { _, _ -> }, {}, {}) } }

        compose.onNodeWithText("KuraStorage").assertIsDisplayed()
        compose.onNodeWithText("Register this device").assertIsDisplayed()
        compose.onNodeWithText("Device: Pixel 9").assertIsDisplayed()
        compose.runOnIdle { state.value = AuthUiState.Form(registration = false) }
        compose.onAllNodesWithText("Sign in").assertCountEquals(2)
    }

    @Test
    fun passwordIsMaskedAndImeSubmitsOnce() {
        var submitted: Pair<String, String>? = null
        compose.setContent {
            KuraStorageTheme {
                AuthScreen(AuthUiState.Form(registration = false), { user, password -> submitted = user to password }, {}, {})
            }
        }

        compose.onNodeWithTag("username-field").performTextInput("member")
        compose.onNodeWithTag("password-field").performTextInput("secret-value")
        compose.onNodeWithContentDescription("Show password").assertIsDisplayed()
        compose.onNodeWithTag("password-field").assert(SemanticsMatcher.keyIsDefined(SemanticsProperties.Password))
        compose.onNodeWithTag("password-field").performImeAction()

        compose.runOnIdle { assertEquals("member" to "secret-value", submitted) }
    }

    @Test
    fun submittingDisablesASecondSubmissionAndRetainsFieldsOnError() {
        val state = mutableStateOf<AuthUiState>(AuthUiState.Form(registration = false))
        var calls = 0
        compose.setContent {
            KuraStorageTheme { AuthScreen(state.value, { _, _ -> calls++ }, {}, {}) }
        }
        compose.onNodeWithTag("username-field").performTextInput("member")
        compose.onNodeWithTag("password-field").performTextInput("secret")
        compose.runOnIdle { state.value = AuthUiState.Form(registration = false, username = "member", submitting = true) }

        compose.onNodeWithText("Signing in…").assertIsDisplayed()
        compose.onNodeWithTag("auth-submit").assertIsNotEnabled()
        compose.runOnIdle {
            state.value =
                AuthUiState.Form(
                    registration = false,
                    username = "member",
                    error = ApiError(ErrorCode.AUTHENTICATION_REQUIRED, "req", 401),
                )
        }
        compose.onNodeWithTag("username-field").assertTextContains("member")
        compose.onNodeWithContentDescription("Show password").performClick()
        compose.onNodeWithTag("password-field").assertTextContains("secret")
        compose.onNodeWithText("Sign-in failed").assertIsDisplayed()
        compose.runOnIdle { assertEquals(0, calls) }
    }

    @Test
    fun remoteUnregisteredDeviceOffersNoRegistrationAction() {
        var submits = 0
        compose.setContent {
            KuraStorageTheme { AuthScreen(AuthUiState.RequiresLocalDirect, { _, _ -> submits++ }, {}, {}) }
        }

        compose.onNodeWithText("Local connection required").assertIsDisplayed()
        compose.onNodeWithText("Return to connection check").assertIsDisplayed()
        compose.onAllNodesWithText("Register and sign in").assertCountEquals(0)
        compose.runOnIdle { assertEquals(0, submits) }
    }

    @Test
    fun revokedDeviceHasSecuritySpecificRecovery() {
        compose.setContent {
            KuraStorageTheme {
                AuthScreen(AuthUiState.Error(ApiError(ErrorCode.DEVICE_REVOKED, "request-7", 401)), { _, _ -> }, {}, {})
            }
        }

        compose.onNodeWithText("This device was revoked").assertIsDisplayed()
        compose.scrollAuthTo("Request ID: request-7").assertIsDisplayed()
    }

    @Test
    fun compactTwoHundredPercentDarkFormRemainsReachableAndMasked() {
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale = 2f)) {
                KuraStorageTheme(darkTheme = true) {
                    Box(Modifier.size(width = 360.dp, height = 800.dp)) {
                        AuthScreen(AuthUiState.Form(registration = false), { _, _ -> }, {}, {})
                    }
                }
            }
        }

        compose.onNodeWithTag("auth-screen").performScrollToNode(hasTestTag("password-field"))
        compose.onNodeWithTag("password-field").performTextInput("not-in-capture")
        compose.onNodeWithTag("password-field").assert(SemanticsMatcher.keyIsDefined(SemanticsProperties.Password))
        compose.onNodeWithTag("auth-screen").performScrollToNode(hasTestTag("auth-submit"))
        compose.onNodeWithTag("auth-screen").captureToImage()
    }

    private fun androidx.compose.ui.test.junit4.ComposeContentTestRule.scrollAuthTo(text: String) =
        onNodeWithTag("auth-screen").performScrollToNode(hasText(text)).let { onNodeWithText(text) }
}
