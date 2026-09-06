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
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.requiredSize
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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusEvent
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.kurastorage.core.data.media.NetworkTransport
import com.kurastorage.core.model.media.LONG_SKIP_MS
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaUiError
import com.kurastorage.core.model.media.PlaybackRate
import com.kurastorage.core.model.media.SHORT_SKIP_MS
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.accessibility.kuraSelected
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel
import kotlinx.coroutines.delay
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
    onConfirmOriginal: () -> Unit,
    onCancelOriginal: () -> Unit,
    onRetryPlayback: () -> Unit,
    onFullscreen: () -> Unit,
    fullscreen: Boolean = false,
    videoSurface: @Composable () -> Unit = {},
) {
    val media = state.media
    val load = media?.loadState
    var videoControlsVisible by remember(fullscreen) { mutableStateOf(!fullscreen || !state.player.playWhenReady) }
    var seeking by remember { mutableStateOf(false) }
    var controlsFocused by remember { mutableStateOf(false) }
    val controlsMustRemainVisible =
        !state.player.playWhenReady ||
            state.player.phase in setOf(PlayerPhase.IDLE, PlayerPhase.BUFFERING, PlayerPhase.FAILED, PlayerPhase.ENDED) ||
            state.player.error != null ||
            controlsFocused ||
            seeking
    LaunchedEffect(fullscreen, videoControlsVisible, state.player.playWhenReady, state.player.phase, seeking, controlsFocused) {
        val canScheduleAutoHide = videoControlsVisible && !controlsMustRemainVisible
        if (canScheduleAutoHide && !seeking && !controlsFocused) {
            delay(CONTROL_AUTO_HIDE_MS)
            videoControlsVisible = false
        }
    }
    if (fullscreen && state.kind == MediaKind.VIDEO) {
        VideoPlayerFrame(
            state = state,
            fullscreen = true,
            modifier = Modifier.fillMaxSize().testTag("fullscreen-player"),
            controlsVisible = videoControlsVisible || controlsMustRemainVisible,
            onToggleControls = { videoControlsVisible = !videoControlsVisible },
            onPlay = onPlay,
            onPause = onPause,
            onSeek = onSeek,
            onSkipBack = onSkipBack,
            onSkipForward = onSkipForward,
            onRate = onRate,
            onFullscreen = onFullscreen,
            onSeeking = {
                seeking = it
                if (it) videoControlsVisible = true
            },
            onControlsFocused = { controlsFocused = it },
            videoSurface = videoSurface,
        )
    } else {
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
                    val aspectRatio = state.player.videoAspectRatio.safeAspectRatio()
                    val maximumHeight = LocalConfiguration.current.screenHeightDp.dp * 0.55f
                    BoxWithConstraints(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                        val frameWidth = minOf(maxWidth, maximumHeight * aspectRatio)
                        val frameHeight = maxOf(frameWidth / aspectRatio, minOf(maximumHeight, 320.dp))
                        VideoPlayerFrame(
                            state = state,
                            fullscreen = false,
                            modifier = Modifier.requiredSize(frameWidth, frameHeight),
                            controlsVisible = videoControlsVisible || controlsMustRemainVisible,
                            onToggleControls = { videoControlsVisible = !videoControlsVisible },
                            onPlay = onPlay,
                            onPause = onPause,
                            onSeek = onSeek,
                            onSkipBack = onSkipBack,
                            onSkipForward = onSkipForward,
                            onRate = onRate,
                            onFullscreen = onFullscreen,
                            onSeeking = {
                                seeking = it
                                if (it) videoControlsVisible = true
                            },
                            onControlsFocused = { controlsFocused = it },
                            videoSurface = videoSurface,
                        )
                    }
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
                    Text("Connection: ${media?.transport.userLabel()}")
                    Text("Displayed size: ${media?.displayedSizeLabel ?: "Not ready"}")
                    Text("Quality: Original")
                }

                PlayerStatus(state, load, onRetryPlayback)

                if (state.kind != MediaKind.VIDEO) {
                    PlayerControls(state.player, onPlay, onPause, onSeek, onSkipBack, onSkipForward, onRate, onSeeking = {})
                }
            }
        }
    }

    media?.confirmation?.let { prompt ->
        AlertDialog(
            onDismissRequest = onCancelOriginal,
            title = { Text(if (state.kind == MediaKind.AUDIO) "Play original audio?" else "Play original video?") },
            text = {
                Text(
                    "Estimated transfer: ${prompt.formattedSize}. Current connection: ${media.transport.userLabel()}. " +
                        "Range playback may receive less data than the full file. Actual usage can vary.",
                )
            },
            confirmButton = { Button(onClick = onConfirmOriginal) { Text("Play original") } },
            dismissButton = { TextButton(onClick = onCancelOriginal) { Text("Cancel") } },
        )
    }
}

