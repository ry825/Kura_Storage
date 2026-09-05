@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
    "MagicNumber",
    "TooManyFunctions",
)

package com.kurastorage.feature.media.photo

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import coil3.ImageLoader
import coil3.compose.AsyncImage
import coil3.compose.AsyncImagePainter
import coil3.request.ImageRequest
import com.kurastorage.core.data.media.KuraMediaImage
import com.kurastorage.core.data.media.MediaGeneratingException
import com.kurastorage.core.model.TagItem
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaUiError
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.components.KuraAppScaffold
import com.kurastorage.core.ui.components.KuraIconButton
import com.kurastorage.core.ui.components.KuraSegmentedControl
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.components.KuraTopAppBar
import com.kurastorage.feature.media.MediaRequestTicket
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope

data class PhotoOrganizationUiState(
    val loading: Boolean = true,
    val isFavorite: Boolean = false,
    val attachedTags: List<TagItem> = emptyList(),
    val availableTags: List<TagItem> = emptyList(),
    val pendingFavorite: Boolean = false,
    val pendingTagIds: Set<String> = emptySet(),
    val canAttach: Boolean = false,
    val errorMessage: String? = null,
)

enum class PhotoDownloadStatus {
    IDLE,
    CHOOSING_DESTINATION,
    SAVING,
    COMPLETED,
    FAILED,
    INCOMPLETE_FILE_MAY_REMAIN,
}

data class PhotoDownloadUiState(
    val status: PhotoDownloadStatus = PhotoDownloadStatus.IDLE,
    val message: String? = null,
)

@OptIn(ExperimentalMaterial3Api::class)
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
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    onZoom: (Float) -> Unit,
    onDetails: () -> Unit,
    onDownloadOriginal: () -> Unit,
    onBack: () -> Unit,
    organization: PhotoOrganizationUiState = PhotoOrganizationUiState(),
    onRefreshOrganization: () -> Unit = {},
    onToggleFavorite: () -> Unit = {},
    onToggleTag: (TagItem) -> Unit = {},
    onManageTags: () -> Unit = {},
    download: PhotoDownloadUiState = PhotoDownloadUiState(),
    onRetryGeneration: () -> Unit = {},
) {
    val file = state.file
    val media = state.media
    val context = LocalContext.current
    var showTags by remember(file?.id) { mutableStateOf(false) }
    var offset by remember(file?.id, media?.quality) { mutableStateOf(Offset.Zero) }
    val transformable =
        rememberTransformableState { zoomChange, panChange, _ ->
            onZoom(state.zoom * zoomChange)
            offset = if (state.zoom <= 1f) Offset.Zero else offset + panChange
        }
    val readySource = media?.displayedSource
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

    KuraAppScaffold(
        topBar = {
            KuraTopAppBar(
                title = file?.name ?: "Photo",
                navigationIcon = { KuraIconButton("Back", onBack) { Text("←") } },
                actions = {
                    if (state.totalCount > 0) Text("${state.currentPosition} / ${state.totalCount}")
                    KuraIconButton("File details", onDetails, enabled = file != null) { Text("ⓘ") }
                },
            )
        },
    ) { contentPadding ->
        BoxWithConstraints(Modifier.fillMaxSize().padding(contentPadding)) {
            val landscape = maxWidth > maxHeight
            if (landscape) {
                Row(Modifier.fillMaxSize()) {
                    PhotoViewport(
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
                        onPrevious,
                        onNext,
                        Modifier.weight(1f),
                    )
                    PhotoControls(
                        state,
                        organization,
                        download,
                        onQuality,
                        onPrevious,
                        onNext,
                        onZoom,
                        onToggleFavorite,
                        { showTags = true },
                        onDownloadOriginal,
                        onDetails,
                        onRetryGeneration,
                        Modifier.fillMaxHeight().widthIn(min = 280.dp, max = 360.dp),
                    )
                }
            } else {
                Column(Modifier.fillMaxSize()) {
                    PhotoViewport(
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
                        onPrevious,
                        onNext,
                        Modifier.weight(1f),
                    )
                    PhotoControls(
                        state,
                        organization,
                        download,
                        onQuality,
                        onPrevious,
                        onNext,
                        onZoom,
                        onToggleFavorite,
                        { showTags = true },
                        onDownloadOriginal,
                        onDetails,
                        onRetryGeneration,
                        Modifier.fillMaxWidth().heightIn(max = 320.dp),
                    )
                }
            }
        }
    }

    if (showTags) {
        PhotoTagsSheet(
            state = organization,
            onDismiss = { showTags = false },
            onRefresh = onRefreshOrganization,
            onToggleTag = onToggleTag,
            onManageTags = onManageTags,
        )
    }
}

