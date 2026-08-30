@file:Suppress(
    "FunctionNaming",
    "CyclomaticComplexMethod",
    "LongParameterList",
    "LongMethod",
    "MagicNumber",
    "ReturnCount",
    "ktlint:standard:function-naming",
)

package com.kurastorage.feature.media.player

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.media.LONG_SKIP_MS
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.PlaybackRate
import com.kurastorage.core.model.media.SHORT_SKIP_MS
import java.util.Locale

@Composable
fun MediaPlayerScreen(
    state: MediaPlayerUiState,
    onBack: () -> Unit,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSeek: (Long) -> Unit,
    onSkipBack: (Long) -> Unit,
    onSkipForward: (Long) -> Unit,
    onRate: (PlaybackRate) -> Unit,
    onQuality: (MediaQuality) -> Unit,
    onConfirmOriginal: () -> Unit,
    onCancelOriginal: () -> Unit,
    onRetryGeneration: () -> Unit,
    onRetryPlayback: () -> Unit,
    onBackgroundGeneration: () -> Unit,
    onFullscreen: () -> Unit,
    videoSurface: @Composable () -> Unit = {},
) {
    val media = state.media
    val load = media?.loadState
    var videoControlsVisible by remember { mutableStateOf(true) }
    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            TextButton(onClick = onBack) { Text("Back") }
            Text(
                state.file?.name ?: if (state.kind == MediaKind.VIDEO) "Video" else "Audio",
                style = MaterialTheme.typography.titleLarge,
                modifier = Modifier.weight(1f).padding(horizontal = 12.dp),
                maxLines = 2,
            )
        }

        if (state.kind == MediaKind.VIDEO) {
            Box(
                Modifier
                    .fillMaxWidth()
                    .aspectRatio(16f / 9f)
                    .background(MaterialTheme.colorScheme.surfaceVariant)
                    .clickable { videoControlsVisible = !videoControlsVisible }
                    .semantics { contentDescription = "Toggle video controls" },
                contentAlignment = Alignment.Center,
            ) {
                videoSurface()
                if (state.player.phase == PlayerPhase.BUFFERING) CircularProgressIndicator()
            }
            OutlinedButton(onClick = onFullscreen, modifier = Modifier.align(Alignment.End)) { Text("Fullscreen") }
            QualityControls(media?.quality, onQuality)
        } else {
            Box(
                Modifier
                    .fillMaxWidth()
                    .heightIn(min = 160.dp)
                    .background(MaterialTheme.colorScheme.surfaceVariant)
                    .semantics { contentDescription = "Audio artwork placeholder" },
                contentAlignment = Alignment.Center,
            ) { Text("♫", style = MaterialTheme.typography.displayLarge) }
            state.file
                ?.size
                ?.takeIf { it >= 0 }
                ?.let { Text("File size: ${formatByteCount(it)}") }
        }

        when (load) {
            MediaLoadState.Idle,
            MediaLoadState.Loading,
            -> Text(if (state.reconnecting) "Reconnecting…" else "Loading selected media…")
            is MediaLoadState.Generating -> GenerationPanel(load, onRetryGeneration, onBackgroundGeneration, onQuality)
            is MediaLoadState.Failed ->
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Media error: ${load.error.name}", color = MaterialTheme.colorScheme.error)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        if (media.canRetryGeneration) {
                            Button(onClick = onRetryGeneration) { Text("Retry conversion") }
                        }
                        OutlinedButton(onClick = onRetryPlayback) { Text("Reconnect") }
                        if (state.kind == MediaKind.VIDEO) {
                            OutlinedButton(onClick = { onQuality(MediaQuality.ORIGINAL) }) { Text("Play original") }
                        }
                    }
                }
            else -> Unit
        }
        when (state.player.phase) {
            PlayerPhase.BUFFERING -> Text("Buffering…")
            PlayerPhase.ENDED -> Text("Playback ended")
            else -> Unit
        }
        state.player.error?.let { Text("Playback error: ${it.name}", color = MaterialTheme.colorScheme.error) }
        state.error?.let { Text("Cannot play this file: ${it.name}", color = MaterialTheme.colorScheme.error) }

        if (state.kind != MediaKind.VIDEO || videoControlsVisible) {
            PlayerControls(state.player, onPlay, onPause, onSeek, onSkipBack, onSkipForward, onRate)
        }
    }

    media?.confirmation?.let { prompt ->
        AlertDialog(
            onDismissRequest = onCancelOriginal,
            title = { Text("Confirm original media") },
            text = {
                Text(
                    "Estimated transfer: ${prompt.formattedSize}. " +
                        "Range playback may receive less data than the full file. Actual usage can vary.",
                )
            },
            confirmButton = { Button(onClick = onConfirmOriginal) { Text("Play original") } },
            dismissButton = { TextButton(onClick = onCancelOriginal) { Text("Cancel") } },
        )
    }
}

