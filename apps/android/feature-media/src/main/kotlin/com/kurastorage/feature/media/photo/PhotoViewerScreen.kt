@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
    "MagicNumber",
)

package com.kurastorage.feature.media.photo

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
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
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import coil3.ImageLoader
import coil3.compose.AsyncImage
import coil3.compose.AsyncImagePainter
import coil3.request.ImageRequest
import com.kurastorage.core.data.media.KuraMediaImage
import com.kurastorage.core.data.media.MediaGeneratingException
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaUiError
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraAdaptiveActionLayout
import com.kurastorage.core.ui.components.KuraCard
import com.kurastorage.core.ui.components.KuraSegmentedControl
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.feature.media.MediaRequestTicket
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope

@Composable
fun PhotoViewerScreen(
    state: PhotoViewerUiState,
    imageLoader: ImageLoader,
    scopeId: String,
    requestTicket: () -> MediaRequestTicket?,
    onImageReady: (MediaRequestTicket) -> Unit,
    onGenerating: (MediaRequestTicket, MediaGeneratingException) -> Unit,
    onImageFailed: (MediaRequestTicket) -> Unit,
    onQuality: (MediaQuality) -> Unit,
    onConfirmOriginal: () -> Unit,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    onZoom: (Float) -> Unit,
    onDetails: () -> Unit,
    onDownload: () -> Unit,
    onBack: () -> Unit,
) {
    val file = state.file
    val media = state.media
    val context = LocalContext.current
    var offset by remember(file?.id, media?.quality) { mutableStateOf(Offset.Zero) }
    val transformable =
        rememberTransformableState { zoomChange, panChange, _ ->
            onZoom(state.zoom * zoomChange)
            offset = if (state.zoom <= 1f) Offset.Zero else offset + panChange
        }
    val readySource = (media?.loadState as? MediaLoadState.Ready)?.source
    LaunchedEffect(file?.id, readySource, state.previousPrefetch?.id, state.nextPrefetch?.id) {
        val source = readySource ?: return@LaunchedEffect
        if (source.variant == MediaVariant.ORIGINAL && media.networkContext == NetworkQualityContext.REMOTE_MOBILE) return@LaunchedEffect
        coroutineScope {
            listOfNotNull(state.previousPrefetch, state.nextPrefetch)
                .map { candidate ->
                    async {
                        imageLoader.execute(
                            ImageRequest
                                .Builder(context)
                                .data(KuraMediaImage(scopeId, candidate.id, candidate.fileVersion, source.variant))
                                .size(MAX_DECODE_EDGE_PX, MAX_DECODE_EDGE_PX)
                                .memoryCacheKey("$scopeId:${candidate.id}:${candidate.fileVersion}:${source.variant.wireValue}")
                                .diskCacheKey("$scopeId:${candidate.id}:${candidate.fileVersion}:${source.variant.wireValue}")
                                .build(),
                        )
                    }
                }.awaitAll()
        }
    }

    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier
                .fillMaxSize()
                .windowInsetsPadding(WindowInsets.safeDrawing)
                .verticalScroll(rememberScrollState())
                .padding(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.md),
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") }
                Column(Modifier.weight(1f).padding(horizontal = KuraTheme.spacing.sm), horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(
                        file?.name ?: "Photo",
                        modifier = Modifier.kuraHeading(),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        style = MaterialTheme.typography.titleLarge,
                    )
                    if (state.totalCount > 0) Text("${state.currentPosition} / ${state.totalCount}")
                }
                TextButton(onClick = onDetails, enabled = file != null, modifier = Modifier.heightIn(min = 48.dp)) { Text("Details") }
            }

            Box(
                Modifier
                    .height(PHOTO_VIEWPORT_HEIGHT)
                    .fillMaxWidth()
                    .background(MaterialTheme.colorScheme.surfaceVariant)
                    .testTag("photo-viewport"),
            ) {
                PhotoCanvas(
                    state,
                    imageLoader,
                    scopeId,
                    requestTicket,
                    onImageReady,
                    onGenerating,
                    onImageFailed,
                    offset,
                    transformable,
                    { offset = Offset.Zero },
                    onZoom,
                )
                OutlinedButton(
                    onClick = onPrevious,
                    enabled = state.canGoPrevious,
                    modifier = Modifier.align(Alignment.CenterStart).padding(KuraTheme.spacing.xs).heightIn(min = 48.dp),
                ) { Text("Previous photo") }
                OutlinedButton(
                    onClick = onNext,
                    enabled = state.canGoNext,
                    modifier = Modifier.align(Alignment.CenterEnd).padding(KuraTheme.spacing.xs).heightIn(min = 48.dp),
                ) { Text("Next photo") }
            }

            PhotoLoadStatus(state)

            KuraCard {
                Text("Viewing quality", style = MaterialTheme.typography.titleMedium, modifier = Modifier.kuraHeading())
                KuraSegmentedControl(
                    labels = listOf("Low", "Medium", "Original"),
                    selectedIndex = media?.quality?.ordinal ?: -1,
                    onSelected = { onQuality(MediaQuality.entries[it]) },
                    enabled = file != null,
                )
                Text("Connection: ${media?.networkContext.userLabel()}")
                Text("Original size: ${state.originalSizeLabel ?: "Inspect before loading"}")
                Text("Zoom: ${"%.1f".format(state.zoom)}x (pinch or double-tap)")
                KuraAdaptiveActionLayout(
                    actions =
                        listOf(
                            {
                                OutlinedButton(
                                    onClick = { onZoom(state.zoom - ZOOM_STEP) },
                                    enabled = state.zoom > PhotoViewerViewModel.MIN_ZOOM,
                                    modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
                                ) { Text("Zoom out") }
                            },
                            {
                                OutlinedButton(
                                    onClick = { onZoom(state.zoom + ZOOM_STEP) },
                                    enabled = state.zoom < PhotoViewerViewModel.MAX_ZOOM,
                                    modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
                                ) { Text("Zoom in") }
                            },
                        ),
                )
            }

            KuraAdaptiveActionLayout(
                actions =
                    listOf(
                        {
                            Button(
                                onClick = onDownload,
                                enabled = file != null && media?.loadState is MediaLoadState.Ready,
                                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
                            ) { Text("Download ${media?.quality.userLabel()}") }
                        },
                        {
                            OutlinedButton(
                                onClick = onDetails,
                                enabled = file != null,
                                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
                            ) { Text("File details") }
                        },
                    ),
            )
        }
    }

    media?.confirmation?.let { prompt ->
        AlertDialog(
            onDismissRequest = {},
            title = { Text("Load original photo?") },
            text = { Text("${prompt.description} Current connection: ${media.networkContext.userLabel()}.") },
            confirmButton = { Button(onClick = onConfirmOriginal) { Text("Load original") } },
            dismissButton = { OutlinedButton(onClick = { onQuality(MediaQuality.MEDIUM) }) { Text("Use medium") } },
        )
    }
}

