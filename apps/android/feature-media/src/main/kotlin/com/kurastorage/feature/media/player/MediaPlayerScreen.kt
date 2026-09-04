@file:Suppress(
    "FunctionNaming",
    "CyclomaticComplexMethod",
    "LongParameterList",
    "LongMethod",
    "MagicNumber",
    "ReturnCount",
    "MaxLineLength",
    "TooManyFunctions",
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
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.kurastorage.core.model.media.LONG_SKIP_MS
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaUiError
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.PlaybackRate
import com.kurastorage.core.model.media.SHORT_SKIP_MS
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.accessibility.kuraSelected
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraSegmentedControl
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusBadge
import com.kurastorage.core.ui.components.KuraStatusPanel
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
    fullscreen: Boolean = false,
    videoSurface: @Composable () -> Unit = {},
) {
    val media = state.media
    val load = media?.loadState
    var videoControlsVisible by remember { mutableStateOf(true) }
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier
                .fillMaxSize()
                .windowInsetsPadding(WindowInsets.safeDrawing)
                .verticalScroll(rememberScrollState())
                .padding(if (fullscreen) KuraTheme.spacing.xs else KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md),
        ) {
            PlayerHeader(state, onBack)

            if (state.kind == MediaKind.VIDEO) {
                Box(
                    Modifier
                        .fillMaxWidth()
                        .aspectRatio(16f / 9f)
                        .background(MaterialTheme.colorScheme.surfaceVariant)
                        .clickable { videoControlsVisible = !videoControlsVisible }
                        .semantics { contentDescription = "Toggle video controls" }
                        .testTag("video-surface"),
                    contentAlignment = Alignment.Center,
                ) {
                    videoSurface()
                    if (state.player.phase == PlayerPhase.BUFFERING) CircularProgressIndicator()
                    if (state.player.phase == PlayerPhase.READY) {
                        KuraStatusBadge(
                            if (state.player.playWhenReady) "Playing" else "Paused",
                            KuraStatus.NEUTRAL,
                            Modifier.align(Alignment.TopStart).padding(KuraTheme.spacing.sm),
                        )
                    }
                }
                OutlinedButton(onClick = onFullscreen, modifier = Modifier.align(Alignment.End).heightIn(min = 48.dp)) {
                    Text(if (fullscreen) "Exit full screen" else "Full screen")
                }
                QualityControls(media?.quality, onQuality)
            } else {
                KuraCard {
                    Box(
                        Modifier
                            .fillMaxWidth()
                            .heightIn(min = 180.dp)
                            .semantics { contentDescription = "Audio artwork placeholder" },
                        contentAlignment = Alignment.Center,
                    ) { Text("♫", style = MaterialTheme.typography.displayLarge) }
                    Text(state.file?.name ?: "Audio", style = MaterialTheme.typography.titleLarge)
                    Text("Original audio only • no conversion")
                }
            }

            KuraCard {
                Text("Media details", style = MaterialTheme.typography.titleMedium, modifier = Modifier.kuraHeading())
                Text("Connection: ${media?.networkContext.userLabel()}")
                state.file
                    ?.size
                    ?.takeIf { it >= 0 }
                    ?.let { Text("Original size: ${formatByteCount(it)}") }
                Text(if (state.kind == MediaKind.VIDEO) "Quality: ${media?.quality.userLabel()}" else "Quality: Original")
            }

            PlayerStatus(state, load, onRetryGeneration, onRetryPlayback, onBackgroundGeneration, onQuality)

            if (state.kind != MediaKind.VIDEO || videoControlsVisible) {
                PlayerControls(state.player, onPlay, onPause, onSeek, onSkipBack, onSkipForward, onRate)
            }
        }
    }

    media?.confirmation?.let { prompt ->
        AlertDialog(
            onDismissRequest = onCancelOriginal,
            title = { Text(if (state.kind == MediaKind.AUDIO) "Play original audio?" else "Play original video?") },
            text = {
                Text(
                    "Estimated transfer: ${prompt.formattedSize}. Current connection: ${media.networkContext.userLabel()}. " +
                        "Range playback may receive less data than the full file. Actual usage can vary.",
                )
            },
            confirmButton = { Button(onClick = onConfirmOriginal) { Text("Play original") } },
            dismissButton = { TextButton(onClick = onCancelOriginal) { Text("Cancel") } },
        )
    }
}

