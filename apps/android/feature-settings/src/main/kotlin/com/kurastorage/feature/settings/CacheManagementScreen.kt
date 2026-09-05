@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "LongMethod")

package com.kurastorage.feature.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.media.AdminMediaCacheStatus
import com.kurastorage.core.model.media.MediaCleanupFailureCode
import com.kurastorage.core.model.media.MediaCleanupRun
import com.kurastorage.core.model.media.MediaCleanupRunStatus
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraCardVariant
import com.kurastorage.core.ui.components.KuraSectionHeader
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.formatting.formatFileSize
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun CacheManagementScreen(
    state: CacheManagementState,
    onRefresh: () -> Unit,
    onCleanup: () -> Unit,
    onBack: () -> Unit,
) {
    var confirming by remember { mutableStateOf(false) }
    LazyColumn(
        modifier = Modifier.fillMaxSize().testTag("cache-management"),
        contentPadding =
            androidx.compose.foundation.layout
                .PaddingValues(KuraTheme.spacing.md),
        verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md),
    ) {
        item {
            Text(
                "Cache management",
                modifier = Modifier.testTag("cache-title"),
                color = MaterialTheme.colorScheme.onBackground,
                style = MaterialTheme.typography.headlineMedium,
            )
        }
        if (state.loading && state.status == null) item { LinearProgressIndicator(Modifier.fillMaxWidth()) }
        state.error?.let { message ->
            item {
                KuraStatusPanel(
                    title =
                        if (state.access == CacheAccessState.FORBIDDEN) {
                            "Administrator access required"
                        } else {
                            "Cache status unavailable"
                        },
                    message = message,
                    status = KuraStatus.ERROR,
                    action =
                        if (state.access ==
                            CacheAccessState.FORBIDDEN
                        ) {
                            null
                        } else {
                            { TextButton(onClick = onRefresh) { Text("Refresh") } }
                        },
                )
            }
        }
        state.status?.let { status ->
            item { UsageCard(status) }
            item { CacheRunCard(status) }
            item { KuraSectionHeader("Ready cache breakdown") }
            item { BreakdownCard(status) }
            item {
                KuraStatusPanel(
                    title = "Regenerable display data",
                    message =
                        "Low and medium quality image and video cache can be regenerated. " +
                            "Original files and thumbnails are not included in this limit.",
                    status = KuraStatus.INFO,
                )
            }
            item {
                Button(
                    onClick = { confirming = true },
                    enabled = !state.requestingCleanup && state.access == CacheAccessState.AVAILABLE,
                    modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).testTag("cleanup-now"),
                ) {
                    Text(if (state.unknownCleanupOutcome) "Retry cleanup request" else "Clean up now")
                }
            }
        }
        item { OutlinedButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") } }
    }
    if (confirming) {
        AlertDialog(
            onDismissRequest = { confirming = false },
            title = { Text("Clean up regenerable cache?") },
            text = {
                Text(
                    "Expired cache and eligible low or medium quality data may be removed. " +
                        "Original files, thumbnails, generating items, and cache in use are not removed.",
                )
            },
            confirmButton = {
                Button(onClick = {
                    confirming = false
                    onCleanup()
                }) { Text("Request cleanup") }
            },
            dismissButton = { TextButton(onClick = { confirming = false }) { Text("Cancel") } },
        )
    }
}

@Composable
private fun UsageCard(status: AdminMediaCacheStatus) {
    val progress =
        if (status.highWatermarkBytes ==
            0L
        ) {
            0f
        } else {
            (status.cacheBytes.toDouble() / status.highWatermarkBytes).coerceIn(0.0, 1.0).toFloat()
        }
    KuraCard {
        Text("Usage / limit", style = MaterialTheme.typography.titleMedium)
        Text(
            "${formatFileSize(status.cacheBytes)} / ${formatFileSize(status.highWatermarkBytes)}",
            style = MaterialTheme.typography.headlineMedium,
        )
        LinearProgressIndicator(
            progress = { progress },
            modifier =
                Modifier.fillMaxWidth().semantics {
                    progressBarRangeInfo = ProgressBarRangeInfo(progress, 0f..1f)
                },
        )
        Text("Automatic cleanup starts above the limit and targets ${formatFileSize(status.lowWatermarkBytes)}.")
    }
}

@Composable
private fun CacheRunCard(status: AdminMediaCacheStatus) {
    val run = status.lastCleanupRun
    val variant = if (run?.status == MediaCleanupRunStatus.FAILED) KuraCardVariant.WARNING else KuraCardVariant.DEFAULT
    KuraCard(variant = variant) {
        StatusRow("Last cleanup", run?.let(::formatRunTime) ?: "Never")
        StatusRow("Generating", (status.queuedJobCount + status.runningJobCount).toString())
        StatusRow("Generation failures", status.failedJobCount.toString())
        StatusRow("Cleanup queue", "${status.pendingRunCount} pending, ${status.runningRunCount} running")
        run?.let { CleanupRunDetails(it) }
    }
}

@Composable
private fun CleanupRunDetails(run: MediaCleanupRun) {
    Text("Latest cleanup: ${run.status.label()}", style = MaterialTheme.typography.titleMedium)
    Text("Examined ${run.examinedCount}, removed ${run.deletedCount}, released ${formatFileSize(run.releasedBytes)}")
    if (run.failureCount > 0) {
        Text("Deletion failures: ${run.failureCount}", color = MaterialTheme.colorScheme.error)
    }
    run.failureCode?.let { Text("Result: ${it.label()}", color = MaterialTheme.colorScheme.error) }
}

@Composable
private fun BreakdownCard(status: AdminMediaCacheStatus) {
    KuraCard {
        StatusRow("Images · Low", formatFileSize(status.imageLowBytes))
        StatusRow("Images · Medium", formatFileSize(status.imageMediumBytes))
        StatusRow("Videos · Low", formatFileSize(status.videoLowBytes))
        StatusRow("Videos · Medium", formatFileSize(status.videoMediumBytes))
        StatusRow("Image total", formatFileSize(status.imageBytes))
        StatusRow("Video total", formatFileSize(status.videoBytes))
    }
}

@Composable
private fun StatusRow(
    label: String,
    value: String,
) {
    Row(
        Modifier.fillMaxWidth().semantics { contentDescription = "$label: $value" },
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(label, style = MaterialTheme.typography.bodyLarge)
        Text(value, style = MaterialTheme.typography.bodyLarge)
    }
}

private fun MediaCleanupRunStatus.label() = name.lowercase().replaceFirstChar(Char::uppercase)

private fun MediaCleanupFailureCode.label() = name.lowercase().replace('_', ' ')

private fun formatRunTime(run: MediaCleanupRun): String =
    (run.completedAt ?: run.startedAt ?: run.requestedAt)
        .atZone(ZoneId.systemDefault())
        .format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm"))