@Composable
private fun PhotoCanvas(
    state: PhotoViewerUiState,
    imageLoader: ImageLoader,
    scopeId: String,
    requestTicket: () -> MediaRequestTicket?,
    onImageReady: (MediaRequestTicket) -> Unit,
    onGenerating: (MediaRequestTicket, MediaGeneratingException) -> Unit,
    onImageFailed: (MediaRequestTicket) -> Unit,
    offset: Offset,
    transformable: androidx.compose.foundation.gestures.TransformableState,
    onResetOffset: () -> Unit,
    onZoom: (Float) -> Unit,
) {
    val file = state.file
    val media = state.media
    val context = LocalContext.current
    Box(
        Modifier
            .fillMaxSize()
            .graphicsLayer(scaleX = state.zoom, scaleY = state.zoom, translationX = offset.x, translationY = offset.y)
            .transformable(transformable)
            .pointerInput(file?.id) {
                detectTapGestures(onDoubleTap = {
                    val target = if (state.zoom > 1f) 1f else 2f
                    if (target == 1f) onResetOffset()
                    onZoom(target)
                })
            }.testTag("photo-canvas"),
        contentAlignment = Alignment.Center,
    ) {
        val ticket = requestTicket()
        val ready = media?.loadState as? MediaLoadState.Ready
        val source = ticket?.source ?: ready?.source
        if (file != null && source != null) {
            var painterState by remember(source) { mutableStateOf<AsyncImagePainter.State>(AsyncImagePainter.State.Empty) }
            AsyncImage(
                model =
                    ImageRequest
                        .Builder(context)
                        .data(KuraMediaImage(scopeId, source.fileId, source.fileVersion, source.variant))
                        .size(MAX_DECODE_EDGE_PX, MAX_DECODE_EDGE_PX)
                        .memoryCacheKey("$scopeId:${source.fileId}:${source.fileVersion}:${source.variant.wireValue}")
                        .diskCacheKey("$scopeId:${source.fileId}:${source.fileVersion}:${source.variant.wireValue}")
                        .build(),
                imageLoader = imageLoader,
                contentDescription = "Photo: ${file.name}",
                contentScale = ContentScale.Fit,
                modifier = Modifier.fillMaxSize(),
                onState = { painterState = it },
            )
            LaunchedEffect(painterState, ticket) {
                val active = ticket ?: return@LaunchedEffect
                when (val current = painterState) {
                    is AsyncImagePainter.State.Success -> onImageReady(active)
                    is AsyncImagePainter.State.Error -> {
                        val error = current.result.throwable
                        if (error is MediaGeneratingException) onGenerating(active, error) else onImageFailed(active)
                    }
                    else -> Unit
                }
            }
        }
        if (media?.loadState == MediaLoadState.Loading || media?.loadState == MediaLoadState.Idle || media == null) {
            CircularProgressIndicator(Modifier.testTag("photo-loading"))
        }
    }
}

