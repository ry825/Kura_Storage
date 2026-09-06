@file:Suppress(
    "ktlint:standard:function-naming",
    "FunctionNaming",
    "LongMethod",
    "LongParameterList",
    "CyclomaticComplexMethod",
    "MaxLineLength",
    "MagicNumber",
)

package com.kurastorage.feature.media.pdf

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.kurastorage.core.ui.KuraTheme
import com.kurastorage.core.ui.accessibility.kuraHeading
import com.kurastorage.core.ui.components.KuraStatus
import com.kurastorage.core.ui.components.KuraStatusPanel
import com.kurastorage.core.ui.formatting.formatFileSize

@Composable
fun PdfViewerScreen(
    state: PdfViewerUiState,
    onConfirm: () -> Unit,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    onPage: (Int) -> Unit,
    onZoom: (Float) -> Unit,
    onViewport: (Int, Int) -> Unit,
    onRetryOpen: () -> Unit,
    onSaveCopy: () -> Unit,
    onBack: () -> Unit,
    onDisposeViewer: () -> Unit,
) {
    var offset by remember(state.pageIndex) { mutableStateOf(Offset.Zero) }
    val transformable =
        rememberTransformableState { zoomChange, panChange, _ ->
            onZoom(state.zoom * zoomChange)
            offset = if (state.zoom <= 1f) Offset.Zero else offset + panChange
        }
    DisposableEffect(Unit) { onDispose(onDisposeViewer) }
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier
                .fillMaxSize()
                .windowInsetsPadding(WindowInsets.safeDrawing)
                .padding(KuraTheme.spacing.md),
            verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.sm),
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text("Back") }
                Text(
                    state.file?.name ?: "PDF viewer",
                    modifier = Modifier.weight(1f).padding(horizontal = KuraTheme.spacing.sm).kuraHeading(),
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    style = MaterialTheme.typography.titleLarge,
                )
                TextButton(
                    onClick = onSaveCopy,
                    enabled = state.file != null,
                    modifier = Modifier.heightIn(min = 48.dp),
                ) { Text("Save a copy") }
            }

            PdfStatus(state, onRetryOpen, onSaveCopy)

            if (state.pageCount > 0 || state.bitmap != null || state.loadState == PdfLoadState.RENDERING) {
                Box(
                    Modifier
                        .weight(1f)
                        .fillMaxWidth()
                        .background(MaterialTheme.colorScheme.surfaceVariant)
                        .onSizeChanged { onViewport(it.width, it.height) }
                        .testTag("pdf-viewport"),
                ) {
                    Box(
                        Modifier
                            .fillMaxSize()
                            .graphicsLayer(scaleX = state.zoom, scaleY = state.zoom, translationX = offset.x, translationY = offset.y)
                            .transformable(transformable)
                            .pointerInput(state.pageIndex) {
                                detectTapGestures(onDoubleTap = {
                                    val target = if (state.zoom > 1f) 1f else 2f
                                    if (target == 1f) offset = Offset.Zero
                                    onZoom(target)
                                })
                            }.testTag("pdf-canvas"),
                        contentAlignment = Alignment.Center,
                    ) {
                        state.bitmap?.let {
                            Image(
                                bitmap = it.asImageBitmap(),
                                contentDescription = "PDF page ${state.pageIndex + 1}",
                                contentScale = ContentScale.Fit,
                                modifier = Modifier.fillMaxSize(),
                            )
                        }
                        if (state.loadState == PdfLoadState.RENDERING) CircularProgressIndicator(Modifier.testTag("pdf-rendering"))
                    }
                }

                Column(verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                    Text(
                        "Page ${if (state.pageCount == 0) 0 else state.pageIndex + 1} / ${state.pageCount} • Zoom ${"%.1f".format(
                            state.zoom,
                        )}x",
                        style = MaterialTheme.typography.titleMedium,
                        modifier = Modifier.kuraHeading(),
                    )
                    FlowRow(
                        Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
                        verticalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs),
                    ) {
                        OutlinedButton(
                            onClick = onPrevious,
                            enabled = state.pageIndex > 0,
                            modifier = Modifier.heightIn(min = 48.dp),
                        ) { Text("Previous") }
                        OutlinedButton(
                            onClick = onNext,
                            enabled = state.pageIndex + 1 < state.pageCount,
                            modifier = Modifier.heightIn(min = 48.dp),
                        ) { Text("Next") }
                        OutlinedButton(
                            onClick = { onPage(0) },
                            enabled = state.pageIndex > 0,
                            modifier = Modifier.heightIn(min = 48.dp),
                        ) { Text("First") }
                        OutlinedButton(
                            onClick = { onZoom(state.zoom - ZOOM_STEP) },
                            enabled = state.zoom > PdfDocumentController.MIN_ZOOM,
                            modifier = Modifier.heightIn(min = 48.dp).semantics { contentDescription = "Zoom out" },
                        ) { Text("− Zoom") }
                        OutlinedButton(
                            onClick = { onZoom(state.zoom + ZOOM_STEP) },
                            enabled = state.zoom < PdfDocumentController.MAX_ZOOM,
                            modifier = Modifier.heightIn(min = 48.dp).semantics { contentDescription = "Zoom in" },
                        ) { Text("+ Zoom") }
                    }
                }
            }
        }
    }

    if (state.loadState == PdfLoadState.CONFIRMING) {
        AlertDialog(
            onDismissRequest = onBack,
            title = { Text("Download PDF for viewing?") },
            text = {
                Text(
                    "MIME: ${state.metadata?.mimeType ?: "Unknown"}\n" +
                        "Estimated transfer: ${formatFileSize(state.metadata?.size?.value)}\n" +
                        "Range support: ${if (state.metadata?.acceptsRanges == true) "Yes" else "No"}\n" +
                        "The PDF will be streamed to private temporary storage and removed by the session cleanup policy.",
                )
            },
            confirmButton = { Button(onClick = onConfirm) { Text("Open PDF") } },
            dismissButton = { OutlinedButton(onClick = onBack) { Text("Cancel") } },
        )
    }
}