@Composable
private fun QualityControls(
    selected: MediaQuality?,
    onQuality: (MediaQuality) -> Unit,
) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        MediaQuality.entries.forEach { quality ->
            val label = quality.name.lowercase().replaceFirstChar { it.uppercase() }
            if (quality == selected) {
                Button(onClick = { onQuality(quality) }, modifier = Modifier.weight(1f)) { Text(label) }
            } else {
                OutlinedButton(onClick = { onQuality(quality) }, modifier = Modifier.weight(1f)) { Text(label) }
            }
        }
    }
}

@Composable
private fun GenerationPanel(
    load: MediaLoadState.Generating,
    onRetry: () -> Unit,
    onBackground: () -> Unit,
    onQuality: (MediaQuality) -> Unit,
) {
    val job = load.job
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        val status =
            when {
                job.status == MediaJobStatus.FAILED -> "Conversion failed"
                job.queuePosition != null -> "Queued: position ${job.queuePosition}"
                job.progressPercent != null -> "Converting: ${job.progressPercent}%"
                else -> "Preparing selected video quality…"
            }
        Text(status, modifier = Modifier.semantics { contentDescription = status })
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (job.retryable) Button(onClick = onRetry) { Text("Retry conversion") }
            OutlinedButton(onClick = onBackground) { Text("Continue in background") }
            OutlinedButton(onClick = { onQuality(MediaQuality.ORIGINAL) }) { Text("Play original") }
        }
    }
}

@Composable
private fun PlayerControls(
    player: PlayerSnapshot,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSeek: (Long) -> Unit,
    onSkipBack: (Long) -> Unit,
    onSkipForward: (Long) -> Unit,
    onRate: (PlaybackRate) -> Unit,
) {
    val duration = player.durationMs.coerceAtLeast(0)
    val position = player.positionMs.coerceIn(0, duration.coerceAtLeast(0))
    Text("${formatDuration(position)} / ${formatDuration(duration)}")
    Slider(
        value = position.toFloat(),
        onValueChange = { onSeek(it.toLong()) },
        valueRange = 0f..duration.coerceAtLeast(1).toFloat(),
        enabled = player.seekable,
        modifier = Modifier.semantics { contentDescription = "Playback position" },
    )
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        SkipButton("Back 10 seconds", "-10s", Modifier.weight(1f).height(48.dp)) { onSkipBack(LONG_SKIP_MS) }
        SkipButton("Back 3 seconds", "-3s", Modifier.weight(1f).height(48.dp)) { onSkipBack(SHORT_SKIP_MS) }
        Button(
            onClick = {
                when {
                    player.phase == PlayerPhase.ENDED -> {
                        onSeek(0)
                        onPlay()
                    }
                    player.playWhenReady -> onPause()
                    else -> onPlay()
                }
            },
            modifier = Modifier.weight(1f).height(48.dp),
            contentPadding = PaddingValues(horizontal = 2.dp),
        ) {
            Text(
                when {
                    player.phase == PlayerPhase.ENDED -> "Replay"
                    player.playWhenReady -> "Pause"
                    else -> "Play"
                },
            )
        }
        SkipButton("Forward 3 seconds", "+3s", Modifier.weight(1f).height(48.dp)) { onSkipForward(SHORT_SKIP_MS) }
        SkipButton("Forward 10 seconds", "+10s", Modifier.weight(1f).height(48.dp)) { onSkipForward(LONG_SKIP_MS) }
    }
    Text("Playback speed: ${player.rate.value}×")
    FlowRow(
        Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(4.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp),
        maxItemsInEachRow = 3,
    ) {
        PlayerCommandController.APPROVED_RATES.forEach { rate ->
            TextButton(
                onClick = { onRate(PlaybackRate(rate)) },
                modifier = Modifier.semantics { contentDescription = "Playback speed $rate times" },
            ) { Text(String.format(Locale.US, "%g×", rate)) }
        }
    }
}

@Composable
private fun SkipButton(
    description: String,
    label: String,
    modifier: Modifier = Modifier,
    action: () -> Unit,
) {
    OutlinedButton(
        onClick = action,
        modifier = modifier.semantics { contentDescription = description },
        contentPadding = PaddingValues(horizontal = 2.dp),
    ) { Text(label) }
}

private fun formatDuration(milliseconds: Long): String {
    val seconds = milliseconds.coerceAtLeast(0) / 1_000
    return "%d:%02d".format(Locale.US, seconds / 60, seconds % 60)
}

private fun formatByteCount(bytes: Long): String {
    if (bytes < 1024) return "$bytes B"
    val kib = bytes / 1024.0
    if (kib < 1024) return String.format(Locale.US, "%.1f KiB", kib)
    return String.format(Locale.US, "%.1f MiB", kib / 1024.0)
}
