@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
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
import androidx.compose.material3.Text
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
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.NetworkQualityContext
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
    LaunchedEffect(
        file?.id,
        readySource,
        state.previousPrefetch?.id,
        state.nextPrefetch?.id,
    ) {
        val source = readySource ?: return@LaunchedEffect
        if (source.variant == MediaVariant.ORIGINAL && media.networkContext == NetworkQualityContext.REMOTE_MOBILE) {
            return@LaunchedEffect
        }
        val adjacent = listOfNotNull(state.previousPrefetch, state.nextPrefetch)
        coroutineScope {
            adjacent
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
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Row(
            Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            OutlinedButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) {
                Text("Back", maxLines = 1)
            }
            Text(
                file?.name ?: "Photo",
                modifier = Modifier.weight(1f).padding(horizontal = 8.dp),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                style = MaterialTheme.typography.titleMedium,
            )
            OutlinedButton(
                onClick = onDetails,
                enabled = file != null,
                modifier = Modifier.heightIn(min = 48.dp),
            ) {
                Text("Details", maxLines = 1)
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            MediaQuality.entries.forEach { quality ->
                OutlinedButton(onClick = { onQuality(quality) }, enabled = media?.quality != quality) {
                    Text(quality.name.lowercase().replaceFirstChar(Char::uppercase))
                }
            }
        }
        Text(
            "Connection: ${media?.networkContext ?: "Checking"} • " +
                "Original: ${state.originalSizeLabel ?: "not inspected"} • Zoom ${"%.1f".format(state.zoom)}x",
        )
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            OutlinedButton(
                onClick = onPrevious,
                enabled = state.canGoPrevious,
                modifier = Modifier.heightIn(min = 48.dp),
            ) { Text("Previous photo") }
            OutlinedButton(
                onClick = onNext,
                enabled = state.canGoNext,
                modifier = Modifier.heightIn(min = 48.dp),
            ) { Text("Next photo") }
        }
        Box(
            Modifier
                .height(VIEWPORT_HEIGHT)
                .fillMaxWidth()
                .background(MaterialTheme.colorScheme.surfaceVariant)
                .graphicsLayer(
                    scaleX = state.zoom,
                    scaleY = state.zoom,
                    translationX = offset.x,
                    translationY = offset.y,
                ).transformable(transformable)
                .pointerInput(file?.id) {
                    detectTapGestures(onDoubleTap = {
                        val target = if (state.zoom > 1f) 1f else 2f
                        if (target == 1f) offset = Offset.Zero
                        onZoom(target)
                    })
                }.testTag("photo-canvas"),
        ) {
            val ticket = requestTicket()
            val ready = media?.loadState as? MediaLoadState.Ready
            val source = ticket?.source ?: ready?.source
            if (file != null && source != null) {
                var painterState by remember(source) { mutableStateOf<AsyncImagePainter.State>(AsyncImagePainter.State.Empty) }
                val model =
                    ImageRequest
                        .Builder(context)
                        .data(KuraMediaImage(scopeId, source.fileId, source.fileVersion, source.variant))
                        .size(MAX_DECODE_EDGE_PX, MAX_DECODE_EDGE_PX)
                        .memoryCacheKey("$scopeId:${source.fileId}:${source.fileVersion}:${source.variant.wireValue}")
                        .diskCacheKey("$scopeId:${source.fileId}:${source.fileVersion}:${source.variant.wireValue}")
                        .build()
                AsyncImage(
                    model = model,
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
            when (val load = media?.loadState) {
                MediaLoadState.Loading,
                MediaLoadState.Idle,
                null,
                -> CircularProgressIndicator(Modifier.testTag("photo-loading"))
                is MediaLoadState.Generating -> Text("Generating ${load.job.progressPercent ?: 0}%")
                is MediaLoadState.Failed -> Text("Unable to display photo: ${load.error}")
                else -> Unit
            }
            state.error?.let { Text("Unable to open photo: $it") }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = onDownload, enabled = file != null && media?.loadState is MediaLoadState.Ready) {
                Text("Download this quality")
            }
        }
    }
    val prompt = media?.confirmation
    if (prompt != null) {
        AlertDialog(
            onDismissRequest = {},
            title = { Text("Load original photo?") },
            text = { Text(prompt.description) },
            confirmButton = { Button(onClick = onConfirmOriginal) { Text("Load original") } },
            dismissButton = { OutlinedButton(onClick = { onQuality(MediaQuality.MEDIUM) }) { Text("Cancel") } },
        )
    }
}

private const val MAX_DECODE_EDGE_PX = 2_896
private val VIEWPORT_HEIGHT = 320.dp
