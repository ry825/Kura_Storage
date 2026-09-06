@file:Suppress("ktlint:standard:function-naming", "FunctionNaming")

package com.kurastorage.feature.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.NetworkQualityContext

@Composable
fun QualitySettingsScreen(
    state: QualitySettingsState,
    onSelect: (NetworkQualityContext, MediaQuality) -> Unit,
    onSave: () -> Unit,
    onReset: () -> Unit,
    onBack: () -> Unit,
) {
    Surface(
        modifier = Modifier.fillMaxSize(),
        color = MaterialTheme.colorScheme.background,
        contentColor = MaterialTheme.colorScheme.onBackground,
    ) {
        Column(
            modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text("Media quality and data use", style = MaterialTheme.typography.headlineMedium)
            Text(
                "These choices set the initial quality for photos. Videos always use the original file. " +
                    "You can always change photo quality while viewing.",
            )
            Text("Actual data use varies by file and format. Original content is never fetched before confirmation.")
            NetworkQualityContext.entries.forEach { context ->
                Text(context.label(), style = MaterialTheme.typography.titleMedium)
                Text(context.description(), style = MaterialTheme.typography.bodyMedium)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    MediaQuality.entries.forEach { quality ->
                        FilterChip(
                            modifier = Modifier.weight(1f),
                            selected = state.preferences.qualityFor(context) == quality,
                            onClick = { onSelect(context, quality) },
                            enabled = !state.loading && !state.saving,
                            label = { Text(quality.label()) },
                        )
                    }
                }
            }
            Text(
                text =
                    "Mobile data is never available for automatic backup. " +
                        "This screen changes initial viewer quality only.",
            )
            state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            Button(
                onClick = onSave,
                enabled = state.dirty && !state.saving,
                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
            ) { Text(if (state.saving) "Saving…" else "Save") }
            OutlinedButton(
                onClick = onReset,
                enabled = !state.loading && !state.saving,
                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
            ) { Text("Reset to defaults") }
            OutlinedButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") }
        }
    }
}

private fun NetworkQualityContext.label(): String =
    when (this) {
        NetworkQualityContext.LOCAL_DIRECT -> "Local direct connection"
        NetworkQualityContext.REGISTERED_REMOTE_WIFI -> "Registered external Wi-Fi + ZeroTier"
        NetworkQualityContext.UNREGISTERED_REMOTE_WIFI -> "Unregistered Wi-Fi + ZeroTier"
        NetworkQualityContext.REMOTE_MOBILE -> "Mobile + ZeroTier"
    }

private fun NetworkQualityContext.description(): String =
    when (this) {
        NetworkQualityContext.LOCAL_DIRECT -> "Same configured local subnet with direct server reachability."
        NetworkQualityContext.REGISTERED_REMOTE_WIFI ->
            "An enabled trusted Wi-Fi policy, with ZeroTier and server identity still verified."
        NetworkQualityContext.UNREGISTERED_REMOTE_WIFI ->
            "External Wi-Fi not enabled in trusted Wi-Fi settings."
        NetworkQualityContext.REMOTE_MOBILE ->
            "Initial viewing quality on mobile data; original still requires viewer confirmation."
    }

private fun MediaQuality.label(): String =
    when (this) {
        MediaQuality.LOW -> "Low"
        MediaQuality.MEDIUM -> "Medium"
        MediaQuality.ORIGINAL -> "Original"
    }
