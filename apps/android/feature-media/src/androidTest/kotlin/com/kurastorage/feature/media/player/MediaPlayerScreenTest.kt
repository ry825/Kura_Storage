package com.kurastorage.feature.media.player

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertHeightIsEqualTo
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import com.kurastorage.core.data.media.TransferConfirmationPrompt
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.PlaybackRate
import com.kurastorage.feature.media.MediaViewerState
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test

class MediaPlayerScreenTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun commonPlayerControlsExposeSeekSkipRateAndCodecErrorSemantics() {
        compose.setContent {
            MediaPlayerScreen(
                state =
                    MediaPlayerUiState(
                        kind = MediaKind.AUDIO,
                        player =
                            PlayerSnapshot(
                                positionMs = 5_000,
                                durationMs = 60_000,
                                seekable = true,
                                rate = PlaybackRate(1.5f),
                                error = PlayerFailure.UNSUPPORTED_CODEC,
                            ),
                    ),
                onBack = {},
                onPlay = {},
                onPause = {},
                onSeek = {},
                onSkipBack = {},
                onSkipForward = {},
                onRate = {},
                onQuality = {},
                onConfirmOriginal = {},
                onCancelOriginal = {},
                onRetryGeneration = {},
                onRetryPlayback = {},
                onBackgroundGeneration = {},
                onFullscreen = {},
            )
        }