@Composable
private fun PhotoViewport(
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
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    modifier: Modifier,
) {
    Box(modifier.fillMaxSize().background(MaterialTheme.colorScheme.surfaceVariant).testTag("photo-viewport")) {
        Box(Modifier.fillMaxSize().graphicsLayer { clip = true }) {
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
                onResetOffset,
                onZoom,
                onPrevious,
                onNext,
            )
        }
    }
}

@Composable
private fun PhotoControls(
    state: PhotoViewerUiState,
    organization: PhotoOrganizationUiState,
    download: PhotoDownloadUiState,
    onQuality: (MediaQuality) -> Unit,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    onZoom: (Float) -> Unit,
    onToggleFavorite: () -> Unit,
    onTags: () -> Unit,
    onDownloadOriginal: () -> Unit,
    onDetails: () -> Unit,
    onRetryGeneration: () -> Unit,
    modifier: Modifier,
) {
    val media = state.media
    Column(
        modifier.verticalScroll(rememberScrollState()).padding(horizontal = KuraTheme.spacing.md, vertical = KuraTheme.spacing.sm),
        verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
    ) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            KuraIconButton("Previous photo", onPrevious, enabled = state.canGoPrevious) { Text("←") }
            Text("Navigate")
            KuraIconButton("Next photo", onNext, enabled = state.canGoNext) { Text("→") }
        }
        Text("Viewing quality", style = MaterialTheme.typography.titleSmall)
        KuraSegmentedControl(
            labels = listOf("Low", "Medium", "Original"),
            selectedIndex = media?.quality?.ordinal ?: -1,
            onSelected = { onQuality(MediaQuality.entries[it]) },
            enabled = state.file != null,
        )
        Text(qualityStatus(media), modifier = Modifier.testTag("quality-status"), style = MaterialTheme.typography.bodySmall)
        Text("Connection: ${media?.networkContext.userLabel()}", style = MaterialTheme.typography.bodySmall)
        Text("Displayed size: ${state.displayedSizeLabel ?: "Not ready"}", style = MaterialTheme.typography.bodySmall)
        PhotoLoadStatus(state, onRetryGeneration)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
            KuraIconButton(
                if (organization.pendingFavorite) {
                    "Saving favorite"
                } else if (organization.isFavorite) {
                    "Remove from favorites"
                } else {
                    "Add to favorites"
                },
                onToggleFavorite,
                enabled = !organization.loading && !organization.pendingFavorite && (organization.canAttach || organization.isFavorite),
            ) { Text(if (organization.isFavorite) "★" else "☆") }
            KuraIconButton("Manage photo tags", onTags, enabled = !organization.loading) { Text("#") }
            KuraIconButton(
                "Download original",
                onDownloadOriginal,
                enabled =
                    state.file != null && download.status !in setOf(PhotoDownloadStatus.CHOOSING_DESTINATION, PhotoDownloadStatus.SAVING),
            ) { Text("↓") }
            KuraIconButton("File details", onDetails, enabled = state.file != null) { Text("ⓘ") }
            KuraIconButton(
                "Zoom out",
                { onZoom(state.zoom - ZOOM_STEP) },
                enabled = state.zoom > PhotoViewerViewModel.MIN_ZOOM,
            ) { Text("−") }
            KuraIconButton(
                "Zoom in",
                { onZoom(state.zoom + ZOOM_STEP) },
                enabled = state.zoom < PhotoViewerViewModel.MAX_ZOOM,
            ) { Text("+") }
        }
        DownloadStatus(download)
        organization.errorMessage?.let { KuraStatusPanel("Organization unavailable", it, KuraStatus.ERROR) }
    }
}