@Composable
private fun PlayerHeader(
    state: MediaPlayerUiState,
    onBack: () -> Unit,
) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        TextButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") }
        Column(Modifier.weight(1f).padding(horizontal = KuraTheme.spacing.sm)) {
            Text(
                state.file?.name ?: if (state.kind == MediaKind.VIDEO) "Video player" else "Audio player",
                style = MaterialTheme.typography.titleLarge,
                modifier = Modifier.kuraHeading(),
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            Text(if (state.kind == MediaKind.VIDEO) "Video" else "Audio")
        }
    }
}

@Composable
private fun QualityControls(
    selected: MediaQuality?,
    onQuality: (MediaQuality) -> Unit,
) {
    KuraCard {
        Text("Video quality", style = MaterialTheme.typography.titleMedium, modifier = Modifier.kuraHeading())
        KuraSegmentedControl(
            labels = listOf("Low", "Medium", "Original"),
            selectedIndex = selected?.ordinal ?: -1,
            onSelected = { onQuality(MediaQuality.entries[it]) },
        )
        Text("Low and Medium never fall back to Original automatically.")
    }
}

@Composable
private fun PlayerStatus(
    state: MediaPlayerUiState,
    load: MediaLoadState?,
    onRetryGeneration: () -> Unit,
    onRetryPlayback: () -> Unit,
    onBackgroundGeneration: () -> Unit,
    onQuality: (MediaQuality) -> Unit,
) {
    when (load) {
        MediaLoadState.Idle, MediaLoadState.Loading ->
            KuraStatusPanel(
                if (state.reconnecting) "Reconnecting" else "Loading media",
                if (state.reconnecting) "Waiting for the current connection." else "Preparing the selected source.",
                KuraStatus.INFO,
            )
        is MediaLoadState.Generating -> GenerationPanel(load, onRetryGeneration, onBackgroundGeneration, onQuality)
        is MediaLoadState.Failed ->
            KuraStatusPanel(
                "Media unavailable",
                load.error.userMessage(),
                KuraStatus.ERROR,
                action = {
                    Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                        if (state.media?.canRetryGeneration == true) Button(onClick = onRetryGeneration) { Text("Retry conversion") }
                        OutlinedButton(onClick = onRetryPlayback) { Text("Reconnect") }
                        if (state.kind ==
                            MediaKind.VIDEO
                        ) {
                            OutlinedButton(onClick = { onQuality(MediaQuality.ORIGINAL) }) { Text("Play original") }
                        }
                    }
                },
            )
        else -> Unit
    }
    when (state.player.phase) {
        PlayerPhase.BUFFERING -> KuraStatusPanel("Buffering", "Playback will resume when enough data is available.", KuraStatus.INFO)
        PlayerPhase.ENDED -> KuraStatusPanel("Playback ended", "Replay starts from the beginning.", KuraStatus.NEUTRAL)
        else -> Unit
    }
    state.player.error?.let { KuraStatusPanel("Playback stopped", it.userMessage(), KuraStatus.ERROR) }
    state.error?.let { KuraStatusPanel("Cannot play this file", it.userMessage(), KuraStatus.ERROR) }
}

