@file:Suppress("ktlint:standard:function-naming", "FunctionNaming")

package com.kurastorage.feature.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.formatting.formatFileSize

@Composable
fun DerivativeStatusScreen(
    state: DerivativeStatusState,
    onRefresh: () -> Unit,
    onBack: () -> Unit,
) {
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding =
                androidx.compose.foundation.layout
                    .PaddingValues(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md),
        ) {
            item { Text("Fast display status", style = MaterialTheme.typography.headlineMedium) }
            if (state.loading && state.status == null) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
            state.error?.let { message ->
                item {
                    KuraStatusPanel(
                        title = "Status unavailable",
                        message = message,
                        status = KuraStatus.ERROR,
                        action = { TextButton(onClick = onRefresh) { Text("Retry") } },
                    )
                }
            }
            state.status?.let { status ->
                item {
                    KuraCard {
                        Text("Fast-display photo coverage", style = MaterialTheme.typography.titleMedium)
                        Text(
                            "${status.readyCount} / ${status.photoCount}",
                            style = MaterialTheme.typography.headlineMedium,
                        )
                        LinearProgressIndicator(progress = { status.coverage }, modifier = Modifier.fillMaxWidth())
                        Text("Profile ${status.profileVersion} · ${formatFileSize(status.readyBytes)}")
                        Text("Updated ${status.observedAt}", style = MaterialTheme.typography.bodySmall)
                    }
                }
                item {
                    KuraCard {
                        StatusRow("Ready", status.readyCount)
                        StatusRow("Queued", status.pendingCount)
                        StatusRow("Generating", status.runningCount)
                        StatusRow("Failed", status.failedCount)
                        StatusRow("Source unavailable", status.blockedCount)
                        StatusRow("Not created", status.missingCount)
                        StatusRow("Old/orphaned", status.orphanCount)
                        StatusRow("Duplicates", status.duplicateCount)
                    }
                }
                item {
                    KuraStatusPanel(
                        title = "Persistent fast-display files",
                        message = "These files are generated once and are not removed by photo cache cleanup.",
                        status = KuraStatus.INFO,
                    )
                }
            }
            item { OutlinedButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") } }
        }
    }
}

@Composable
private fun StatusRow(
    label: String,
    value: Long,
) {
    Row(
        Modifier.fillMaxWidth().semantics { contentDescription = "$label: $value" },
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(label)
        Text(value.toString())
    }
}
