package com.kurastorage.feature.media.pdf

import android.graphics.pdf.PdfDocument
import androidx.test.platform.app.InstrumentationRegistry
import com.kurastorage.core.data.media.MediaContentResult
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.TemporaryPdfStore
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.io.FileOutputStream

class PdfDocumentControllerTest {
    @Test
    fun rendererOpensOnePageAndBoundsBitmap() =
        runBlocking {
            val context = InstrumentationRegistry.getInstrumentation().targetContext
            val scopeId = "renderer-test-${System.nanoTime()}"
            val store = TemporaryPdfStore(context.cacheDir, scopeId, UnusedRepository())
            val directory = File(context.cacheDir, "media-pdf/$scopeId")
            val file = File(directory, "fixture.pdf")
            val fixture = PdfDocument()
            try {
                val document = fixture
                val page = document.startPage(PdfDocument.PageInfo.Builder(5_000, 7_000, 1).create())
                page.canvas.drawText("KuraStorage", 20f, 40f, android.graphics.Paint())
                document.finishPage(page)
                FileOutputStream(file).use(document::writeTo)
            } finally {
                fixture.close()
            }

            val controller = PdfDocumentController.open(store.acquire(file))
            val bitmap = controller.render(0, 4_000, 4_000, 4f)

            assertEquals(1, controller.pageCount)
            assertTrue(maxOf(bitmap.width, bitmap.height) <= PdfDocumentController.MAX_EDGE_PX)
            assertTrue(bitmap.allocationByteCount <= PdfDocumentController.MAX_BITMAP_BYTES)
            bitmap.recycle()
            controller.close()
            store.close()
        }

    private class UnusedRepository : MediaRepository {
        override suspend fun inspectOriginal(fileId: String) = OriginalMetadata(ByteCount(1), "application/pdf", true)

        override suspend fun job(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun retryJob(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult = error("not used")
    }
}
