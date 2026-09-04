@file:Suppress(
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "MaxLineLength",
    "ktlint:standard:function-naming",
)

package com.kurastorage.app

import android.content.pm.ActivityInfo
import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.feature.media.MediaViewerController
import com.kurastorage.feature.media.player.AndroidMediaPlayerController
import com.kurastorage.feature.media.player.MediaPlayerScreen
import com.kurastorage.feature.media.player.MediaPlayerViewModel
import com.kurastorage.feature.media.player.MediaVideoSurface
import com.kurastorage.feature.media.player.RepositoryMediaReadinessProbe

@Composable
internal fun MediaPlayerRoute(
    fileId: String,
    kind: MediaKind,
    current: SessionServices,
    route: ConnectionRoute,
    onBack: () -> Unit,
) {
    val playerViewModel: MediaPlayerViewModel =
        viewModel(
            key = "player-${kind.name}-$fileId-${current.sessionId}-${current.media.scopeId}",
            factory =
                simpleViewModelFactory {
                    MediaPlayerViewModel(
                        fileId,
                        kind,
                        current.files,
                        MediaViewerController(
                            current.media.repository,
                            current.media.qualityPreferences,
                            current.media.contextResolver,
                            current.media.confirmationPolicy,
                            route,
                            current.media.coroutineScope,
                        ),
                        RepositoryMediaReadinessProbe(current.media.repository),
                    )
                },
        )
    val state by playerViewModel.state.collectAsStateWithLifecycle()
    val context = LocalContext.current
    val activity = context as? ComponentActivity
    val lifecycleOwner = LocalLifecycleOwner.current
    val mobile = state.media?.networkContext == NetworkQualityContext.REMOTE_MOBILE
    val engine =
        remember(current.media.scopeId, mobile) {
            AndroidMediaPlayerController(context, current.media.repository, mobile, current.media.coroutineScope)
        }
    var fullscreen by remember { mutableStateOf(false) }

    DisposableEffect(engine) {
        playerViewModel.attachEngine(engine)
        onDispose {
            playerViewModel.detachEngine(engine)
            engine.close()
        }
    }
    DisposableEffect(lifecycleOwner, playerViewModel, activity) {
        val observer =
            LifecycleEventObserver { _, event ->
                if (event == Lifecycle.Event.ON_STOP && activity?.isChangingConfigurations != true) {
                    playerViewModel.onAppBackgrounded()
                }
            }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }
    DisposableEffect(Unit) {
        onDispose { activity?.requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED }
    }

    MediaPlayerScreen(
        state = state,
        onBack = onBack,
        onPlay = playerViewModel::play,
        onPause = playerViewModel::pause,
        onSeek = playerViewModel::seekTo,
        onSkipBack = playerViewModel::skipBack,
        onSkipForward = playerViewModel::skipForward,
        onRate = playerViewModel::setRate,
        onQuality = playerViewModel::selectQuality,
        onConfirmOriginal = playerViewModel::confirmOriginal,
        onCancelOriginal = playerViewModel::cancelOriginal,
        onRetryGeneration = playerViewModel::retryGeneration,
        onRetryPlayback = playerViewModel::retryPlayback,
        onBackgroundGeneration = onBack,
        onFullscreen = {
            fullscreen = !fullscreen
            activity?.requestedOrientation =
                if (fullscreen) ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE else ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
        },
        fullscreen = fullscreen,
        videoSurface = {
            if (kind == MediaKind.VIDEO) MediaVideoSurface(engine, Modifier.fillMaxSize())
        },
    )
}
