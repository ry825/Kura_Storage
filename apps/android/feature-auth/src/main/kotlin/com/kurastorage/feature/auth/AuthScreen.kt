@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "LongMethod")

package com.kurastorage.feature.auth

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraPrimaryButton
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.icons.KuraLogo

@Composable
fun AuthScreen(
    state: AuthUiState,
    onSubmit: (String, String) -> Unit,
    onRetry: () -> Unit,
    onAuthenticated: () -> Unit,
) {
    if (state == AuthUiState.Authenticated) {
        LaunchedEffect(Unit) { onAuthenticated() }
        return
    }
    Surface(
        modifier = Modifier.fillMaxSize(),
        color = MaterialTheme.colorScheme.background,
    ) {
        LazyColumn(
            modifier = Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing).testTag("auth-screen"),
            contentPadding = PaddingValues(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            item { AuthHeader() }
            item {
                when (state) {
                    AuthUiState.Loading -> AuthLoading()
                    AuthUiState.RequiresLocalDirect -> RequiresLocalDirect(onRetry)
                    is AuthUiState.Form -> AuthForm(state, onSubmit)
                    is AuthUiState.Error -> BlockingAuthError(state.error, onRetry)
                    AuthUiState.Authenticated -> Unit
                }
            }
        }
    }
}

@Composable
private fun AuthHeader() {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        KuraLogo(size = 88.dp)
        Text("KuraStorage", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.headlineMedium)
        Text("Private storage, reached through a verified connection", style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
private fun AuthLoading() {
    KuraCard(modifier = Modifier.fillMaxWidth()) {
        CircularProgressIndicator(modifier = Modifier.testTag("auth-progress"))
        Text("Checking device registration", style = MaterialTheme.typography.titleLarge)
        Text("Looking for an existing secure session on this device.")
    }
}

@Composable
@Suppress("CyclomaticComplexMethod")
private fun AuthForm(
    state: AuthUiState.Form,
    onSubmit: (String, String) -> Unit,
) {
    var username by remember(state.registration) { mutableStateOf(state.username) }
    var password by remember(state.registration) { mutableStateOf("") }
    var passwordVisible by remember { mutableStateOf(false) }
    val passwordFocus = remember { FocusRequester() }
    val focusManager = LocalFocusManager.current
    val canSubmit = username.isNotBlank() && password.isNotEmpty() && !state.submitting
    val submit = {
        if (canSubmit) {
            focusManager.clearFocus()
            onSubmit(username, password)
        }
    }
    KuraCard(modifier = Modifier.fillMaxWidth()) {
        Text(
            if (state.registration) "Register this device" else "Sign in",
            modifier = Modifier.kuraHeading(),
            style = MaterialTheme.typography.headlineSmall,
        )
        Text(
            if (state.registration) {
                "Registration is available only while connected directly to the KuraStorage home network."
            } else {
                "Use your KuraStorage account to continue."
            },
        )
        if (state.registration && state.deviceName.isNotBlank()) {
            Text("Device: ${state.deviceName}", style = MaterialTheme.typography.bodyMedium)
        }
        Text(
            "🔒 Secure authentication",
            modifier = Modifier.semantics { contentDescription = "Secure authentication" },
            color = MaterialTheme.colorScheme.primary,
            style = MaterialTheme.typography.labelLarge,
        )
        OutlinedTextField(
            value = username,
            onValueChange = { username = it },
            modifier = Modifier.fillMaxWidth().testTag("username-field"),
            enabled = !state.submitting,
            label = { Text("Username") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Text, imeAction = ImeAction.Next),
            keyboardActions = KeyboardActions(onNext = { passwordFocus.requestFocus() }),
        )
        OutlinedTextField(
            value = password,
            onValueChange = { password = it },
            modifier = Modifier.fillMaxWidth().focusRequester(passwordFocus).testTag("password-field"),
            enabled = !state.submitting,
            label = { Text("Password") },
            singleLine = true,
            visualTransformation = if (passwordVisible) VisualTransformation.None else PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { submit() }),
            trailingIcon = {
                val action = if (passwordVisible) "Hide password" else "Show password"
                TextButton(
                    onClick = { passwordVisible = !passwordVisible },
                    enabled = !state.submitting,
                    modifier = Modifier.semantics { contentDescription = action },
                ) { Text(if (passwordVisible) "Hide" else "Show") }
            },
        )
        state.error?.let { error ->
            KuraStatusPanel(
                title = if (state.registration) "Device registration failed" else "Sign-in failed",
                message = inlineErrorMessage(state.registration, error),
                status = KuraStatus.ERROR,
            )
        }
        if (state.submitting) {
            CircularProgressIndicator(modifier = Modifier.testTag("auth-submit-progress"))
            Text(if (state.registration) "Registering this device…" else "Signing in…")
        }
        KuraPrimaryButton(
            label = if (state.registration) "Register and sign in" else "Sign in",
            onClick = submit,
            modifier = Modifier.fillMaxWidth().testTag("auth-submit"),
            enabled = canSubmit,
        )
    }
}

@Composable
private fun RequiresLocalDirect(onRetry: () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md)) {
        KuraStatusPanel(
            title = "Local connection required",
            message =
                "This device is not registered. Connect directly to the KuraStorage home network, " +
                    "then return here. ZeroTier registration is intentionally unavailable.",
            status = KuraStatus.WARNING,
        )
        KuraPrimaryButton("Return to connection check", onRetry, Modifier.fillMaxWidth())
    }
}

@Composable
private fun BlockingAuthError(
    error: ApiError,
    onRetry: () -> Unit,
) {
    val (title, message) =
        when (error.code) {
            ErrorCode.DEVICE_REVOKED -> "This device was revoked" to "Reconnect locally to register this device again."
            ErrorCode.REFRESH_TOKEN_REUSED ->
                "Sign in again" to "This session was ended because its security token was reused."
            ErrorCode.AUTHENTICATION_REQUIRED -> "Session expired" to "Sign in again to continue securely."
            else -> "Authentication unavailable" to "Return to the connection check and try again."
        }
    Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md)) {
        KuraStatusPanel(title, message, KuraStatus.ERROR)
        error.requestId?.let { Text("Request ID: $it", style = MaterialTheme.typography.bodySmall) }
        KuraPrimaryButton("Return to connection check", onRetry, Modifier.fillMaxWidth())
    }
}

private fun inlineErrorMessage(
    registration: Boolean,
    error: ApiError,
): String =
    when (error.code) {
        ErrorCode.RATE_LIMIT_EXCEEDED -> "Too many attempts. Wait a moment and try again."
        else ->
            if (registration) {
                "Check your details or ask an administrator, then try again."
            } else {
                "Check your details and try again."
            }
    }
