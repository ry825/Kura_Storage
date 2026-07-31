package com.kurastorage.core.data

import android.content.Context
import android.content.Intent
import android.net.Uri
import com.kurastorage.core.model.DownloadOperation
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadOperation
import com.kurastorage.core.network.FileApi
import com.kurastorage.core.network.NetworkCallResult
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.flow.flow
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okio.BufferedSink
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID

interface ContentStreamProvider {
    fun openInput(uri: String): InputStream?

    fun openOutput(uri: String): OutputStream?

    fun delete(uri: String): Boolean

    fun openIntent(
        uri: String,
        mimeType: String?,
    ): Intent
}

class AndroidContentStreamProvider(
    context: Context,
) : ContentStreamProvider {
    private val resolver = context.applicationContext.contentResolver

    override fun openInput(uri: String) = resolver.openInputStream(Uri.parse(uri))

    override fun openOutput(uri: String) = resolver.openOutputStream(Uri.parse(uri), "w")

    override fun delete(uri: String) = resolver.delete(Uri.parse(uri), null, null) > 0

    override fun openIntent(
        uri: String,
        mimeType: String?,
    ) = Intent(Intent.ACTION_VIEW)
        .setDataAndType(Uri.parse(uri), mimeType ?: "application/octet-stream")
        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
}

interface TransferRepository {
    fun newUpload(
        sourceUri: String,
        destinationFolderId: String,
        fileName: String,
        size: Long,
        contentType: String?,
    ): UploadOperation

    fun upload(operation: UploadOperation): Flow<TransferEvent>

    fun download(operation: DownloadOperation): Flow<TransferEvent>

    fun openDownloadedFile(
        destinationUri: String,
        mimeType: String?,
    ): Intent
}

class DefaultTransferRepository(
    private val api: FileApi,
    private val executor: AuthenticatedRequestExecutor,
    private val streams: ContentStreamProvider,
) : TransferRepository {
    override fun newUpload(
        sourceUri: String,
        destinationFolderId: String,
        fileName: String,
        size: Long,
        contentType: String?,
    ) = UploadOperation(
        sourceUri,
        destinationFolderId,
        fileName,
        size,
        contentType,
        idempotencyKey = UUID.randomUUID().toString(),
    )

    @Suppress("TooGenericExceptionCaught")
    override fun upload(operation: UploadOperation): Flow<TransferEvent> =
        channelFlow {
            send(TransferEvent.Progress(0, operation.size))
            try {
                val body = StreamingRequestBody(operation, streams) { send(TransferEvent.Progress(it, operation.size)) }
                val result =
                    authenticated { token ->
                        api.upload(
                            token,
                            operation.idempotencyKey,
                            operation.destinationFolderId.textBody(),
                            operation.fileName.textBody(),
                            operation.size.toString().textBody(),
                            operation.contentType?.textBody(),
                            operation.sha256?.textBody(),
                            MultipartBody.Part.createFormData("file", operation.fileName, body),
                        )
                    }
                send(TransferEvent.UploadCompleted(result.toModel()))
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (error: Throwable) {
                send(TransferEvent.Failed(error))
            }
        }

    @Suppress("TooGenericExceptionCaught")
    override fun download(operation: DownloadOperation): Flow<TransferEvent> =
        flow {
            emit(TransferEvent.Progress(0, operation.file.size))
            try {
                val response = authenticated { api.download(it, operation.file.id) }
                response.use { body ->
                    val output =
                        streams.openOutput(operation.destinationUri)
                            ?: error("Unable to open download destination")
                    output.use { destination ->
                        body.byteStream().use { source ->
                            copyWithProgress(source, destination, operation.file.size) {
                                emit(TransferEvent.Progress(it, operation.file.size))
                            }
                        }
                    }
                }
                emit(TransferEvent.DownloadCompleted(operation.destinationUri))
            } catch (cancelled: CancellationException) {
                streams.delete(operation.destinationUri)
                throw cancelled
            } catch (error: Throwable) {
                emit(TransferEvent.Failed(error, streams.delete(operation.destinationUri)))
            }
        }

    override fun openDownloadedFile(
        destinationUri: String,
        mimeType: String?,
    ) = streams.openIntent(destinationUri, mimeType)

    private suspend fun <T> authenticated(call: suspend (String) -> NetworkCallResult<T>): T =
        executor.execute { token ->
            when (val result = call(token)) {
                is NetworkCallResult.Success -> AuthenticatedCallResult.Success(result.value)
                NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
            }
        }
}

private class StreamingRequestBody(
    private val operation: UploadOperation,
    private val streams: ContentStreamProvider,
    private val progress: suspend (Long) -> Unit,
) : RequestBody() {
    override fun contentType() = operation.contentType?.toMediaTypeOrNull()

    override fun contentLength() = operation.size

    override fun writeTo(sink: BufferedSink) {
        val input = streams.openInput(operation.sourceUri) ?: error("Unable to open upload source")
        input.use {
            val buffer = ByteArray(BUFFER_SIZE)
            var transferred = 0L
            while (true) {
                val count = it.read(buffer)
                if (count < 0) break
                sink.write(buffer, 0, count)
                transferred += count
                kotlinx.coroutines.runBlocking { progress(transferred) }
            }
        }
    }
}

private suspend fun copyWithProgress(
    input: InputStream,
    output: OutputStream,
    total: Long,
    progress: suspend (Long) -> Unit,
) {
    val buffer = ByteArray(BUFFER_SIZE)
    var transferred = 0L
    while (true) {
        currentCoroutineContext().ensureActive()
        val count = input.read(buffer)
        if (count < 0) break
        output.write(buffer, 0, count)
        transferred += count
        progress(transferred.coerceAtMost(total))
    }
}

private fun String.textBody() = toRequestBody(MultipartBody.FORM)

private const val BUFFER_SIZE = 64 * 1024
