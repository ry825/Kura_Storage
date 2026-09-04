@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "LongMethod")

package com.kurastorage.feature.connection

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.StorageAvailability
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraPrimaryButton
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.icons.KuraLogo

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

    Surface(
        modifier = Modifier.fillMaxSize(),
        color = MaterialTheme.colorScheme.background,
    ) {
        LazyColumn(
            modifier =
                Modifier
                    .fillMaxSize()
                    .windowInsetsPadding(WindowInsets.safeDrawing)
                    .testTag("connection-screen"),
            contentPadding =
                androidx.compose.foundation.layout
                    .PaddingValues(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            item { ConnectionHeader() }
            if (state == ConnectionStatus.Checking) {
                item { CheckingState() }
            } else {
                item { ConnectionResult(state) }
                item { ConnectionChecks(state) }
                item {
                    KuraPrimaryButton(
                        label = "Check again",
                        onClick = onRecheck,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
        }
    }
}

@Composable
private fun ConnectionHeader() {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        KuraLogo(size = 88.dp)
        Text("KuraStorage", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.headlineMedium)
        Text("Finding the safest available route to your storage", style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
private fun CheckingState() {
    KuraCard(modifier = Modifier.fillMaxWidth().semantics { stateDescription = "Connection check in progress" }) {
        CircularProgressIndicator(modifier = Modifier.testTag("connection-progress"))
        Text("Checking connection", style = MaterialTheme.typography.titleLarge)
        Text("Checking local direct access first, then ZeroTier reachability.")
        ConnectionCheckRow("Local direct", "Checking the local network")
        ConnectionCheckRow("ZeroTier", "Checked only when local direct is unavailable")
        ConnectionCheckRow("Server and storage", "Waiting for a verified HTTPS response")
    }
}

@Composable
private fun ConnectionResult(state: ConnectionStatus) {
    val presentation = state.presentation()
    KuraStatusPanel(
        title = presentation.title,
        message = presentation.message,
        status = presentation.status,
        modifier = Modifier.fillMaxWidth().semantics { stateDescription = presentation.stateDescription },
    )
}

@Composable
private fun ConnectionChecks(state: ConnectionStatus) {
    KuraCard(modifier = Modifier.fillMaxWidth()) {
        Text("Connection details", modifier = Modifier.kuraHeading(), style = MaterialTheme.typography.titleLarge)
        when (state) {
            is ConnectionStatus.Connected -> {
                ConnectionCheckRow(
                    "Connection route",
                    if (state.route == ConnectionRoute.LOCAL_DIRECT) "Local direct" else "ZeroTier",
                )
                ConnectionCheckRow(
                    "Base network",
                    if (state.route == ConnectionRoute.LOCAL_DIRECT) "Wi-Fi or Ethernet" else "Managed by Android",
                )
                ConnectionCheckRow("Server", "Reachable with verified HTTPS")
                ConnectionCheckRow(
                    "Dedicated storage",
                    if (state.storage == StorageAvailability.AVAILABLE) "Available" else "Unavailable",
                )
            }
            ConnectionStatus.Disconnected -> {
                ConnectionCheckRow("Base network", "No verified KuraStorage route")
                ConnectionCheckRow("ZeroTier", "Check connection and membership in the separate ZeroTier app")
                ConnectionCheckRow("Server", "Unreachable")
                ConnectionCheckRow("Dedicated storage", "Not checked")
            }
            ConnectionStatus.TlsFailure -> {
                ConnectionCheckRow("Connection route", "Network route found")
                ConnectionCheckRow("Server identity", "Certificate or hostname verification failed")
                ConnectionCheckRow("Dedicated storage", "Not checked")
            }
            ConnectionStatus.IncompatibleProtocol -> {
                ConnectionCheckRow("Server", "Reachable")
                ConnectionCheckRow("Protocol", "Unsupported by this app version")
                ConnectionCheckRow("Dedicated storage", "Not checked")
            }
            ConnectionStatus.Checking -> Unit
        }
    }
}

@Composable
private fun ConnectionCheckRow(
    label: String,
    value: String,
) {
    Column(modifier = Modifier.fillMaxWidth().padding(vertical = KuraTheme.spacing.xxs)) {
        Text(label, style = MaterialTheme.typography.labelLarge)
        Text(value, style = MaterialTheme.typography.bodyMedium)
    }
}

private data class ConnectionPresentation(
    val title: String,
    val message: String,
    val stateDescription: String,
    val status: KuraStatus,
)

private fun ConnectionStatus.presentation(): ConnectionPresentation =
    when (this) {
        is ConnectionStatus.Connected ->
            if (storage == StorageAvailability.UNAVAILABLE) {
                ConnectionPresentation(
                    "Storage unavailable",
                    "The server is reachable, but its dedicated storage cannot be used. Contact the administrator.",
                    "Server reachable, dedicated storage unavailable",
                    KuraStatus.ERROR,
                )
            } else if (route == ConnectionRoute.LOCAL_DIRECT) {
                ConnectionPresentation(
                    "Connected locally",
                    "Local direct was verified and takes priority over ZeroTier.",
                    "Connected by local direct route",
                    KuraStatus.SUCCESS,
                )
            } else {
                ConnectionPresentation(
                    "Connected through ZeroTier",
                    "Secure remote reachability is verified. ZeroTier remains managed in its separate app.",
                    "Connected by ZeroTier route",
                    KuraStatus.SUCCESS,
                )
            }
        ConnectionStatus.Disconnected ->
            ConnectionPresentation(
                "KuraStorage is unreachable",
                "Check your network and the separate ZeroTier app, then return and check again.",
                "No verified route to KuraStorage",
                KuraStatus.WARNING,
            )
        ConnectionStatus.TlsFailure ->
            ConnectionPresentation(
                "Secure connection failed",
                "The server certificate or hostname could not be verified. Do not continue on this connection.",
                "TLS certificate or hostname verification failed",
                KuraStatus.ERROR,
            )
        ConnectionStatus.IncompatibleProtocol ->
            ConnectionPresentation(
                "App update required",
                "This server uses a protocol version that this app cannot safely use.",
                "Incompatible server protocol",
                KuraStatus.ERROR,
            )
        ConnectionStatus.Checking -> error("Checking has a dedicated presentation")
    }
