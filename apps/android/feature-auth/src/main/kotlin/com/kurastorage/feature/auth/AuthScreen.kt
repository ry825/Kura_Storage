@file:Suppress("ktlint:standard:function-naming", "FunctionNaming")

package com.kurastorage.feature.auth

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.kurastorage.core.ui.ErrorState
import com.kurastorage.core.ui.LoadingState

@Composable
fun AuthScreen(
    state: AuthUiState,
    onSubmit: (String, String) -> Unit,
    onRetry: () -> Unit,
    onAuthenticated: () -> Unit,
) {
    when (state) {
        AuthUiState.Loading -> LoadingState("Checking device registration…")
        AuthUiState.Submitting -> LoadingState("Signing in…")
        AuthUiState.RequiresLocalDirect ->
            MessageState(
                "Local connection required",
                "Connect this device directly to the KuraStorage home network before registering it.",
                onRetry,
            )
        AuthUiState.Authenticated -> LaunchedEffect(Unit) { onAuthenticated() }
        is AuthUiState.Form -> AuthForm(state, onSubmit)
        is AuthUiState.Error ->
            ErrorState(
                message =
                    if (state.error.code.name == "DEVICE_REVOKED") {
                        "This device was revoked. Reconnect locally to register it again."
                    } else {
                        "Authentication failed. Check your details and try again."
                    },
                requestId = state.error.requestId,
                onRetry = onRetry,
            )
    }
}

@Composable
private fun AuthForm(
    state: AuthUiState.Form,
    onSubmit: (String, String) -> Unit,
) {
    var username by remember(state.username) { mutableStateOf(state.username) }
    var password by remember { mutableStateOf("") }
    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
    ) {
        Text(
            if (state.registration) "Register this device" else "Sign in",
            style = MaterialTheme.typography.headlineSmall,
        )
        OutlinedTextField(
            value = username,
            onValueChange = { username = it },
            label = { Text("Username") },
            singleLine = true,
        )
        OutlinedTextField(
            value = password,
            onValueChange = { password = it },
            label = { Text("Password") },
            singleLine = true,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
        )
        Button(
            onClick = { onSubmit(username, password) },
            enabled = username.isNotBlank() && password.isNotEmpty(),
        ) {
            Text(if (state.registration) "Register and sign in" else "Sign in")
        }
    }
}

@Composable
private fun MessageState(
    title: String,
    detail: String,
    onRetry: () -> Unit,
) {
    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(title, style = MaterialTheme.typography.headlineSmall)
        Text(detail)
        Button(onClick = onRetry) { Text("Check again") }
    }
}