@Composable
private fun VideoPlayerFrame(
    state: MediaPlayerUiState,
    fullscreen: Boolean,
    modifier: Modifier,
    controlsVisible: Boolean,
    onToggleControls: () -> Unit,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSeek: (Long) -> Unit,
    onSkipBack: (Long) -> Unit,
    onSkipForward: (Long) -> Unit,
    onRate: (PlaybackRate) -> Unit,
    onFullscreen: () -> Unit,
    onSeeking: (Boolean) -> Unit,
    onControlsFocused: (Boolean) -> Unit,
    videoSurface: @Composable () -> Unit,
) {
    val aspectRatio = state.player.videoAspectRatio.safeAspectRatio()
    Surface(modifier, color = Color.Black) {
        BoxWithConstraints(Modifier.fillMaxSize().background(Color.Black), contentAlignment = Alignment.Center) {
            val containerAspectRatio = maxWidth.value / maxHeight.value.coerceAtLeast(1f)
            val contentWidth = if (aspectRatio >= containerAspectRatio) maxWidth else maxHeight * aspectRatio
            val contentHeight = if (aspectRatio >= containerAspectRatio) maxWidth / aspectRatio else maxHeight
            val videoModifier =
                Modifier.requiredSize(width = contentWidth, height = contentHeight)
            Box(videoModifier.testTag("video-content"), contentAlignment = Alignment.Center) { videoSurface() }
            VideoControlTapLayer(onToggleControls)
            if (state.player.phase == PlayerPhase.BUFFERING) CircularProgressIndicator()
            if (controlsVisible) {
                Column(
                    Modifier
                        .align(Alignment.BottomCenter)
                        .fillMaxWidth()
                        .background(Color.Black.copy(alpha = 0.72f))
                        .windowInsetsPadding(WindowInsets.safeDrawing)
                        .padding(KuraTheme.spacing.sm)
                        .onFocusEvent { onControlsFocused(it.hasFocus) }
                        .testTag("player-overlay"),
                    verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
                ) {
                    VideoPlaybackControls(
                        state.player,
                        onPlay,
                        onPause,
                        onSeek,
                        onSkipBack,
                        onSkipForward,
                        onRate,
                        onSeeking,
                        onFullscreen,
                        fullscreen,
                    )
                }
            }
        }
    }
}

@Composable
private fun BoxScope.VideoControlTapLayer(onToggleControls: () -> Unit) {
    Box(
        Modifier
            .matchParentSize()
            .clickable(onClickLabel = "Toggle video controls", onClick = onToggleControls)
            .semantics { contentDescription = "Toggle video controls" }
            .testTag("video-surface"),
    )
}

