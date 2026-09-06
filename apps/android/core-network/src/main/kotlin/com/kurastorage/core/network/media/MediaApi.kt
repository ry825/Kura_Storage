package com.kurastorage.core.network.media

import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.network.ErrorResponseDto
import com.kurastorage.core.network.NetworkCallResult
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import okhttp3.Call
import okhttp3.Callback
import okhttp3.Headers
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okio.Buffer
import java.io.IOException
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

interface MediaApi {
    suspend fun headContent(
        accessToken: String,
        fileId: String,
        variant: MediaVariant,
    ): NetworkCallResult<MediaMetadataNetworkResult> {
        require(variant == MediaVariant.ORIGINAL)
        return when (val result = headOriginal(accessToken, fileId)) {
            is NetworkCallResult.Success ->
                NetworkCallResult.Success(
                    MediaMetadataNetworkResult.Ready(
                        VariantMetadataDto(
                            result.value.contentLength,
                            result.value.mimeType,
                            result.value.acceptsRanges,
                        ),
                    ),
                )
            NetworkCallResult.Unauthorized -> NetworkCallResult.Unauthorized
        }
    }

    suspend fun headOriginal(
        accessToken: String,
        fileId: String,
    ): NetworkCallResult<OriginalMetadataDto>

    suspend fun mediaJob(
        accessToken: String,
        jobId: String,
    ): NetworkCallResult<MediaJobDto>

    suspend fun retryMediaJob(
        accessToken: String,
        jobId: String,
    ): NetworkCallResult<MediaJobDto>

    suspend fun thumbnailJobSummary(accessToken: String): NetworkCallResult<ThumbnailJobSummaryDto> =
        error("Thumbnail job summary is not implemented by this test double")

    fun contentRequest(
        accessToken: String,
        fileId: String,
        variant: MediaVariant,
        range: String? = null,
    ): Call

    suspend fun openContent(
        accessToken: String,
        fileId: String,
        variant: MediaVariant,
        range: String? = null,
    ): NetworkCallResult<MediaContentNetworkResult>
}

sealed interface MediaContentNetworkResult {
    data class Ready(
        val response: Response,
    ) : MediaContentNetworkResult

    data class Generating(
        val accepted: MediaAcceptedResponseDto,
    ) : MediaContentNetworkResult
}

sealed interface MediaMetadataNetworkResult {
    data class Ready(
        val metadata: VariantMetadataDto,
    ) : MediaMetadataNetworkResult

    data class Generating(
        val accepted: MediaAcceptedResponseDto,
    ) : MediaMetadataNetworkResult
}

