package com.kurastorage.core.data.media

import android.net.Uri
import androidx.media3.common.C
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.BaseDataSource
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.ReadyMediaSource
import kotlinx.coroutines.runBlocking
import java.io.IOException
import java.io.InputStream
import java.net.ProtocolException

class MediaGeneratingIOException(
    val job: MediaJobSnapshot,
) : IOException("Selected media variant is still generating")

@UnstableApi
class KuraMediaDataSource(
    private val repository: MediaRepository,
    private val source: ReadyMediaSource,
) : BaseDataSource(true) {
    private var content: ReadyMediaContent? = null
    private var input: InputStream? = null
    private var remaining = C.LENGTH_UNSET.toLong()
    private var opened = false

    @Suppress("SwallowedException")
    override fun open(dataSpec: DataSpec): Long {
        check(!opened) { "Data source is already open" }
        transferInitializing(dataSpec)
        val range = MediaRangeRequest.header(dataSpec.position, dataSpec.length)
        val result =
            try {
                runBlocking { repository.openContent(source.fileId, source.variant, range) }
            } catch (error: KuraStorageException.Api) {
                throw MediaDataSourceIOException.Http(error.error.statusCode ?: HTTP_UNKNOWN, error)
            } catch (error: KuraStorageException.CredentialUnavailable) {
                throw MediaDataSourceIOException.Http(HTTP_UNAUTHORIZED, error)
            } catch (error: KuraStorageException.InvalidServerResponse) {
                throw MediaDataSourceIOException.InvalidRange
            } catch (error: KuraStorageException.Network) {
                throw MediaDataSourceIOException.Network(error)
            } catch (error: KuraStorageException) {
                throw MediaDataSourceIOException.Network(error)
            }
        var success = false
        try {
            when (result) {
                is MediaContentResult.Generating -> throw MediaGeneratingIOException(result.job)
                is MediaContentResult.Ready -> openReady(result.content, dataSpec)
            }
            opened = true
            transferStarted(dataSpec)
            success = true
            return remaining
        } finally {
            if (!success) close()
        }
    }

    @Suppress("ReturnCount")
    override fun read(
        buffer: ByteArray,
        offset: Int,
        length: Int,
    ): Int {
        if (length == 0) return 0
        if (remaining == 0L) return C.RESULT_END_OF_INPUT
        val allowed = if (remaining == C.LENGTH_UNSET.toLong()) length else minOf(length.toLong(), remaining).toInt()
        val read =
            try {
                input?.read(buffer, offset, allowed) ?: throw IOException("Data source is not open")
            } catch (_: ProtocolException) {
                throw MediaDataSourceIOException.Incomplete
            }
        if (read == -1) {
            if (remaining > 0) throw MediaDataSourceIOException.Incomplete
            return C.RESULT_END_OF_INPUT
        }
        if (remaining != C.LENGTH_UNSET.toLong()) remaining -= read
        bytesTransferred(read)
        return read
    }

    override fun getUri(): Uri? = MEDIA_URI

    override fun close() {
        val wasOpened = opened
        opened = false
        input = null
        remaining = C.LENGTH_UNSET.toLong()
        content?.close()
        content = null
        if (wasOpened) transferEnded()
    }

    private fun openReady(
        ready: ReadyMediaContent,
        dataSpec: DataSpec,
    ) {
        content = ready
        val stream = ready.body.byteStream()
        input = stream
        val available =
            MediaRangeResponseValidator
                .validate(
                    requestedPosition = dataSpec.position,
                    requestedLength = dataSpec.length,
                    statusCode = ready.statusCode,
                    contentRange = ready.headers["Content-Range"],
                    contentLength = ready.contentLength,
                ).responseLength
        remaining =
            when {
                dataSpec.length != C.LENGTH_UNSET.toLong() -> dataSpec.length
                available != null && available >= 0 -> available
                else -> C.LENGTH_UNSET.toLong()
            }
    }

    class Factory(
        private val repository: MediaRepository,
        private val source: ReadyMediaSource,
    ) : DataSource.Factory {
        override fun createDataSource(): DataSource = KuraMediaDataSource(repository, source)
    }

    private companion object {
        val MEDIA_URI: Uri = Uri.parse("kurastorage-media://content")
        const val HTTP_UNAUTHORIZED = 401
        const val HTTP_UNKNOWN = 0
    }
}

sealed class MediaDataSourceIOException(
    message: String,
    cause: Throwable? = null,
) : IOException(message, cause) {
    class Http(
        val statusCode: Int,
        cause: Throwable,
    ) : MediaDataSourceIOException("Media request failed with HTTP $statusCode", cause)

    class Network(
        cause: Throwable,
    ) : MediaDataSourceIOException("Media network request failed", cause)

    data object Incomplete : MediaDataSourceIOException("Media response ended before the requested range")

    data object InvalidRange : MediaDataSourceIOException("Media response did not match the requested range")
}