@Composable
private fun VideoPlaybackControls(
    player: PlayerSnapshot,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSeek: (Long) -> Unit,
    onSkipBack: (Long) -> Unit,
    onSkipForward: (Long) -> Unit,
    onRate: (PlaybackRate) -> Unit,
    onSeeking: (Boolean) -> Unit,
    onFullscreen: () -> Unit,
    fullscreen: Boolean,
) {
    val duration = player.durationMs.coerceAtLeast(0)
    val position = player.positionMs.coerceIn(0, duration)
    Text("${formatDuration(position)} / ${formatDuration(duration)}", color = Color.White)
    Slider(
        value = position.toFloat(),
        onValueChange = {
            onSeeking(true)
            onSeek(it.toLong())
        },
        onValueChangeFinished = { onSeeking(false) },
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
        ) {
            Text(
                if (player.playWhenReady) {
                    "Pause"
                } else if (player.phase == PlayerPhase.ENDED) {
                    "Replay"
                } else {
                    "Play"
                },
            )
        }
        SkipButton("Forward 3 seconds", "+3s") { onSkipForward(SHORT_SKIP_MS) }
        SkipButton("Forward 10 seconds", "+10s") { onSkipForward(LONG_SKIP_MS) }
        val nextRate = PlayerCommandController.nextRate(player.rate)
        OutlinedButton(
            onClick = { onRate(nextRate) },
            modifier = Modifier.heightIn(min = 48.dp).semantics { contentDescription = "Playback speed ${player.rate.value} times" },
        ) { Text("Speed ${player.rate.value}×") }
        OutlinedButton(onClick = onFullscreen, modifier = Modifier.heightIn(min = 48.dp)) {
            Text(if (fullscreen) "Exit full screen" else "Full screen")
        }
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
private fun PlayerStatus(
    state: MediaPlayerUiState,
    load: MediaLoadState?,
    onRetryPlayback: () -> Unit,
) {
    when (load) {
        MediaLoadState.Idle, MediaLoadState.Loading ->
            KuraStatusPanel(
                if (state.reconnecting) "Reconnecting" else "Loading media",
                if (state.reconnecting) "Waiting for the current connection." else "Preparing the selected source.",
                KuraStatus.INFO,
            )
        is MediaLoadState.Generating ->
            KuraStatusPanel(
                "Media unavailable",
                "Original playback does not require a conversion job.",
                KuraStatus.ERROR,
            )
        is MediaLoadState.Failed ->
            KuraStatusPanel(
                "Media unavailable",
                load.error.userMessage(),
                KuraStatus.ERROR,
                action = {
                    Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                        OutlinedButton(onClick = onRetryPlayback) { Text("Reconnect") }
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
private fun PlayerControls(
    player: PlayerSnapshot,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSeek: (Long) -> Unit,
    onSkipBack: (Long) -> Unit,
    onSkipForward: (Long) -> Unit,
    onRate: (PlaybackRate) -> Unit,
    onSeeking: (Boolean) -> Unit,
) {
    val duration = player.durationMs.coerceAtLeast(0)
    val position = player.positionMs.coerceIn(0, duration.coerceAtLeast(0))
    KuraCard {
        Text("Playback", style = MaterialTheme.typography.titleMedium, modifier = Modifier.kuraHeading())
        Text("${formatDuration(position)} / ${formatDuration(duration)}")
        Slider(
            value = position.toFloat(),
            onValueChange = {
                onSeeking(true)
                onSeek(it.toLong())
            },
            onValueChangeFinished = { onSeeking(false) },
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

private fun NetworkTransport?.userLabel(): String =
    when (this) {
        NetworkTransport.WIFI -> "Wi-Fi"
        NetworkTransport.ETHERNET -> "Ethernet"
        NetworkTransport.CELLULAR -> "Mobile network"
        NetworkTransport.OTHER_OR_UNKNOWN -> "Other or unknown network"
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
        MediaUiError.SERVER_ERROR -> "The server could not provide this media response. Try again."
        MediaUiError.GENERATION_FAILED -> "Original media could not be prepared."
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
        PlayerFailure.INCOMPLETE -> "The media response ended unexpectedly. Reconnect to try again."
        PlayerFailure.SERVER -> "The server could not provide this media response. Try again."
        PlayerFailure.UNSUPPORTED_CODEC, PlayerFailure.DECODER -> "This codec is not supported on this device. Automatic retry is disabled."
        PlayerFailure.UNKNOWN -> "Playback stopped safely because of an unexpected error."
    }

private fun formatDuration(milliseconds: Long): String {
    val seconds = milliseconds.coerceAtLeast(0) / 1_000
    return "%d:%02d".format(Locale.US, seconds / 60, seconds % 60)
}

private fun Float.safeAspectRatio(): Float = takeIf { isFinite() && this > 0f } ?: DEFAULT_VIDEO_ASPECT_RATIO

private const val CONTROL_AUTO_HIDE_MS = 3_000L