        compose.onNodeWithContentDescription("Playback position").performScrollTo().assertIsDisplayed()
        compose
            .onNodeWithContentDescription("Back 3 seconds")
            .performScrollTo()
            .assertIsDisplayed()
            .assertHeightIsEqualTo(48.dp)
        compose
            .onNodeWithContentDescription("Forward 10 seconds")
            .performScrollTo()
            .assertIsDisplayed()
            .assertHeightIsEqualTo(48.dp)
        compose.onNodeWithContentDescription("Playback speed 1.5 times").performScrollTo().assertIsDisplayed()
        compose
            .onNodeWithText(
                "This codec is not supported on this device. Automatic retry is disabled.",
            ).performScrollTo()
            .assertIsDisplayed()
        compose.onNodeWithText("Video quality").assertDoesNotExist()
        compose.onNodeWithText("Original audio only • no conversion").performScrollTo().assertIsDisplayed()
    }

    @Test
    fun videoOriginalConfirmationDoesNotStartUntilExplicitApproval() {
        var confirmed = false
        compose.setContent {
            MediaPlayerScreen(
                state =
                    MediaPlayerUiState(
                        kind = MediaKind.VIDEO,
                        media =
                            MediaViewerState(
                                "file",
                                1,
                                MediaKind.VIDEO,
                                MediaQuality.ORIGINAL,
                                NetworkQualityContext.REMOTE_MOBILE,
                                MediaLoadState.ConfirmingTransfer,
                                TransferConfirmationPrompt(
                                    "file",
                                    1,
                                    MediaKind.VIDEO,
                                    MediaVariant.ORIGINAL,
                                    ByteCount(1024),
                                    true,
                                    "1.0 KiB",
                                    "Up to 1.0 KiB may be transferred.",
                                ),
                            ),
                    ),
                onConfirmOriginal = { confirmed = true },
                onCancelOriginal = {},
                onBack = {},
                onPlay = {},
                onPause = {},
                onSeek = {},
                onSkipBack = {},
                onSkipForward = {},
                onRate = {},
                onQuality = {},
                onRetryGeneration = {},
                onRetryPlayback = {},
                onBackgroundGeneration = {},
                onFullscreen = {},
            )
        }

        compose
            .onNodeWithText(
                "Estimated transfer: 1.0 KiB. Current connection: Mobile network. Range playback may receive less data than the full file. Actual usage can vary.",
            ).assertIsDisplayed()
        compose.onNodeWithText("Play original").performClick()
        compose.runOnIdle { assertTrue(confirmed) }
    }

    @Test
    fun conversionQueueOffersBackgroundAndOriginalWithoutPlayingPartialMedia() {
        compose.setContent {
            MediaPlayerScreen(
                state =
                    MediaPlayerUiState(
                        kind = MediaKind.VIDEO,
                        media =
                            MediaViewerState(
                                "file",
                                1,
                                MediaKind.VIDEO,
                                MediaQuality.LOW,
                                NetworkQualityContext.REMOTE_MOBILE,
                                MediaLoadState.Generating(
                                    MediaJobSnapshot("job", MediaJobStatus.GENERATING, null, null, null, 3, 3, false),
                                ),
                            ),
                    ),
                onBack = {},
                onPlay = {},
                onPause = {},
                onSeek = {},
                onSkipBack = {},
                onSkipForward = {},
                onRate = {},
                onQuality = {},
                onConfirmOriginal = {},
                onCancelOriginal = {},
                onRetryGeneration = {},
                onRetryPlayback = {},
                onBackgroundGeneration = {},
                onFullscreen = {},
            )
        }

        compose.onNodeWithContentDescription("Queued: position 3").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Continue in background").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Play original").performScrollTo().assertIsDisplayed()
    }

    @Test
    fun terminalPlaybackAndNonRetryableFailureRemainDistinctAndActionable() {
        var played = false
        var seekPosition = -1L
        compose.setContent {
            MediaPlayerScreen(
                state =
                    MediaPlayerUiState(
                        kind = MediaKind.VIDEO,
                        media =
                            MediaViewerState(
                                "file",
                                1,
                                MediaKind.VIDEO,
                                MediaQuality.LOW,
                                NetworkQualityContext.REMOTE_MOBILE,
                                MediaLoadState.Failed(com.kurastorage.core.model.media.MediaUiError.GENERATION_FAILED),
                            ),
                        player = PlayerSnapshot(phase = PlayerPhase.ENDED),
                    ),
                onBack = {},
                onPlay = { played = true },
                onPause = {},
                onSeek = { seekPosition = it },
                onSkipBack = {},
                onSkipForward = {},
                onRate = {},
                onQuality = {},
                onConfirmOriginal = {},
                onCancelOriginal = {},
                onRetryGeneration = {},
                onRetryPlayback = {},
                onBackgroundGeneration = {},
                onFullscreen = {},
            )
        }

        compose.onNodeWithText("Playback ended").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Replay").performScrollTo().performClick()
        compose.runOnIdle {
            assertTrue(played)
            assertTrue(seekPosition == 0L)
        }
        compose.onNodeWithText("Reconnect").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Retry conversion").assertDoesNotExist()
    }

    @Test
    fun compactLargeTextFullscreenKeepsVideoAndPlaybackActionsReachable() {
        val density =
            InstrumentationRegistry
                .getInstrumentation()
                .targetContext.resources.displayMetrics.density
        compose.setContent {
            CompositionLocalProvider(LocalDensity provides Density(density, fontScale = 2f)) {
                MaterialTheme(colorScheme = darkColorScheme()) {
                    MediaPlayerScreen(
                        state =
                            MediaPlayerUiState(
                                kind = MediaKind.VIDEO,
                                media =
                                    MediaViewerState(
                                        "file",
                                        1,
                                        MediaKind.VIDEO,
                                        MediaQuality.MEDIUM,
                                        NetworkQualityContext.REGISTERED_REMOTE_WIFI,
                                        MediaLoadState.Ready(
                                            com.kurastorage.core.model.media.ReadyMediaSource(
                                                "file",
                                                1,
                                                MediaVariant.VIDEO_MEDIUM,
                                            ),
                                        ),
                                    ),
                                player = PlayerSnapshot(durationMs = 60_000, seekable = true, phase = PlayerPhase.READY),
                            ),
                        onBack = {},
                        onPlay = {},
                        onPause = {},
                        onSeek = {},
                        onSkipBack = {},
                        onSkipForward = {},
                        onRate = {},
                        onQuality = {},
                        onConfirmOriginal = {},
                        onCancelOriginal = {},
                        onRetryGeneration = {},
                        onRetryPlayback = {},
                        onBackgroundGeneration = {},
                        onFullscreen = {},
                        fullscreen = true,
                    )
                }
            }
        }

        compose.onNodeWithTag("video-surface").captureToImage()
        compose.onNodeWithText("Exit full screen").assertIsDisplayed()
        compose
            .onNodeWithContentDescription("Forward 10 seconds")
            .performScrollTo()
            .assertIsDisplayed()
            .assertHeightIsEqualTo(48.dp)
        compose.onNodeWithContentDescription("Playback speed 3.0 times").performScrollTo().assertIsDisplayed()
    }
}
