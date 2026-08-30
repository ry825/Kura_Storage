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
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.kurastorage.core.data.media.formatIec

@Composable
fun PdfViewerScreen(
    state: PdfViewerUiState,
    onConfirm: () -> Unit,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    onPage: (Int) -> Unit,
    onZoom: (Float) -> Unit,
    onViewport: (Int, Int) -> Unit,
    onDownload: () -> Unit,
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
    var pageInput by remember(state.pageIndex) { mutableStateOf((state.pageIndex + 1).toString()) }
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
                state.file?.name ?: "PDF",
                modifier = Modifier.weight(1f).padding(horizontal = 8.dp),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                style = MaterialTheme.typography.titleMedium,
            )
            OutlinedButton(
                onClick = onDownload,
                enabled = state.file != null,
                modifier = Modifier.heightIn(min = 48.dp),
            ) {
                Text("Download", maxLines = 1)
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedTextField(
                value = pageInput,
                onValueChange = { pageInput = it.filter(Char::isDigit).take(6) },
                label = { Text("Page number") },
                singleLine = true,
                modifier = Modifier.weight(1f),
            )
            Button(
                onClick = { pageInput.toIntOrNull()?.let { onPage(it - 1) } },
                enabled = pageInput.toIntOrNull()?.let { it in 1..state.pageCount } == true,
            ) { Text("Go") }
        }
        Text("Page ${if (state.pageCount == 0) 0 else state.pageIndex + 1} / ${state.pageCount} • Zoom ${"%.1f".format(state.zoom)}x")
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            OutlinedButton(
                onClick = onPrevious,
                enabled = state.pageIndex > 0,
                modifier = Modifier.heightIn(min = 48.dp),
            ) { Text("Previous page") }
            OutlinedButton(
                onClick = onNext,
                enabled = state.pageIndex + 1 < state.pageCount,
                modifier = Modifier.heightIn(min = 48.dp),
            ) { Text("Next page") }
        }
        Box(
            Modifier
                .height(VIEWPORT_HEIGHT)
                .fillMaxWidth()
                .background(MaterialTheme.colorScheme.surfaceVariant)
                .onSizeChanged { onViewport(it.width, it.height) }
                .graphicsLayer(
                    scaleX = state.zoom,
                    scaleY = state.zoom,
                    translationX = offset.x,
                    translationY = offset.y,
                ).transformable(transformable)
                .pointerInput(state.pageIndex) {
                    detectTapGestures(onDoubleTap = {
                        val target = if (state.zoom > 1f) 1f else 2f
                        if (target == 1f) offset = Offset.Zero
                        onZoom(target)
                    })
                }.testTag("pdf-canvas"),
        ) {
            state.bitmap?.let {
                Image(
                    bitmap = it.asImageBitmap(),
                    contentDescription = "PDF page ${state.pageIndex + 1}",
                    contentScale = ContentScale.Fit,
                    modifier = Modifier.fillMaxSize(),
                )
            }
            when (state.loadState) {
                PdfLoadState.LOADING_METADATA,
                PdfLoadState.DOWNLOADING,
                PdfLoadState.RENDERING,
                -> CircularProgressIndicator(Modifier.testTag("pdf-loading"))
                PdfLoadState.FAILED -> Text(state.error ?: "PDF error")
                else -> Unit
            }
        }
    }
    if (state.loadState == PdfLoadState.CONFIRMING) {
        AlertDialog(
            onDismissRequest = onBack,
            title = { Text("Download PDF for viewing?") },
            text = { Text("${state.metadata?.size?.formatIec() ?: "Unknown size"} will be saved temporarily in the app cache.") },
            confirmButton = { Button(onClick = onConfirm) { Text("Open PDF") } },
            dismissButton = { OutlinedButton(onClick = onBack) { Text("Cancel") } },
        )
    }
}

private val VIEWPORT_HEIGHT = 320.dp