@Composable
private fun GenerationPanel(
    load: MediaLoadState.Generating,
    onRetry: () -> Unit,
    onBackground: () -> Unit,
    onQuality: (MediaQuality) -> Unit,
) {
    val job = load.job
    val status =
        when {
            job.status == MediaJobStatus.FAILED -> "Conversion failed"
            job.queuePosition != null -> "Queued: position ${job.queuePosition}"
            job.progressPercent != null -> "Converting: ${job.progressPercent}%"
            else -> "Waiting for the media worker"
        }
    KuraStatusPanel(
        "Preparing selected video quality",
        status,
        if (job.status == MediaJobStatus.FAILED) KuraStatus.ERROR else KuraStatus.INFO,
        action = {
            Column(Modifier.semantics { contentDescription = status }, verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                if (job.retryable) Button(onClick = onRetry) { Text("Retry conversion") }
                OutlinedButton(onClick = onBackground) { Text("Continue in background") }
                OutlinedButton(onClick = { onQuality(MediaQuality.ORIGINAL) }) { Text("Play original") }
            }
        },
    )
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
    KuraCard {
        Text("Playback", style = MaterialTheme.typography.titleMedium, modifier = Modifier.kuraHeading())
        Text("${formatDuration(position)} / ${formatDuration(duration)}")
        Slider(
            value = position.toFloat(),
            onValueChange = { onSeek(it.toLong()) },
            valueRange = 0f..duration.coerceAtLeast(1).toFloat(),
            enabled = player.seekable,
            modifier = Modifier.semantics { contentDescription = "Playback position" },
        )
        FlowRow(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
        ) {
            SkipButton("Back 10 seconds", "-10s") { onSkipBack(LONG_SKIP_MS) }
            SkipButton("Back 3 seconds", "-3s") { onSkipBack(SHORT_SKIP_MS) }
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
                modifier = Modifier.widthIn(min = 96.dp).height(48.dp),
                contentPadding = PaddingValues(horizontal = KuraTheme.spacing.xs),
            ) {
                Text(
                    if (player.phase == PlayerPhase.ENDED) {
                        "Replay"
                    } else if (player.playWhenReady) {
                        "Pause"
                    } else {
                        "Play"
                    },
                )
            }
            SkipButton("Forward 3 seconds", "+3s") { onSkipForward(SHORT_SKIP_MS) }
            SkipButton("Forward 10 seconds", "+10s") { onSkipForward(LONG_SKIP_MS) }
        }
        Text("Playback speed: ${player.rate.value}×")
        FlowRow(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
        ) {
            PlayerCommandController.APPROVED_RATES.forEach { rate ->
                val selected = player.rate.value == rate
                val action: @Composable () -> Unit = {
                    Text(String.format(Locale.US, "%g×", rate))
                }
                if (selected) {
                    Button(
                        onClick = { onRate(PlaybackRate(rate)) },
                        modifier =
                            Modifier.heightIn(min = 48.dp).kuraSelected(true).semantics {
                                contentDescription =
                                    "Playback speed $rate times"
                            },
                        content = { action() },
                    )
                } else {
                    OutlinedButton(
                        onClick = { onRate(PlaybackRate(rate)) },
                        modifier =
                            Modifier.heightIn(min = 48.dp).kuraSelected(false).semantics {
                                contentDescription =
                                    "Playback speed $rate times"
                            },
                        content = { action() },
                    )
                }
            }
        }
    }
}

@Composable
private fun SkipButton(
    description: String,
    label: String,
    action: () -> Unit,
) {
    OutlinedButton(
        onClick = action,
        modifier = Modifier.widthIn(min = 84.dp).height(48.dp).semantics { contentDescription = description },
        contentPadding = PaddingValues(horizontal = KuraTheme.spacing.xs),
    ) { Text(label) }
}

private fun MediaQuality?.userLabel(): String = this?.name?.lowercase()?.replaceFirstChar(Char::uppercase) ?: "Checking"

private fun NetworkQualityContext?.userLabel(): String =
    when (this) {
        NetworkQualityContext.LOCAL_DIRECT -> "Local direct"
        NetworkQualityContext.REGISTERED_REMOTE_WIFI -> "Registered Wi-Fi"
        NetworkQualityContext.UNREGISTERED_REMOTE_WIFI -> "Other Wi-Fi"
        NetworkQualityContext.REMOTE_MOBILE -> "Mobile network"
        null -> "Checking"
    }

private fun MediaUiError.userMessage(): String =
    when (this) {
        MediaUiError.AUTHENTICATION_REQUIRED -> "Sign in again to continue."
        MediaUiError.PERMISSION_DENIED -> "You no longer have permission to play this file."
        MediaUiError.NOT_FOUND -> "This file is no longer available."
        MediaUiError.FILE_CHANGED -> "The file changed. Return and open the latest version."
        MediaUiError.DISCONNECTED -> "Connection lost. Reconnect to continue."
        MediaUiError.RANGE_INVALID -> "The server cannot resume this playback range."
        MediaUiError.RESPONSE_INCOMPLETE -> "The media response ended unexpectedly."
        MediaUiError.GENERATION_FAILED -> "The selected video quality could not be prepared."
        MediaUiError.UNSUPPORTED -> "This codec is not supported on this device. Return to choose another file."
        MediaUiError.UNKNOWN -> "Playback stopped safely because of an unexpected error."
    }

private fun PlayerFailure.userMessage(): String =
    when (this) {
        PlayerFailure.AUTHENTICATION -> "Sign in again to continue."
        PlayerFailure.PERMISSION -> "You no longer have permission to play this file."
        PlayerFailure.FILE_CHANGED -> "The file changed. Return and open the latest version."
        PlayerFailure.RANGE -> "The server cannot resume this playback range."
        PlayerFailure.NETWORK -> "Connection lost. Use Reconnect after connectivity returns."
        PlayerFailure.UNSUPPORTED_CODEC, PlayerFailure.DECODER -> "This codec is not supported on this device. Automatic retry is disabled."
        PlayerFailure.UNKNOWN -> "Playback stopped safely because of an unexpected error."
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