@Composable
private fun PdfStatus(
    state: PdfViewerUiState,
    onRetryOpen: () -> Unit,
    onSaveCopy: () -> Unit,
) {
    when (state.loadState) {
        PdfLoadState.LOADING_METADATA ->
            KuraStatusPanel(
                "Checking PDF",
                "Verifying MIME, size, Range support, and storage limits before download.",
                KuraStatus.INFO,
            )
        PdfLoadState.DOWNLOADING ->
            KuraStatusPanel(
                "Downloading PDF",
                "Streaming to private temporary storage. The full document is not loaded into memory.",
                KuraStatus.INFO,
                action = { CircularProgressIndicator(Modifier.testTag("pdf-loading")) },
            )
        PdfLoadState.RENDERING ->
            KuraStatusPanel(
                "Rendering page",
                "Only the current page is decoded within the bitmap limit.",
                KuraStatus.INFO,
            )
        PdfLoadState.FAILED ->
            KuraStatusPanel(
                state.failure?.title ?: "PDF unavailable",
                state.failure?.userMessage() ?: "This PDF could not be opened safely.",
                KuraStatus.ERROR,
                action = {
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(KuraTheme.spacing.xs)) {
                        if (state.failure?.retryable != false) {
                            Button(onClick = onRetryOpen, modifier = Modifier.heightIn(min = 48.dp)) {
                                Text("Retry open")
                            }
                        }
                        OutlinedButton(onClick = onSaveCopy, enabled = state.file != null, modifier = Modifier.heightIn(min = 48.dp)) {
                            Text("Save a copy")
                        }
                    }
                },
            )
        PdfLoadState.CONFIRMING, PdfLoadState.READY -> Unit
    }
}

private const val ZOOM_STEP = 0.5f

private val PdfFailure.title: String
    get() =
        when (this) {
            PdfFailure.AUTHENTICATION -> "Sign in required"
            PdfFailure.PERMISSION -> "Permission changed"
            PdfFailure.NOT_FOUND -> "PDF not found"
            PdfFailure.TOO_LARGE -> "PDF is too large"
            PdfFailure.STORAGE -> "Storage is full"
            PdfFailure.INCOMPLETE -> "Download incomplete"
            PdfFailure.CORRUPT -> "PDF is damaged"
            PdfFailure.PASSWORD_PROTECTED -> "Password-protected PDF"
            PdfFailure.RENDER_UNSUPPORTED -> "Page could not be rendered"
            PdfFailure.NETWORK -> "Connection lost"
            PdfFailure.UNKNOWN -> "PDF unavailable"
        }

private val PdfFailure.retryable: Boolean
    get() =
        this !in
            setOf(PdfFailure.PERMISSION, PdfFailure.NOT_FOUND, PdfFailure.TOO_LARGE, PdfFailure.CORRUPT, PdfFailure.PASSWORD_PROTECTED)

private fun PdfFailure.userMessage(): String =
    when (this) {
        PdfFailure.AUTHENTICATION -> "Sign in again, then retry opening this PDF."
        PdfFailure.PERMISSION -> "You no longer have permission to view this PDF."
        PdfFailure.NOT_FOUND -> "This PDF is no longer available."
        PdfFailure.TOO_LARGE -> "PDFs larger than 256 MB cannot be opened in the viewer."
        PdfFailure.STORAGE -> "Free private app storage and retry. No partial file was kept."
        PdfFailure.INCOMPLETE -> "The transfer ended early or did not satisfy the media contract."
        PdfFailure.CORRUPT -> "The downloaded file is not a valid PDF."
        PdfFailure.PASSWORD_PROTECTED -> "Encrypted PDFs requiring a password are not supported by this viewer."
        PdfFailure.RENDER_UNSUPPORTED -> "The current page uses PDF features this device cannot render. Try saving a copy."
        PdfFailure.NETWORK -> "Reconnect and retry opening this PDF."
        PdfFailure.UNKNOWN -> "This PDF could not be opened safely."
    }
