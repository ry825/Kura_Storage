package com.kurastorage.core.data

import android.content.Context
import android.content.Intent
import android.net.Uri
import com.kurastorage.core.model.DownloadOperation
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadOperation
import com.kurastorage.core.model.UploadState
import com.kurastorage.core.network.CreateUploadSessionRequestDto
import com.kurastorage.core.network.FileApi
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.UploadSessionApi
import com.kurastorage.core.network.UploadSessionDto
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.flow.flow
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.InputStream
import java.io.OutputStream
import java.security.MessageDigest
import java.time.Instant
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

    suspend fun cancelUpload(operation: UploadOperation) = Unit

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
    private val uploadSessions: UploadSessionApi? = api as? UploadSessionApi,
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

    @Suppress("TooGenericExceptionCaught", "LongMethod", "CyclomaticComplexMethod")
    override fun upload(operation: UploadOperation): Flow<TransferEvent> =
        channelFlow {
            var current = operation.copy(state = UploadState.PREPARING)

            suspend fun status(
                message: String? = null,
                canRetry: Boolean = false,
            ) {
                send(TransferEvent.UploadStatus(current, message, canRetry))
            }
            status("Preparing upload")
            try {
                val sessionApi = uploadSessions ?: throw KuraStorageException.ServerUpgradeRequired()
                val sourceSha = hashSource(current)
                if (current.sha256 != null && current.sha256 != sourceSha) {
                    throw KuraStorageException.UploadSourceChanged()
                }
                current = current.copy(sha256 = sourceSha, state = UploadState.CREATING_SESSION)
                status("Creating upload session")
                var session =
                    if (current.sessionId == null) {
                        try {
                            authenticated { token ->
                                sessionApi.createUploadSession(
                                    token,
                                    current.idempotencyKey,
                                    CreateUploadSessionRequestDto(
                                        current.destinationFolderId,
                                        current.fileName,
                                        current.contentType,
                                        current.size,
                                        sourceSha,
                                    ),
                                )
                            }
                        } catch (error: KuraStorageException.Api) {
                            if (error.error.statusCode == HTTP_NOT_FOUND && error.error.code == ErrorCode.UNKNOWN) {
                                throw KuraStorageException.ServerUpgradeRequired()
                            }
                            throw error
                        }
                    } else {
                        authenticated { sessionApi.getUploadSession(it, checkNotNull(current.sessionId)) }
                    }
                current = current.fromSession(session, UploadState.UPLOADING)
                status(if (current.confirmedOffset > 0) "Resuming from confirmed position" else "Uploading")
                val chunkBytes = minOf(session.preferredChunkBytes, session.maximumChunkBytes)
                require(chunkBytes > 0) { "Server returned an invalid chunk size" }
                var retries = 0
                var source = openSourceAt(current)
                try {
                    while (current.confirmedOffset < current.size) {
                        currentCoroutineContext().ensureActive()
                        val chunk = readChunk(source, current.size - current.confirmedOffset, chunkBytes)
                        val chunkSha = chunk.sha256()
                        try {
                            val response =
                                authenticated { token ->
                                    sessionApi.uploadChunk(
                                        token,
                                        checkNotNull(current.sessionId),
                                        current.confirmedOffset,
                                        chunkSha,
                                        chunk.toRequestBody(OCTET_STREAM),
                                    )
                                }
                            check(response.sha256.equals(chunkSha, ignoreCase = true)) {
                                "Server chunk checksum differed"
                            }
                            check(response.nextOffset >= current.confirmedOffset) {
                                "Server offset moved backwards"
                            }
                            current =
                                current.copy(
                                    confirmedOffset = response.nextOffset,
                                    expiresAt = Instant.parse(response.expiresAt),
                                    state = UploadState.UPLOADING,
                                )
                            retries = 0
                            status("Uploading")
                        } catch (error: KuraStorageException.Api) {
                            if (!error.isResynchronizable() || retries >= MAX_RESYNCHRONIZATION_ATTEMPTS) throw error
                            current = current.copy(state = UploadState.PAUSED)
                            status("Connection interrupted; checking server position", canRetry = true)
                            delay((error.error.retryAfterSeconds ?: DEFAULT_RETRY_SECONDS) * MILLISECONDS_PER_SECOND)
                            session = authenticated { sessionApi.getUploadSession(it, checkNotNull(current.sessionId)) }
                            current = current.fromSession(session, UploadState.UPLOADING)
                            source.close()
                            source = openSourceAt(current)
                            retries++
                            status("Resuming from confirmed position")
                        } catch (error: KuraStorageException.Network) {
                            if (retries >= MAX_RESYNCHRONIZATION_ATTEMPTS) throw error
                            current = current.copy(state = UploadState.PAUSED)
                            status("Connection interrupted; checking server position", canRetry = true)
                            delay(DEFAULT_RETRY_SECONDS * MILLISECONDS_PER_SECOND)
                            session = authenticated { sessionApi.getUploadSession(it, checkNotNull(current.sessionId)) }
                            current = current.fromSession(session, UploadState.UPLOADING)
                            source.close()
                            source = openSourceAt(current)
                            retries++
                            status("Resuming from confirmed position")
                        }
                    }
                } finally {
                    source.close()
                }
                current = current.copy(state = UploadState.VERIFYING)
                status("Verifying upload")
                val result = authenticated { sessionApi.completeUploadSession(it, checkNotNull(current.sessionId)) }
                current = current.copy(state = UploadState.COMPLETED, confirmedOffset = current.size)
                status("Upload completed")
                send(TransferEvent.UploadCompleted(result.toModel()))
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (error: Throwable) {
                val retryable = error.canRetryUpload()
                current =
                    current.copy(
                        state =
                            when {
                                current.state == UploadState.CANCELLED -> UploadState.CANCELLED
                                retryable -> UploadState.PAUSED
                                else -> UploadState.FAILED
                            },
                    )
                send(TransferEvent.Failed(error))
                send(TransferEvent.UploadStatus(current, error.uploadMessage(), retryable))
            }
        }

    override suspend fun cancelUpload(operation: UploadOperation) {
        val sessionId = operation.sessionId ?: return
        val sessionApi = uploadSessions ?: throw KuraStorageException.ServerUpgradeRequired()
        authenticated { sessionApi.cancelUploadSession(it, sessionId) }
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

    private fun hashSource(operation: UploadOperation): String {
        val input = streams.openInput(operation.sourceUri) ?: throw KuraStorageException.UploadSourceUnavailable()
        val digest = MessageDigest.getInstance(SHA_256)
        var count = 0L
        input.use {
            val buffer = ByteArray(BUFFER_SIZE)
            while (true) {
                val read = it.read(buffer)
                if (read < 0) break
                digest.update(buffer, 0, read)
                count += read
            }
        }
        if (count != operation.size) throw KuraStorageException.UploadSourceChanged()
        return digest.digest().toHex()
    }

    @Suppress("ThrowsCount")
    private fun openSourceAt(operation: UploadOperation): InputStream {
        val input = streams.openInput(operation.sourceUri) ?: throw KuraStorageException.UploadSourceUnavailable()
        return try {
            input.skipFully(operation.confirmedOffset)
            input
        } catch (error: KuraStorageException.UploadSourceChanged) {
            input.close()
            throw error
        } catch (error: java.io.IOException) {
            input.close()
            throw error
        }
    }

    private fun readChunk(
        input: InputStream,
        remaining: Long,
        preferredChunkBytes: Int,
    ): ByteArray {
        val target = minOf(remaining, preferredChunkBytes.toLong()).toInt()
        val chunk = ByteArray(target)
        var read = 0
        while (read < target) {
            val count = input.read(chunk, read, target - read)
            if (count < 0) throw KuraStorageException.UploadSourceChanged()
            read += count
        }
        return chunk
    }
}

private fun UploadOperation.fromSession(
    session: UploadSessionDto,
    state: UploadState,
) = copy(
    sessionId = session.id,
    confirmedOffset = session.nextOffset,
    expiresAt = Instant.parse(session.expiresAt),
    state = state,
)

private fun InputStream.skipFully(byteCount: Long) {
    val buffer = ByteArray(BUFFER_SIZE)
    var remaining = byteCount
    while (remaining > 0) {
        val skipped = skip(remaining)
        if (skipped > 0) {
            remaining -= skipped
        } else {
            val read = read(buffer, 0, minOf(buffer.size.toLong(), remaining).toInt())
            if (read < 0) throw KuraStorageException.UploadSourceChanged()
            remaining -= read
        }
    }
}

private fun ByteArray.sha256() = MessageDigest.getInstance(SHA_256).digest(this).toHex()

private fun ByteArray.toHex() = joinToString("") { "%02x".format(it) }

@Suppress("MaxLineLength")
private fun KuraStorageException.Api.isResynchronizable() = error.canRetry || error.code == ErrorCode.UPLOAD_OFFSET_MISMATCH

@Suppress("MaxLineLength")
private fun Throwable.canRetryUpload() = this is KuraStorageException.Network || (this is KuraStorageException.Api && error.canRetry)

private fun Throwable.uploadMessage() =
    when (this) {
        is KuraStorageException.UploadSourceUnavailable -> "The selected file can no longer be opened. Select it again."
        is KuraStorageException.UploadSourceChanged -> "The selected file changed. Start a new upload."
        is KuraStorageException.ServerUpgradeRequired -> "Update the KuraStorage server to use resumable uploads."
        is KuraStorageException.Api ->
            when (error.code) {
                ErrorCode.UPLOAD_SESSION_EXPIRED -> "The upload session expired. Start a new upload."
                ErrorCode.UPLOAD_SESSION_CANCELLED -> "The upload was cancelled."
                ErrorCode.STORAGE_CAPACITY_INSUFFICIENT -> "The server does not have enough storage capacity."
                ErrorCode.STORAGE_UNAVAILABLE -> "Server storage is unavailable."
                ErrorCode.DEVICE_REVOKED -> "This device is no longer authorized."
                else -> "Upload failed: ${error.code}"
            }
        else -> "Upload failed."
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

private const val BUFFER_SIZE = 64 * 1024
private const val SHA_256 = "SHA-256"
private const val DEFAULT_RETRY_SECONDS = 1L
private const val MILLISECONDS_PER_SECOND = 1000L
private const val MAX_RESYNCHRONIZATION_ATTEMPTS = 2
private const val HTTP_NOT_FOUND = 404
private val OCTET_STREAM = "application/octet-stream".toMediaTypeOrNull()
