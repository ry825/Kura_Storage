package com.kurastorage.feature.media.player

import androidx.compose.ui.test.assertHeightIsEqualTo
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.unit.dp
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

        compose.onNodeWithContentDescription("Playback position").assertIsDisplayed()
        compose.onNodeWithContentDescription("Back 3 seconds").assertIsDisplayed().assertHeightIsEqualTo(48.dp)
        compose.onNodeWithContentDescription("Forward 10 seconds").assertIsDisplayed().assertHeightIsEqualTo(48.dp)
        compose.onNodeWithContentDescription("Playback speed 1.5 times").assertIsDisplayed()
        compose.onNodeWithText("Playback error: UNSUPPORTED_CODEC").assertIsDisplayed()
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
                "Estimated transfer: 1.0 KiB. Range playback may receive less data than the full file. Actual usage can vary.",
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

        compose.onNodeWithContentDescription("Queued: position 3").assertIsDisplayed()
        compose.onNodeWithText("Continue in background").assertIsDisplayed()
        compose.onNodeWithText("Play original").assertIsDisplayed()
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

        compose.onNodeWithText("Playback ended").assertIsDisplayed()
        compose.onNodeWithText("Replay").performClick()
        compose.runOnIdle {
            assertTrue(played)
            assertTrue(seekPosition == 0L)
        }
        compose.onNodeWithText("Reconnect").assertIsDisplayed()
        compose.onNodeWithText("Retry conversion").assertDoesNotExist()
    }
}