@Composable
private fun DownloadStatus(download: PhotoDownloadUiState) {
    when (download.status) {
        PhotoDownloadStatus.IDLE -> Unit
        PhotoDownloadStatus.CHOOSING_DESTINATION -> Text(download.message ?: "Choose where to save the original file.")
        PhotoDownloadStatus.SAVING -> {
            LinearProgressIndicator(Modifier.fillMaxWidth())
            Text(download.message ?: "Saving original file…")
        }
        PhotoDownloadStatus.COMPLETED ->
            KuraStatusPanel(
                "Download completed",
                download.message ?: "The original file was saved.",
                KuraStatus.SUCCESS,
            )
        PhotoDownloadStatus.FAILED ->
            KuraStatusPanel(
                "Download failed",
                download.message ?: "The original file was not saved. Try again.",
                KuraStatus.ERROR,
            )
        PhotoDownloadStatus.INCOMPLETE_FILE_MAY_REMAIN ->
            KuraStatusPanel(
                "Download failed",
                download.message ?: "The incomplete destination could not be removed. Delete it before retrying.",
                KuraStatus.ERROR,
            )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PhotoTagsSheet(
    state: PhotoOrganizationUiState,
    onDismiss: () -> Unit,
    onRefresh: () -> Unit,
    onToggleTag: (TagItem) -> Unit,
    onManageTags: () -> Unit,
) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        modifier = Modifier.testTag("photo-tags-sheet"),
    ) {
        Column(
            Modifier
                .fillMaxWidth()
                .verticalScroll(
                    rememberScrollState(),
                ).padding(horizontal = KuraTheme.spacing.lg, vertical = KuraTheme.spacing.sm),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            Text("Photo tags", style = MaterialTheme.typography.headlineSmall)
            if (state.loading) LinearProgressIndicator(Modifier.fillMaxWidth())
            if (!state.canAttach) Text("Only existing tags can be removed from this item.")
            state.availableTags.forEach { tag ->
                val attached = state.attachedTags.any { it.id == tag.id }
                FilterChip(
                    selected = attached,
                    onClick = { onToggleTag(tag) },
                    enabled = tag.id !in state.pendingTagIds && (state.canAttach || attached),
                    label = { Text(tag.name) },
                    modifier = Modifier.fillMaxWidth().testTag("photo-tag-${tag.id}"),
                )
            }
            if (!state.loading && state.availableTags.isEmpty()) Text("No tags yet.")
            state.errorMessage?.let { KuraStatusPanel("Tags could not be updated", it, KuraStatus.ERROR) }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                KuraIconButton("Refresh photo tags", onRefresh, enabled = !state.loading && state.pendingTagIds.isEmpty()) { Text("↻") }
                KuraIconButton("Open tag management", onManageTags) { Text("+") }
                KuraIconButton("Close photo tags", onDismiss) { Text("×") }
            }
        }
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
    onPrevious: () -> Unit,
    onNext: () -> Unit,
) {
    val file = state.file
    val media = state.media
    val context = LocalContext.current
    var horizontalDrag by remember(file?.id) { mutableFloatStateOf(0f) }
    val swipeModifier =
        if (state.zoom <= 1f) {
            Modifier.pointerInput(file?.id, state.canGoPrevious, state.canGoNext) {
                awaitEachGesture {
                    awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                    horizontalDrag = 0f
                    var multiTouch = false
                    var pressed: Boolean
                    do {
                        val event = awaitPointerEvent(PointerEventPass.Initial)
                        if (event.changes.size > 1) multiTouch = true
                        if (!multiTouch) {
                            horizontalDrag +=
                                event.changes
                                    .first()
                                    .positionChange()
                                    .x
                        }
                        pressed = event.changes.any { it.pressed }
                    } while (pressed)
                    if (!multiTouch) {
                        when {
                            horizontalDrag <= -SWIPE_THRESHOLD.toPx() && state.canGoNext -> onNext()
                            horizontalDrag >= SWIPE_THRESHOLD.toPx() && state.canGoPrevious -> onPrevious()
                        }
                    }
                    horizontalDrag = 0f
                }
            }
        } else {
            Modifier
        }
    Box(
        Modifier
            .fillMaxSize()
            .then(swipeModifier)
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
        val source = ticket?.source ?: media?.displayedSource
        if (file != null && source != null) {
            val displayedSource = media?.displayedSource
            if (ticket != null && displayedSource != null && displayedSource != source) {
                AsyncImage(
                    model =
                        ImageRequest
                            .Builder(context)
                            .data(
                                KuraMediaImage(
                                    scopeId,
                                    displayedSource.fileId,
                                    displayedSource.fileVersion,
                                    displayedSource.variant,
                                ),
                            ).size(MAX_DECODE_EDGE_PX, MAX_DECODE_EDGE_PX)
                            .memoryCacheKey(
                                "$scopeId:${displayedSource.fileId}:${displayedSource.fileVersion}:${displayedSource.variant.wireValue}",
                            ).diskCacheKey(
                                "$scopeId:${displayedSource.fileId}:${displayedSource.fileVersion}:${displayedSource.variant.wireValue}",
                            ).build(),
                    imageLoader = imageLoader,
                    contentDescription = null,
                    contentScale = ContentScale.Fit,
                    modifier = Modifier.fillMaxSize(),
                )
            }
            var painterState by remember(
                source,
                ticket?.generation,
            ) { mutableStateOf<AsyncImagePainter.State>(AsyncImagePainter.State.Empty) }
            val requestGeneration = ((ticket?.generation ?: 0L) and Int.MAX_VALUE.toLong()).toInt()
            AsyncImage(
                model =
                    ImageRequest
                        .Builder(context)
                        .data(KuraMediaImage(scopeId, source.fileId, source.fileVersion, source.variant, requestGeneration))
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
private fun PhotoLoadStatus(
    state: PhotoViewerUiState,
    onRetryGeneration: () -> Unit,
) {
    when (val load = state.media?.loadState) {
        is MediaLoadState.Generating -> {
            val message =
                load.job.progressPercent?.let { "Generated $it%" }
                    ?: load.job.queuePosition?.let { "Queue position $it" }
                    ?: "Waiting for the server"
            KuraStatusPanel("Preparing selected quality", message, KuraStatus.INFO)
        }
        is MediaLoadState.Failed ->
            KuraStatusPanel("Photo unavailable", load.error.userMessage(), KuraStatus.ERROR) {
                if (state.media.canRetryGeneration) {
                    KuraIconButton("Retry photo generation", onRetryGeneration) { Text("↻") }
                }
            }
        MediaLoadState.Loading -> KuraStatusPanel("Loading photo", "The selected quality is being loaded.", KuraStatus.INFO)
        else -> Unit
    }
    state.error?.let { KuraStatusPanel("Unable to open photo", it.userMessage(), KuraStatus.ERROR) }
}

private fun qualityStatus(media: com.kurastorage.feature.media.MediaViewerState?): String {
    val selected = media?.quality.userLabel()
    val ready = media?.displayedSource?.variant?.qualityLabel()
    return if (ready != null) "Displayed: $ready" else "Loading: $selected"
}

private fun MediaVariant.qualityLabel(): String =
    when (this) {
        MediaVariant.IMAGE_LOW, MediaVariant.VIDEO_LOW -> "Low"
        MediaVariant.IMAGE_MEDIUM, MediaVariant.VIDEO_MEDIUM -> "Medium"
        MediaVariant.ORIGINAL -> "Original"
        MediaVariant.THUMBNAIL -> "Thumbnail"
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
        MediaUiError.SERVER_ERROR -> "The server could not provide this photo. Try again."
        MediaUiError.GENERATION_FAILED -> "The selected quality could not be prepared."
        MediaUiError.UNSUPPORTED -> "This photo format is not supported on this device."
        MediaUiError.UNKNOWN -> "The photo could not be opened safely."
    }

private const val MAX_DECODE_EDGE_PX = 2_896
private const val ZOOM_STEP = 0.5f
private val SWIPE_THRESHOLD = 72.dp
