@file:Suppress("ktlint:standard:function-naming", "FunctionNaming")

package com.kurastorage.feature.connection

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.StorageAvailability
import com.kurastorage.core.ui.LoadingState

@Composable
fun ConnectionScreen(
    state: ConnectionStatus,
    onRecheck: () -> Unit,
    onConnected: (ConnectionStatus.Connected) -> Unit,
) {
    LaunchedEffect(state) {
        val connected = state as? ConnectionStatus.Connected
        if (connected?.storage == StorageAvailability.AVAILABLE) onConnected(connected)
    }

    when (state) {
        ConnectionStatus.Checking -> LoadingState("Checking KuraStorage connection…")
        is ConnectionStatus.Connected -> ConnectedState(state, onRecheck)
        ConnectionStatus.Disconnected ->
            ActionState(
                title = "KuraStorage is unreachable",
                detail = "Check the separate ZeroTier app, then return and try again.",
                onRecheck = onRecheck,
            )
        ConnectionStatus.TlsFailure ->
            ActionState(
                title = "Secure connection failed",
                detail = "The server certificate or hostname could not be verified.",
                onRecheck = onRecheck,
            )
    }
}

@Composable
private fun ConnectedState(
    state: ConnectionStatus.Connected,
    onRecheck: () -> Unit,
) {
    if (state.storage == StorageAvailability.UNAVAILABLE) {
        ActionState(
            title = "Storage unavailable",
            detail = "The server is reachable, but its dedicated storage is not available.",
            onRecheck = onRecheck,
        )
        return
    }
    val route =
        when (state.route) {
            ConnectionRoute.LOCAL_DIRECT -> "Connected directly on the local network"
            ConnectionRoute.REMOTE_SECURE -> "Connected through ZeroTier"
        }
    ActionState(route, "Secure connection verified.", onRecheck)
}

@Composable
private fun ActionState(
    title: String,
    detail: String,
    onRecheck: () -> Unit,
) {
    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(title, style = MaterialTheme.typography.headlineSmall)
        Text(detail)
        Button(onClick = onRecheck) { Text("Check again") }
    }
}
