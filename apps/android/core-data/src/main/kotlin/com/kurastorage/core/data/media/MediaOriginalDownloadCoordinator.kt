package com.kurastorage.core.data.media

import com.kurastorage.core.model.media.MediaVariant
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.OutputStream

interface MediaDownloadTarget {
    fun openOutputStream(): OutputStream?

    fun delete(): Boolean
}

sealed interface MediaDownloadOutcome {
    data class Completed(
        val bytesWritten: Long,
    ) : MediaDownloadOutcome

    data class Failed(
        val incompleteTargetMayRemain: Boolean,
    ) : MediaDownloadOutcome
}

class MediaOriginalDownloadCoordinator(
    private val downloader: MediaContentDownloader,
) {
    suspend fun download(
        fileId: String,
        target: MediaDownloadTarget,
    ): MediaDownloadOutcome =
        withContext(Dispatchers.IO) {
            try {
                val output = checkNotNull(target.openOutputStream()) { "The selected destination cannot be opened" }
                val bytes = output.use { downloader.download(fileId, MediaVariant.ORIGINAL, it) }
                MediaDownloadOutcome.Completed(bytes)
            } catch (cancelled: CancellationException) {
                runCatching { target.delete() }
                throw cancelled
            } catch (_: Throwable) {
                val removed = runCatching { target.delete() }.getOrDefault(false)
                MediaDownloadOutcome.Failed(incompleteTargetMayRemain = !removed)
            }
        }
}