@Suppress("TooManyFunctions")
class OkHttpMediaApi(
    baseUrl: String,
    client: OkHttpClient,
    private val json: Json = Json { ignoreUnknownKeys = false },
) : MediaApi {
    private val baseUrl: HttpUrl = "${baseUrl.trimEnd('/')}/".toHttpUrl()
    private val client =
        client
            .newBuilder()
            .followRedirects(false)
            .followSslRedirects(false)
            .build()

    override suspend fun headContent(
        accessToken: String,
        fileId: String,
        variant: MediaVariant,
    ): NetworkCallResult<MediaMetadataNetworkResult> =
        executeHead(
            requestBuilder(accessToken, contentUrl(fileId, variant)).head().build(),
        ) { response ->
            when (response.code) {
                HTTP_OK -> {
                    val length = response.header("Content-Length")?.toLongOrNull()
                    val mime = response.header("Content-Type")?.substringBefore(';')?.trim()
                    val hasInvalidMetadata = length == null || length < 0 || mime.isNullOrBlank()
                    val supportsRanges = response.header("Accept-Ranges").equals("bytes", ignoreCase = true)
                    if (
                        hasInvalidMetadata ||
                        !supportsRanges
                    ) {
                        invalidMediaResponse()
                    }
                    MediaMetadataNetworkResult.Ready(VariantMetadataDto(length, mime, true))
                }
                HTTP_ACCEPTED -> {
                    val jobId = response.header("X-Kura-Media-Job-Id") ?: invalidMediaResponse()
                    val location = response.header("Location") ?: invalidMediaResponse()
                    val retryAfter = response.header("Retry-After")?.toIntOrNull() ?: invalidMediaResponse()
                    if (jobId.isBlank() || location != "/api/v1/media-jobs/$jobId" || retryAfter <= 0) {
                        invalidMediaResponse()
                    }
                    MediaMetadataNetworkResult.Generating(
                        MediaAcceptedResponseDto("GENERATING", jobId, location, retryAfter),
                    )
                }
                else -> invalidMediaResponse()
            }
        }

    override suspend fun headOriginal(
        accessToken: String,
        fileId: String,
    ): NetworkCallResult<OriginalMetadataDto> =
        executeAuthenticated(
            requestBuilder(accessToken, contentUrl(fileId, MediaVariant.ORIGINAL)).head().build(),
        ) { response ->
            val length = response.header("Content-Length")?.toLongOrNull()
            val mime = response.header("Content-Type")?.substringBefore(';')?.trim()
            if (length == null || length < 0 || mime.isNullOrBlank()) invalidMediaResponse()
            OriginalMetadataDto(length, mime, response.header("Accept-Ranges") == "bytes")
        }

    override suspend fun mediaJob(
        accessToken: String,
        jobId: String,
    ): NetworkCallResult<MediaJobDto> =
        executeJson(
            requestBuilder(accessToken, resourceUrl("media-jobs", jobId)).get().build(),
        )

    override suspend fun retryMediaJob(
        accessToken: String,
        jobId: String,
    ): NetworkCallResult<MediaJobDto> =
        executeJson(
            requestBuilder(accessToken, resourceUrl("media-jobs", jobId, "retry"))
                .post(ByteArray(0).toRequestBody())
                .build(),
        )

    override suspend fun thumbnailJobSummary(accessToken: String): NetworkCallResult<ThumbnailJobSummaryDto> =
        executeJson(
            requestBuilder(accessToken, resourceUrl("media", "thumbnail-jobs", "summary")).get().build(),
        )

    override fun contentRequest(
        accessToken: String,
        fileId: String,
        variant: MediaVariant,
        range: String?,
    ): Call {
        require(range == null || SINGLE_RANGE.matches(range)) { "Only one explicit byte range is allowed" }
        val builder = requestBuilder(accessToken, contentUrl(fileId, variant)).get()
        range?.let { builder.header("Range", it) }
        return client.newCall(builder.build())
    }

    override suspend fun openContent(
        accessToken: String,
        fileId: String,
        variant: MediaVariant,
        range: String?,
    ): NetworkCallResult<MediaContentNetworkResult> =
        withContext(Dispatchers.IO) {
            try {
                val response = contentRequest(accessToken, fileId, variant, range).awaitResponse()
                when {
                    response.code == HTTP_UNAUTHORIZED -> {
                        response.close()
                        NetworkCallResult.Unauthorized
                    }
                    response.code == HTTP_ACCEPTED ->
                        response.use {
                            NetworkCallResult.Success(
                                MediaContentNetworkResult.Generating(
                                    json.decodeFromString<MediaAcceptedResponseDto>(it.body.readBoundedUtf8()),
                                ),
                            )
                        }
                    response.code == HTTP_OK || response.code == HTTP_PARTIAL_CONTENT -> {
                        validateMediaRangeResponse(response, range)
                        NetworkCallResult.Success(MediaContentNetworkResult.Ready(response))
                    }
                    !response.isSuccessful -> response.use { throw it.toApiException(json) }
                    else -> response.use { invalidMediaResponse() }
                }
            } catch (error: KuraStorageException) {
                throw error
            } catch (error: IOException) {
                throw KuraStorageException.Network(error)
            } catch (error: SerializationException) {
                throw KuraStorageException.Network(error)
            }
        }

    private suspend inline fun <reified T> executeJson(request: Request): NetworkCallResult<T> =
        executeAuthenticated(request) { response ->
            val body = response.body.readBoundedUtf8()
            try {
                json.decodeFromString<T>(body)
            } catch (error: SerializationException) {
                throw KuraStorageException.Network(error)
            }
        }

    private suspend fun <T> executeAuthenticated(
        request: Request,
        map: (Response) -> T,
    ): NetworkCallResult<T> =
        withContext(Dispatchers.IO) {
            try {
                client.newCall(request).awaitResponse().use { response ->
                    if (response.code == HTTP_UNAUTHORIZED) return@withContext NetworkCallResult.Unauthorized
                    if (!response.isSuccessful) throw response.toApiException(json)
                    NetworkCallResult.Success(map(response))
                }
            } catch (error: KuraStorageException) {
                throw error
            } catch (error: IOException) {
                throw KuraStorageException.Network(error)
            }
        }

    private suspend fun <T> executeHead(
        request: Request,
        map: (Response) -> T,
    ): NetworkCallResult<T> =
        withContext(Dispatchers.IO) {
            try {
                client.newCall(request).awaitResponse().use { response ->
                    if (response.code == HTTP_UNAUTHORIZED) return@withContext NetworkCallResult.Unauthorized
                    if (response.code != HTTP_OK && response.code != HTTP_ACCEPTED) {
                        val code = if (response.code == HTTP_NOT_FOUND) ErrorCode.FILE_NOT_FOUND else ErrorCode.UNKNOWN
                        throw KuraStorageException.Api(
                            ApiError(
                                code = code,
                                requestId = response.header("X-Request-Id"),
                                statusCode = response.code,
                                retryAfterSeconds = response.header("Retry-After")?.toLongOrNull(),
                            ),
                        )
                    }
                    NetworkCallResult.Success(map(response))
                }
            } catch (error: KuraStorageException) {
                throw error
            } catch (error: IOException) {
                throw KuraStorageException.Network(error)
            }
        }

    private fun requestBuilder(
        accessToken: String,
        url: HttpUrl,
    ): Request.Builder = Request.Builder().url(url).header("Authorization", "Bearer $accessToken")

    private fun contentUrl(
        fileId: String,
        variant: MediaVariant,
    ): HttpUrl =
        resourceUrl("files", fileId, "content")
            .newBuilder()
            .addQueryParameter("variant", variant.wireValue)
            .addQueryParameter("disposition", "inline")
            .build()

    private fun resourceUrl(vararg segments: String): HttpUrl {
        val builder = baseUrl.newBuilder()
        segments.forEach {
            require(it.isNotBlank()) { "URL identifiers must not be blank" }
            builder.addPathSegment(it)
        }
        return builder.build()
    }

    private companion object {
        const val HTTP_UNAUTHORIZED = 401
        const val HTTP_OK = 200
        const val HTTP_ACCEPTED = 202
        const val HTTP_PARTIAL_CONTENT = 206
        const val HTTP_NOT_FOUND = 404
        val SINGLE_RANGE = Regex("bytes=(?:[0-9]+-[0-9]*|-[0-9]+)")
    }
}