@Composable
private fun PhotoLoadStatus(state: PhotoViewerUiState) {
    when (val load = state.media?.loadState) {
        is MediaLoadState.Generating -> {
            val message =
                load.job.progressPercent?.let { "Generated $it%" } ?: load.job.queuePosition?.let { "Queue position $it" }
                    ?: "Waiting for the server"
            KuraStatusPanel("Preparing selected quality", message, KuraStatus.INFO)
        }
        is MediaLoadState.Failed -> KuraStatusPanel("Photo unavailable", load.error.userMessage(), KuraStatus.ERROR)
        MediaLoadState.Loading -> KuraStatusPanel("Loading photo", "The selected quality is being loaded.", KuraStatus.INFO)
        else -> Unit
    }
    state.error?.let { KuraStatusPanel("Unable to open photo", it.userMessage(), KuraStatus.ERROR) }
}

private fun MediaQuality?.userLabel(): String = this?.name?.lowercase()?.replaceFirstChar(Char::uppercase) ?: "selected quality"

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
        MediaUiError.PERMISSION_DENIED -> "You no longer have permission to view this photo."
        MediaUiError.NOT_FOUND -> "This photo is no longer available."
        MediaUiError.FILE_CHANGED -> "The file changed. Return and open the latest version."
        MediaUiError.DISCONNECTED -> "Connection lost. Reconnect and select the quality again."
        MediaUiError.RANGE_INVALID, MediaUiError.RESPONSE_INCOMPLETE -> "The server response was incomplete."
        MediaUiError.GENERATION_FAILED -> "The selected quality could not be prepared."
        MediaUiError.UNSUPPORTED -> "This photo format is not supported on this device."
        MediaUiError.UNKNOWN -> "The photo could not be opened safely."
    }

private const val MAX_DECODE_EDGE_PX = 2_896
private const val ZOOM_STEP = 0.5f
private val PHOTO_VIEWPORT_HEIGHT = 360.dp
