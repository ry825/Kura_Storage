@file:Suppress("ktlint:standard:function-naming", "FunctionNaming")

package com.kurastorage.feature.media.thumbnail

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import coil3.ImageLoader
import coil3.compose.AsyncImage
import coil3.compose.AsyncImagePainter
import coil3.request.ImageRequest
import com.kurastorage.core.data.media.KuraMediaImage
import com.kurastorage.core.data.media.MediaGeneratingException
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.SupportedMediaMimeTypes
import kotlinx.coroutines.delay

@Composable
fun FileThumbnail(
    entry: FileEntry,
    scopeId: String,
    imageLoader: ImageLoader,
    modifier: Modifier = Modifier,
) {
    val kind = thumbnailKind(entry)
    if (kind == null || entry.status != FileEntryStatus.ACTIVE) {
        ThumbnailFallback(entry, kind, modifier)
        return
    }
    var retryToken by remember(entry.id, entry.fileVersion, scopeId) { mutableIntStateOf(0) }
    var state by remember(entry.id, entry.fileVersion, scopeId) {
        androidx.compose.runtime.mutableStateOf<AsyncImagePainter.State>(AsyncImagePainter.State.Empty)
    }
    val generating = (state as? AsyncImagePainter.State.Error)?.result?.throwable as? MediaGeneratingException
    LaunchedEffect(generating?.job?.jobId, retryToken) {
        val job = generating?.job ?: return@LaunchedEffect
        if (retryToken >= MAX_GENERATING_RETRIES) return@LaunchedEffect
        delay(job.retryAfterSeconds.coerceAtLeast(1) * MILLIS_PER_SECOND)
        retryToken++
    }
    val request =
        ImageRequest
            .Builder(androidx.compose.ui.platform.LocalContext.current)
            .data(KuraMediaImage(scopeId, entry.id, entry.fileVersion, MediaVariant.THUMBNAIL, retryToken))
            .memoryCacheKey("$scopeId:${entry.id}:${entry.fileVersion}:thumbnail")
            .diskCacheKey("$scopeId:${entry.id}:${entry.fileVersion}:thumbnail")
            .build()
    Box(
        modifier
            .sizeIn(minWidth = 48.dp, minHeight = 48.dp)
            .clip(RoundedCornerShape(8.dp))
            .background(MaterialTheme.colorScheme.surfaceVariant)
            .semantics { contentDescription = thumbnailDescription(entry, state) },
        contentAlignment = Alignment.Center,
    ) {
        AsyncImage(
            model = request,
            imageLoader = imageLoader,
            contentDescription = null,
            contentScale = ContentScale.Crop,
            modifier = Modifier.fillMaxSize(),
            onState = { state = it },
        )
        when (state) {
            is AsyncImagePainter.State.Loading,
            AsyncImagePainter.State.Empty,
            -> CircularProgressIndicator()
            is AsyncImagePainter.State.Error -> Text(if (generating != null) "Generating" else kind.label)
            is AsyncImagePainter.State.Success -> Unit
        }
    }
}

@Composable
private fun ThumbnailFallback(
    entry: FileEntry,
    kind: ThumbnailKind?,
    modifier: Modifier,
) {
    val label =
        when {
            entry.status == FileEntryStatus.MISSING -> "Missing file"
            entry.status == FileEntryStatus.MISSING_CANDIDATE -> "Checking file"
            entry.entryType == FileEntryType.FOLDER -> "Folder"
            else -> kind?.label ?: "File"
        }
    Box(
        modifier
            .sizeIn(minWidth = 48.dp, minHeight = 48.dp)
            .clip(RoundedCornerShape(8.dp))
            .background(MaterialTheme.colorScheme.surfaceVariant)
            .semantics { contentDescription = "$label: ${entry.name}" },
        contentAlignment = Alignment.Center,
    ) {
        Text(label)
    }
}

private fun thumbnailDescription(
    entry: FileEntry,
    state: AsyncImagePainter.State,
): String =
    when (state) {
        is AsyncImagePainter.State.Success -> "Thumbnail: ${entry.name}"
        is AsyncImagePainter.State.Error ->
            if (state.result.throwable is MediaGeneratingException) {
                "Generating thumbnail: ${entry.name}"
            } else {
                "Thumbnail unavailable: ${entry.name}"
            }
        else -> "Loading thumbnail: ${entry.name}"
    }

private fun thumbnailKind(entry: FileEntry): ThumbnailKind? =
    when {
        entry.entryType != FileEntryType.FILE -> null
        entry.mimeType == null -> null
        SupportedMediaMimeTypes.isPhoto(entry.mimeType) -> ThumbnailKind.PHOTO
        entry.mimeType.isVideoMime() -> ThumbnailKind.VIDEO
        SupportedMediaMimeTypes.isPdf(entry.mimeType) -> ThumbnailKind.PDF
        else -> null
    }

private fun String?.isVideoMime(): Boolean =
    this
        ?.substringBefore(';')
        ?.trim()
        ?.lowercase()
        ?.startsWith("video/") == true

private enum class ThumbnailKind(
    val label: String,
) {
    PHOTO("Photo"),
    VIDEO("Video"),
    PDF("PDF"),
}

private const val MAX_GENERATING_RETRIES = 1
private const val MILLIS_PER_SECOND = 1_000L
