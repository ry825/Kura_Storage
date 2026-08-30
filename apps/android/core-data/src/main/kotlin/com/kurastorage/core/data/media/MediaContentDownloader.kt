package com.kurastorage.core.data.media

import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaVariant
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import java.io.OutputStream

class MediaDerivativeNotReadyException(
    val job: MediaJobSnapshot,
) : IllegalStateException("Selected media quality is still being generated")

class MediaContentDownloader(
    private val repository: MediaRepository,
) {
    suspend fun download(
        fileId: String,
        variant: MediaVariant,
        output: OutputStream,
        maximumBytes: Long = Long.MAX_VALUE,
    ): Long =
        withContext(Dispatchers.IO) {
            val context = currentCoroutineContext()
            when (val result = repository.openContent(fileId, variant)) {
                is MediaContentResult.Generating -> throw MediaDerivativeNotReadyException(result.job)
                is MediaContentResult.Ready ->
                    result.content.use { content ->
                        content.copyTo(output, maximumBytes) { context.ensureActive() }
                    }
            }
        }
}
