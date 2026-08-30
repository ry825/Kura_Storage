@file:Suppress("MaxLineLength")

package com.kurastorage.feature.media.pdf

import android.graphics.Bitmap
import android.graphics.pdf.PdfRenderer
import android.os.ParcelFileDescriptor
import com.kurastorage.core.data.media.PdfFileLease
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.Closeable
import kotlin.math.floor
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sqrt

class PdfDocumentController private constructor(
    private val lease: PdfFileLease,
    private val descriptor: ParcelFileDescriptor,
    private val renderer: PdfRenderer,
) : Closeable {
    val pageCount: Int get() = renderer.pageCount
    private var closed = false

    suspend fun render(
        pageIndex: Int,
        viewportWidth: Int,
        viewportHeight: Int,
        zoom: Float,
    ): Bitmap =
        withContext(Dispatchers.IO) {
            synchronized(this@PdfDocumentController) {
                check(!closed)
                require(pageIndex in 0 until pageCount)
                renderer.openPage(pageIndex).use { page ->
                    val fit = min(viewportWidth.toFloat() / page.width, viewportHeight.toFloat() / page.height)
                    val requestedScale = (fit * zoom.coerceIn(MIN_ZOOM, MAX_ZOOM)).coerceAtLeast(MIN_RENDER_SCALE)
                    val rawWidth = (page.width * requestedScale).roundToInt().coerceAtLeast(1)
                    val rawHeight = (page.height * requestedScale).roundToInt().coerceAtLeast(1)
                    val edgeScale = min(1f, MAX_EDGE_PX.toFloat() / maxOf(rawWidth, rawHeight))
                    val memoryScale =
                        min(1f, sqrt(MAX_BITMAP_BYTES.toDouble() / (rawWidth.toLong() * rawHeight * BYTES_PER_PIXEL)).toFloat())
                    val scale = min(edgeScale, memoryScale)
                    val width = floor(rawWidth * scale.toDouble()).toInt().coerceAtLeast(1)
                    val heightByMemory = (MAX_BITMAP_BYTES / (width.toLong() * BYTES_PER_PIXEL)).toInt().coerceAtLeast(1)
                    val height = min(floor(rawHeight * scale.toDouble()).toInt().coerceAtLeast(1), heightByMemory)
                    Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).also { bitmap ->
                        page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                    }
                }
            }
        }

    @Synchronized
    override fun close() {
        if (closed) return
        closed = true
        runCatching { renderer.close() }
        runCatching { descriptor.close() }
        lease.close()
    }

    companion object {
        const val MAX_EDGE_PX = 4_096
        const val MAX_BITMAP_BYTES = 32L * 1024 * 1024
        const val MIN_ZOOM = 1f
        const val MAX_ZOOM = 4f
        private const val MIN_RENDER_SCALE = 0.1f
        private const val BYTES_PER_PIXEL = 4L

        @Suppress("TooGenericExceptionCaught")
        suspend fun open(lease: PdfFileLease): PdfDocumentController =
            withContext(Dispatchers.IO) {
                var descriptor: ParcelFileDescriptor? = null
                try {
                    descriptor = ParcelFileDescriptor.open(lease.file, ParcelFileDescriptor.MODE_READ_ONLY)
                    val renderer = PdfRenderer(descriptor)
                    require(renderer.pageCount > 0) { "PDF has no pages" }
                    PdfDocumentController(lease, descriptor, renderer)
                } catch (error: Throwable) {
                    runCatching { descriptor?.close() }
                    lease.close()
                    throw error
                }
            }
    }
}