private fun invalidMediaResponse(): Nothing = throw KuraStorageException.InvalidServerResponse()

@Suppress("ComplexCondition", "CyclomaticComplexMethod", "MagicNumber")
private fun validateMediaRangeResponse(
    response: Response,
    requestedRange: String?,
) {
    if (response.code == HTTP_PARTIAL_CONTENT) {
        val contentRange = response.header("Content-Range") ?: invalidMediaResponse()
        val responseMatch = CONTENT_RANGE.matchEntire(contentRange) ?: invalidMediaResponse()
        val requestedMatch = requestedRange?.let(REQUEST_RANGE::matchEntire) ?: invalidMediaResponse()
        val requestedStart = requestedMatch.groupValues[1].toLongOrNull() ?: invalidMediaResponse()
        val requestedEnd = requestedMatch.groupValues[2].takeIf(String::isNotEmpty)?.toLongOrNull()
        val responseStart = responseMatch.groupValues[1].toLongOrNull() ?: invalidMediaResponse()
        val responseEnd = responseMatch.groupValues[2].toLongOrNull() ?: invalidMediaResponse()
        val total = responseMatch.groupValues[3].toLongOrNull() ?: invalidMediaResponse()
        if (
            responseStart != requestedStart ||
            responseEnd < responseStart ||
            (requestedEnd != null && responseEnd != requestedEnd) ||
            total <= responseEnd
        ) {
            invalidMediaResponse()
        }
    }
    if (requestedRange == null && response.code == HTTP_PARTIAL_CONTENT) invalidMediaResponse()
}

private fun okhttp3.ResponseBody.readBoundedUtf8(): String {
    val buffer = Buffer()
    val source = source()
    while (buffer.size <= MAX_JSON_BYTES) {
        val read = source.read(buffer, MAX_JSON_BYTES + 1L - buffer.size)
        if (read == -1L) break
    }
    if (buffer.size > MAX_JSON_BYTES) throw KuraStorageException.InvalidServerResponse()
    return buffer.readUtf8()
}

private const val HTTP_PARTIAL_CONTENT = 206
private val CONTENT_RANGE = Regex("bytes ([0-9]+)-([0-9]+)/([0-9]+)")
private val REQUEST_RANGE = Regex("bytes=([0-9]+)-([0-9]*)")

private suspend fun Call.awaitResponse(): Response =
    suspendCancellableCoroutine { continuation ->
        continuation.invokeOnCancellation { cancel() }
        enqueue(
            object : Callback {
                override fun onFailure(
                    call: Call,
                    e: IOException,
                ) {
                    if (continuation.isActive) continuation.resumeWithException(e)
                }

                override fun onResponse(
                    call: Call,
                    response: Response,
                ) {
                    if (continuation.isActive) {
                        continuation.resume(response) { _, value, _ -> value.close() }
                    } else {
                        response.close()
                    }
                }
            },
        )
    }

private fun Response.toApiException(json: Json): KuraStorageException.Api {
    val body = runCatching { body.readBoundedUtf8() }.getOrDefault("")
    val error = runCatching { json.decodeFromString<ErrorResponseDto>(body) }.getOrNull()
    val code = error?.code?.let { runCatching { ErrorCode.valueOf(it) }.getOrNull() } ?: ErrorCode.UNKNOWN
    return KuraStorageException.Api(
        ApiError(
            code = code,
            requestId = error?.requestId,
            statusCode = this.code,
            retryAfterSeconds = headers.retryAfterSeconds(),
        ),
    )
}

private fun Headers.retryAfterSeconds(): Long? = this["Retry-After"]?.toLongOrNull()

private const val MAX_JSON_BYTES = 64 * 1024
