@file:Suppress("ktlint:standard:function-naming", "FunctionNaming", "LongMethod", "MagicNumber")

package com.kurastorage.core.ui.icons

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.kurastorage.core.ui.KuraTheme

@Composable
fun KuraLogo(
    modifier: Modifier = Modifier,
    size: Dp = 96.dp,
    contentDescription: String? = null,
) {
    val ink = MaterialTheme.colorScheme.primary
    val semantics =
        if (contentDescription == null) {
            Modifier.clearAndSetSemantics { }
        } else {
            Modifier.semantics { this.contentDescription = contentDescription }
        }
    Canvas(modifier = modifier.size(size).then(semantics)) {
        val stroke = this.size.minDimension * 0.035f
        drawCircle(color = ink, style = Stroke(width = stroke))
        val mountain =
            Path().apply {
                moveTo(this@Canvas.size.width * 0.18f, this@Canvas.size.height * 0.69f)
                lineTo(this@Canvas.size.width * 0.43f, this@Canvas.size.height * 0.43f)
                lineTo(this@Canvas.size.width * 0.53f, this@Canvas.size.height * 0.55f)
                lineTo(this@Canvas.size.width * 0.62f, this@Canvas.size.height * 0.48f)
                lineTo(this@Canvas.size.width * 0.83f, this@Canvas.size.height * 0.72f)
                close()
            }
        drawPath(path = mountain, color = ink)
        repeat(3) { index ->
            val y = this.size.height * (0.69f + index * 0.075f)
            drawArc(
                color = ink,
                startAngle = 195f,
                sweepAngle = 150f,
                useCenter = false,
                topLeft = Offset(this.size.width * (0.12f - index * 0.03f), y - this.size.height * 0.08f),
                size = Size(this.size.width * 0.62f, this.size.height * 0.17f),
                style = Stroke(width = stroke),
            )
        }
    }
}

enum class KuraFileType(
    val accessibilityLabel: String,
    internal val shortLabel: String,
) {
    FOLDER("Folder", "DIR"),
    PHOTO("Photo", "IMG"),
    VIDEO("Video", "VID"),
    AUDIO("Audio", "AUD"),
    PDF("PDF document", "PDF"),
    TEXT("Text file", "TXT"),
    DOCUMENT("Document", "DOC"),
    UNKNOWN("Unknown file type", "?"),
    ;

    companion object {
        fun from(
            mimeType: String?,
            isFolder: Boolean,
        ): KuraFileType {
            if (isFolder) return FOLDER
            val normalized =
                mimeType
                    ?.substringBefore(';')
                    ?.trim()
                    ?.lowercase()
                    .orEmpty()
            return when {
                normalized.startsWith("image/") -> PHOTO
                normalized.startsWith("video/") -> VIDEO
                normalized.startsWith("audio/") -> AUDIO
                normalized == "application/pdf" -> PDF
                normalized.startsWith("text/") ||
                    normalized in setOf("application/json", "application/xml", "application/yaml") -> TEXT
                normalized.startsWith("application/") -> DOCUMENT
                else -> UNKNOWN
            }
        }
    }
}

@Composable
fun KuraFileTypeIcon(
    type: KuraFileType,
    modifier: Modifier = Modifier,
    contentDescription: String = type.accessibilityLabel,
) {
    require(contentDescription.isNotBlank()) { "File type icon content description must not be blank." }
    val color =
        when (type) {
            KuraFileType.FOLDER -> KuraTheme.colors.warning
            KuraFileType.PHOTO -> KuraTheme.colors.categoryPhoto
            KuraFileType.VIDEO -> KuraTheme.colors.categoryVideo
            KuraFileType.AUDIO -> KuraTheme.colors.categoryAudio
            KuraFileType.PDF -> MaterialTheme.colorScheme.error
            KuraFileType.TEXT, KuraFileType.DOCUMENT -> KuraTheme.colors.categoryDocument
            KuraFileType.UNKNOWN -> MaterialTheme.colorScheme.outline
        }
    Box(
        modifier = modifier.size(48.dp).semantics { this.contentDescription = contentDescription },
        contentAlignment = Alignment.Center,
    ) {
        Canvas(modifier = Modifier.size(40.dp)) {
            val strokeWidth = 2.dp.toPx()
            if (type == KuraFileType.FOLDER) {
                val folder =
                    Path().apply {
                        moveTo(size.width * 0.08f, size.height * 0.28f)
                        lineTo(size.width * 0.39f, size.height * 0.28f)
                        lineTo(size.width * 0.49f, size.height * 0.39f)
                        lineTo(size.width * 0.92f, size.height * 0.39f)
                        lineTo(size.width * 0.92f, size.height * 0.84f)
                        lineTo(size.width * 0.08f, size.height * 0.84f)
                        close()
                    }
                drawPath(folder, color = color.copy(alpha = 0.14f))
                drawPath(folder, color = color, style = Stroke(strokeWidth))
            } else {
                drawRoundRect(
                    color = color.copy(alpha = 0.12f),
                    topLeft = Offset(size.width * 0.12f, size.height * 0.06f),
                    size = Size(size.width * 0.76f, size.height * 0.88f),
                    cornerRadius =
                        androidx.compose.ui.geometry
                            .CornerRadius(4.dp.toPx()),
                )
                drawRoundRect(
                    color = color,
                    topLeft = Offset(size.width * 0.12f, size.height * 0.06f),
                    size = Size(size.width * 0.76f, size.height * 0.88f),
                    cornerRadius =
                        androidx.compose.ui.geometry
                            .CornerRadius(4.dp.toPx()),
                    style = Stroke(strokeWidth),
                )
            }
        }
        if (type != KuraFileType.FOLDER) {
            Text(
                text = type.shortLabel,
                color = color,
                style = MaterialTheme.typography.bodySmall,
                fontWeight = FontWeight.Bold,
            )
        }
    }
}
